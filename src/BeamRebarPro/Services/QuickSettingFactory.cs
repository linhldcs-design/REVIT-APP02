using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Tạo cấu hình Quick Setting mặc định ("3xD16" thép chủ, đai D6 @150/@200, thép chống phình khi
///     h > 550). Logic thuần → test xUnit.
/// </summary>
public static class QuickSettingFactory
{
    public static QuickSettingModel CreateDefault() => new()
    {
        MainTop = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(16) },
        MainBottom = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(16) },

        TopAdditional = new AdditionalBarConfig { Enabled = false, Count = 1, Diameter = new RebarDiameter(16), Side = AdditionalBarSide.TopAtSupport },
        TopAdditionalLayer2 = new AdditionalBarConfig { Enabled = false, Count = 2, Diameter = new RebarDiameter(20), Layer = 2, Side = AdditionalBarSide.TopAtSupport },
        BottomAdditional = new AdditionalBarConfig { Enabled = false, Count = 1, Diameter = new RebarDiameter(16), Side = AdditionalBarSide.BottomAtMidspan },
        BottomAdditionalLayer2 = new AdditionalBarConfig { Enabled = false, Count = 2, Diameter = new RebarDiameter(20), Layer = 2, Side = AdditionalBarSide.BottomAtMidspan },

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
            Count = 2,
            TieDiameter = new RebarDiameter(6),
            SpacingMm = 500
        },

        Cover = new CoverSettings()
    };

    /// <summary>
    ///     Hợp nhất cấu hình chung với override của một nhịp: field nào override null → lấy giá trị chung.
    ///     Dùng khi tạo thép cho từng nhịp trong dầm nhiều nhịp.
    /// </summary>
    public static QuickSettingModel ResolveForSpan(QuickSettingModel baseModel, int spanIndex)
    {
        var ov = baseModel.SpanOverrides.FirstOrDefault(o => o.SpanIndex == spanIndex);
        if (ov is null) return baseModel;

        // Đai phụ (AdditionalStirrups) là cấu hình CHUNG — override per-span chỉ đổi bước/đường kính đai
        // chính, không build đai phụ. Nên khi dùng ov.Stirrup, merge lại AdditionalStirrups từ base để
        // đai phụ không bị nuốt mất.
        var stirrup = ov.Stirrup is { } ovStirrup
            ? ovStirrup with { AdditionalStirrups = baseModel.Stirrup.AdditionalStirrups }
            : baseModel.Stirrup;

        return baseModel with
        {
            MainTop = ov.MainTop ?? baseModel.MainTop,
            MainBottom = ov.MainBottom ?? baseModel.MainBottom,
            TopAdditional = ov.TopAdditional ?? baseModel.TopAdditional,
            BottomAdditional = ov.BottomAdditional ?? baseModel.BottomAdditional,
            Stirrup = stirrup
        };
    }
}
