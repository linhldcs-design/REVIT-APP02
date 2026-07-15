using Autodesk.Revit.DB;
using SheetAlign.Addin.Models;
using Serilog;

namespace SheetAlign.Addin.Services;

/// <summary>
///     Căn chỉnh viewport giữa các sheet theo 1 điểm neo lưới trục: trên mỗi sheet, điểm giao của
///     2 trục được chọn rơi đúng cùng vị trí trên giấy như sheet mẫu. Đồng thời căn vị trí nhãn
///     (tên bản vẽ) về cùng vị trí tuyệt đối trên giấy.
/// </summary>
public sealed class SheetAlignmentService
{
    public IReadOnlyList<Grid> GetGrids(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .Where(grid => grid.Curve is Line)
            .OrderBy(grid => grid.Name)
            .ToList();
    }

    public IReadOnlyList<ViewSheet> GetSheets(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(sheet => !sheet.IsPlaceholder)
            .OrderBy(sheet => sheet.SheetNumber)
            .ToList();
    }

    public IReadOnlyList<Level> GetLevels(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ToList();
    }

    /// <summary>
    ///     Điểm neo model cho view mặt cắt/elevation: 1 điểm trên đường trục, nâng lên cao độ Level.
    ///     Transform của viewport mặt cắt sẽ chiếu điểm 3D này về đúng vị trí trên giấy
    ///     (trục -> vị trí ngang, Level -> vị trí dọc).
    /// </summary>
    public XYZ? GetGridLevelAnchorModel(Grid grid, Level level)
    {
        if (grid.Curve is not Line line)
        {
            return null;
        }

        var p = line.GetEndPoint(0);
        return new XYZ(p.X, p.Y, level.Elevation);
    }

    /// <summary>Điểm giao 2 lưới trục trong toạ độ model (XY). Null nếu hai trục song song.</summary>
    public XYZ? GetGridIntersectionModel(Grid gridA, Grid gridB)
    {
        if (gridA.Curve is not Line lineA || gridB.Curve is not Line lineB)
        {
            return null;
        }

        var pA = lineA.GetEndPoint(0);
        var dA = lineA.Direction;
        var pB = lineB.GetEndPoint(0);
        var dB = lineB.Direction;

        var hit = SheetAlignMath.IntersectLines(
            (pA.X, pA.Y), (dA.X, dA.Y),
            (pB.X, pB.Y), (dB.X, dB.Y));

        return hit == null ? null : new XYZ(hit.Value.X, hit.Value.Y, 0);
    }

    public Viewport? GetPrimaryViewport(Document document, ViewSheet sheet)
    {
        foreach (var viewportId in sheet.GetAllViewports())
        {
            if (document.GetElement(viewportId) is not Viewport viewport)
            {
                continue;
            }

            if (document.GetElement(viewport.ViewId) is View view && !view.IsTemplate)
            {
                return viewport;
            }
        }

        return null;
    }

    public XYZ GetPaperAnchor(Viewport viewport, XYZ modelAnchor)
    {
        return viewport.GetProjectionToSheetTransform().OfPoint(modelAnchor);
    }

    /// <summary>
    ///     Căn chỉnh các sheet đích theo sheet mẫu trong 1 Transaction.
    ///     <paramref name="modelAnchor"/> là điểm neo model đã tính sẵn theo chế độ user chọn
    ///     (giao 2 trục cho mặt bằng, hoặc trục × Level cho mặt cắt/elevation).
    /// </summary>
    public SheetAlignResult Apply(
        Document document,
        ViewSheet masterSheet,
        IReadOnlyList<ViewSheet> targetSheets,
        XYZ modelAnchor)
    {
        var result = new SheetAlignResult();

        var masterViewport = GetPrimaryViewport(document, masterSheet);
        if (masterViewport == null)
        {
            result.Skipped.Add(new SheetAlignSkip(masterSheet.SheetNumber,
                "Sheet mẫu không có viewport hợp lệ."));
            return result;
        }

        var paperMaster = GetPaperAnchor(masterViewport, modelAnchor);
        var masterRotation = masterViewport.Rotation;

        // Vị trí tuyệt đối đầu nhãn (tên bản vẽ) trên giấy ở sheet mẫu.
        // LabelOffset là vector từ góc trái-dưới box -> đầu nhãn, nên cộng vào MinimumPoint của box.
        var masterLabelPaper = masterViewport.GetBoxOutline().MinimumPoint + masterViewport.LabelOffset;

        using var transaction = new Transaction(document, "Can chinh view theo luoi truc");
        transaction.Start();

        foreach (var sheet in targetSheets)
        {
            if (sheet.Id == masterSheet.Id)
            {
                continue;
            }

            var viewport = GetPrimaryViewport(document, sheet);
            if (viewport == null)
            {
                result.Skipped.Add(new SheetAlignSkip(sheet.SheetNumber, "Khong co viewport hop le."));
                continue;
            }

            if (viewport.Rotation != masterRotation)
            {
                result.Skipped.Add(new SheetAlignSkip(sheet.SheetNumber, "Viewport xoay khac sheet mau."));
                continue;
            }

            var paperCurrent = GetPaperAnchor(viewport, modelAnchor);
            var delta = SheetAlignMath.ComputeDelta((paperMaster.X, paperMaster.Y), (paperCurrent.X, paperCurrent.Y));

            var currentCenter = viewport.GetBoxCenter();
            viewport.SetBoxCenter(new XYZ(currentCenter.X + delta.X, currentCenter.Y + delta.Y, currentCenter.Z));

            // Box đã dịch -> đọc lại góc trái-dưới mới, đặt nhãn về đúng vị trí giấy như mẫu
            // (bù theo kích thước box riêng nên nhãn khớp tuyệt đối kể cả box khác mẫu).
            document.Regenerate();
            var newMin = viewport.GetBoxOutline().MinimumPoint;
            viewport.LabelOffset = new XYZ(
                masterLabelPaper.X - newMin.X,
                masterLabelPaper.Y - newMin.Y,
                0);

            result.UpdatedCount++;
            Log.Debug("Aligned sheet {Sheet}: delta=({Dx:F4},{Dy:F4})", sheet.SheetNumber, delta.X, delta.Y);
        }

        transaction.Commit();

        Log.Information("Sheet align done: updated {Updated}, skipped {Skipped}",
            result.UpdatedCount, result.Skipped.Count);

        return result;
    }
}
