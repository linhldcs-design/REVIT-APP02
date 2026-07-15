using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Tìm/tạo ViewSheet theo số hiệu trong setting và đặt view lên sheet bằng Viewport.
///     PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class SheetBuilder
{
    public ViewSheet ResolveSheet(Document doc, SheetConfig sheet, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sheet.Number))
            throw new InvalidOperationException("Chưa chọn Sheet có sẵn để đặt mặt cắt.");

        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => s.SheetNumber == sheet.Number);

        if (existing == null)
            throw new InvalidOperationException(
                $"Không tìm thấy Sheet '{sheet.Number}'. Hãy mở lại form và chọn Sheet có sẵn.");

        return existing;
    }

    public Viewport? PlaceView(Document doc, ViewSheet sheet, ElementId viewId, XYZ point,
        ElementId? viewportTypeId, List<string> warnings)
    {
        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
        {
            warnings.Add("Một view không thể đặt lên sheet (có thể đã nằm trên sheet khác).");
            return null;
        }
        var viewport = Viewport.Create(doc, sheet.Id, viewId, point);
        if (viewportTypeId != null && viewportTypeId != ElementId.InvalidElementId)
        {
            try { viewport.ChangeTypeId(viewportTypeId); }
            catch (Exception ex) { warnings.Add($"Không áp được Viewport Type cho view: {ex.Message}"); }
        }
        return viewport;
    }

    /// <summary>Khoảng cách CENTER 2 viewport cross (GỐI↔NHỊP) cùng dầm (feet ~ 65mm).</summary>
    private const double IntraBeamCenterFeet = 0.213;

    /// <summary>Bước CENTER giữa 2 cụm dầm liền kề theo X (feet ~ 142mm).</summary>
    private const double BeamPitchXFeet = 0.466;

    /// <summary>Bước CENTER giữa 2 hàng theo Y (feet ~ 63mm).</summary>
    private const double RowPitchYFeet = 0.2075;

    /// <summary>Số dầm mỗi hàng khi xếp lưới.</summary>
    private const int BeamsPerRow = 3;

    /// <summary>Bề rộng danh nghĩa 1 cụm dầm (GỐI+NHỊP) theo X, để căn giữa hàng.</summary>
    private const double BeamClusterWidthFeet = IntraBeamCenterFeet;

    /// <summary>
    ///     Căn lưới viewport cross: mỗi dầm 1 cụm (GỐI, NHỊP) cách IntraBeam theo X; <see cref="BeamsPerRow"/>
    ///     dầm/hàng, các hàng cách RowPitch theo Y; toàn khối căn GIỮA vùng vẽ của sheet (trừ khung tên phải);
    ///     hàng cuối thiếu dầm được căn giữa riêng. Gọi sau annotate (box ổn định), trong Transaction.
    /// </summary>
    public void ArrangeCrossViewports(Document doc, List<List<ElementId>> beamGroups, List<string> warnings)
    {
        if (beamGroups.Count == 0) return;
        try
        {
            var sheet = FirstSheetOf(doc, beamGroups);
            if (sheet == null) return;
            var (left, right, top, bottom) = DrawingRegion(doc, sheet);

            // Grid lại TẤT CẢ cụm dầm MCN đang trên sheet (kể cả từ lệnh trước) để nhiều lần vẽ
            // không đè lên nhau. Gom cặp theo tên view "MCN-<mark>-<GOI|NHIP>", sắp theo mark rồi số.
            var allGroups = CollectBeamClustersOnSheet(doc, sheet);
            if (allGroups.Count > 0) beamGroups = allGroups;

            var nBeams = beamGroups.Count;
            var rows = (int)Math.Ceiling((double)nBeams / BeamsPerRow);
            var blockH = (rows - 1) * RowPitchYFeet;
            var topCenterY = top - Math.Max(0, (top - bottom) - blockH) / 2.0;

            for (var i = 0; i < nBeams; i++)
            {
                var row = i / BeamsPerRow;
                var col = i % BeamsPerRow;
                var beamsThisRow = Math.Min(BeamsPerRow, nBeams - row * BeamsPerRow);
                var rowW = (beamsThisRow - 1) * BeamPitchXFeet + BeamClusterWidthFeet;
                var rowLeftCenter = left + Math.Max(0, (right - left) - rowW) / 2.0;

                var clusterX = rowLeftCenter + col * BeamPitchXFeet;
                var clusterY = topCenterY - row * RowPitchYFeet;

                var xi = clusterX;
                foreach (var id in beamGroups[i])
                {
                    if (doc.GetElement(id) is not Viewport vp) continue;
                    vp.SetBoxCenter(new XYZ(xi, clusterY, 0));
                    PullTitleUnderView(vp);
                    xi += IntraBeamCenterFeet;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Không căn lưới được viewport: {ex.Message}");
        }
    }

    private static ViewSheet? FirstSheetOf(Document doc, List<List<ElementId>> beamGroups)
    {
        foreach (var group in beamGroups)
            foreach (var id in group)
                if (doc.GetElement(id) is Viewport vp && doc.GetElement(vp.SheetId) is ViewSheet s)
                    return s;
        return null;
    }

    /// <summary>
    ///     Gom mọi viewport mặt cắt ngang dầm (tên "MCN-&lt;mark&gt;-&lt;GOI|NHIP&gt;...") trên sheet thành
    ///     danh sách cụm dầm — mỗi cụm = [GỐI, NHỊP] cùng số hiệu — sắp theo mark rồi số. Cho phép grid lại
    ///     toàn bộ khi triển khai nhiều lệnh lên cùng sheet (không đè nhau).
    /// </summary>
    private static List<List<ElementId>> CollectBeamClustersOnSheet(Document doc, ViewSheet sheet)
    {
        var rx = new System.Text.RegularExpressions.Regex(
            @"^MCN-(?<mark>[A-Za-z]+\d+)-(?<kind>GOI|NHIP)\s*(?:\((?<num>\d+)\))?");
        var clusters = new SortedDictionary<(int mark, int num), (ElementId? goi, ElementId? nhip)>();

        foreach (var vp in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            if (doc.GetElement(vp.ViewId) is not View v || v.Name == null) continue;
            var m = rx.Match(v.Name);
            if (!m.Success) continue;

            var markDigits = new string(m.Groups["mark"].Value.Where(char.IsDigit).ToArray());
            var markNum = int.TryParse(markDigits, out var mn) ? mn : 0;
            var num = m.Groups["num"].Success ? int.Parse(m.Groups["num"].Value) : 0;
            var key = (markNum, num);

            clusters.TryGetValue(key, out var pair);
            if (m.Groups["kind"].Value == "GOI") pair.goi = vp.Id; else pair.nhip = vp.Id;
            clusters[key] = pair;
        }

        var result = new List<List<ElementId>>();
        foreach (var pair in clusters.Values)
        {
            var group = new List<ElementId>();
            if (pair.goi != null) group.Add(pair.goi);
            if (pair.nhip != null) group.Add(pair.nhip);
            if (group.Count > 0) result.Add(group);
        }
        return result;
    }

    /// <summary>
    ///     Vùng vẽ khả dụng trên sheet: bbox title block trừ dải khung tên bên phải (~28% chiều rộng) và
    ///     lề trong. Trả (left, right, top, bottom) theo tọa độ sheet (feet).
    /// </summary>
    private static (double left, double right, double top, double bottom) DrawingRegion(Document doc, ViewSheet sheet)
    {
        var tb = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .FirstElement();
        var bb = tb?.get_BoundingBox(sheet);
        if (bb == null) return (-0.775, 0.60, 1.295, 0.03);

        var w = bb.Max.X - bb.Min.X;
        var h = bb.Max.Y - bb.Min.Y;
        var margin = 0.03 * Math.Max(w, h);
        var titleBlockBandX = bb.Max.X - 0.28 * w; // mép trái dải khung tên phải
        return (bb.Min.X + margin, titleBlockBandX - margin, bb.Max.Y - margin, bb.Min.Y + margin);
    }

    /// <summary>Khoảng cách title tới ĐÁY box hình (feet ~ 12mm) — kéo tên sát ngay dưới hình như mẫu.</summary>
    private const double TitleGapBelowViewFeet = 12.0 / 304.8;

    /// <summary>Kéo title (view name) lên ngay sát dưới đáy box hình, căn trái theo mép trái box.</summary>
    private static void PullTitleUnderView(Viewport vp)
    {
        var box = vp.GetBoxOutline();
        var label = vp.GetLabelOutline();
        var current = vp.LabelOffset;
        // Đưa cạnh TRÊN của label lên cách ĐÁY box đúng TitleGapBelowView; căn mép trái label với mép trái box.
        var targetTopY = box.MinimumPoint.Y - TitleGapBelowViewFeet;
        var deltaY = targetTopY - label.MaximumPoint.Y;
        var deltaX = box.MinimumPoint.X - label.MinimumPoint.X;
        vp.LabelOffset = new XYZ(current.X + deltaX, current.Y + deltaY, 0);
    }
}
