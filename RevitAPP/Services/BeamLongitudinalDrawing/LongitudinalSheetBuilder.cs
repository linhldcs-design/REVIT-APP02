using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalSheetBuilder
{
    private readonly ProjectResourceProvider _resources = new();

    public void CreateAndPlace(Document document, IReadOnlyList<ViewSection> views,
        LongitudinalDrawingSetting setting, BeamLongitudinalDrawingResult result)
    {
        var warnings = new List<string>();
        var longitudinalViewportTypeId =
            _resources.ResolveViewportType(document, setting.ViewportTypeName, warnings);
        var crossViewportTypeId = _resources.ResolveViewportType(document,
            setting.CrossViewportTypeName ?? setting.ViewportTypeName, warnings);
        var sheet = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(candidate => !candidate.IsPlaceholder &&
                string.Equals(candidate.SheetNumber, setting.SheetNumber, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Không tìm thấy Sheet có sẵn '{setting.SheetNumber}'. Hãy mở lại form và chọn Sheet trong project.");
        result.SheetId = sheet.Id.ToValue();

        document.Regenerate();
        var titleBlock = new FilteredElementCollector(document, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
        var box = titleBlock?.get_BoundingBox(sheet);
        var left = box?.Min.X ?? 0.05;
        var right = box?.Max.X ?? 1.05;
        var bottom = box?.Min.Y ?? 0.05;
        var top = box?.Max.Y ?? 0.80;
        var width = right - left;
        var height = top - bottom;
        var placed = new List<Viewport>();

        for (var index = 0; index < views.Count; index++)
        {
            var point = index == 0
                ? new XYZ(left + width * 0.50, bottom + height * 0.70, 0)
                : new XYZ(left + width * 0.50, bottom + height * 0.22, 0);
            if (!Viewport.CanAddViewToSheet(document, sheet.Id, views[index].Id))
            {
                warnings.Add($"Không thể đặt view '{views[index].Name}' lên Sheet '{sheet.SheetNumber}'.");
                continue;
            }
            var viewport = Viewport.Create(document, sheet.Id, views[index].Id, point);
            var viewportTypeId = index == 0 ? longitudinalViewportTypeId : crossViewportTypeId;
            if (viewportTypeId is { } id && id != ElementId.InvalidElementId)
                try { viewport.ChangeTypeId(id); }
                catch (Exception exception) { warnings.Add(exception.Message); }
            placed.Add(viewport);
        }

        document.Regenerate();
        if (placed.Count > 0)
        {
            placed[0].SetBoxCenter(new XYZ(left + width * 0.50, bottom + height * 0.70, 0));
            document.Regenerate();
            PullTitleUnderView(placed[0], centerTitle: true);
            document.Regenerate();
        }

        var crossViewports = placed.Skip(1).ToList();
        if (crossViewports.Count > 0)
        {
            const double rowGapBelowLongTitleFeet = 15.0 / 304.8;
            var longBottom = BottomOfViewportAndTitle(placed[0]);
            var maxCrossHeight = crossViewports.Max(viewport =>
            {
                var outline = viewport.GetBoxOutline();
                return outline.MaximumPoint.Y - outline.MinimumPoint.Y;
            });
            var crossCenterY = longBottom - rowGapBelowLongTitleFeet - maxCrossHeight * 0.5;
            PackCrossSectionsInOneRow(crossViewports,
                left + width * 0.05, right - width * 0.05, crossCenterY);
            document.Regenerate();
            foreach (var viewport in crossViewports) PullTitleUnderView(viewport, centerTitle: true);
        }

        foreach (var warning in warnings)
            result.AddWarning(new BeamLongitudinalDrawingWarning("SHEET", warning));
    }

    private static void PackCrossSectionsInOneRow(IReadOnlyList<Viewport> viewports,
        double availableLeft, double availableRight, double centerY)
    {
        if (viewports.Count == 0) return;
        const double preferredGapFeet = 10.0 / 304.8;
        var widths = viewports.Select(viewport =>
        {
            var outline = viewport.GetBoxOutline();
            return Math.Max(outline.MaximumPoint.X - outline.MinimumPoint.X, 1.0 / 304.8);
        }).ToList();
        var availableWidth = availableRight - availableLeft;
        var gap = viewports.Count == 1
            ? 0
            : Math.Max(2.0 / 304.8,
                Math.Min(preferredGapFeet, (availableWidth - widths.Sum()) / (viewports.Count - 1)));
        var packedWidth = widths.Sum() + gap * (viewports.Count - 1);
        var cursor = availableLeft + Math.Max(0, (availableWidth - packedWidth) * 0.5);
        for (var index = 0; index < viewports.Count; index++)
        {
            viewports[index].SetBoxCenter(new XYZ(cursor + widths[index] * 0.5, centerY, 0));
            cursor += widths[index] + gap;
        }
    }

    private const double TitleGapBelowViewFeet = 4.0 / 304.8;

    private static double BottomOfViewportAndTitle(Viewport viewport)
    {
        var bottom = viewport.GetBoxOutline().MinimumPoint.Y;
        try { bottom = Math.Min(bottom, viewport.GetLabelOutline().MinimumPoint.Y); }
        catch { }
        return bottom;
    }

    private static void PullTitleUnderView(Viewport viewport, bool centerTitle)
    {
        try
        {
            var box = viewport.GetBoxOutline();
            var label = viewport.GetLabelOutline();
            var current = viewport.LabelOffset;
            var deltaX = centerTitle
                ? (box.MinimumPoint.X + box.MaximumPoint.X - label.MinimumPoint.X - label.MaximumPoint.X) * 0.5
                : box.MinimumPoint.X - label.MinimumPoint.X;
            viewport.LabelOffset = new XYZ(
                current.X + deltaX,
                current.Y + box.MinimumPoint.Y - TitleGapBelowViewFeet - label.MaximumPoint.Y, 0);
        }
        catch
        {
            // Viewport types without a visible title do not expose a usable label outline.
        }
    }
}
