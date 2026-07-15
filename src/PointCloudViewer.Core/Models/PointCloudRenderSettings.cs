using System;

namespace PointCloudViewer.Core.Models;

public sealed class PointCloudRenderSettings
{
    public VisualizationMode Mode { get; set; } = VisualizationMode.Rgb;
    public double PointSize { get; set; } = 6;
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Transparency { get; set; }
    public double XRayContrast { get; set; } = 80;

    public bool UsesBrightnessContrast => Mode == VisualizationMode.Rgb || Mode == VisualizationMode.ColorMap;
    public bool UsesXRayContrast => Mode == VisualizationMode.XRay;

    public static PointCloudRenderSettings CreateDefault()
    {
        return new PointCloudRenderSettings();
    }

    public PointCloudRenderSettings Clone()
    {
        return new PointCloudRenderSettings
        {
            Mode = Mode,
            PointSize = PointSize,
            Brightness = Brightness,
            Contrast = Contrast,
            Transparency = Transparency,
            XRayContrast = XRayContrast
        };
    }

    public void Clamp()
    {
        PointSize = Clamp(PointSize, RenderSettingLimits.MinPointSize, RenderSettingLimits.MaxPointSize);
        Brightness = Clamp(Brightness, RenderSettingLimits.MinBrightness, RenderSettingLimits.MaxBrightness);
        Contrast = Clamp(Contrast, RenderSettingLimits.MinContrast, RenderSettingLimits.MaxContrast);
        Transparency = Clamp(Transparency, RenderSettingLimits.MinTransparency, RenderSettingLimits.MaxTransparency);
        XRayContrast = Clamp(XRayContrast, RenderSettingLimits.MinXRayContrast, RenderSettingLimits.MaxXRayContrast);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}
