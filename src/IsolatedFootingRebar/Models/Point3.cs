namespace IsolatedFootingRebar.Models;

/// <summary>
///     Điểm 3D thuần (feet, hệ Revit) — tách khỏi Autodesk.Revit.DB.XYZ để model test được
///     out-of-process. Adapter sang XYZ nằm ở lớp Revit-API khi dựng curve.
/// </summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public double DistanceTo(Point3 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
