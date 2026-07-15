using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Đặt dimension chiều dài nhịp dầm trên sectional view (từ 2 đầu location line của dầm).
///     Best-effort: lỗi hoặc thiếu reference → warn, không chặn. PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class DimensionPlacer
{
    public bool PlaceSpanLength(Document doc, View view, ViewBeamPair pair, ElementId? dimensionTypeId,
        double distanceToBotFaceMm, List<string> warnings)
    {
        try
        {
            // Reference 2 mặt đầu dầm (normal song song trục dầm).
            var refs = GetEndReferences(pair.Beam, view, pair.Geometry);
            if (refs.Size < 2)
            {
                warnings.Add($"Không đủ reference để dim chiều dài ở view '{view.Name}'.");
                return false;
            }

            var g = pair.Geometry;
            // Đường dim chạy song song trục dầm, dịch xuống dưới đáy dầm 1 chút.
            var offsetFeet = distanceToBotFaceMm / 304.8;
            var start = new XYZ(g.Start.X, g.Start.Y, g.BottomZFeet - offsetFeet);
            var end = new XYZ(g.End.X, g.End.Y, g.BottomZFeet - offsetFeet);
            if (start.DistanceTo(end) < 1e-6)
            {
                warnings.Add($"Chiều dài dầm quá nhỏ để dim ở view '{view.Name}'.");
                return false;
            }

            var line = Line.CreateBound(start, end);
            var dimensionType = dimensionTypeId == null ? null : doc.GetElement(dimensionTypeId) as DimensionType;
            var dim = dimensionType == null
                ? doc.Create.NewDimension(view, line, refs)
                : doc.Create.NewDimension(view, line, refs, dimensionType);
            return dim != null;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được dimension ở view '{view.Name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Đặt dim rộng + dim cao chuỗi ở cross section. Chuỗi cao best-effort gồm mặt đáy, các lớp rebar
    ///     nhìn thấy tại station và mặt đỉnh; reference rebar không hợp lệ sẽ được Revit từ chối và ghi warning.
    /// </summary>
    public int PlaceCrossDimensions(Document doc, View view, ViewBeamPair pair, IReadOnlyList<Rebar> rebars,
        ElementId? dimensionTypeId, DimensionConfig config, List<string> warnings)
    {
        try
        {
            var references = GetCrossFaceReferences(pair.Beam, view, pair.Geometry);
            var center = StationPoint(pair);
            var axis = Normalize(new XYZ(
                pair.Geometry.End.X - pair.Geometry.Start.X,
                pair.Geometry.End.Y - pair.Geometry.Start.Y,
                pair.Geometry.End.Z - pair.Geometry.Start.Z));
            var side = Normalize(XYZ.BasisZ.CrossProduct(axis));
            var dimensionType = dimensionTypeId == null ? null : doc.GetElement(dimensionTypeId) as DimensionType;
            var placed = 0;

            if (references.Left != null && references.Right != null)
            {
                var widthRefs = new ReferenceArray();
                widthRefs.Append(references.Left);
                widthRefs.Append(references.Right);
                var offset = config.DistanceToBotFaceMm / 304.8;
                var z = pair.Geometry.BottomZFeet - offset;
                var halfWidth = pair.Geometry.WidthFeet * 0.75;
                var line = Line.CreateBound(
                    new XYZ(center.X, center.Y, z) - side * halfWidth,
                    new XYZ(center.X, center.Y, z) + side * halfWidth);
                if (TryCreateCrossDimension(doc, view, line, widthRefs, dimensionType, "rộng", warnings))
                    placed++;
            }

            var paperSpacing = config.SpacingFactor * Math.Max(view.Scale, 1) / 304.8;
            var sideOffset = config.DistanceToSideBeamMm / 304.8;
            var xBase = center + side * (pair.Geometry.WidthFeet * 0.5 + sideOffset);

            // Chuỗi cao linh hoạt: đáy dầm / đáy sàn / top sàn / top dầm. Mặt trùng nhau trong 2mm tự gộp.
            // Sàn hạ cốt: 280 + 120 + 50; top sàn trùng top dầm: chỉ còn 330 + 120.
            var faceLevels = new List<FaceLevel>();
            if (references.Bottom != null)
                faceLevels.Add(references.Bottom);
            if (references.Top != null)
                faceLevels.Add(references.Top);
            var slabRefs = GetSlabHorizontalReferences(doc, view, pair);
            if (slabRefs != null)
            {
                faceLevels.Add(slabRefs.Bottom);
                faceLevels.Add(slabRefs.Top);
            }

            var orderedLevels = CrossDimensionLayerMath.OrderedUniqueLevels(
                faceLevels.Select(level => level.Z),
                CrossDimensionLayerMath.CoincidentToleranceMm / 304.8);
            var orderedFaces = orderedLevels
                .Select(z => faceLevels.OrderBy(level => Math.Abs(level.Z - z)).First())
                .ToList();

            if (orderedFaces.Count >= 3)
            {
                var chainRefs = new ReferenceArray();
                foreach (var face in orderedFaces) chainRefs.Append(face.Ref);
                var chainLine = Line.CreateBound(
                    new XYZ(xBase.X, xBase.Y, orderedLevels[0] - 0.25),
                    new XYZ(xBase.X, xBase.Y, orderedLevels[^1] + 0.25));
                if (TryCreateCrossDimension(doc, view, chainLine, chainRefs, dimensionType, "chuỗi đứng", warnings,
                        removeDisplayedZeroSegments: true))
                    placed++;
            }

            if (orderedFaces.Count >= 2)
            {
                var overallRefs = new ReferenceArray();
                overallRefs.Append(orderedFaces[0].Ref);
                overallRefs.Append(orderedFaces[^1].Ref);
                var overallBase = xBase + side * paperSpacing;
                var overallLine = Line.CreateBound(
                    new XYZ(overallBase.X, overallBase.Y, orderedLevels[0] - 0.25),
                    new XYZ(overallBase.X, overallBase.Y, orderedLevels[^1] + 0.25));
                if (TryCreateCrossDimension(doc, view, overallLine, overallRefs, dimensionType, "tổng chiều cao", warnings))
                    placed++;
            }

            return placed;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được dimension cross ở view '{view.Name}': {ex.Message}");
            return 0;
        }
    }

    private sealed record FaceLevel(Reference Ref, double Z);
    private sealed record SlabFaceLevels(FaceLevel Bottom, FaceLevel Top);

    /// <summary>Gets the actual bottom/top Floor face references at this cross-section station.</summary>
    private static SlabFaceLevels? GetSlabHorizontalReferences(Document doc, View view, ViewBeamPair pair)
    {
        var station = StationPoint(pair);
        var beamBottom = pair.Geometry.BottomZFeet;
        var beamTop = pair.Geometry.TopZFeet;
        var probe = 2.0 / 304.8;
        var floors = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(
                new XYZ(station.X - probe, station.Y - probe, beamBottom - probe),
                new XYZ(station.X + probe, station.Y + probe, beamTop + 600.0 / 304.8))))
            .Cast<Element>()
            .ToList();

        var options = new Options { ComputeReferences = true, View = view };
        SlabFaceLevels? best = null;
        var bestDistance = double.MaxValue;
        foreach (var floor in floors)
        {
            var horiz = CollectHorizontalFacesAtStation(floor, options, station);
            if (horiz.Count < 2)
                horiz = CollectHorizontalFacesAtStation(floor, new Options { ComputeReferences = true }, station);
            if (horiz.Count < 2) continue;
            horiz.Sort((a, b) => a.Z.CompareTo(b.Z));
            var bottom = horiz[0];
            var top = horiz[^1];
            if (top.Z < beamBottom - probe || bottom.Z > beamTop + 300.0 / 304.8) continue;
            var distance = DistanceToInterval(beamTop, bottom.Z, top.Z);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = new SlabFaceLevels(bottom, top);
        }
        return best;
    }

    private static List<FaceLevel> CollectHorizontalFacesAtStation(Element element, Options options, XYZ station)
    {
        var faces = new List<FaceLevel>();
        var geometry = element.get_Geometry(options);
        if (geometry == null) return faces;
        foreach (var solid in CollectSolids(geometry))
        {
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace pf || face.Reference == null) continue;
                var normal = pf.FaceNormal.Normalize();
                if (Math.Abs(normal.Z) < 0.5) continue;
                var z = pf.Origin.Z -
                        (normal.X * (station.X - pf.Origin.X) + normal.Y * (station.Y - pf.Origin.Y)) / normal.Z;
                var point = new XYZ(station.X, station.Y, z);
                var projection = pf.Project(point);
                if (projection == null || !pf.IsInside(projection.UVPoint) ||
                    projection.XYZPoint.DistanceTo(point) > 1.0 / 304.8) continue;
                faces.Add(new FaceLevel(face.Reference, projection.XYZPoint.Z));
            }
        }
        return faces;
    }

    private static IEnumerable<Solid> CollectSolids(GeometryElement geometry)
    {
        foreach (var obj in geometry)
        {
            if (obj is Solid { Faces.Size: > 0, Volume: > 1e-9 } solid) yield return solid;
            else if (obj is GeometryInstance instance)
            {
                foreach (var nested in CollectSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }
    }

    private static double DistanceToInterval(double value, double low, double high) =>
        value < low ? low - value : value > high ? value - high : 0;

    /// <summary>
    /// Revit 2025 introduced LinearDimension.Create with an IList of geometric references. Use that API for
    /// cross dimensions instead of the legacy ItemFactoryBase.NewDimension overload, which produced the opaque
    /// "Invalid number of references" error for a valid two-reference overall dimension in joined beam/slab cuts.
    /// Keep every cross dimension isolated: one rejected reference set must not discard dimensions already made.
    /// </summary>
    private static bool TryCreateCrossDimension(Document doc, View view, Line line, ReferenceArray refs,
        DimensionType? type, string label, List<string> warnings, bool removeDisplayedZeroSegments = false)
    {
        try
        {
            var referenceList = new List<Reference>(refs.Size);
            foreach (Reference reference in refs) referenceList.Add(reference);
            if (referenceList.Count < 2)
            {
                warnings.Add($"Bỏ qua dim {label} ở view '{view.Name}': chỉ có {referenceList.Count} reference.");
                return false;
            }

            while (referenceList.Count >= 2)
            {
                var dimension = CreateDimension(doc, view, line, referenceList);
                if (dimension == null) return false;
                if (type != null && dimension.GetTypeId() != type.Id)
                    dimension.ChangeTypeId(type.Id);

                if (!removeDisplayedZeroSegments || referenceList.Count < 3)
                    return true;

                doc.Regenerate();
                var zeroSegmentIndex = FindDisplayedZeroSegment(dimension);
                if (zeroSegmentIndex < 0) return true;

                doc.Delete(dimension.Id);
                var removeIndex = CrossDimensionLayerMath.ReferenceIndexToRemoveForZeroSegment(
                    referenceList.Count, zeroSegmentIndex);
                referenceList.RemoveAt(removeIndex);
            }

            return false;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được dim {label} ở view '{view.Name}' " +
                         $"({refs.Size} reference): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Tao dimension tuyen tinh. R25+ dung LinearDimension.Create (IList reference);
    ///     R23/R24 dung Document.Create.NewDimension (ReferenceArray).
    /// </summary>
    private static Dimension? CreateDimension(Document doc, Autodesk.Revit.DB.View view, Line line,
        IList<Reference> references)
    {
#if REVIT2025_OR_GREATER
        return LinearDimension.Create(doc, view, line, references);
#else
        var arr = new ReferenceArray();
        foreach (var r in references) arr.Append(r);
        return doc.Create.NewDimension(view, line, arr);
#endif
    }

    private static int FindDisplayedZeroSegment(Dimension dimension)
    {
        for (var i = 0; i < dimension.Segments.Size; i++)
        {
            var displayed = dimension.Segments.get_Item(i).ValueString?.Trim();
            if (displayed == "0") return i;
        }
        return -1;
    }

    private static XYZ StationPoint(ViewBeamPair pair)
    {
        var t = pair.Station ?? 0.5;
        return new XYZ(
            pair.Geometry.Start.X + (pair.Geometry.End.X - pair.Geometry.Start.X) * t,
            pair.Geometry.Start.Y + (pair.Geometry.End.Y - pair.Geometry.Start.Y) * t,
            (pair.Geometry.TopZFeet + pair.Geometry.BottomZFeet) * 0.5);
    }

    private static CrossReferences GetCrossFaceReferences(FamilyInstance beam, View view, BeamGeometry geometry)
    {
        var axis = Normalize(new XYZ(
            geometry.End.X - geometry.Start.X,
            geometry.End.Y - geometry.Start.Y,
            geometry.End.Z - geometry.Start.Z));
        var side = Normalize(XYZ.BasisZ.CrossProduct(axis));
        var sideFaces = new List<(Reference Ref, double Position)>();
        var verticalFaces = new List<FaceLevel>();
        var options = new Options { ComputeReferences = true, View = view };

        foreach (var obj in beam.get_Geometry(options))
        {
            if (obj is not Solid { Faces.Size: > 0 } solid) continue;
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planar || face.Reference == null) continue;
                var normal = planar.FaceNormal.Normalize();
                if (Math.Abs(normal.DotProduct(side)) > 0.9)
                    sideFaces.Add((face.Reference, planar.Origin.DotProduct(side)));
                else if (Math.Abs(normal.DotProduct(XYZ.BasisZ)) > 0.9)
                    verticalFaces.Add(new FaceLevel(face.Reference, planar.Origin.Z));
            }
        }

        sideFaces.Sort((a, b) => a.Position.CompareTo(b.Position));
        verticalFaces.Sort((a, b) => a.Z.CompareTo(b.Z));
        return new CrossReferences(
            sideFaces.FirstOrDefault().Ref,
            sideFaces.LastOrDefault().Ref,
            verticalFaces.Count == 0 ? null : verticalFaces[0],
            verticalFaces.Count == 0 ? null : verticalFaces[^1]);
    }

    private sealed record CrossReferences(Reference? Left, Reference? Right, FaceLevel? Bottom, FaceLevel? Top);

    /// <summary>
    ///     Lấy reference 2 MẶT ĐẦU dầm (mặt phẳng cắt ngang trục) để dim chiều dài. Chọn face có normal
    ///     song song trục dầm; sắp theo vị trí chiếu lên trục để lấy 2 mặt xa nhau nhất (2 đầu).
    /// </summary>
    private static ReferenceArray GetEndReferences(FamilyInstance beam, View view, BeamGeometry geometry)
    {
        var dir = Normalize(new XYZ(
            geometry.End.X - geometry.Start.X,
            geometry.End.Y - geometry.Start.Y,
            geometry.End.Z - geometry.Start.Z));

        var candidates = new List<(Reference Reference, double Projection)>();
        var options = new Options { ComputeReferences = true, View = view };
        foreach (var obj in beam.get_Geometry(options))
        {
            if (obj is not Solid { Faces.Size: > 0 } solid) continue;
            foreach (Face face in solid.Faces)
            {
                if (face.Reference == null || face is not PlanarFace planar) continue;
                // Chỉ mặt có normal ~ song song trục dầm = mặt đầu.
                if (Math.Abs(planar.FaceNormal.Normalize().DotProduct(dir)) < 0.95) continue;
                candidates.Add((face.Reference, planar.Origin.DotProduct(dir)));
            }
        }

        var array = new ReferenceArray();
        if (candidates.Count >= 2)
        {
            candidates.Sort((a, b) => a.Projection.CompareTo(b.Projection));
            array.Append(candidates[0].Reference);          // đầu gần nhất
            array.Append(candidates[^1].Reference);         // đầu xa nhất
        }
        return array;
    }

    private static XYZ Normalize(XYZ v)
    {
        var len = v.GetLength();
        return len < 1e-9 ? XYZ.BasisX : v / len;
    }
}
