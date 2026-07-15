namespace RevitAPP.Core.Models;

/// <summary>Kiểu bố trí đai trong tiết diện cột.</summary>
public enum SectionStirrupType
{
    /// <summary>Đai kín đơn bao quanh chu vi (mặc định).</summary>
    ClosedTie,

    /// <summary>Đai kín + các móc chéo (crosstie) giằng thanh giữa.</summary>
    Crosstie,

    /// <summary>Nhiều đai con tách rời ôm từng nhóm thanh.</summary>
    Separated
}

/// <summary>Phương đặt móc chéo (crosstie) khi kiểu đai = Crosstie.</summary>
public enum CrosstieDirection
{
    /// <summary>Chỉ phương X — móc đứng nối thanh giữa mặt trên/dưới.</summary>
    X,

    /// <summary>Chỉ phương Y — móc ngang nối thanh giữa mặt trái/phải.</summary>
    Y,

    /// <summary>Cả hai phương X và Y (linh hoạt).</summary>
    Both
}
