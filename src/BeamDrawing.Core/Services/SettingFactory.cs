using BeamDrawing.Core.Models;

namespace BeamDrawing.Core.Services;

/// <summary>
///     Tạo setting mặc định khớp giá trị hiển thị trong UI ảnh tham chiếu (scale 25, spacing factor 6,
///     DS1 = 200...). Dùng làm điểm khởi đầu khi user chưa load setting nào.
/// </summary>
public static class SettingFactory
{
    public static BeamDrawingSetting CreateDefault() => new()
    {
        Name = "BEAM-DEFAULT",
        TagMapping = new RebarTagMapping
        {
            RebarBreakSymbol = true
        },
        Sectional = new ViewConfig { Scale = 25 },
        CrossSection = new ViewConfig { Scale = 25 },
        SpotElevation = new SpotElevationConfig { Enabled = true, Offset = 0 },
        Dimension = new DimensionConfig
        {
            Enabled = true,
            SpacingFactor = 6,
            DistanceToSideBeam = 200,
            DistanceToBotFace = 200
        },
        BreakLine = new BreakLineConfig { Enabled = true },
        SheetNumber = "KC-0011.1.1",
        SheetName = "CHI TIẾT THÉP DẦM",
        Flags = new BeamDrawingFlags { CrossSection = true }
    };
}
