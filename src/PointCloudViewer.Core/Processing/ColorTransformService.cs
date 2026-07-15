using System;
using PointCloudViewer.Core.Models;

namespace PointCloudViewer.Core.Processing;

public sealed class ColorTransformService
{
    public RgbaColor Transform(PointRecord point, PointCloudRenderSettings settings)
    {
        settings.Clamp();

        var alpha = (byte)Math.Round(255 * (1 - settings.Transparency / 100d));

        switch (settings.Mode)
        {
            case VisualizationMode.Normal:
                return new RgbaColor(
                    NormalToChannel(point.NormalX),
                    NormalToChannel(point.NormalY),
                    NormalToChannel(point.NormalZ),
                    alpha);
            case VisualizationMode.XRay:
                var xray = ScaleToByte(70 + settings.XRayContrast * 1.85);
                return new RgbaColor(xray, xray, xray, alpha);
            case VisualizationMode.ColorMap:
                return ColorMap(point.Scalar, alpha);
            case VisualizationMode.Rgb:
            default:
                return AdjustRgb(point.Color, settings.Brightness, settings.Contrast, alpha);
        }
    }

    private static RgbaColor AdjustRgb(RgbaColor color, double brightness, double contrast, byte alpha)
    {
        var factor = (259 * (contrast + 255)) / (255 * (259 - contrast));
        return new RgbaColor(
            ScaleToByte(factor * (color.Red - 128) + 128 + brightness),
            ScaleToByte(factor * (color.Green - 128) + 128 + brightness),
            ScaleToByte(factor * (color.Blue - 128) + 128 + brightness),
            alpha);
    }

    private static RgbaColor ColorMap(double scalar, byte alpha)
    {
        var normalized = Math.Min(Math.Max(scalar, 0), 1);
        var red = ScaleToByte(255 * normalized);
        var green = ScaleToByte(180 * (1 - Math.Abs(normalized - 0.5) * 2));
        var blue = ScaleToByte(255 * (1 - normalized));
        return new RgbaColor(red, green, blue, alpha);
    }

    private static byte NormalToChannel(double value)
    {
        return ScaleToByte((Math.Min(Math.Max(value, -1), 1) + 1) * 127.5);
    }

    private static byte ScaleToByte(double value)
    {
        return (byte)Math.Round(Math.Min(Math.Max(value, 0), 255));
    }
}
