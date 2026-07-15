namespace IsolatedFootingRebar.Models;

/// <summary>
///     Hình học móng đơn đã đọc từ element (đơn vị feet, hệ Revit). Gốc tọa độ tại TÂM đáy đế móng.
///     Móng cốc: <see cref="Pedestal"/> mô tả cổ móng nhô lên; null nếu là móng bằng (không có cổ).
/// </summary>
public sealed record FootingGeometry
{
    /// <summary>Tâm đáy đế móng (feet).</summary>
    public required Point3 BaseCenter { get; init; }

    /// <summary>Hướng phương X của thép chính (đơn vị, mặt phẳng XY).</summary>
    public required Point3 DirX { get; init; }

    /// <summary>Hướng phương Y (= Up × DirX, đơn vị).</summary>
    public required Point3 DirY { get; init; }

    /// <summary>Bề rộng đế theo DirX (feet).</summary>
    public required double WidthXFeet { get; init; }

    /// <summary>Bề rộng đế theo DirY (feet).</summary>
    public required double WidthYFeet { get; init; }

    /// <summary>Cao độ đáy đế (Z, feet).</summary>
    public required double BottomZFeet { get; init; }

    /// <summary>Cao độ mặt trên đế móng (Z, feet) — nơi đặt lưới thép trên của đế.</summary>
    public required double BaseTopZFeet { get; init; }

    /// <summary>Cổ móng (nếu móng cốc). null = móng bằng.</summary>
    public PedestalBox? Pedestal { get; init; }

    /// <summary>Chiều cao đế móng (feet).</summary>
    public double BaseHeightFeet => BaseTopZFeet - BottomZFeet;
}

/// <summary>Cổ móng (pedestal) hình hộp: tâm đáy + bề rộng 2 phương + cao độ đỉnh.</summary>
public sealed record PedestalBox
{
    public double CenterU { get; init; } = 0.5;
    public double CenterV { get; init; } = 0.5;
    public required double WidthXFeet { get; init; }
    public required double WidthYFeet { get; init; }
    public required double TopZFeet { get; init; }
}
