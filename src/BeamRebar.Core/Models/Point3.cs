namespace BeamRebar.Core.Models;

/// <summary>
///     Điểm 3D thuần (feet, đơn vị nội bộ Revit) — tách khỏi <c>Autodesk.Revit.DB.XYZ</c> để
///     <c>BeamRebar.Core</c> không phụ thuộc RevitAPI, cho phép test span model bằng xUnit out-of-process.
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
