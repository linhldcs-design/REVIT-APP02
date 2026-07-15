using PointCloudViewer.Core.Models;
using PointCloudViewer.Core.Processing;

namespace PointCloudViewer.Core.Tests;

public sealed class ColorTransformServiceTests
{
    [Fact]
    public void Rgb_mode_applies_transparency_to_alpha()
    {
        var service = new ColorTransformService();
        var point = new PointRecord(0, 0, 0, new RgbaColor(10, 20, 30));
        var settings = PointCloudRenderSettings.CreateDefault();
        settings.Transparency = 50;

        var color = service.Transform(point, settings);

        Assert.Equal(128, color.Alpha);
    }

    [Fact]
    public void Normal_mode_maps_normal_axis_to_color_channel()
    {
        var service = new ColorTransformService();
        var point = new PointRecord(0, 0, 0, new RgbaColor(0, 0, 0), nx: 1, ny: 0, nz: -1);
        var settings = PointCloudRenderSettings.CreateDefault();
        settings.Mode = VisualizationMode.Normal;

        var color = service.Transform(point, settings);

        Assert.Equal(255, color.Red);
        Assert.Equal(128, color.Green);
        Assert.Equal(0, color.Blue);
    }

    [Fact]
    public void Color_map_mode_maps_low_scalar_to_blue()
    {
        var service = new ColorTransformService();
        var point = new PointRecord(0, 0, 0, new RgbaColor(0, 0, 0), scalar: 0);
        var settings = PointCloudRenderSettings.CreateDefault();
        settings.Mode = VisualizationMode.ColorMap;

        var color = service.Transform(point, settings);

        Assert.Equal(0, color.Red);
        Assert.Equal(255, color.Blue);
    }
}
