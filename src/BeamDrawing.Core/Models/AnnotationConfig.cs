namespace BeamDrawing.Core.Models;

/// <summary>
///     Cấu hình spot elevation (cao độ mặt đứng). Khớp nhóm "Spot Elevation" trong UI.
/// </summary>
public sealed record SpotElevationConfig
{
    public bool Enabled { get; init; } = true;
    public string? SpotTypeName { get; init; }

    /// <summary>Giá trị offset/làm tròn hiển thị trong ô số bên dưới Spot Elevation (mặc định 0).</summary>
    public double Offset { get; init; }
}

/// <summary>
///     Cấu hình dimension (kích thước). Khớp nhóm "Dimension" với SE/CS trong UI.
/// </summary>
public sealed record DimensionConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>Dimension type cho Sectional Elevation (ô "SE:").</summary>
    public string? SectionalDimTypeName { get; init; }

    /// <summary>Dimension type cho Cross Section (ô "CS:").</summary>
    public string? CrossSectionDimTypeName { get; init; }

    /// <summary>Hệ số khoảng cách dim (UI: "DIM. SPACING FACTOR", mặc định 6).</summary>
    public int SpacingFactor { get; init; } = 6;

    /// <summary>Khoảng cách dim tới dầm bên (UI: "DISTANCE DIM TO SIDE BEAM. (DS1)", mặc định 200).</summary>
    public double DistanceToSideBeam { get; init; } = 200;

    /// <summary>Khoảng cách dim tới mặt đáy (UI: "DISTANCE DIM TO BOT FACE", mặc định 200).</summary>
    public double DistanceToBotFace { get; init; } = 200;
}

/// <summary>
///     Cấu hình break line (nét cắt). Khớp nhóm "Break Line - Nét Cắt" trong UI.
/// </summary>
public sealed record BreakLineConfig
{
    public bool Enabled { get; init; } = true;
    public string? BreakLineFamilyName { get; init; }
}
