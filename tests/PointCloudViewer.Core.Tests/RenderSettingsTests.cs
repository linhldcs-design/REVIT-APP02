using PointCloudViewer.Core.Caching;
using PointCloudViewer.Core.Models;

namespace PointCloudViewer.Core.Tests;

public sealed class RenderSettingsTests
{
    [Fact]
    public void Clamp_keeps_settings_inside_supported_ranges()
    {
        var settings = new PointCloudRenderSettings
        {
            PointSize = 500,
            Brightness = -500,
            Contrast = 500,
            Transparency = 200,
            XRayContrast = -20
        };

        settings.Clamp();

        Assert.Equal(RenderSettingLimits.MaxPointSize, settings.PointSize);
        Assert.Equal(RenderSettingLimits.MinBrightness, settings.Brightness);
        Assert.Equal(RenderSettingLimits.MaxContrast, settings.Contrast);
        Assert.Equal(RenderSettingLimits.MaxTransparency, settings.Transparency);
        Assert.Equal(RenderSettingLimits.MinXRayContrast, settings.XRayContrast);
    }

    [Fact]
    public void Classifier_rebuilds_batches_when_point_size_changes()
    {
        var classifier = new RenderSettingsChangeClassifier();
        var before = PointCloudRenderSettings.CreateDefault();
        var after = before.Clone();
        after.PointSize += 1;

        var impact = classifier.Classify(before, after);

        Assert.Equal(RenderSettingsChangeImpact.RebuildBatches, impact);
    }

    [Fact]
    public void Classifier_redraws_only_when_color_setting_changes()
    {
        var classifier = new RenderSettingsChangeClassifier();
        var before = PointCloudRenderSettings.CreateDefault();
        var after = before.Clone();
        after.Brightness = 20;

        var impact = classifier.Classify(before, after);

        Assert.Equal(RenderSettingsChangeImpact.RedrawOnly, impact);
    }
}
