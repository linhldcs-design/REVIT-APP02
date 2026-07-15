namespace BeamDrawing.Core.Models;

/// <summary>
///     Các cờ bật/tắt ở thanh dưới cùng UI. v1 chỉ Sectional + Cross Section là chạy thật;
///     các cờ nâng cao (multi-beam, view 3D, pick pillow) model hoá để bind UI, hiện thực dần.
/// </summary>
public sealed record BeamDrawingFlags
{
    public bool LongSection { get; init; }
    public bool CrossSection { get; init; } = true;
    public bool CrossSectionForMultiBeam { get; init; }
    public bool PickPillowToDim { get; init; }
    public bool CreateView3D { get; init; }
}
