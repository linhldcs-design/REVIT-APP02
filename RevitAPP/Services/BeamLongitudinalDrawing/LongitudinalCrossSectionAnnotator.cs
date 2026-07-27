using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

/// <summary>
/// Annotation riêng cho các MC ngang sinh bởi lệnh Mặt Cắt Dọc Dầm.
/// Không phụ thuộc BeamAnnotator hoặc BeamDrawingSetting.
/// </summary>
public sealed class LongitudinalCrossSectionAnnotator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly MultiRebarAnnotationPlacer _mra = new();
    private readonly RebarTagPlacer _tags = new();
    private readonly DimensionPlacer _dimensions = new();
    private readonly SpotElevationPlacer _spots = new();
    private readonly BreakLinePlacer _breaks = new();

    public void Annotate(Document document, IReadOnlyList<LongitudinalCrossViewContext> contexts,
        LongitudinalDrawingSetting setting, BeamLongitudinalDrawingResult result)
    {
        var warnings = new List<string>();
        var supportMain = _resources.ResolveMultiRebarAnnotationType(
            document, setting.CrossSupportLongitudinalMraTypeName, warnings);
        var midMain = _resources.ResolveMultiRebarAnnotationType(
            document, setting.CrossMidLongitudinalMraTypeName, warnings);
        var supportL2 = _resources.ResolveMultiRebarAnnotationType(
            document, setting.CrossSupportReinforceL2MraTypeName, warnings);
        var midL2 = _resources.ResolveMultiRebarAnnotationType(
            document, setting.CrossMidReinforceL2MraTypeName, warnings);
        var supportL1 = _resources.ResolveRebarTagType(
            document, setting.CrossSupportReinforceL1TagTypeName, warnings);
        var midL1 = _resources.ResolveRebarTagType(
            document, setting.CrossMidReinforceL1TagTypeName, warnings);
        var supportStirrup = _resources.ResolveRebarTagType(
            document, setting.CrossSupportStirrupTagTypeName, warnings);
        var midStirrup = _resources.ResolveRebarTagType(
            document, setting.CrossMidStirrupTagTypeName, warnings);
        var dimType = _resources.ResolveDimType(document, setting.DimensionTypeName, warnings);
        var spotType = _resources.ResolveSpotType(document, setting.SpotElevationTypeName, warnings);
        var breakType = _resources.ResolveBreakLineSymbol(
            document, setting.CrossBreakLineTypeName ?? setting.DetailComponentTypeName, warnings);

        var byHost = RebarsByHost(document);
        foreach (var context in contexts)
        {
            var hosted = byHost.TryGetValue(context.Beam.Id, out var source) ? source : [];
            var rebars = hosted.Where(rebar => IntersectsView(rebar, context.View)).ToList();
            foreach (var rebar in rebars)
            {
                try { rebar.SetUnobscuredInView(context.View, true); }
                catch { }
            }
            document.Regenerate();

            var inverse = context.View.CropBox.Transform.Inverse;
            var longitudinal = rebars.Where(rebar => !IsStirrup(rebar))
                .OrderByDescending(rebar => LocalY(rebar, inverse)).ToList();
            var stirrups = rebars.Where(IsStirrup).ToList();
            var groups = longitudinal
                .Select(rebar => (Group: (IReadOnlyList<Rebar>)new List<Rebar> { rebar },
                    Y: LocalY(rebar, inverse), Qty: Count(rebar)))
                .OrderByDescending(item => item.Y).ToList();

            var maxQuantity = groups.Count == 0 ? 0 : groups.Max(item => item.Qty);
            var mainCandidates = groups.Where(item => item.Qty == maxQuantity).ToList();
            var mainTop = mainCandidates.FirstOrDefault();
            var mainBottom = mainCandidates.Count > 1 ? mainCandidates[^1] : default;
            var additional = groups.Where(item =>
                    !ReferenceEquals(item.Group, mainTop.Group) &&
                    !ReferenceEquals(item.Group, mainBottom.Group))
                .ToList();

            var ordered = new List<CrossEntity>();
            if (mainTop.Group != null) ordered.Add(new CrossEntity(0, mainTop.Group, null));
            if (context.IsSupport)
            {
                ordered.AddRange(additional.Select(item => new CrossEntity(1, item.Group, null)));
                ordered.AddRange(stirrups.Select(item => new CrossEntity(2, null, item)));
            }
            else
            {
                ordered.AddRange(stirrups.Select(item => new CrossEntity(2, null, item)));
                ordered.AddRange(additional.Select(item => new CrossEntity(1, item.Group, null)));
            }
            if (mainBottom.Group != null) ordered.Add(new CrossEntity(0, mainBottom.Group, null));

            var bounds = BeamBounds(context.Beam, context.View, inverse);
            var ys = CrossTagLayout.TagYsFromBeamBounds(ordered.Count, bounds.Top, bounds.Bottom);
            var x = bounds.Right + CrossTagLayout.TagColumnOffsetFromBeamFeet;
            var mainGroups = new List<IReadOnlyList<Rebar>>();
            var mainSlots = new List<(double X, double Y)>();
            var l2Groups = new List<IReadOnlyList<Rebar>>();
            var l2Slots = new List<(double X, double Y)>();
            var l1Bars = new List<Rebar>();
            var l1Slots = new List<(double X, double Y)>();
            var stirrupSlots = new List<(double X, double Y)>();

            for (var index = 0; index < ordered.Count; index++)
            {
                var slot = (x, ys[index]);
                var entity = ordered[index];
                if (entity.Kind == 0)
                {
                    mainGroups.Add(entity.Group!); mainSlots.Add(slot);
                }
                else if (entity.Kind == 1 && Count(entity.Group![0]) >= 2)
                {
                    l2Groups.Add(entity.Group); l2Slots.Add(slot);
                }
                else if (entity.Kind == 1)
                {
                    l1Bars.Add(entity.Group![0]); l1Slots.Add(slot);
                }
                else
                {
                    stirrupSlots.Add(slot);
                }
            }

            var mainType = context.IsSupport ? supportMain : midMain;
            var l2Type = context.IsSupport ? supportL2 : midL2;
            var l1Type = context.IsSupport ? supportL1 : midL1;
            var stirrupType = context.IsSupport ? supportStirrup : midStirrup;
            _mra.Place(document, context.View, mainGroups, mainType, mainSlots, warnings);
            _mra.Place(document, context.View, l2Groups, l2Type, l2Slots, warnings);
            _tags.TagRebars(document, context.View, l1Bars,
                Enumerable.Repeat(l1Type, l1Bars.Count).ToList(), warnings, 4, l1Slots);
            _tags.TagRebars(document, context.View, stirrups,
                Enumerable.Repeat(stirrupType, stirrups.Count).ToList(), warnings, 4, stirrupSlots);

            var pair = new ViewBeamPair(context.View, context.Beam, context.Geometry, true,
                context.Station, context.IsSupport);
            var axisX = context.Geometry.End.X - context.Geometry.Start.X;
            var axisY = context.Geometry.End.Y - context.Geometry.Start.Y;
            // Dầm phương X: kéo dim cao vào gần tiết diện thêm 1 mm trên giấy.
            // Quy đổi sang model = 1 mm × tỷ lệ view.
            var sideDimensionOffsetMm = Math.Abs(axisX) >= Math.Abs(axisY)
                ? Math.Max(0, setting.AnnotationOffsetMm - context.View.Scale)
                : setting.AnnotationOffsetMm;
            _dimensions.PlaceCrossDimensions(document, context.View, pair, rebars, dimType,
                new DimensionConfig(true, setting.DimensionTypeName, setting.DimensionTypeName, 4,
                    sideDimensionOffsetMm, setting.AnnotationOffsetMm), warnings,
                placeHeightOnViewLeft: true);
            _spots.Place(document, context.View, pair, spotType, setting.AnnotationOffsetMm, warnings);
            _breaks.Place(document, context.View, pair, breakType, warnings);
        }

        foreach (var warning in warnings.Distinct())
            result.AddWarning(new BeamLongitudinalDrawingWarning("CROSS_ANNOTATION", warning));
    }

    private static Dictionary<ElementId, List<Rebar>> RebarsByHost(Document document) =>
        new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>()
            .Where(rebar => rebar.GetHostId() != ElementId.InvalidElementId)
            .GroupBy(rebar => rebar.GetHostId()).ToDictionary(group => group.Key, group => group.ToList());

    private static bool IntersectsView(Rebar rebar, View view)
    {
        var box = rebar.get_BoundingBox(null);
        if (box == null) return false;
        var local = Corners(box).Select(view.CropBox.Transform.Inverse.OfPoint).ToList();
        var crop = view.CropBox;
        const double tolerance = 10.0 / 304.8;
        return local.Max(point => point.Z) >= crop.Min.Z - tolerance &&
               local.Min(point => point.Z) <= crop.Max.Z + tolerance;
    }

    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return box.Transform.OfPoint(new XYZ(x, y, z));
    }

    private static double LocalY(Rebar rebar, Transform inverse)
    {
        var box = rebar.get_BoundingBox(null);
        return box == null ? 0 : inverse.OfPoint((box.Min + box.Max) * 0.5).Y;
    }

    private static int Count(Rebar rebar)
    {
        try { return rebar.NumberOfBarPositions; }
        catch { return 1; }
    }

    private static bool IsStirrup(Rebar rebar)
    {
        try { return rebar.Document.GetElement(rebar.GetShapeId()) is RebarShape { RebarStyle: RebarStyle.StirrupTie }; }
        catch { return false; }
    }

    private static (double Right, double Top, double Bottom) BeamBounds(
        FamilyInstance beam, View view, Transform inverse)
    {
        var box = beam.get_BoundingBox(null) ?? view.CropBox;
        var local = Corners(box).Select(inverse.OfPoint).ToList();
        return (local.Max(point => point.X), local.Max(point => point.Y), local.Min(point => point.Y));
    }

    private sealed record CrossEntity(int Kind, IReadOnlyList<Rebar>? Group, Rebar? Stirrup);
}

public sealed record LongitudinalCrossViewContext(
    ViewSection View,
    FamilyInstance Beam,
    BeamGeometry Geometry,
    double Station,
    bool IsSupport);
