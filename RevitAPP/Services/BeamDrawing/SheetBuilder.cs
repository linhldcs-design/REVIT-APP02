using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Helpers;

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

    /// <summary>
    ///     Chỉ sắp các viewport cross vừa được tạo trong lần chạy hiện tại. Mọi nội dung có sẵn trên sheet
    ///     là vùng chiếm chỗ bất biến; không suy luận quyền sở hữu bằng tên view hoặc Mark.
    /// </summary>
    public void ArrangeCrossViewports(Document doc, List<List<ElementId>> beamGroups, List<string> warnings)
    {
        if (beamGroups.Count == 0) return;
        var sheet = FirstSheetOf(doc, beamGroups);
        if (sheet == null) return;

        var movingGroups = beamGroups
            .Select(group => group
                .Select(id => doc.GetElement(id) as Viewport)
                .Where(viewport => viewport != null && viewport.SheetId == sheet.Id)
                .Cast<Viewport>()
                .ToList())
            .Where(group => group.Count > 0)
            .ToList();
        if (movingGroups.Count == 0) return;

        var movingIds = movingGroups.SelectMany(group => group)
            .Select(viewport => viewport.Id).ToHashSet();

        foreach (var viewport in movingGroups.SelectMany(group => group))
            PullTitleUnderView(viewport);
        doc.Regenerate();

        var plannerGroups = movingGroups.Select(group =>
            new BeamCrossViewportGroup(group.Select(MeasureFootprint).ToList())).ToList();
        var occupied = CollectOccupiedRects(doc, sheet, movingIds);
        var (left, right, top, bottom) = DrawingRegion(doc, sheet);

        IReadOnlyList<BeamCrossViewportPlacement> placements;
        try
        {
            placements = BeamCrossSheetLayoutPlanner.Plan(
                left, right, top, bottom, plannerGroups, occupied);
        }
        catch (InvalidOperationException ex)
        {
            warnings.Add(
                $"Không tìm được vùng trống để tự sắp xếp: {ex.Message} " +
                "Đã giữ viewport mới trên sheet để người dùng chỉnh tay; các viewport cũ không bị di chuyển.");
            return;
        }

        var byId = movingGroups.SelectMany(group => group)
            .ToDictionary(viewport => viewport.Id.ToValue());
        foreach (var placement in placements)
        {
            if (!byId.TryGetValue(placement.ViewportId, out var viewport)) continue;
            viewport.SetBoxCenter(new XYZ(placement.BoxCenterX, placement.BoxCenterY, 0));
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

    private static BeamCrossViewportFootprint MeasureFootprint(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        var minX = box.MinimumPoint.X;
        var minY = box.MinimumPoint.Y;
        var maxX = box.MaximumPoint.X;
        var maxY = box.MaximumPoint.Y;
        try
        {
            var label = viewport.GetLabelOutline();
            minX = Math.Min(minX, label.MinimumPoint.X);
            minY = Math.Min(minY, label.MinimumPoint.Y);
            maxX = Math.Max(maxX, label.MaximumPoint.X);
            maxY = Math.Max(maxY, label.MaximumPoint.Y);
        }
        catch
        {
            // Viewport type không có title.
        }

        var center = viewport.GetBoxCenter();
        return new BeamCrossViewportFootprint(
            viewport.Id.ToValue(),
            maxX - minX,
            maxY - minY,
            (minX + maxX) / 2.0 - center.X,
            (minY + maxY) / 2.0 - center.Y);
    }

    private static List<BeamSheetRect> CollectOccupiedRects(
        Document doc, ViewSheet sheet, HashSet<ElementId> movingIds)
    {
        var occupied = new List<BeamSheetRect>();
        foreach (var element in new FilteredElementCollector(doc)
                     .OwnedByView(sheet.Id)
                     .WhereElementIsNotElementType())
        {
            if (element.Id == sheet.Id || movingIds.Contains(element.Id) ||
                element.Category?.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
                continue;

            if (element is Viewport viewport)
            {
                var footprint = MeasureFootprint(viewport);
                var center = viewport.GetBoxCenter();
                occupied.Add(BeamSheetRect.Centered(
                    center.X + footprint.FootprintOffsetX,
                    center.Y + footprint.FootprintOffsetY,
                    footprint.Width,
                    footprint.Height));
                continue;
            }

            var box = element.get_BoundingBox(sheet);
            if (box != null)
                occupied.Add(new BeamSheetRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
        }
        return occupied;
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
        try
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
        catch
        {
            // Viewport type không có title.
        }
    }
}
