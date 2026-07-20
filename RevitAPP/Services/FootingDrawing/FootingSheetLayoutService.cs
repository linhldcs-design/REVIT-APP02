using Autodesk.Revit.DB;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.FootingDrawing;

/// <summary>Sắp đúng các cặp viewport được chỉ định; không suy luận từ tên view hoặc Mark.</summary>
public sealed class FootingSheetLayoutService
{
    public void Arrange(Document document, long sheetId, IReadOnlyList<FootingViewportPair> pairs,
        double titleBlockReserveRatio, double gapMm)
    {
        if (document.GetElement(RevitAPP.Chat.Tools.ChatElementIdCompat.Create(sheetId)) is not ViewSheet sheet)
            throw new ArgumentException($"Không tìm thấy sheet {sheetId}.");
        if (pairs.Count == 0) throw new ArgumentException("Danh sách cặp viewport rỗng.");

        var allViewportIds = pairs.SelectMany(pair => new[] { pair.PlanViewportId, pair.SectionViewportId }).ToList();
        if (allViewportIds.Any(id => id <= 0))
            throw new ArgumentException("ViewportId phải lớn hơn 0.");
        if (allViewportIds.Distinct().Count() != allViewportIds.Count)
            throw new ArgumentException("Mỗi viewport chỉ được dùng đúng một lần và plan/section không được trùng nhau.");
        if (pairs.Any(pair => pair.PlanFootingId <= 0 || pair.PlanFootingId != pair.SectionFootingId))
            throw new ArgumentException("Mặt bằng và mặt cắt trong mỗi cặp phải có cùng footingId nguồn.");

        var resolved = pairs.Select(pair => ResolvePair(document, sheet, pair)).ToList();
        var gapFeet = Math.Max(0, gapMm) / 304.8;

        using var transaction = new Transaction(document, "Sắp xếp bản vẽ móng trên sheet");
        transaction.Start();
        try
        {
            foreach (var pair in resolved)
            {
                AlignTitleBelowView(pair.Plan);
                AlignTitleBelowView(pair.Section);
            }
            document.Regenerate();

            var footprints = resolved.Select(pair => new PairFootprints(
                MeasureFootprint(pair.Plan), MeasureFootprint(pair.Section))).ToList();
            var sizes = footprints.Select(value => new FootingViewportPairSize(
                value.Plan.Width, value.Plan.Height, value.Section.Width, value.Section.Height)).ToList();
            var slots = FootingSheetLayoutCalculator.Pack(
                sheet.Outline.Min.U, sheet.Outline.Min.V, sheet.Outline.Max.U, sheet.Outline.Max.V,
                sizes, titleBlockReserveRatio, gapFeet, gapFeet);
            EnsureNoExistingContentCollision(document, sheet, resolved, footprints, slots);

            for (var index = 0; index < resolved.Count; index++)
            {
                resolved[index].Plan.SetBoxCenter(new XYZ(
                    slots[index].X - footprints[index].Plan.OffsetX,
                    slots[index].PlanY - footprints[index].Plan.OffsetY, 0));
                resolved[index].Section.SetBoxCenter(new XYZ(
                    slots[index].X - footprints[index].Section.OffsetX,
                    slots[index].SectionY - footprints[index].Section.OffsetY, 0));
            }
            transaction.Commit();
        }
        catch
        {
            transaction.RollBack();
            throw;
        }
    }

    private static void AlignTitleBelowView(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        var width = box.MaximumPoint.X - box.MinimumPoint.X;
        viewport.LabelOffset = new XYZ(0, -8.0 / 304.8, 0);
        viewport.LabelLineLength = Math.Max(width, 10.0 / 304.8);
    }

    private static void EnsureNoExistingContentCollision(Document document, ViewSheet sheet,
        IReadOnlyList<ResolvedPair> pairs, IReadOnlyList<PairFootprints> footprints,
        IReadOnlyList<FootingSheetLayoutSlot> slots)
    {
        var movingIds = pairs.SelectMany(pair => new[] { pair.Plan.Id, pair.Section.Id }).ToHashSet();
        var occupied = new FilteredElementCollector(document)
            .OwnedByView(sheet.Id)
            .WhereElementIsNotElementType()
            .Where(element => !movingIds.Contains(element.Id) && element.Id != sheet.Id &&
                              element.Category?.Id != new ElementId(BuiltInCategory.OST_TitleBlocks))
            .Select(element => element.get_BoundingBox(sheet))
            .Where(box => box != null)
            .Select(box => new SheetRect(box!.Min.X, box.Min.Y, box.Max.X, box.Max.Y))
            .ToList();

        for (var index = 0; index < slots.Count; index++)
        {
            var targets = new[]
            {
                SheetRect.Centered(slots[index].X, slots[index].PlanY,
                    footprints[index].Plan.Width, footprints[index].Plan.Height),
                SheetRect.Centered(slots[index].X, slots[index].SectionY,
                    footprints[index].Section.Width, footprints[index].Section.Height)
            };
            if (targets.Any(target => occupied.Any(target.Intersects)))
                throw new InvalidOperationException(
                    "Vị trí sắp xếp sẽ chồng lên nội dung đã có trên sheet (viewport, schedule hoặc ghi chú). Hãy dùng sheet trống hoặc di chuyển nội dung hiện có.");
        }
    }

    private static ResolvedPair ResolvePair(Document document, ViewSheet sheet, FootingViewportPair pair)
    {
        var plan = document.GetElement(RevitAPP.Chat.Tools.ChatElementIdCompat.Create(pair.PlanViewportId)) as Viewport
                   ?? throw new ArgumentException($"Không tìm thấy plan viewport {pair.PlanViewportId}.");
        var section = document.GetElement(RevitAPP.Chat.Tools.ChatElementIdCompat.Create(pair.SectionViewportId)) as Viewport
                      ?? throw new ArgumentException($"Không tìm thấy section viewport {pair.SectionViewportId}.");
        if (plan.SheetId != sheet.Id || section.SheetId != sheet.Id)
            throw new InvalidOperationException($"Cặp '{pair.Mark}' không nằm chung trên sheet {sheet.SheetNumber}.");
        var planView = document.GetElement(plan.ViewId) as View;
        var sectionView = document.GetElement(section.ViewId) as View;
        if (planView?.ViewType != ViewType.Detail)
            throw new InvalidOperationException($"Viewport {pair.PlanViewportId} không phải mặt bằng móng dạng Detail.");
        // Revit có thể phân loại view mặt cắt tạo từ Section Type dạng Detail là ViewType.Detail.
        // Cặp viewport ở luồng tổng hợp đã được giữ trực tiếp từ hai orchestrator nên không suy đoán theo tên.
        if (sectionView?.ViewType is not (ViewType.Section or ViewType.Detail))
            throw new InvalidOperationException($"Viewport {pair.SectionViewportId} không phải mặt cắt móng.");
        return new ResolvedPair(plan, section);
    }

    private static ViewportFootprint MeasureFootprint(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        var label = viewport.GetLabelOutline();
        var minX = Math.Min(box.MinimumPoint.X, label.MinimumPoint.X);
        var minY = Math.Min(box.MinimumPoint.Y, label.MinimumPoint.Y);
        var maxX = Math.Max(box.MaximumPoint.X, label.MaximumPoint.X);
        var maxY = Math.Max(box.MaximumPoint.Y, label.MaximumPoint.Y);
        var boxCenter = viewport.GetBoxCenter();
        return new ViewportFootprint(maxX - minX, maxY - minY,
            (minX + maxX) / 2 - boxCenter.X,
            (minY + maxY) / 2 - boxCenter.Y);
    }
    private sealed record ResolvedPair(Viewport Plan, Viewport Section);
    private sealed record PairFootprints(ViewportFootprint Plan, ViewportFootprint Section);
    private sealed record ViewportFootprint(double Width, double Height, double OffsetX, double OffsetY);
    private sealed record SheetRect(double MinX, double MinY, double MaxX, double MaxY)
    {
        private const double Tolerance = 1.0 / 304.8;
        public bool Intersects(SheetRect other) =>
            MinX < other.MaxX - Tolerance && MaxX > other.MinX + Tolerance &&
            MinY < other.MaxY - Tolerance && MaxY > other.MinY + Tolerance;

        public static SheetRect Centered(double x, double y, double width, double height) =>
            new(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
    }
}

public sealed record FootingViewportPair(
    string Mark, long PlanFootingId, long SectionFootingId, long PlanViewportId, long SectionViewportId);
