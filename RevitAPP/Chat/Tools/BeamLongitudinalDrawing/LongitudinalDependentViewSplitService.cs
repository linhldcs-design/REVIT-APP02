using Autodesk.Revit.DB;
using RevitAPP.Core.Chat.BeamLongitudinalDrawing;
using RevitAPP.Helpers;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

internal sealed record LongitudinalSplitResult(
    IReadOnlyList<long> PlacedViewIds,
    bool WasSplit,
    string? GridName);

internal sealed class LongitudinalDependentViewSplitService
{
    private const double CutExtensionFeet = 500.0 / 304.8;
    private const double CropPastBreakFeet = 25.0 / 304.8;
    private readonly LongitudinalGridSplitLocator _locator = new();
    private readonly LongitudinalSplitBreakLinePlacer _breakLines = new();

    public LongitudinalSplitResult SplitOnlyWhenRequired(
        Document document,
        ViewSheet sheet,
        FamilyInstance beam,
        long primaryViewId,
        double availableWidth,
        string breakLineTypeName)
    {
        var primary = document.GetElement(ChatElementIdCompat.Create(primaryViewId)) as ViewSection
                      ?? throw new InvalidOperationException("Không tìm thấy view mặt cắt dọc vừa tạo.");
        var originalViewport = ViewportsOn(document, sheet)
            .FirstOrDefault(viewport => viewport.ViewId == primary.Id)
            ?? throw new InvalidOperationException("Không tìm thấy viewport mặt cắt dọc trên sheet.");
        var outline = originalViewport.GetBoxOutline();
        var width = outline.MaximumPoint.X - outline.MinimumPoint.X;
        if (width <= availableWidth + 1.0 / 304.8)
            return new LongitudinalSplitResult([primaryViewId], false, null);

        LongitudinalGridSplit split;
        try
        {
            split = _locator.Find(document, beam);
        }
        catch (InvalidOperationException)
        {
            // Sheet chật nhưng không có lưới phù hợp: vẫn giữ view nguyên để người dùng xếp tay.
            return new LongitudinalSplitResult([primaryViewId], false, null);
        }
        var crop = primary.CropBox;
        var splitCoordinate = crop.Transform.Inverse.OfPoint(split.Point).X;
        var ranges = LongitudinalDependentCropPlanner.Plan(
            crop.Min.X, crop.Max.X, splitCoordinate,
            (CutExtensionFeet + CropPastBreakFeet) * 2);
        var center = originalViewport.GetBoxCenter();
        var viewportTypeId = originalViewport.GetTypeId();

        using var transaction = new Transaction(document, "Chat AI - chia dependent view dầm");
        transaction.Start();
        try
        {
            if (!primary.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
                throw new InvalidOperationException("View dọc này không cho phép Duplicate As Dependent.");
            document.Delete(originalViewport.Id);
            var first = Duplicate(primary, "ĐOẠN 1/2", ranges.First);
            var second = Duplicate(primary, "ĐOẠN 2/2", ranges.Second);
            document.Regenerate();
            var breakPositions = LongitudinalBreakLinePlanner.Plan(
                splitCoordinate, ranges.First, ranges.Second,
                CutExtensionFeet, CropPastBreakFeet);
            var firstBreakId = _breakLines.Place(
                document, first, beam, breakLineTypeName, breakPositions.First,
                reverseDirection: false);
            var secondBreakId = _breakLines.Place(
                document, second, beam, breakLineTypeName, breakPositions.Second,
                reverseDirection: true);
            document.Regenerate();
            HideRelatedBreakLines(document, primary, first, second, firstBreakId, secondBreakId);
            var firstViewport = Viewport.Create(document, sheet.Id, first.Id, center);
            var secondViewport = Viewport.Create(document, sheet.Id, second.Id, center);
            if (viewportTypeId != ElementId.InvalidElementId)
            {
                firstViewport.ChangeTypeId(viewportTypeId);
                secondViewport.ChangeTypeId(viewportTypeId);
            }
            document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit không commit được transaction chia dependent view.");
            return new LongitudinalSplitResult(
                [first.Id.ToValue(), second.Id.ToValue()], true, split.GridName);
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
            throw;
        }
    }

    private static void HideRelatedBreakLines(
        Document document,
        ViewSection primary,
        ViewSection first,
        ViewSection second,
        ElementId firstBreakId,
        ElementId secondBreakId)
    {
        HideIfPossible(document, first, secondBreakId);
        HideIfPossible(document, second, firstBreakId);
        HideIfPossible(document, primary, firstBreakId);
        HideIfPossible(document, primary, secondBreakId);
    }

    private static void HideIfPossible(Document document, View view, ElementId elementId)
    {
        var element = document.GetElement(elementId);
        if (element != null && element.CanBeHidden(view))
            view.HideElements([elementId]);
    }

    private static ViewSection Duplicate(ViewSection primary, string suffix, LongitudinalCropRange range)
    {
        var document = primary.Document;
        var duplicateId = primary.Duplicate(ViewDuplicateOption.AsDependent);
        var view = (ViewSection)document.GetElement(duplicateId);
        view.Name = UniqueName(document, $"{primary.Name} - {suffix}");
        var crop = view.CropBox;
        crop.Min = new XYZ(range.Minimum, crop.Min.Y, crop.Min.Z);
        crop.Max = new XYZ(range.Maximum, crop.Max.Y, crop.Max.Z);
        view.CropBox = crop;
        ConfigureAnnotationCrop(view);
        return view;
    }

    private static void ConfigureAnnotationCrop(View view)
    {
        try { view.CropBoxActive = true; }
        catch when (view.CropBoxActive) { }
        try { view.CropBoxVisible = false; }
        catch { }

        var manager = view.GetCropRegionShapeManager();
        if (!manager.CanHaveAnnotationCrop) return;
        var active = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
        if (active == null)
            throw new InvalidOperationException("View dependent không hỗ trợ bật Annotation Crop.");
        if (active.AsInteger() != 1)
        {
            if (active.IsReadOnly)
                throw new InvalidOperationException(
                    "View Template đang khóa Annotation Crop ở trạng thái tắt.");
            active.Set(1);
        }

        const double onePaperInchFeet = 1.0 / 12.0;
        manager.LeftAnnotationCropOffset = onePaperInchFeet;
        manager.RightAnnotationCropOffset = onePaperInchFeet;
        manager.TopAnnotationCropOffset = onePaperInchFeet;
        manager.BottomAnnotationCropOffset = onePaperInchFeet;
    }

    private static string UniqueName(Document document, string desired)
    {
        var existing = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
            .Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(desired)) return desired;
        for (var index = 2; ; index++)
        {
            var candidate = $"{desired} ({index})";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static IReadOnlyList<Viewport> ViewportsOn(Document document, ViewSheet sheet) =>
        new FilteredElementCollector(document, sheet.Id)
            .OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
}
