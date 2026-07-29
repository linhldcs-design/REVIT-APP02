using Autodesk.Revit.DB;
using RevitAPP.Core.Chat.BeamLongitudinalDrawing;
using RevitAPP.Helpers;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

internal sealed record LongitudinalDrawingArea(double Left, double Right, double Bottom, double Top)
{
    public double Width => Right - Left;
}

internal sealed record LongitudinalChatPlacement(
    long BeamId,
    IReadOnlyList<long> LongitudinalViewIds,
    IReadOnlyList<long> CrossSectionViewIds);

internal sealed class LongitudinalBatchSheetLayoutService
{
    private const double MillimetersToFeet = 1.0 / 304.8;
    private const double PreferredVerticalGap = 5.0 * MillimetersToFeet;
    private const double MinimumVerticalGap = 1.0 * MillimetersToFeet;
    private const double HorizontalGap = 2.0 * MillimetersToFeet;
    private const double TitleGap = 2.0 * MillimetersToFeet;
    private const double PreferredSheetMargin = 2.0 * MillimetersToFeet;
    private const double MinimumSheetMargin = 0.5 * MillimetersToFeet;

    public LongitudinalDrawingArea ResolveDrawingArea(Document document, ViewSheet sheet)
        => ResolveDrawingArea(document, sheet, PreferredSheetMargin);

    private static LongitudinalDrawingArea ResolveDrawingArea(
        Document document, ViewSheet sheet, double verticalMargin)
    {
        var titleBlock = new FilteredElementCollector(document, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .FirstElement()
            ?? throw new InvalidOperationException($"Sheet '{sheet.SheetNumber}' không có title block.");
        var box = titleBlock.get_BoundingBox(sheet)
                  ?? throw new InvalidOperationException($"Không đọc được kích thước sheet '{sheet.SheetNumber}'.");
        var width = box.Max.X - box.Min.X;
        var leftInset = ReadLength(titleBlock, "RevitAPP Drawing Left Inset");
        var rightInset = ReadLength(titleBlock, "RevitAPP Drawing Right Inset");
        var left = box.Min.X + (leftInset ?? PreferredSheetMargin);
        var right = box.Max.X - (rightInset ?? Math.Max(PreferredSheetMargin, width * 0.15));
        var bottom = box.Min.Y + verticalMargin;
        var top = box.Max.Y - verticalMargin;
        if (right <= left || top <= bottom)
            throw new InvalidOperationException($"Vùng vẽ của sheet '{sheet.SheetNumber}' không hợp lệ.");
        return new LongitudinalDrawingArea(left, right, bottom, top);
    }

    public void Arrange(Document document, ViewSheet sheet,
        IReadOnlyList<LongitudinalChatPlacement> groups)
    {
        if (groups.Count == 0) return;
        using var transaction = new Transaction(document, "Chat AI - xếp mặt cắt dọc dầm");
        transaction.Start();
        try
        {
            document.Regenerate();
            var area = ResolveDrawingArea(document, sheet);
            var byViewId = new FilteredElementCollector(document, sheet.Id)
                .OfClass(typeof(Viewport)).Cast<Viewport>()
                .ToDictionary(viewport => viewport.ViewId.ToValue());
            var usedIds = groups.SelectMany(group =>
                    group.LongitudinalViewIds.Concat(group.CrossSectionViewIds))
                .Distinct().ToList();
            if (usedIds.Any(id => !byViewId.ContainsKey(id)))
                throw new InvalidOperationException("Thiếu viewport vừa tạo trên sheet.");

        foreach (var id in usedIds) CenterTitleBelowView(byViewId[id]);
        document.Regenerate();

        var rows = new List<LayoutRow>();
        foreach (var group in groups)
        {
            foreach (var viewId in group.LongitudinalViewIds)
            {
                var viewport = byViewId[viewId];
                var bounds = Bounds(viewport);
                rows.Add(new LayoutRow([viewport], [bounds]));
            }

            var cross = group.CrossSectionViewIds.Select(id => byViewId[id]).ToList();
            foreach (var row in BuildRows(cross, area.Width))
                rows.Add(new LayoutRow(row, row.Select(Bounds).ToList()));
        }

        var availableHeight = area.Top - area.Bottom;
        var contentHeight = rows.Sum(row => row.Height);
        var gapCount = Math.Max(0, rows.Count - 1);
        var minimumRequired = contentHeight + MinimumVerticalGap * gapCount;
        if (minimumRequired > availableHeight)
        {
            var titleBlockArea = ResolveDrawingArea(document, sheet, 0);
            try
            {
                var verticalMargin = LongitudinalVerticalMarginPlanner.Select(
                    titleBlockArea.Top - titleBlockArea.Bottom,
                    minimumRequired,
                    PreferredSheetMargin,
                    MinimumSheetMargin);
                area = ResolveDrawingArea(document, sheet, verticalMargin);
                availableHeight = area.Top - area.Bottom;
            }
            catch (InvalidOperationException)
            {
                area = ResolveDrawingArea(document, sheet, MinimumSheetMargin);
                availableHeight = area.Top - area.Bottom;
            }
        }
        var verticalGap = gapCount == 0
            ? 0
            : minimumRequired <= availableHeight
                ? Math.Min(PreferredVerticalGap, (availableHeight - contentHeight) / gapCount)
                : MinimumVerticalGap;

        var cursorTop = area.Top;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowWidth = row.Bounds.Sum(value => value.Width) +
                           HorizontalGap * Math.Max(0, row.Viewports.Count - 1);
            var x = area.Left + Math.Max(0, (area.Width - rowWidth) * 0.5);
            for (var index = 0; index < row.Viewports.Count; index++)
            {
                MoveUnionTo(row.Viewports[index], x - row.Bounds[index].MinimumX,
                    cursorTop - row.Bounds[index].MaximumY);
                x += row.Bounds[index].Width + HorizontalGap;
            }
            cursorTop -= row.Height;
            if (rowIndex < rows.Count - 1) cursorTop -= verticalGap;
        }

        document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit không commit được transaction xếp sheet.");
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
            throw;
        }
    }

    private static IReadOnlyList<IReadOnlyList<Viewport>> BuildRows(
        IReadOnlyList<Viewport> viewports, double availableWidth)
    {
        if (viewports.Count == 0) return [];
        var widths = viewports.Select(viewport => Bounds(viewport).Width).ToList();
        if (viewports.Count == 1) return [viewports];

        double RowWidth(int start, int count) =>
            widths.Skip(start).Take(count).Sum() + HorizontalGap * Math.Max(0, count - 1);

        if (RowWidth(0, viewports.Count) <= availableWidth)
            return [viewports];

        for (var splitIndex = viewports.Count - 1; splitIndex >= 1; splitIndex--)
        {
            if (RowWidth(0, splitIndex) > availableWidth ||
                RowWidth(splitIndex, viewports.Count - splitIndex) > availableWidth)
                continue;
            return
            [
                viewports.Take(splitIndex).ToList(),
                viewports.Skip(splitIndex).ToList()
            ];
        }

        var bestEffortSplit = Enumerable.Range(1, viewports.Count - 1)
            .OrderBy(splitIndex => Math.Max(
                RowWidth(0, splitIndex),
                RowWidth(splitIndex, viewports.Count - splitIndex)))
            .First();
        return
        [
            viewports.Take(bestEffortSplit).ToList(),
            viewports.Skip(bestEffortSplit).ToList()
        ];
    }

    private sealed record ViewportBounds(
        double MinimumX, double MaximumX, double MinimumY, double MaximumY)
    {
        public double Width => Math.Max(MaximumX - MinimumX, MillimetersToFeet);
        public double Height => Math.Max(MaximumY - MinimumY, MillimetersToFeet);
        public double CenterX => (MinimumX + MaximumX) * 0.5;
    }

    private sealed record LayoutRow(
        IReadOnlyList<Viewport> Viewports,
        IReadOnlyList<ViewportBounds> Bounds)
    {
        public double Height => Bounds.Max(value => value.Height);
    }

    private static ViewportBounds Bounds(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        var minX = box.MinimumPoint.X;
        var maxX = box.MaximumPoint.X;
        var minY = box.MinimumPoint.Y;
        var maxY = box.MaximumPoint.Y;
        try
        {
            var label = viewport.GetLabelOutline();
            minX = Math.Min(minX, label.MinimumPoint.X);
            maxX = Math.Max(maxX, label.MaximumPoint.X);
            minY = Math.Min(minY, label.MinimumPoint.Y);
            maxY = Math.Max(maxY, label.MaximumPoint.Y);
        }
        catch
        {
            // Viewport type without visible title.
        }
        return new ViewportBounds(minX, maxX, minY, maxY);
    }

    private static void MoveUnionTo(Viewport viewport, double deltaX, double deltaY)
    {
        var center = viewport.GetBoxCenter();
        viewport.SetBoxCenter(new XYZ(center.X + deltaX, center.Y + deltaY, 0));
    }

    private static double Bottom(Viewport viewport)
    {
        var bottom = viewport.GetBoxOutline().MinimumPoint.Y;
        try { bottom = Math.Min(bottom, viewport.GetLabelOutline().MinimumPoint.Y); }
        catch { }
        return bottom;
    }

    private static void CenterTitleBelowView(Viewport viewport)
    {
        try
        {
            var box = viewport.GetBoxOutline();
            var label = viewport.GetLabelOutline();
            var current = viewport.LabelOffset;
            var target = LongitudinalViewportTitleOffsetPlanner.CenterBelowView(
                box.MinimumPoint.X, box.MaximumPoint.X, box.MinimumPoint.Y,
                label.MinimumPoint.X, label.MaximumPoint.X, label.MaximumPoint.Y,
                current.X, current.Y, TitleGap);
            viewport.LabelOffset = new XYZ(target.X, target.Y, 0);
        }
        catch
        {
            // Viewport type without visible title.
        }
    }

    private static double? ReadLength(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        return parameter is { StorageType: StorageType.Double } ? parameter.AsDouble() : null;
    }
}
