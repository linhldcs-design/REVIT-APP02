using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

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
        IReadOnlyList<(double X, double Y)> tagHeadLocals, List<string> warnings)
    {
        if (groups.Count == 0) return 0;
        if (annotationTypeId == null ||
            doc.GetElement(annotationTypeId) is not MultiReferenceAnnotationType annotationType)
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
            // Dim line CÙNG Y với head (như đích: dimOrigin.Y = headY) → leader đi NGANG thẳng từ head tới dim,
            // vuông góc hàng thép, KHÔNG bám xiên vào từng thanh. X = giữa dầm (nơi hàng thép nằm).
            var dimensionOrigin = transform.OfPoint(new XYZ((crop.Min.X + crop.Max.X) * 0.5, slot.Y, 0));
            var options = new MultiReferenceAnnotationOptions(annotationType)
            {
                DimensionLineOrigin = dimensionOrigin,
                DimensionLineDirection = right,
                DimensionPlaneNormal = normal,
                TagHeadPosition = tagHead,
                TagHasLeader = true
            };
            // GOM cả nhóm vào 1 MRA (như đích nRef=3) → dim đi ngang qua các thanh cùng lớp, leader thẳng.
            options.SetElementsToDimension(group.Select(r => r.Id).ToList());

            if (!MultiReferenceAnnotation.AreElementsValidForMultiReferenceAnnotation(doc, options))
            {
                // MRA không tạo được (vd rebar 1 thanh) → fallback IndependentTag để KHÔNG mất tag.
                if (TagFallback(doc, view, group[0], tagHead)) placed++;
                else failed++;
                continue;
            }

            try
            {
                var annotation = MultiReferenceAnnotation.Create(doc, view.Id, options);
                if (doc.GetElement(annotation.TagId) is IndependentTag tag)
                {
                    var tagRef = tag.GetTaggedReferences().FirstOrDefault();
                    if (tagRef != null) StraightLeader.Apply(tag, tagRef, tagHead);
                }
                placed++;
            }
            catch
            {
                failed++;
            }
        }

        if (failed > 0)
            warnings.Add($"{failed}/{groups.Count} nhóm thép dọc chưa tạo được MRA trong '{view.Name}'.");

        return placed;
    }

    /// <summary>Tag rebar bằng IndependentTag (fallback khi MRA từ chối, vd rebar 1 thanh). Dùng rebar tag mặc định.</summary>
    private static bool TagFallback(Document doc, View view, Rebar rebar, XYZ tagHead)
    {
        var tagTypeId = RequiredFamilyValidator.FindRebarTagTypeId(doc);
        if (tagTypeId == null) return false;
        try
        {
            rebar.SetUnobscuredInView(view, true);
            doc.Regenerate();
            var subs = rebar.GetSubelements();
            var reference = subs is { Count: > 0 } ? subs[0].GetReference() : new Reference(rebar);
            if (reference == null) return false;
            var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, true,
                TagOrientation.Horizontal, tagHead);
            try { tag.TagHeadPosition = tagHead; } catch { }
            return true;
        }
        catch { return false; }
    }
}
