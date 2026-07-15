using BeamRebar.Core.Models;
using BeamRebar.Core.Services;
using Xunit;

namespace BeamRebar.Core.Tests;

public class QuickSettingFactoryTests
{
    [Fact]
    public void Default_matches_ui_sample_values()
    {
        var m = QuickSettingFactory.CreateDefault();

        // 3xD16 thép chủ trên & dưới (như ảnh).
        Assert.Equal(3, m.MainTop.Count);
        Assert.Equal(16, m.MainTop.Diameter.Millimeters);
        Assert.Equal(3, m.MainBottom.Count);
        Assert.Equal(16, m.MainBottom.Diameter.Millimeters);

        // Đai D6 @150 (đầu) / @200 (giữa), 2 đầu.
        Assert.Equal(6, m.Stirrup.Diameter.Millimeters);
        Assert.Equal(StirrupMode.TwoEnds, m.Stirrup.Mode);
        Assert.Equal(150, m.Stirrup.SpacingEndMm);
        Assert.Equal(200, m.Stirrup.SpacingMidMm);

        // Chống phình ngưỡng 550mm.
        Assert.Equal(550, m.AntiBulge.HeightThresholdMm);

        // Cover mặc định 25mm.
        Assert.Equal(25, m.Cover.TopMm);
    }
}
