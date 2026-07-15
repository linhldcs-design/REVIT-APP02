using Autodesk.Revit.DB;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Đặt dimension cho mặt cắt móng: chuỗi ĐỨNG (cao độ đáy đế → đỉnh đế → đỉnh cổ) đặt bên trái,
///     và chuỗi NGANG (bề rộng đế) đặt bên dưới. Reference lấy từ mặt phẳng solid móng trong view.
///     Best-effort: thiếu reference → warn, không chặn. PHẢI gọi trong Transaction (view đã regenerate).
/// </summary>
public sealed class FootingDimensionPlacer
{
    private const double MmPerFoot = 304.8;
    private readonly Dictionary<long, PendingChain> _pendingChains = new();
    private sealed record PendingChain(List<ElementId> PairIds, List<string> OrderedStableReferences,
        Line ChainLine, ElementId? DimTypeId,
        ElementId? ChainId = null);

    public int Place(Document doc, ViewSection view, Element footing, FootingSectionGeometry geometry,
        ElementId? dimTypeId, double offsetMm, List<string> warnings)
    {
        var placed = 0;
        var dimType = dimTypeId == null ? null : doc.GetElement(dimTypeId) as DimensionType;
        var right = Normalize(view.RightDirection); // phương ngang trên view
        var up = XYZ.BasisZ;                          // phương đứng
        var offset = Math.Max(offsetMm, 50) / MmPerFoot;

        var familyInstance = footing as FamilyInstance;
        var (familyHorizontal, familyVertical) = CollectFamilyReferences(familyInstance, view);
        List<FaceRef> horizontal;
        List<FaceRef> vertical;
        if (familyHorizontal.Count >= 2 && familyVertical.Count >= 2)
        {
            horizontal = familyHorizontal;
            vertical = familyVertical;
        }
        else
        {
            // Chỉ quét solid khi family thật sự không expose đủ reference; tránh làm chậm model lớn.
            var fallback = CollectFaces(footing, view);
            horizontal = familyHorizontal.Count >= 2 ? familyHorizontal : fallback.Horizontal;
            vertical = familyVertical.Count >= 2 ? familyVertical : fallback.Vertical;
        }
        var levelFaces = CollectLevelReferences(doc, view, geometry.BottomZFeet, geometry.TopZFeet);
        ShortenLevelLines(view, geometry, levelFaces, offsetMm, warnings);
        horizontal = ReplaceHorizontalFacesWithWeakReferences(
            doc, view, familyInstance, horizontal, levelFaces, geometry, right, warnings);
        var verticalChain = DedupeSorted(horizontal.Concat(levelFaces).ToList());
        var horizontalFaces = DedupeSorted(vertical.Where(item => !item.IsEdge).ToList());
        // Hai depth-edge tìm được trên family M3 thuộc cổ/cột (cách nhau 400mm), không phải mép bệ.
        // Dùng hai mặt ngoài song song của đế để DIM ngang ổn định (2200mm).
        var horizontalChain = horizontalFaces;

        // Chuỗi ĐỨNG: mặt family + Level, ví dụ 100/200/350/1000/450.
        var tim = new XYZ(geometry.Center.X, geometry.Center.Y, 0);
        var lineBase = tim - right * (geometry.WidthFeet * 0.5 + offset);
        if (verticalChain.Count >= 2)
        {
            var chainLine = Line.CreateBound(
                new XYZ(lineBase.X, lineBase.Y, verticalChain[0].Coord - 0.25),
                new XYZ(lineBase.X, lineBase.Y, verticalChain[^1].Coord + 0.25));
            var pairIds = new List<ElementId>();
            for (var index = 0; index < verticalChain.Count - 1; index++)
            {
                var refs = new ReferenceArray();
                refs.Append(verticalChain[index].Ref);
                refs.Append(verticalChain[index + 1].Ref);
                var pairLine = Line.CreateBound(
                    new XYZ(lineBase.X, lineBase.Y, verticalChain[index].Coord - 0.1),
                    new XYZ(lineBase.X, lineBase.Y, verticalChain[index + 1].Coord + 0.1));
                var pair = CreateDimension(doc, view, pairLine, refs, dimType, $"đứng tạm {index + 1}", warnings);
                if (pair == null) continue;
                pairIds.Add(pair.Id);
                placed++;
            }

            if (pairIds.Count == verticalChain.Count - 1)
            {
                var orderedStableReferences = verticalChain
                    .Select(item => item.Ref.ConvertToStableRepresentation(doc))
                    .ToList();
                _pendingChains[view.Id.ToValue()] = new PendingChain(
                    pairIds, orderedStableReferences, chainLine, dimType?.Id);
            }
        }
        else warnings.Add($"Không đủ mặt ngang móng để dim cao độ ở view '{view.Name}'.");

        // DIM tổng độc lập để lỗi chuỗi family không làm mất DIM Level -> Level.
        if (levelFaces.Count >= 2)
        {
            var overallRefs = new ReferenceArray();
            overallRefs.Append(levelFaces[0].Ref);
            overallRefs.Append(levelFaces[^1].Ref);
            var overallBase = lineBase - right * offset;
            var overallLine = Line.CreateBound(
                new XYZ(overallBase.X, overallBase.Y, levelFaces[0].Coord - 0.25),
                new XYZ(overallBase.X, overallBase.Y, levelFaces[^1].Coord + 0.25));
            if (TryCreate(doc, view, overallLine, overallRefs, dimType, "tổng cao", warnings)) placed++;
        }

        // Chuỗi NGANG: dùng tất cả mặt đứng để giữ các đoạn biên, ví dụ 100/2000/100.
        if (horizontalChain.Count >= 2)
        {
            var refs = new ReferenceArray();
            foreach (var face in horizontalChain) refs.Append(face.Ref);
            var zBase = geometry.BottomZFeet - offset;
            // Đường dim chạy theo phương right, tại cao độ dưới đáy đế; dựng từ 2 mép.
            var p0 = PointOnRight(geometry, right, horizontalChain[0].Coord, zBase);
            var p1 = PointOnRight(geometry, right, horizontalChain[^1].Coord, zBase);
            if (p0.DistanceTo(p1) > 1e-6)
            {
                var line = Line.CreateBound(p0, p1);
                if (TryCreate(doc, view, line, refs, dimType, "bề rộng", warnings)) placed++;
            }
        }
        else warnings.Add($"Không đủ mặt cạnh móng để dim bề rộng ở view '{view.Name}'.");

        return placed;
    }

    public void CreateContinuousChain(Document doc, ViewSection view, List<string> warnings)
    {
        if (!_pendingChains.TryGetValue(view.Id.ToValue(), out var pending)) return;
        var pairs = pending.PairIds
            .Select(id => doc.GetElement(id) as Dimension)
            .Where(dim => dim != null)
            .Cast<Dimension>()
            .ToList();
        if (pairs.Count != pending.PairIds.Count) return;

        var references = new ReferenceArray();
        try
        {
            foreach (var stableReference in pending.OrderedStableReferences)
                references.Append(Reference.ParseFromStableRepresentation(doc, stableReference));
        }
        catch (Exception ex)
        {
            warnings.Add($"Không khôi phục được thứ tự reference DIM đứng: {ex.Message}");
            return;
        }

        var dimType = pending.DimTypeId == null ? null : doc.GetElement(pending.DimTypeId) as DimensionType;
        var chain = CreateDimension(doc, view, pending.ChainLine, references, dimType,
            "chuỗi đứng liên tục sau commit", warnings);
        if (chain != null) _pendingChains[view.Id.ToValue()] = pending with { ChainId = chain.Id };
    }

    public void CleanupTemporaryPairs(Document doc, ViewSection view, List<string> warnings)
    {
        if (!_pendingChains.Remove(view.Id.ToValue(), out var pending)) return;
        // T3 đã commit: chỉ xóa DIM cặp khi chain thật sự còn tồn tại. Nếu Revit xóa chain, giữ fallback.
        if (pending.ChainId == null || doc.GetElement(pending.ChainId) == null)
        {
            warnings.Add("Không hợp nhất được DIM đứng liên tục; giữ các DIM đoạn để không mất kích thước.");
            return;
        }
        doc.Delete(pending.PairIds);
    }

    private static List<FaceRef> CollectLevelReferences(Document doc, View view, double bottomZ, double topZ)
    {
        var tolerance = 5.0 / MmPerFoot;
        return new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(level => level.Elevation >= bottomZ - tolerance && level.Elevation <= topZ + tolerance)
            .Select(level => new FaceRef(new Reference(level), level.Elevation))
            .OrderBy(item => item.Coord)
            .ToList();
    }

    private static void ShortenLevelLines(ViewSection view, FootingSectionGeometry geometry,
        IReadOnlyList<FaceRef> levelFaces, double dimOffsetMm, List<string> warnings)
    {
        if (!view.CropBoxActive || view.CropBox == null) return;
        var transform = view.CropBox.Transform;
        var inverse = transform.Inverse;
        var centerLocal = inverse.OfPoint(new XYZ(geometry.Center.X, geometry.Center.Y, geometry.BottomZFeet));
        var targetRightX = centerLocal.X + geometry.WidthFeet * 0.5 + 100.0 / MmPerFoot;
        var dimOffset = Math.Max(dimOffsetMm, 50.0) / MmPerFoot;
        // DIM tổng đứng nằm ngoài chuỗi DIM một offset; Level vượt sang trái thêm 150mm.
        var targetLeftX = centerLocal.X - geometry.WidthFeet * 0.5 - dimOffset * 2.0 - 150.0 / MmPerFoot;

        foreach (var face in levelFaces)
        {
            if (view.Document.GetElement(face.Ref.ElementId) is not Level level) continue;
            try
            {
                var curves = level.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                if (curves.Count == 0) curves = level.GetCurvesInView(DatumExtentType.Model, view);
                var curve = curves.OfType<Line>().FirstOrDefault();
                if (curve == null) continue;
                var a = inverse.OfPoint(curve.GetEndPoint(0));
                var b = inverse.OfPoint(curve.GetEndPoint(1));
                var originalLeft = a.X <= b.X ? a : b;
                var right = a.X <= b.X ? b : a;
                if (targetRightX <= targetLeftX + 10.0 / MmPerFoot) continue;
                var left = new XYZ(targetLeftX, originalLeft.Y, originalLeft.Z);
                var shortenedRight = new XYZ(targetRightX, right.Y, right.Z);
                var shortened = Line.CreateBound(transform.OfPoint(left), transform.OfPoint(shortenedRight));
                level.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
                level.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
                level.SetCurveInView(DatumExtentType.ViewSpecific, view, shortened);
            }
            catch (Exception ex)
            {
                warnings.Add($"Không thu gọn đường Level '{level.Name}' trong view '{view.Name}': {ex.Message}");
            }
        }
    }

    private sealed record FaceRef(Reference Ref, double Coord, bool IsEdge = false);

    private static (List<FaceRef> Horizontal, List<FaceRef> Vertical) CollectFamilyReferences(
        FamilyInstance? instance, ViewSection view)
    {
        var horizontal = new List<FaceRef>();
        var vertical = new List<FaceRef>();
        if (instance == null) return (horizontal, vertical);

        var right = Normalize(view.RightDirection);

        // Family móng BS_FI_Móng Đơn đúng tâm expose hai Reference Plane có tên H1/H2.
        // Lấy đúng named reference thay vì quét Strong/Weak (không xác định và rất chậm).
        // H2 là chiều cao tiếp theo H1 nên cao độ mốc = Ref.Level + H1 + H2.
        var referenceLevelZ = instance.GetTransform().Origin.Z;
        var h1 = GetLengthParameter(instance, "H1");
        var h2 = GetLengthParameter(instance, "H2");
        AddNamedHorizontalReference(instance, "H1", referenceLevelZ + h1, horizontal);
        AddNamedHorizontalReference(instance, "H2", referenceLevelZ + h1 + h2, horizontal);

        var referenceTypes = new[]
        {
            FamilyInstanceReferenceType.Bottom,
            FamilyInstanceReferenceType.Top,
            FamilyInstanceReferenceType.Left,
            FamilyInstanceReferenceType.Right
        };

        foreach (var referenceType in referenceTypes)
        {
            var references = instance.GetReferences(referenceType);
            if (references == null) continue;
            foreach (var reference in references.Take(32))
            {
                var normalized = NormalizeReference(reference, instance.Document);
                var geometryObject = instance.GetGeometryObjectFromReference(normalized);
                if (geometryObject is PlanarFace planar)
                {
                    var normal = Normalize(planar.FaceNormal);
                    if (Math.Abs(normal.Z) > 0.7)
                        horizontal.Add(new FaceRef(normalized, planar.Origin.Z));
                    else if (Math.Abs(normal.DotProduct(right)) > 0.7)
                        vertical.Add(new FaceRef(normalized, planar.Origin.DotProduct(right)));
                    continue;
                }

                // Named references đôi khi không trả GeometryObject. Các reference biên hệ thống vẫn ánh xạ
                // chắc chắn tới bounding box của instance.
                var box = instance.get_BoundingBox(view) ?? instance.get_BoundingBox(null);
                if (box == null) continue;
                switch (referenceType)
                {
                    case FamilyInstanceReferenceType.Bottom:
                        horizontal.Add(new FaceRef(normalized, box.Min.Z));
                        break;
                    case FamilyInstanceReferenceType.Top:
                        horizontal.Add(new FaceRef(normalized, box.Max.Z));
                        break;
                    case FamilyInstanceReferenceType.Left:
                        vertical.Add(new FaceRef(normalized, MinProjection(box, right)));
                        break;
                    case FamilyInstanceReferenceType.Right:
                        vertical.Add(new FaceRef(normalized, MaxProjection(box, right)));
                        break;
                }
            }
        }

        return (DedupeSorted(horizontal), DedupeSorted(vertical));
    }

    private static void AddNamedHorizontalReference(FamilyInstance instance, string name, double elevation,
        List<FaceRef> horizontal)
    {
        var reference = instance.GetReferenceByName(name);
        if (reference != null)
            horizontal.Add(new FaceRef(NormalizeReference(reference, instance.Document), elevation));
    }

    private static double GetLengthParameter(FamilyInstance instance, string name)
    {
        var parameter = instance.LookupParameter(name)
                        ?? instance.Symbol?.LookupParameter(name);
        return parameter is { StorageType: StorageType.Double } ? parameter.AsDouble() : 0;
    }

    private static List<FaceRef> ReplaceHorizontalFacesWithWeakReferences(Document doc, ViewSection view,
        FamilyInstance? instance, List<FaceRef> horizontalFaces, List<FaceRef> levelFaces,
        FootingSectionGeometry geometry, XYZ right, List<string> warnings)
    {
        if (instance == null || horizontalFaces.Count == 0 || levelFaces.Count == 0) return horizontalFaces;
        var weakReferences = instance.GetReferences(FamilyInstanceReferenceType.WeakReference);
        if (weakReferences == null || weakReferences.Count == 0) return horizontalFaces;

        var originZ = instance.GetTransform().Origin.Z;
        var baseLevel = levelFaces.OrderBy(item => Math.Abs(item.Coord - originZ)).First();
        var probeBase = new XYZ(geometry.Center.X, geometry.Center.Y, 0)
                        - right * (geometry.WidthFeet * 0.5 + 1.0);
        var probeLine = Line.CreateBound(
            new XYZ(probeBase.X, probeBase.Y, geometry.BottomZFeet - 1.0),
            new XYZ(probeBase.X, probeBase.Y, geometry.TopZFeet + 1.0));
        var tolerance = 3.0 / MmPerFoot;
        var replacements = new Dictionary<int, Reference>();

        foreach (var weakReference in weakReferences.Take(32))
        {
            Dimension? probe = null;
            try
            {
                var refs = new ReferenceArray();
                refs.Append(baseLevel.Ref);
                refs.Append(NormalizeReference(weakReference, doc));
                probe = doc.Create.NewDimension(view, probeLine, refs);
                if (probe.Value is not double distance) continue;

                var candidates = horizontalFaces
                    .Select((face, index) => new
                    {
                        Index = index,
                        Error = Math.Abs(Math.Abs(face.Coord - baseLevel.Coord) - Math.Abs(distance))
                    })
                    .Where(item => item.Error <= tolerance)
                    .OrderBy(item => item.Error)
                    .ToList();
                if (candidates.Count > 0 && !replacements.ContainsKey(candidates[0].Index))
                    replacements[candidates[0].Index] = NormalizeReference(weakReference, doc);
            }
            catch
            {
                // Reference không song song Level: không phải mặt phẳng ngang cần cho chuỗi đứng.
            }
            finally
            {
                if (probe != null && doc.GetElement(probe.Id) != null) doc.Delete(probe.Id);
            }
        }

        if (replacements.Count == 0)
        {
            warnings.Add("DIM DEBUG: không ánh xạ được Weak Reference ngang nào; vẫn dùng surface fallback.");
            return horizontalFaces;
        }

        var result = horizontalFaces.ToList();
        foreach (var replacement in replacements)
            result[replacement.Key] = result[replacement.Key] with { Ref = replacement.Value };
        return result;
    }

    private static double MinProjection(BoundingBoxXYZ box, XYZ direction) =>
        BoxCorners(box).Min(point => point.DotProduct(direction));

    private static double MaxProjection(BoundingBoxXYZ box, XYZ direction) =>
        BoxCorners(box).Max(point => point.DotProduct(direction));

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        for (var x = 0; x <= 1; x++)
        for (var y = 0; y <= 1; y++)
        for (var z = 0; z <= 1; z++)
        {
            var local = new XYZ(
                x == 0 ? box.Min.X : box.Max.X,
                y == 0 ? box.Min.Y : box.Max.Y,
                z == 0 ? box.Min.Z : box.Max.Z);
            yield return box.Transform.OfPoint(local);
        }
    }

    /// <summary>Gom mặt phẳng solid móng: ngang (normal ~Z, sắp theo Z) + đứng theo phương right (sắp theo vị trí right).</summary>
    private static (List<FaceRef> Horizontal, List<FaceRef> Vertical) CollectFaces(Element footing, ViewSection view)
    {
        var right = Normalize(view.RightDirection);
        var horizontal = new List<FaceRef>();
        var vertical = new List<FaceRef>();

        var options = new Options { ComputeReferences = true, View = view };
        var geometry = footing.get_Geometry(options) ?? footing.get_Geometry(new Options { ComputeReferences = true });
        if (geometry == null) return (horizontal, vertical);

        CollectFaceReferences(geometry, Transform.Identity, right, Normalize(view.ViewDirection), view.Document,
            horizontal, vertical);

        horizontal = DedupeSorted(horizontal);
        vertical = DedupeSorted(vertical);
        return (horizontal, vertical);
    }

    /// <summary>
    /// Duyệt symbol geometry để giữ reference gốc có thể dùng tạo Dimension. GetInstanceGeometry trả về
    /// bản sao geometry; reference từ bản sao có thể đọc được nhưng Revit từ chối khi NewDimension.
    /// </summary>
    private static void CollectFaceReferences(GeometryElement geometry, Transform transform, XYZ right,
        XYZ viewDirection, Document doc, List<FaceRef> horizontal, List<FaceRef> vertical)
    {
        foreach (var obj in geometry)
        {
            if (obj is Solid { Faces.Size: > 0, Volume: > 1e-9 } solid)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is not PlanarFace planar || face.Reference == null) continue;
                    var origin = transform.OfPoint(planar.Origin);
                    var normal = Normalize(transform.OfVector(planar.FaceNormal));

                    if (Math.Abs(normal.Z) > 0.7)
                        horizontal.Add(new FaceRef(NormalizeReference(face.Reference, doc), origin.Z));
                    else if (Math.Abs(normal.DotProduct(right)) > 0.7)
                        vertical.Add(new FaceRef(NormalizeReference(face.Reference, doc),
                            origin.DotProduct(right)));
                }

                // Các mốc vai dốc/mép bệ trong section là cạnh chạy theo chiều sâu view, không phải mặt.
                // DIM thủ công snap vào edge reference này để tạo 100/2000/100 và các đoạn cao chuyển tiếp.
                foreach (Edge edge in solid.Edges)
                {
                    if (edge.Reference == null || edge.AsCurve() is not Line edgeLine) continue;
                    var p0 = transform.OfPoint(edgeLine.GetEndPoint(0));
                    var p1 = transform.OfPoint(edgeLine.GetEndPoint(1));
                    var direction = Normalize(p1 - p0);
                    if (Math.Abs(direction.DotProduct(viewDirection)) < 0.7) continue;
                    var midpoint = (p0 + p1) * 0.5;
                    // Edge chỉ dùng cho DIM ngang. Không trộn với horizontal face/Level trong DIM đứng:
                    // Revit sẽ báo "References ... are no longer parallel" và tự xóa dimension lúc commit.
                    vertical.Add(new FaceRef(NormalizeReference(edge.Reference, doc),
                        midpoint.DotProduct(right), IsEdge: true));
                }
            }
            else if (obj is GeometryInstance instance)
            {
                var symbolGeometry = instance.GetSymbolGeometry();
                if (symbolGeometry != null)
                    CollectFaceReferences(symbolGeometry, transform.Multiply(instance.Transform), right, viewDirection,
                        doc, horizontal, vertical);
            }
        }
    }

    /// <summary>Bỏ mặt trùng cao độ/vị trí (dung sai 2mm), sắp tăng dần.</summary>
    private static List<FaceRef> DedupeSorted(List<FaceRef> faces)
    {
        var tol = 2.0 / MmPerFoot;
        var sorted = faces.OrderBy(f => f.Coord).ToList();
        var result = new List<FaceRef>();
        foreach (var f in sorted)
            if (result.Count == 0 || Math.Abs(f.Coord - result[^1].Coord) > tol)
                result.Add(f);
        return result;
    }

    private static XYZ PointOnRight(FootingSectionGeometry geometry, XYZ right, double coordAlongRight, double z)
    {
        // Điểm trên mặt phẳng có projection theo right = coordAlongRight, giữ nguyên thành phần khác của tim.
        var tim = new XYZ(geometry.Center.X, geometry.Center.Y, z);
        var timAlong = tim.DotProduct(right);
        return tim + right * (coordAlongRight - timAlong);
    }

    private static bool TryCreate(Document doc, View view, Line line, ReferenceArray refs,
        DimensionType? dimType, string label, List<string> warnings)
        => CreateDimension(doc, view, line, refs, dimType, label, warnings) != null;

    private static Dimension? CreateDimension(Document doc, View view, Line line, ReferenceArray refs,
        DimensionType? dimType, string label, List<string> warnings)
    {
        try
        {
            if (refs.Size < 2) return null;
            var dim = dimType == null
                ? doc.Create.NewDimension(view, line, refs)
                : doc.Create.NewDimension(view, line, refs, dimType);
            return dim;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được dim {label} ở view '{view.Name}': {ex.Message}");
            return null;
        }
    }

    private static XYZ Normalize(XYZ vector)
    {
        var length = vector.GetLength();
        return length < 1e-9 ? XYZ.BasisX : vector / length;
    }

    private static Reference NormalizeReference(Reference reference, Document doc)
    {
        try
        {
            var stable = reference.ConvertToStableRepresentation(doc);
            return Reference.ParseFromStableRepresentation(doc, stable);
        }
        catch
        {
            return reference;
        }
    }
}
