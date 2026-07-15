using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Đặt tag cho thép trong 1 section view 2D.
///     QUAN TRỌNG: section view 2D phải dùng <c>Rebar.SetUnobscuredInView</c> để thép hiện rõ trước khi tag
///     (KHÔNG dùng SetSolidInView — chỉ cho View3D). PHẢI gọi trong Transaction đang mở, SAU khi view đã
///     tạo + commit + Regenerate. Logic tái dùng từ bản đã verify trong src/BeamDrawing.Addin.
/// </summary>
public sealed class RebarTagPlacer
{
    public int TagRebarGroups(Document doc, View view, IReadOnlyList<IReadOnlyList<Rebar>> groups,
        ElementId? tagTypeId, List<string> warnings,
        IReadOnlyList<(double X, double Y)> tagHeadLocals)
    {
        if (groups.Count == 0 || tagTypeId == null || tagTypeId == ElementId.InvalidElementId) return 0;
        foreach (var rebar in groups.SelectMany(group => group).DistinctBy(rebar => rebar.Id.ToValue()))
            try { rebar.SetUnobscuredInView(view, true); } catch { }
        doc.Regenerate();

        var transform = view.CropBox?.Transform;
        if (transform == null) return 0;
        var placed = 0;
        for (var index = 0; index < groups.Count && index < tagHeadLocals.Count; index++)
        {
            // Group 1 phần tử = thép nằm ngang: một leader.
            // Group 2 phần tử = thép chấm: lấy hai thanh con liền kề phía tag để có hai leader.
            var dottedReferences = groups[index].Count > 1
                ? GetFirstAndLastBarReferences(groups[index][0], view)
                : new List<(Reference Reference, XYZ? End)>();
            var references = dottedReferences.Count > 0
                ? dottedReferences.Select(item => item.Reference).ToList()
                : new[] { GetTaggableReference(groups[index][0]) }
                    .Where(reference => reference != null).Cast<Reference>().ToList();
            var horizontalEnd = groups[index].Count == 1
                ? GetRightBarAnchor(groups[index][0], view)
                : null;
            if (references.Count == 0) continue;
            warnings.Add($"TAG DEBUG group={index + 1}, mode={(groups[index].Count > 1 ? "DOT-2-BRANCH" : "HORIZONTAL-1-BRANCH")}, " +
                         $"rebarId={groups[index][0].Id.ToValue()}, subelements={SafeSubelementCount(groups[index][0])}, " +
                         $"preparedRefs={references.Count}.");
            try
            {
                var slot = tagHeadLocals[index];
                var head = transform.OfPoint(new XYZ(slot.X, slot.Y, 0));
                var tag = IndependentTag.Create(doc, tagTypeId, view.Id, references[0], true,
                    TagOrientation.Horizontal, head);
                if (references.Count > 1) tag.AddReferences(references.Skip(1).ToList());
                try { tag.TagHeadPosition = head; } catch { }
                var hostCountAfterAdd = tag.GetTaggedReferences().Count;
                warnings.Add($"TAG DEBUG tagId={tag.Id.ToValue()}, hostCountAfterAdd={hostCountAfterAdd}.");
                if (dottedReferences.Count > 1)
                {
                    try { tag.LeaderEndCondition = LeaderEndCondition.Free; }
                    catch (Exception ex) { warnings.Add($"TAG DEBUG set Free End FAIL: {ex.Message}"); }
                    doc.Regenerate();
                    foreach (var item in dottedReferences.Where(item => item.End != null))
                    {
                        try
                        {
                            tag.SetLeaderEnd(item.Reference, item.End!);
                            warnings.Add($"TAG DEBUG SetLeaderEnd OK: " + PointText(item.End!, transform));
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"TAG DEBUG SetLeaderEnd FAIL: {ex.Message}");
                        }
                    }
                }
                else if (horizontalEnd != null)
                {
                    try
                    {
                        tag.LeaderEndCondition = LeaderEndCondition.Free;
                        doc.Regenerate();
                        tag.SetLeaderEnd(references[0], horizontalEnd);
                        warnings.Add($"TAG DEBUG horizontal SetLeaderEnd OK: {PointText(horizontalEnd, transform)}");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"TAG DEBUG horizontal SetLeaderEnd FAIL: {ex.Message}");
                    }
                }
                StraightLeader.ApplyPerpendicular(tag, references[0], head, transform);
                var finalEnds = tag.GetTaggedReferences().Select(reference =>
                {
                    try { return PointText(tag.GetLeaderEnd(reference), transform); }
                    catch { return "END-UNAVAILABLE"; }
                });
                warnings.Add($"TAG DEBUG final hosts={tag.GetTaggedReferences().Count}, ends=[{string.Join("; ", finalEnds)}].");
                placed++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Không tạo được tag thép móng 2 nhánh trong view '{view.Name}': {ex.Message}");
            }
        }
        return placed;
    }

    private static int SafeSubelementCount(Rebar rebar)
    {
        try { return rebar.GetSubelements()?.Count ?? 0; }
        catch { return -1; }
    }

    private static string PointText(XYZ point, Transform cropTransform)
    {
        var local = cropTransform.Inverse.OfPoint(point);
        return $"({local.X * 304.8:0.#},{local.Y * 304.8:0.#})mm";
    }

    private static List<(Reference Reference, XYZ? End)> GetFirstAndLastBarReferences(Rebar rebar, View view)
    {
        try
        {
            var subelements = rebar.GetSubelements();
            if (subelements is { Count: > 1 })
            {
                var inverse = view.CropBox.Transform.Inverse;
                var rightmost = subelements
                    .Select(subelement => new
                    {
                        Reference = subelement.GetReference(),
                        Center = BoxCenter(subelement.GetBoundingBox(view))
                    })
                    .Where(item => item.Reference != null && item.Center != null)
                    .OrderByDescending(item => inverse.OfPoint(item.Center!).X)
                    .Take(2)
                    .ToList();
                if (rightmost.Count == 2)
                    return rightmost
                        .Select(item => (item.Reference!, item.Center))
                        .ToList();
            }
        }
        catch { }

        var fallback = GetTaggableReference(rebar);
        return fallback == null
            ? new List<(Reference, XYZ?)>()
            : new List<(Reference, XYZ?)> { (fallback, null) };
    }

    private static XYZ? BoxCenter(BoundingBoxXYZ? box) => box == null ? null : (box.Min + box.Max) * 0.5;

    private static XYZ? GetRightBarAnchor(Rebar rebar, View view)
    {
        try
        {
            var inverse = view.CropBox.Transform.Inverse;
            var horizontalLine = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .OfType<Line>()
                .OrderByDescending(line =>
                {
                    var direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                    return Math.Abs(direction.DotProduct(view.RightDirection)) * line.Length;
                })
                .FirstOrDefault();
            if (horizontalLine == null) return null;

            var p0 = horizontalLine.GetEndPoint(0);
            var p1 = horizontalLine.GetEndPoint(1);
            var p0Local = inverse.OfPoint(p0);
            var p1Local = inverse.OfPoint(p1);
            var rightEnd = p0Local.X >= p1Local.X ? p0 : p1;
            var leftEnd = p0Local.X >= p1Local.X ? p1 : p0;
            var inward = (leftEnd - rightEnd).Normalize();
            var inset = Math.Min(250.0 / 304.8, horizontalLine.Length * 0.25);
            return rightEnd + inward * inset;
        }
        catch
        {
            return null;
        }
    }

    public int TagRebars(Document doc, View view, IReadOnlyList<Rebar> rebars,
        IReadOnlyList<ElementId?> tagTypeIds,
        List<string> warnings, int spacingFactor = 6,
        IReadOnlyList<(double X, double Y)>? tagHeadLocals = null)
    {
        if (rebars.Count == 0) return 0;
        if (tagTypeIds.Count == 0 || tagTypeIds.All(id => id == null || id == ElementId.InvalidElementId))
        {
            warnings.Add($"Project chưa có Rebar Tag — bỏ qua tag thép ở view '{view.Name}'.");
            return 0;
        }

        // Bước 1: cho tất cả thép unobscured trong section view 2D, rồi regenerate để reference hợp lệ.
        foreach (var rebar in rebars)
        {
            try { rebar.SetUnobscuredInView(view, true); }
            catch { /* view không hỗ trợ — bỏ qua */ }
        }
        doc.Regenerate();

        // Bước 2: xếp tag head thẳng hàng bên phải vùng crop, cách đều theo chiều cao.
        var crop = view.CropBox;
        var cropOk = view.CropBoxActive && crop != null;
        var tagX = 0.0; var yTop = 0.0; var yStep = 1.0; var z = 0.0;
        Transform? cropTransform = null;
        if (cropOk)
        {
            cropTransform = crop!.Transform;
            var paperSpacingFeet = Math.Max(spacingFactor, 1) * Math.Max(view.Scale, 1) / 304.8;
            tagX = crop.Max.X + Math.Max((crop.Max.X - crop.Min.X) * 0.30, paperSpacingFeet);
            yTop = crop.Max.Y - (crop.Max.Y - crop.Min.Y) * 0.12;
            var span = (crop.Max.Y - crop.Min.Y) * 0.72;
            yStep = rebars.Count > 1 ? span / (rebars.Count - 1) : 0;
            z = (crop.Min.Z + crop.Max.Z) * 0.5;
        }

        var placed = 0;
        var i = 0;
        for (var rebarIndex = 0; rebarIndex < rebars.Count; rebarIndex++)
        {
            var rebar = rebars[rebarIndex];
            var tagTypeId = tagTypeIds[Math.Min(rebarIndex, tagTypeIds.Count - 1)];
            if (tagTypeId == null || tagTypeId == ElementId.InvalidElementId) continue;
            var reference = GetTaggableReference(rebar);
            if (reference == null)
            {
                warnings.Add($"Không lấy được reference cho thép '{rebar.Id}' trong view '{view.Name}'.");
                continue;
            }

            try
            {
                XYZ tagHead;
                if (tagHeadLocals != null && rebarIndex < tagHeadLocals.Count && cropTransform != null)
                {
                    // Slot = cùng X (cột chung) + Y rải đều (BeamAnnotator tính). z=0 để nhất quán với MRA placer
                    // (z≠0 lệch theo phương nhìn → head bị dịch X, căn cứ: đai ra X=2.469 thay vì 2.202).
                    var slot = tagHeadLocals[rebarIndex];
                    tagHead = cropTransform.OfPoint(new XYZ(slot.X, slot.Y, 0));
                }
                else
                {
                    tagHead = cropOk && cropTransform != null
                        ? cropTransform.OfPoint(new XYZ(tagX, yTop - yStep * i, z))
                        : GetTagPoint(rebar, view);
                }

                var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, true,
                    TagOrientation.Horizontal, tagHead);
                // Ép head về đúng cột (Revit tự dời sau Create) TRƯỚC, rồi set leader vuông góc theo head đó.
                try { tag.TagHeadPosition = tagHead; }
                catch { /* tag không cho set head — bỏ qua */ }
                if (cropTransform != null)
                    StraightLeader.ApplyPerpendicular(tag, reference, tagHead, cropTransform);
                else
                    StraightLeader.Apply(tag, reference, tagHead);

                placed++;
                i++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Không tag được thép '{rebar.Id}' trong view '{view.Name}': {ex.Message}");
            }
        }

        return placed;
    }

    /// <summary>Reference tag được: Revit 2023+ cần subelement (1 thanh con), không nhận cả set.</summary>
    private static Reference? GetTaggableReference(Rebar rebar)
    {
        try
        {
            var subelements = rebar.GetSubelements();
            if (subelements is { Count: > 0 })
            {
                var reference = subelements[0].GetReference();
                if (reference != null) return reference;
            }
        }
        catch { /* fallback */ }

        try { return new Reference(rebar); }
        catch { return null; }
    }

    private static XYZ GetTagPoint(Rebar rebar, View view)
    {
        try
        {
            var bbox = rebar.get_BoundingBox(view);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;
        }
        catch { /* fallback */ }

        var viewBox = view.get_BoundingBox(view);
        return viewBox != null ? (viewBox.Min + viewBox.Max) * 0.5 : XYZ.Zero;
    }
}
