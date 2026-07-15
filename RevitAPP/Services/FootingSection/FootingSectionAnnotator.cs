using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.FootingSection;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Đặt annotation trong T2 cho mặt cắt móng: tag thép (đế / đai cổ / thép chờ), dimension chuỗi,
///     break line cắt cột. Reuse <see cref="RebarTagPlacer"/> đã verify. Best-effort: thiếu → warn, không crash.
/// </summary>
public sealed class FootingSectionAnnotator : IFootingSectionAnnotator, IFootingSectionPostCommitAnnotator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly RebarTagPlacer _tagPlacer = new();
    private readonly FootingDimensionPlacer _dimensionPlacer = new();
    private readonly FootingBreakLinePlacer _breakLinePlacer = new();

    public void Annotate(Document doc, FootingViewContext context, FootingSectionSetting setting,
        FootingSectionResult result)
    {
        var view = context.View;

        if (setting.Flags.TagEnabled)
            TagRebars(doc, view, context, setting, result);

        if (setting.Flags.DimEnabled)
        {
            var dimTypeId = _resources.ResolveDimType(doc, setting.Dim.DimTypeName, result.Warnings);
            _dimensionPlacer.Place(doc, view, context.Footing, context.Geometry, dimTypeId,
                setting.Dim.OffsetMm, result.Warnings);
        }

        if (setting.Flags.BreakLineEnabled)
        {
            var breakLineId = _resources.ResolveBreakLineSymbol(doc, setting.BreakLine.FamilyName, result.Warnings);
            _breakLinePlacer.Place(doc, view, context.Geometry, breakLineId, result.Warnings);
        }
    }

    public void FinalizeAfterCommit(Document doc, FootingViewContext context, FootingSectionResult result) =>
        _dimensionPlacer.CreateContinuousChain(doc, context.View, result.Warnings);

    public void CleanupAfterFinalize(Document doc, FootingViewContext context, FootingSectionResult result) =>
        _dimensionPlacer.CleanupTemporaryPairs(doc, context.View, result.Warnings);

    /// <summary>Gom thép do móng host (đế + đai cổ + thép chờ) hiển thị trong section, tag qua RebarTagPlacer.</summary>
    private void TagRebars(Document doc, View view, FootingViewContext context, FootingSectionSetting setting,
        FootingSectionResult result)
    {
        var allRebars = CollectFootingRebars(doc, context);
        if (allRebars.Count == 0)
        {
            result.Warnings.Add("Không tìm thấy thép nào của móng trong mặt cắt — bỏ qua tag.");
            return;
        }

        // Chọn tag type: dùng FootingBarTag làm mặc định cho mọi thanh (đế/đai/chờ dùng chung nếu không tách).
        var footingTagId = _resources.ResolveRebarTagType(doc, setting.Tags.FootingBarTagName, result.Warnings);
        var stirrupTagId = setting.Tags.TagStirrup
            ? _resources.ResolveRebarTagType(doc, setting.Tags.StirrupTagName, result.Warnings)
            : footingTagId;
        var starterTagId = setting.Tags.TagStarter
            ? _resources.ResolveRebarTagType(doc, setting.Tags.StarterTagName, result.Warnings)
            : footingTagId;

        if (setting.Tags.TagFooting)
        {
            var footingGroups = SelectFootingDirectionGroups(allRebars, context.Geometry, view);
            var footingHeads = BuildFootingTagHeads(view, context.Geometry, footingGroups.Count);
            _tagPlacer.TagRebarGroups(doc, view, footingGroups, footingTagId, result.Warnings, footingHeads);
        }

        var otherRebars = SelectRebarsToTag(allRebars, context.Geometry,
            setting with { Tags = setting.Tags with { TagFooting = false } });
        var otherTagTypeIds = otherRebars
            .Select(r => ClassifyTagType(r, context.Geometry, footingTagId, stirrupTagId, starterTagId))
            .ToList();
        if (otherRebars.Count > 0)
            _tagPlacer.TagRebars(doc, view, otherRebars, otherTagTypeIds, result.Warnings);
    }

    private static IReadOnlyList<IReadOnlyList<Rebar>> SelectFootingDirectionGroups(
        IReadOnlyList<Rebar> rebars, FootingSectionGeometry geometry, View view)
    {
        var span = Math.Max(geometry.TopZFeet - geometry.BottomZFeet, 1e-6);
        var footingRebars = rebars
            .Select(rebar => new
            {
                Rebar = rebar,
                Z = (rebar.get_BoundingBox(null)?.Min.Z + rebar.get_BoundingBox(null)?.Max.Z) * 0.5 ?? 0,
                IsDot = IsPerpendicularToSection(rebar, view)
            })
            .Where(item => (item.Z - geometry.BottomZFeet) / span < 0.33)
            .ToList();

        var result = new List<IReadOnlyList<Rebar>>();
        // Phương ngang trong section: một reference → một nhánh leader.
        var horizontal = footingRebars.Where(item => !item.IsDot).OrderByDescending(item => item.Z).FirstOrDefault();
        if (horizontal != null) result.Add(new[] { horizontal.Rebar });

        // Phương vuông góc section hiển thị dạng chấm: reference lớp trên + dưới → hai nhánh.
        var dots = footingRebars
            .Where(item => item.IsDot)
            .GroupBy(item => Math.Round(item.Z * 304.8 / 30.0))
            .Select(group => group.OrderByDescending(item => item.Z).First())
            .OrderByDescending(item => item.Z)
            .Take(2)
            .Select(item => item.Rebar)
            .ToList();
        // Lặp lại cùng Rebar Set để báo cho placer lấy subelement đầu/cuối của bộ chấm.
        if (dots.Count > 0) result.Add(new[] { dots[0], dots[0] });
        return result;
    }

    private static bool IsPerpendicularToSection(Rebar rebar, View view)
    {
        try
        {
            var accessor = rebar.GetShapeDrivenAccessor();
            var distributionPath = accessor.GetDistributionPath();
            if (distributionPath is Line distributionLine)
            {
                var distributionDirection =
                    (distributionLine.GetEndPoint(1) - distributionLine.GetEndPoint(0)).Normalize();
                // Thanh vuông góc mặt cắt hiển thị thành chấm và được phân bố theo RightDirection.
                return Math.Abs(distributionDirection.DotProduct(view.RightDirection)) >
                       Math.Abs(distributionDirection.DotProduct(view.ViewDirection));
            }

            var longest = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .OfType<Line>()
                .OrderByDescending(line => line.Length)
                .FirstOrDefault();
            if (longest == null) return false;
            var direction = (longest.GetEndPoint(1) - longest.GetEndPoint(0)).Normalize();
            return Math.Abs(direction.DotProduct(view.ViewDirection)) >
                   Math.Abs(direction.DotProduct(view.RightDirection));
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<Rebar> SelectRebarsToTag(IReadOnlyList<Rebar> rebars,
        FootingSectionGeometry geometry, FootingSectionSetting setting)
    {
        var selected = new List<Rebar>();
        var span = Math.Max(geometry.TopZFeet - geometry.BottomZFeet, 1e-6);
        double Relative(Rebar rebar)
        {
            var box = rebar.get_BoundingBox(null);
            return box == null ? 0 : ((box.Min.Z + box.Max.Z) * 0.5 - geometry.BottomZFeet) / span;
        }

        if (setting.Tags.TagFooting)
        {
            // Mặt cắt chỉ cần một tag đại diện cho mỗi lớp thép đế: trên trước, dưới sau.
            var footingLayers = rebars
                .Where(rebar => Relative(rebar) < 0.33)
                .Select(rebar => new
                {
                    Rebar = rebar,
                    Z = (rebar.get_BoundingBox(null)?.Min.Z + rebar.get_BoundingBox(null)?.Max.Z) * 0.5 ?? 0
                })
                .GroupBy(item => Math.Round(item.Z * 304.8 / 10.0)) // gom cùng lớp, dung sai xấp xỉ 10mm
                .Select(group => group.First())
                .OrderByDescending(item => item.Z)
                .Take(2)
                .Select(item => item.Rebar);
            selected.AddRange(footingLayers);
        }

        if (setting.Tags.TagStirrup)
            selected.AddRange(rebars.Where(rebar => Relative(rebar) is >= 0.33 and < 0.66));
        if (setting.Tags.TagStarter)
            selected.AddRange(rebars.Where(rebar => Relative(rebar) >= 0.66));
        return selected.DistinctBy(rebar => rebar.Id.ToValue()).ToList();
    }

    private static IReadOnlyList<(double X, double Y)> BuildFootingTagHeads(View view,
        FootingSectionGeometry geometry, int count)
    {
        if (!view.CropBoxActive || view.CropBox == null || count == 0) return Array.Empty<(double, double)>();
        var inverse = view.CropBox.Transform.Inverse;
        var centerLocal = inverse.OfPoint(new XYZ(geometry.Center.X, geometry.Center.Y, geometry.BottomZFeet));
        var headX = centerLocal.X + geometry.WidthFeet * 0.5 + 600.0 / 304.8;
        var firstY = centerLocal.Y + 700.0 / 304.8;
        var stepY = 250.0 / 304.8;
        return Enumerable.Range(0, count)
            .Select(index => (headX, firstY - stepY * index))
            .ToList();
    }

    /// <summary>Phân loại tag theo vùng Z của thanh thép: gần đáy = đế, giữa = đai cổ, trên cùng = thép chờ.</summary>
    private static ElementId? ClassifyTagType(Rebar rebar, FootingSectionGeometry geometry,
        ElementId? footingTag, ElementId? stirrupTag, ElementId? starterTag)
    {
        var box = rebar.get_BoundingBox(null);
        if (box == null) return footingTag;

        var span = Math.Max(geometry.TopZFeet - geometry.BottomZFeet, 1e-6);
        var relative = ((box.Min.Z + box.Max.Z) * 0.5 - geometry.BottomZFeet) / span;

        if (relative < 0.33) return footingTag;   // đế móng
        if (relative < 0.66) return stirrupTag;    // đai cổ
        return starterTag;                          // thép chờ cột
    }

    private static IReadOnlyList<Rebar> CollectFootingRebars(Document doc, FootingViewContext context)
    {
        var hostId = context.Footing.Id;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar))
            .WhereElementIsNotElementType()
            .Cast<Rebar>()
            .Where(r => r.GetHostId() == hostId)
            .ToList();
    }

}
