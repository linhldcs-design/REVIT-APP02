using BeamRebar.Core.Models;

namespace BeamRebar.Core.Services;

/// <summary>
///     Tạo cấu hình Quick Setting mặc định khớp giá trị mẫu trong UI ("3xD16" thép chủ, đai D6 @150/@200,
///     thép chống phình khi h > 550). Logic thuần → test xUnit.
/// </summary>
public static class QuickSettingFactory
{
    public static QuickSettingModel CreateDefault() => new()
    {
        MainTop = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(16) },
        MainBottom = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(16) },

        TopAdditional = new AdditionalBarConfig { Enabled = false, Count = 1, Diameter = new RebarDiameter(16) },
        TopAdditionalLayer2 = new AdditionalBarConfig { Enabled = false, Count = 2, Diameter = new RebarDiameter(20), Layer = 2 },
        BottomAdditional = new AdditionalBarConfig { Enabled = false, Count = 1, Diameter = new RebarDiameter(16) },
        BottomAdditionalLayer2 = new AdditionalBarConfig { Enabled = false, Count = 2, Diameter = new RebarDiameter(20), Layer = 2 },

        Stirrup = new StirrupConfig
        {
            Diameter = new RebarDiameter(6),
            Mode = StirrupMode.TwoEnds,
            SpacingEndMm = 150,
            SpacingMidMm = 200
        },

        AntiBulge = new AntiBulgeConfig
        {
            Enabled = false,
            HeightThresholdMm = 550,
            Diameter = new RebarDiameter(12),
            SpacingMm = 500
        },

        Cover = new CoverSettings()
    };
}
