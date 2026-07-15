using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>Đặt annotation trong T2, dùng đúng type đã chọn và lọc rebar thật tại từng cross station.</summary>
public sealed class BeamAnnotator : IBeamAnnotator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly RebarTagPlacer _tagPlacer = new();
    private readonly MultiRebarAnnotationPlacer _multiRebarPlacer = new();
    private readonly DimensionPlacer _dimensionPlacer = new();
    private readonly SpotElevationPlacer _spotPlacer = new();
    private readonly BreakLinePlacer _breakLinePlacer = new();

    public void Annotate(Document doc, IReadOnlyList<ViewBeamPair> pairs, BeamDrawingSetting setting,
        BeamDrawingResult result)
    {
        var tagTypes = ResolveTagTypes(doc, setting.Tags, result.Warnings);
        var crossAnnotation = setting.CrossAnnotation ?? CrossAnnotationConfig.Empty;
        var endMraTypeId = _resources.ResolveMultiRebarAnnotationType(
            doc, crossAnnotation.EndLongitudinalMraTypeName, result.Warnings);
        var midMraTypeId = _resources.ResolveMultiRebarAnnotationType(
            doc, crossAnnotation.MidLongitudinalMraTypeName, result.Warnings);
        var endStirrupTagId = _resources.ResolveRebarTagType(
            doc, crossAnnotation.EndStirrupTagTypeName ?? setting.Tags.D4, result.Warnings);
        var midStirrupTagId = _resources.ResolveRebarTagType(
            doc, crossAnnotation.MidStirrupTagTypeName ?? setting.Tags.D2, result.Warnings);
        var spotTypeId = _resources.ResolveSpotType(doc, setting.Spot.TypeName, result.Warnings);
        var sectionalDimTypeId = _resources.ResolveDimType(doc, setting.Dim.SectionalDimTypeName, result.Warnings);
        var crossDimTypeId = _resources.ResolveDimType(doc, setting.Dim.CrossDimTypeName, result.Warnings);
        // Break-line ở mép sàn LUÔN cần khi dầm có sàn (nghiệp vụ), không phụ thuộc cờ preset cũ.
        // Placer tự bỏ qua nếu dầm không có sàn. Vẫn tôn trọng tên family user chọn.
        var breakLineId = _resources.ResolveBreakLineSymbol(doc, setting.BreakLineFamilyName, result.Warnings);

        var rebarsByHost = GroupRebarsByHost(doc);
        foreach (var pair in pairs)
        {
            var hosted = rebarsByHost.TryGetValue(pair.Beam.Id, out var list) ? list : new List<Rebar>();
            if (pair.IsCross)
            {
                var atStation = FilterAndSortAtStation(hosted, pair);
                // Phân vùng GỐI/NHỊP theo cờ IsSupportZone (từ orchestrator, dò cột) — chính xác cho dầm nhiều nhịp.
                // Fallback ngưỡng Station nếu cờ chưa set (tương thích cũ).
                var isEndZone = pair.IsSupportZone ?? (pair.Station is < 0.25 or > 0.75);
                var crop = pair.View.CropBox;
                var inv = crop.Transform.Inverse;
                // Cột tag đặt cách MÉP PHẢI DẦM (không phải mép crop — crop rộng hơn dầm) một offset cố định,
                // khớp đích DK2-1 (head cách mép dầm ~1.378ft). Fallback cropMax nếu không đọc được bbox dầm.
                var (beamRightX, beamTopY, beamBottomY) = BeamBoundsLocal(pair.Beam, pair.View, inv, crop);
                var columnX = beamRightX + CrossTagLayout.TagColumnOffsetFromBeamFeet;

                // GOM thép dọc cùng LỚP (cùng đường kính + cùng cao độ Y band) vào 1 MRA — như đích (tag "3D16"
                // gom 3 thanh, leader ngang thẳng). Đai mỗi thanh 1 tag riêng. Mỗi "entity" = 1 tag đầu ra.
                var longGroups = GroupLongitudinalByLayer(atStation.Where(r => !IsStirrup(r)), inv);
                var stirrupList = atStation.Where(IsStirrup).ToList();

                // Phân loại theo SỐ THANH + Y (không chỉ Y — vì tăng cường 1D16 có thể cùng Y thép chủ 2D16):
                // TĂNG CƯỜNG = nhóm ít thanh nhất so với chủ (thường 1 cây). CHỦ = nhóm nhiều thanh, lấy Y cao nhất
                // làm chủ-trên, Y thấp nhất làm chủ-dưới. Còn lại (kể cả cùng Y chủ) = tăng cường.
                double GroupY(IReadOnlyList<Rebar> g) => g.Average(r => inv.OfPoint(Center(r)).Y);
                var longByY = longGroups.OrderByDescending(GroupY).ToList();
                var maxQty = longByY.Count > 0 ? longByY.Max(g => BarCount(g[0])) : 0;
                // Thép chủ = nhóm có qty = maxQty (nhiều thanh nhất). Chủ trên/dưới = qty-max Y cao/thấp nhất.
                var mainCandidates = longByY.Where(g => BarCount(g[0]) == maxQty).ToList();
                var mainTop = mainCandidates.Count > 0 ? mainCandidates[0] : null;
                var mainBot = mainCandidates.Count > 1 ? mainCandidates[^1] : null;
                // Tăng cường = mọi nhóm KHÔNG phải mainTop/mainBot (gồm nhóm qty nhỏ + nhóm chủ dư nếu >2).
                var reinforce = longByY.Where(g => !ReferenceEquals(g, mainTop) && !ReferenceEquals(g, mainBot)).ToList();

                // THỨ TỰ TAG theo VÙNG (quy luật user): GỐI = chủ-trên / tăng-cường / ĐAI / chủ-dưới;
                // NHỊP = chủ-trên / ĐAI / tăng-cường / chủ-dưới. Kind: 0=chủ, 1=tăng-cường, 2=đai.
                var ordered = new List<(int Kind, IReadOnlyList<Rebar>? Group, Rebar? Stirrup)>();
                if (mainTop != null) ordered.Add((0, mainTop, null));
                if (isEndZone)
                {
                    foreach (var g in reinforce) ordered.Add((1, g, null));
                    foreach (var s in stirrupList) ordered.Add((2, null, s));
                }
                else
                {
                    foreach (var s in stirrupList) ordered.Add((2, null, s));
                    foreach (var g in reinforce) ordered.Add((1, g, null));
                }
                if (mainBot != null) ordered.Add((0, mainBot, null));

                // Cân đối toàn bộ cột tag theo chiều cao dầm thật; chủ trên/ dưới làm hai neo ngoài tiết diện.
                var slotYs = CrossTagLayout.TagYsFromBeamBounds(ordered.Count, beamTopY, beamBottomY);

                // Thép CHỦ dùng MRA (type chung). Thép TĂNG CƯỜNG L1 (1 cây) dùng REBAR TAG (IndependentTag)
                // = tag đai của vùng (ReinforceL1 là tên MRA type, không phải rebar-tag → không resolve tại đây).
                var mainMraId = isEndZone ? endMraTypeId : midMraTypeId;
                ElementId? reinforceTagId = null;

                var mainGroups = new List<IReadOnlyList<Rebar>>(); var mainSlots = new List<(double X, double Y)>();
                var reinMraGroups = new List<IReadOnlyList<Rebar>>(); var reinMraSlots = new List<(double X, double Y)>();
                var reinBars = new List<Rebar>(); var reinSlots = new List<(double X, double Y)>();
                var stirrups = new List<Rebar>();
                var stirrupSlots = new List<(double X, double Y)>();
                for (var e = 0; e < ordered.Count; e++)
                {
                    var slot = (columnX, slotYs[e]);
                    switch (ordered[e].Kind)
                    {
                        case 2: stirrups.Add(ordered[e].Stirrup!); stirrupSlots.Add(slot); break;
                        case 1:
                            // Tăng cường: LỚP 2 (≥2 cây) → MRA; LỚP 1 (1 cây) → IndependentTag + leader vuông (elbow).
                            if (BarCount(ordered[e].Group![0]) >= 2)
                            { reinMraGroups.Add(ordered[e].Group!); reinMraSlots.Add(slot); }
                            else
                            { reinBars.Add(ordered[e].Group![0]); reinSlots.Add(slot); }
                            break;
                        default: mainGroups.Add(ordered[e].Group!); mainSlots.Add(slot); break;
                    }
                }

                _multiRebarPlacer.Place(doc, pair.View, mainGroups, mainMraId, mainSlots, result.Warnings);
                // Tăng cường LỚP 2 (nhiều cây) → MRA (dùng type reinforce nếu chọn MRA-type; fallback type chủ).
                if (reinMraGroups.Count > 0)
                {
                    var reinMraId = isEndZone
                        ? _resources.ResolveMultiRebarAnnotationType(doc, crossAnnotation.EndReinforceL2MraTypeName, result.Warnings) ?? mainMraId
                        : _resources.ResolveMultiRebarAnnotationType(doc, crossAnnotation.MidReinforceL2MraTypeName, result.Warnings) ?? mainMraId;
                    _multiRebarPlacer.Place(doc, pair.View, reinMraGroups, reinMraId, reinMraSlots, result.Warnings);
                }
                // Tăng cường LỚP 1 (1 cây) → IndependentTag.
                if (reinBars.Count > 0)
                {
                    var reinTagId = reinforceTagId ?? (isEndZone ? endStirrupTagId : midStirrupTagId);
                    _tagPlacer.TagRebars(doc, pair.View, reinBars,
                        Enumerable.Repeat(reinTagId, reinBars.Count).ToList(), result.Warnings,
                        setting.Dim.SpacingFactor, reinSlots);
                }
                var stirrupTagId = isEndZone ? endStirrupTagId : midStirrupTagId;
                _tagPlacer.TagRebars(doc, pair.View, stirrups,
                    Enumerable.Repeat(stirrupTagId, stirrups.Count).ToList(), result.Warnings,
                    setting.Dim.SpacingFactor, stirrupSlots);
                if (setting.Dim.Enabled)
                    _dimensionPlacer.PlaceCrossDimensions(doc, pair.View, pair, atStation, crossDimTypeId,
                        setting.Dim, result.Warnings);
                if (setting.Spot.Enabled)
                    _spotPlacer.Place(doc, pair.View, pair, spotTypeId, setting.Spot.OffsetMm, result.Warnings);
                // Break-line (nét cắt đứt sàn) ở 2 mép dầm — placer tự bỏ qua nếu dầm KHÔNG có sàn (đích DS1-01).
                _breakLinePlacer.Place(doc, pair.View, pair, breakLineId, result.Warnings);
                continue;
            }

            var sectional = SortSectionalRebars(hosted, pair.Geometry);
            var sectionalIds = sectional.Select(r => SelectSectionalTagId(r, pair.Geometry, tagTypes)).ToList();
            _tagPlacer.TagRebars(doc, pair.View, sectional, sectionalIds, result.Warnings,
                setting.Dim.SpacingFactor);

            if (setting.Dim.Enabled)
                _dimensionPlacer.PlaceSpanLength(doc, pair.View, pair, sectionalDimTypeId,
                    setting.Dim.DistanceToBotFaceMm, result.Warnings);

            if (setting.Spot.Enabled)
                _spotPlacer.Place(doc, pair.View, pair, spotTypeId, setting.Spot.OffsetMm, result.Warnings);

            if (setting.BreakLine)
                _breakLinePlacer.Place(doc, pair.View, pair, breakLineId, result.Warnings);
        }
    }

    /// <summary>Dung sai band cao độ Y coi 2 element là CÙNG LỚP (feet). 0.08ft (~24mm): các lớp thép cách nhau
    /// ≥0.11ft (đích DK2-1: 3D16 Y1.35 / 2D16 Y1.24 / 3D16 Y0.78) → tách riêng, KHÔNG gộp mất tag 2D16 tăng cường.</summary>
    private const double LayerBandFeet = 0.08;

    /// <summary>
    ///     Gom thép dọc CÙNG LỚP vào 1 nhóm: cùng đường kính (RebarBarType) + cùng cao độ Y (trong band).
    ///     Mỗi nhóm → 1 MRA (như đích: 3D16 gom 3 thanh). Sắp nhóm trên→dưới theo Y.
    /// </summary>
    private static List<List<Rebar>> GroupLongitudinalByLayer(IEnumerable<Rebar> longitudinal, Transform inv)
    {
        // Căn cứ đích DK2-1 (MCP): mỗi MRA dim ĐÚNG 1 Rebar ELEMENT (Revit tự nref theo số thanh con của set).
        // Mỗi LỚP Y chỉ lấy 1 element ĐẠI DIỆN (qty NumberOfBarPositions lớn nhất) → bỏ element trùng lớp.
        var items = longitudinal
            .Select(r => (Rebar: r, Y: inv.OfPoint(Center(r)).Y, Qty: BarCount(r)))
            .OrderByDescending(x => x.Y)
            .ToList();

        // Mỗi Rebar element = 1 TAG RIÊNG (thép tăng cường tag riêng dù cùng lớp Y với thép chủ — user yêu cầu).
        // CHỈ gom khi 2 element TRÙNG HẲN (cùng Y band + cùng số thanh qty) = Revit tách 1 set thành nhiều element.
        var layers = new List<(double Y, Rebar Rep, int Qty)>();
        foreach (var it in items)
        {
            var idx = layers.FindIndex(l => Math.Abs(l.Y - it.Y) <= LayerBandFeet && l.Qty == it.Qty);
            if (idx < 0) layers.Add((it.Y, it.Rebar, it.Qty));
            // else: trùng hệt (cùng Y + cùng qty) → bỏ, tránh tag đôi.
        }
        // Mỗi element đại diện → 1 nhóm (MRA dim 1 element, Revit tự nref theo số thanh con).
        return layers.OrderByDescending(l => l.Y).Select(l => new List<Rebar> { l.Rep }).ToList();
    }

    private static int BarCount(Rebar rebar)
    {
        try { return rebar.NumberOfBarPositions; }
        catch { return 1; }
    }

    /// <summary>Mép phải + đỉnh + đáy dầm trong view, hệ crop-local. Fallback theo crop.</summary>
    private static (double RightX, double TopY, double BottomY) BeamBoundsLocal(
        FamilyInstance beam, View view, Transform inv, BoundingBoxXYZ crop)
    {
        try
        {
            var bb = beam.get_BoundingBox(view);
            if (bb != null)
            {
                var p1 = inv.OfPoint(bb.Min);
                var p2 = inv.OfPoint(bb.Max);
                return (Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y), Math.Min(p1.Y, p2.Y));
            }
        }
        catch { /* fallback */ }
        return (crop.Max.X, crop.Max.Y - 0.5, crop.Min.Y + 0.5);
    }

    private AnnotationTagTypes ResolveTagTypes(Document doc, RebarTagSet tags, List<string> warnings) => new(
        _resources.ResolveRebarTagType(doc, tags.T1, warnings),
        _resources.ResolveRebarTagType(doc, tags.T2, warnings),
        _resources.ResolveRebarTagType(doc, tags.MidItem, warnings),
        [
            _resources.ResolveRebarTagType(doc, tags.D0, warnings),
            _resources.ResolveRebarTagType(doc, tags.D1, warnings),
            _resources.ResolveRebarTagType(doc, tags.D2, warnings),
            _resources.ResolveRebarTagType(doc, tags.D3, warnings),
            _resources.ResolveRebarTagType(doc, tags.D4, warnings),
            _resources.ResolveRebarTagType(doc, tags.D5, warnings)
        ]);

    private static ElementId? SelectSectionalTagId(Rebar rebar, BeamGeometry geometry, AnnotationTagTypes types)
    {
        if (IsStirrup(rebar)) return types.Mid;
        var bbox = rebar.get_BoundingBox(null);
        if (bbox == null) return types.Mid;
        var centerZ = (bbox.Min.Z + bbox.Max.Z) * 0.5;
        var third = Math.Max((geometry.TopZFeet - geometry.BottomZFeet) / 3.0, 1e-6);
        if (centerZ >= geometry.TopZFeet - third) return types.Top;
        if (centerZ <= geometry.BottomZFeet + third) return types.Bottom;
        return types.Mid;
    }

    private static List<Rebar> SortSectionalRebars(IEnumerable<Rebar> rebars, BeamGeometry geometry) =>
        rebars.OrderByDescending(r => Center(r).Z)
            .ThenBy(r => IsStirrup(r) ? 1 : 0)
            .ToList();

    private static List<Rebar> FilterAndSortAtStation(IEnumerable<Rebar> rebars, ViewBeamPair pair)
    {
        if (pair.Station is not { } station) return rebars.ToList();
        var start = ToXyz(pair.Geometry.Start);
        var end = ToXyz(pair.Geometry.End);
        var direction = (end - start).Normalize();
        var stationProjection = (start + (end - start) * station).DotProduct(direction);
        const double toleranceFeet = 10.0 / 304.8;

        return rebars.Where(rebar => IntersectsStation(rebar, direction, stationProjection, toleranceFeet))
            .OrderBy(r => IsStirrup(r) ? 1 : 0)
            .ThenByDescending(r => Center(r).Z)
            .ThenBy(r => Center(r).DotProduct(pair.View.RightDirection))
            .ToList();
    }

    private static bool IntersectsStation(Rebar rebar, XYZ direction, double stationProjection, double tolerance)
    {
        var bbox = rebar.get_BoundingBox(null);
        if (bbox == null) return true;
        var projections = BoxCorners(bbox).Select(p => p.DotProduct(direction)).ToList();
        return BeamAnnotationMath.IntersectsStation(
            stationProjection, projections.Min(), projections.Max(), tolerance);
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return new XYZ(x, y, z);
    }

    private static XYZ Center(Rebar rebar)
    {
        var bbox = rebar.get_BoundingBox(null);
        return bbox == null ? XYZ.Zero : (bbox.Min + bbox.Max) * 0.5;
    }

    private static bool IsStirrup(Rebar rebar)
    {
        try
        {
            return rebar.Document.GetElement(rebar.GetShapeId()) is RebarShape
            {
                RebarStyle: RebarStyle.StirrupTie
            };
        }
        catch
        {
            return false;
        }
    }

    private static XYZ ToXyz(Point3 p) => new(p.X, p.Y, p.Z);

    private static Dictionary<ElementId, List<Rebar>> GroupRebarsByHost(Document doc)
    {
        var map = new Dictionary<ElementId, List<Rebar>>();
        foreach (var rebar in new FilteredElementCollector(doc)
                     .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>())
        {
            var host = rebar.GetHostId();
            if (host == null || host == ElementId.InvalidElementId) continue;
            if (!map.TryGetValue(host, out var list)) map[host] = list = new List<Rebar>();
            list.Add(rebar);
        }
        return map;
    }

    private sealed record AnnotationTagTypes(
        ElementId? Bottom,
        ElementId? Top,
        ElementId? Mid,
        IReadOnlyList<ElementId?> Cross);
}
