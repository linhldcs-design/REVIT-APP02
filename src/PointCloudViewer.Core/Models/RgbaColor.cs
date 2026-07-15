using System;

namespace PointCloudViewer.Core.Models;

public readonly struct RgbaColor : IEquatable<RgbaColor>
{
    public RgbaColor(byte red, byte green, byte blue, byte alpha = 255)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public byte Alpha { get; }

    public bool Equals(RgbaColor other)
    {
        return Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;
    }

    public override bool Equals(object? obj)
    {
        return obj is RgbaColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Red.GetHashCode();
            hash = (hash * 397) ^ Green.GetHashCode();
            hash = (hash * 397) ^ Blue.GetHashCode();
            hash = (hash * 397) ^ Alpha.GetHashCode();
            return hash;
        }
    }
}
