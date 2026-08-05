namespace RevitAPP.Core.Models.CadGrid;

/// <summary>
/// A point in the active Revit view plane, expressed in millimeters.
/// </summary>
public readonly record struct CadGridPoint2(double Xmm, double Ymm)
{
    public static CadGridPoint2 operator +(CadGridPoint2 left, CadGridPoint2 right) =>
        new(left.Xmm + right.Xmm, left.Ymm + right.Ymm);

    public static CadGridPoint2 operator -(CadGridPoint2 left, CadGridPoint2 right) =>
        new(left.Xmm - right.Xmm, left.Ymm - right.Ymm);

    public static CadGridPoint2 operator *(CadGridPoint2 point, double factor) =>
        new(point.Xmm * factor, point.Ymm * factor);

    public double DistanceTo(CadGridPoint2 other)
    {
        var dx = other.Xmm - Xmm;
        var dy = other.Ymm - Ymm;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
