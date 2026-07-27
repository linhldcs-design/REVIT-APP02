using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Tạo một Multi-Rebar Annotation cho mỗi NHÓM thép dọc CÙNG LỚP (khớp view mẫu: tag "3D16" gom cả 3 thanh
///     cùng lớp trên vào 1 MRA nRef=3 → leader thẳng ngang). MRA tự sinh Dimension + IndependentTag theo type đã chọn.
/// </summary>
public sealed class MultiRebarAnnotationPlacer
{
    private const double TagOffsetXFeet = 0.72; // ~220 mm bên phải crop (fallback).

    /// <param name="groups">Mỗi nhóm = các thanh cùng lớp (cùng đường kính + cùng cao độ Y). 1 nhóm → 1 MRA.</param>
    /// <param name="tagHeadLocals">Vị trí head (crop-local) cho từng NHÓM, cùng thứ tự groups.</param>
    public int Place(Document doc, View view, IReadOnlyList<IReadOnlyList<Rebar>> groups, ElementId? annotationTypeId,
        IReadOnlyList<(double X, double Y)> tagHeadLocals, List<string> warnings,
        ElementId? fallbackTagTypeId = null, bool preferVerticalDimension = false,
        bool useSetReferenceFallback = false)
    {
        if (groups.Count == 0) return 0;
        var annotationTypes = ResolveAnnotationTypes(doc, annotationTypeId, useSetReferenceFallback);
        if (annotationTypes.Count == 0)
        {
            warnings.Add($"Thiếu Multi-Rebar Annotation Type cho view '{view.Name}'.");
            return 0;
        }

        foreach (var rebar in groups.SelectMany(g => g))
        {
            try { rebar.SetUnobscuredInView(view, true); }
            catch { /* view không hỗ trợ */ }
        }
        doc.Regenerate();

        var crop = view.CropBox;
        var transform = crop.Transform;
        var right = transform.BasisX.Normalize();
        var up = transform.BasisY.Normalize();
        var normal = transform.BasisZ.Normalize();
        var inverse = transform.Inverse;

        var placed = 0;
        var failed = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Count == 0) continue;

            // Tâm nhóm (trung bình bbox các thanh) để đặt dim origin.
            double sumY = 0; var valid = 0;
            foreach (var r in group)
            {
                var bb = r.get_BoundingBox(view) ?? r.get_BoundingBox(null);
                if (bb == null) continue;
                sumY += inverse.OfPoint((bb.Min + bb.Max) * 0.5).Y;
                valid++;
            }
            if (valid == 0) { failed++; continue; }
            var groupY = sumY / valid;

            (double X, double Y) slot = i < tagHeadLocals.Count
                ? tagHeadLocals[i]
                : (crop.Max.X + TagOffsetXFeet, groupY);
            var tagHead = transform.OfPoint(new XYZ(slot.X, slot.Y, 0));
            var elementIds = group.Select(r => r.Id).ToList();
            var horizontalOrigin = transform.OfPoint(
                new XYZ((crop.Min.X + crop.Max.X) * 0.5, slot.Y, 0));
            var verticalOrigin = transform.OfPoint(
                new XYZ(crop.Max.X + 0.15, (crop.Min.Y + crop.Max.Y) * 0.5, 0));

            var candidates = preferVerticalDimension
                ? new[] { (Origin: verticalOrigin, Direction: up), (Origin: horizontalOrigin, Direction: right) }
                : new[] { (Origin: horizontalOrigin, Direction: right), (Origin: verticalOrigin, Direction: up) };

            var validOptions = new List<MultiReferenceAnnotationOptions>();
            foreach (var annotationType in annotationTypes)
            foreach (var candidate in candidates)
            {
                var options = new MultiReferenceAnnotationOptions(annotationType)
                {
                    DimensionLineOrigin = candidate.Origin,
                    DimensionLineDirection = candidate.Direction,
                    DimensionPlaneNormal = normal,
                    TagHeadPosition = tagHead,
                    TagHasLeader = true
                };
                options.SetElementsToDimension(elementIds);
                if (!MultiReferenceAnnotation.AreElementsValidForMultiReferenceAnnotation(doc, options))
                    continue;
                validOptions.Add(options);
            }

            if (validOptions.Count == 0)
            {
                if (TagFallback(doc, view, group[0], tagHead, fallbackTagTypeId, useSetReferenceFallback)) placed++;
                else failed++;
                continue;
            }

            MultiReferenceAnnotation? annotation = null;
            foreach (var options in validOptions)
            {
                if (!useSetReferenceFallback)
                {
                    try
                    {
                        annotation = MultiReferenceAnnotation.Create(doc, view.Id, options);
                        break;
                    }
                    catch { }
                    continue;
                }

                SubTransaction? probe = null;
                try
                {
                    probe = new SubTransaction(doc);
                    probe.Start();
                    var trial = MultiReferenceAnnotation.Create(doc, view.Id, options);
                    if (doc.GetElement(trial.TagId) is not IndependentTag trialTag)
                    {
                        probe.RollBack();
                        continue;
                    }
                    if (fallbackTagTypeId is { } desiredTagType &&
                        desiredTagType != ElementId.InvalidElementId &&
                        trialTag.GetTypeId() != desiredTagType)
                    {
                        try { trialTag.ChangeTypeId(desiredTagType); } catch { }
                    }
                    try { trialTag.TagHeadPosition = tagHead; } catch { }
                    probe.Commit();
                    annotation = trial;
                    break;
                }
                catch
                {
                    if (probe?.GetStatus() == TransactionStatus.Started)
                        probe.RollBack();
                }
            }
            if (annotation == null)
            {
                if (TagFallback(doc, view, group[0], tagHead, fallbackTagTypeId, useSetReferenceFallback)) placed++;
                else failed++;
                continue;
            }
            if (doc.GetElement(annotation.TagId) is IndependentTag tag)
            {
                var tagRef = tag.GetTaggedReferences().FirstOrDefault();
                if (tagRef != null) StraightLeader.Apply(tag, tagRef, tagHead);
            }
            placed++;
        }

        if (failed > 0)
            warnings.Add($"{failed}/{groups.Count} nhóm thép dọc chưa tạo được MRA trong '{view.Name}'.");

        return placed;
    }

    /// <summary>Tag rebar bằng IndependentTag (fallback khi MRA từ chối, vd rebar 1 thanh). Dùng rebar tag mặc định.</summary>
    private static bool TagFallback(
        Document doc, View view, Rebar rebar, XYZ tagHead, ElementId? preferredTagTypeId = null,
        bool useSetReferenceFallback = false)
    {
        var tagTypeId = preferredTagTypeId ?? RequiredFamilyValidator.FindRebarTagTypeId(doc);
        if (tagTypeId == null) return false;
        try
        {
            rebar.SetUnobscuredInView(view, true);
            doc.Regenerate();
            if (useSetReferenceFallback)
            {
                Reference? reference = null;
                try
                {
                    var subelements = rebar.GetSubelements();
                    if (subelements.Count > 0)
                        reference = subelements[0].GetReference();
                }
                catch { }
                if (reference == null)
                {
                    try { reference = new Reference(rebar); }
                    catch { return false; }
                }
                var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, true,
                    TagOrientation.Horizontal, tagHead);
                try { tag.TagHeadPosition = tagHead; } catch { }
                try { tag.LeaderEndCondition = LeaderEndCondition.Attached; } catch { }
                doc.Regenerate();
                if (tag.get_BoundingBox(view) != null) return true;
                try { doc.Delete(tag.Id); } catch { }
                return false;
            }
            foreach (var reference in OrderedReferences(rebar, view))
            {
                SubTransaction? probe = null;
                try
                {
                    probe = new SubTransaction(doc);
                    probe.Start();
                    var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, true,
                        TagOrientation.Horizontal, tagHead);
                    try { tag.TagHeadPosition = tagHead; } catch { }
                    doc.Regenerate();
                    if (tag.get_BoundingBox(view) != null)
                    {
                        probe.Commit();
                        return true;
                    }
                    probe.RollBack();
                }
                catch
                {
                    if (probe?.GetStatus() == TransactionStatus.Started)
                        probe.RollBack();
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static IReadOnlyList<Reference> OrderedReferences(Rebar rebar, View view)
    {
        // Không rẽ nhánh theo tên view. MCN-DAM và view của lệnh Bản Vẽ Dầm phải
        // dùng cùng một tập reference thật và cùng thứ tự ưu tiên.
        var known = RebarReferenceResolver.FromExistingTags(rebar).ToList();
        try { known.Add(new Reference(rebar)); } catch { }
        var subelements = rebar.GetSubelements().ToList();
        if (subelements.Count == 0)
        {
            if (known.Count == 0) known.Add(new Reference(rebar));
            return known;
        }
        var visible = subelements.FirstOrDefault(subelement =>
        {
            try { return subelement.GetBoundingBox(view) != null; }
            catch { return false; }
        });
        known.AddRange(subelements
            .Select(subelement => new
            {
                Subelement = subelement,
                Box = SafeBoundingBox(subelement)
            })
            .OrderBy(item =>
            {
                if (visible != null && ReferenceEquals(item.Subelement, visible))
                    return double.MinValue;
                if (item.Box == null) return double.MaxValue;
                var center = (item.Box!.Min + item.Box.Max) * 0.5;
                return Math.Abs((center - view.Origin).DotProduct(view.ViewDirection));
            })
            .Select(item => item.Subelement.GetReference())
            .Where(reference => reference != null)
            .Cast<Reference>()
            .ToList());
        return known
            .GroupBy(reference =>
            {
                try { return reference.ConvertToStableRepresentation(rebar.Document); }
                catch { return reference.ElementId.ToValue().ToString(); }
            })
            .Select(group => group.First())
            .ToList();
    }

    private static BoundingBoxXYZ? SafeBoundingBox(Subelement subelement)
    {
        try { return subelement.GetBoundingBox(null); }
        catch { return null; }
    }

    private static bool IntersectsCutPlane(Subelement subelement, View view)
    {
        var box = SafeBoundingBox(subelement);
        if (box == null) return false;
        var normal = view.ViewDirection.Normalize();
        var distances = BoxCorners(box)
            .Select(point => (point - view.Origin).DotProduct(normal))
            .ToList();
        const double tolerance = 1.0 / 304.8;
        return distances.Min() <= tolerance && distances.Max() >= -tolerance;
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return new XYZ(x, y, z);
    }

    private static IReadOnlyList<MultiReferenceAnnotationType> ResolveAnnotationTypes(
        Document document, ElementId? selectedId, bool includeCompatibleTypes)
    {
        var all = new FilteredElementCollector(document)
            .OfClass(typeof(MultiReferenceAnnotationType))
            .Cast<MultiReferenceAnnotationType>()
            .ToList();
        var selected = selectedId is not { } id
            ? null
            : all.FirstOrDefault(type => type.Id == id);
        if (!includeCompatibleTypes)
            return selected == null ? [] : [selected];

        return all
            .OrderByDescending(type => selected != null && type.Id == selected.Id)
            .ThenByDescending(type =>
                type.Name.Contains("MRA", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("SL", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("MCN", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("Dầm", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("Dam", StringComparison.OrdinalIgnoreCase))
            .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
