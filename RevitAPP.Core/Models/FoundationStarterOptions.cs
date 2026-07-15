namespace RevitAPP.Core.Models;

/// <summary>Hướng bẻ chân thép chờ (trong mặt phẳng tiết diện, trước khi xoay theo cột).</summary>
public enum StarterBendDirection
{
    Up,     // +Y
    Down,   // −Y
    Left,   // −X
    Right   // +X
}

/// <summary>
///     Tùy chọn thép chờ móng (starter bar) — bẻ chữ L tại chân cột tầng dưới cùng:
///     đoạn đứng Hm vươn lên trong cột (nối chồng với thép chủ), chân ngang Lb nằm trong móng.
/// </summary>
/// <param name="Enabled">Bật sinh thép chờ móng.</param>
/// <param name="HmMm">Chiều cao đoạn đứng vươn lên trong cột (mm).</param>
/// <param name="LbMm">Chiều dài chân ngang nằm trong móng (mm).</param>
/// <param name="Direction">Hướng bẻ chân ngang (khi không bật bẻ 2 bên).</param>
/// <param name="SplitBothSides">
///     true = bẻ chân đối xứng SANG 2 BÊN: thanh nửa âm bẻ một phía, thanh nửa dương bẻ phía ngược lại
///     (theo trục của <see cref="Direction"/>) — chân chĩa ra ngoài 2 phía thay vì cùng 1 bên.
/// </param>
public sealed record FoundationStarterOptions(
    bool Enabled = false,
    double HmMm = 250,
    double LbMm = 200,
    StarterBendDirection Direction = StarterBendDirection.Right,
    bool SplitBothSides = false)
{
    /// <summary>Vector hướng chân (đơn vị) trong mặt phẳng tiết diện.</summary>
    public (double X, double Y) DirectionVector => Direction switch
    {
        StarterBendDirection.Up => (0, 1),
        StarterBendDirection.Down => (0, -1),
        StarterBendDirection.Left => (-1, 0),
        _ => (1, 0)
    };
}
