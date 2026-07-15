namespace SheetAlign.Addin.Services;

/// <summary>
///     Toán căn chỉnh viewport thuần (không phụ thuộc Revit) — giao 2 đường lưới trục trong
///     mặt phẳng giấy và tính vector dịch để 1 điểm neo trùng vị trí trên mọi sheet.
/// </summary>
public static class SheetAlignMath
{
    /// <summary>
    ///     Giao 2 đường thẳng cho bởi điểm + hướng trong mặt phẳng XY.
    ///     Trả null nếu hai đường gần song song.
    /// </summary>
    public static (double X, double Y)? IntersectLines(
        (double X, double Y) p1, (double X, double Y) dir1,
        (double X, double Y) p2, (double X, double Y) dir2)
    {
        var cross = dir1.X * dir2.Y - dir1.Y * dir2.X;
        if (Math.Abs(cross) < 1e-9)
        {
            return null;
        }

        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var t = (dx * dir2.Y - dy * dir2.X) / cross;

        return (p1.X + t * dir1.X, p1.Y + t * dir1.Y);
    }

    /// <summary>Vector dịch: đưa điểm neo hiện tại trên giấy về vị trí điểm neo mẫu.</summary>
    public static (double X, double Y) ComputeDelta(
        (double X, double Y) paperAnchorMaster,
        (double X, double Y) paperAnchorCurrent)
    {
        return (paperAnchorMaster.X - paperAnchorCurrent.X,
                paperAnchorMaster.Y - paperAnchorCurrent.Y);
    }
}
