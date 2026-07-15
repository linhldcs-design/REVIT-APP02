using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Leader kiểu view đích (DK1-15): dùng LeaderEndCondition.Attached (KHÔNG Free+elbow — cái đó gây dog-leg
///     gấp khúc). Với Attached + tag head thẳng hàng X + rải đều Y, Revit tự vẽ leader gọn không zic-zac.
///     Best-effort: tag/type không hỗ trợ → bỏ qua.
/// </summary>
public static class StraightLeader
{
    public static void Apply(IndependentTag tag, Reference reference, XYZ tagHead)
    {
        try { tag.LeaderEndCondition = LeaderEndCondition.Attached; }
        catch { /* tag type không cho đổi — bỏ qua */ }
    }

    /// <summary>
    ///     Leader VUÔNG GÓC (shoulder): head → đoạn NGANG (cùng Y head) → gập VUÔNG xuống/lên điểm thép.
    ///     Dùng Free + SetLeaderElbow tại (X trên điểm thép, Y = head). Cho tag lệch Y thép (vd 1D16 tăng cường).
    /// </summary>
    /// <param name="cropTransform">Transform của view crop — để tính elbow trong hệ LOCAL rồi đổi world (tránh
    /// elbow off-plane khi head.Z ≠ end.Z). Elbow local = (end.X_local, head.Y_local) → head ngang tới trên thép rồi vuông.</param>
    public static void ApplyPerpendicular(IndependentTag tag, Reference referenceIgnored, XYZ tagHead, Transform cropTransform)
    {
        try { tag.LeaderEndCondition = LeaderEndCondition.Free; }
        catch { return; }
        try
        {
            tag.Document.Regenerate();
            var inv = cropTransform.Inverse;
            var headLocal = inv.OfPoint(tag.TagHeadPosition);        // head thực (local)
            var taggedReferences = tag.GetTaggedReferences().ToList();
            var endLocals = taggedReferences
                .Select(reference => (Reference: reference, End: inv.OfPoint(tag.GetLeaderEnd(reference))))
                .ToList();
            // Tag bên phải: dùng elbow chung ngay trên host ngoài cùng. Hai leader sau đó tách
            // từ cùng một elbow xuống hai thanh chấm (Host Count=2, Free End) đúng mẫu thủ công.
            var commonElbowX = endLocals.Count > 1
                ? endLocals.Max(item => item.End.X)
                : endLocals.FirstOrDefault().End?.X ?? headLocal.X;
            var commonElbow = cropTransform.OfPoint(new XYZ(commonElbowX, headLocal.Y, 0));
            foreach (var item in endLocals)
            {
                tag.SetLeaderElbow(item.Reference, commonElbow);
            }
        }
        catch
        {
            try { tag.LeaderEndCondition = LeaderEndCondition.Attached; } catch { }
        }
    }
}
