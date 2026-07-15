namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Hình học dầm đọc từ Revit, biểu diễn thuần (feet) để tính section box không phụ thuộc Revit API.
///     Start/End = 2 đầu location curve; Width/Height = tiết diện; TopZ/BottomZ = cao độ mặt trên/dưới (solid thật).
/// </summary>
public sealed record BeamGeometry(
    Point3 Start,
    Point3 End,
    double WidthFeet,
    double HeightFeet,
    double TopZFeet,
    double BottomZFeet)
{
    /// <summary>Chiều dài dầm (feet) theo khoảng cách 2 đầu location curve.</summary>
    public double LengthFeet => Start.DistanceTo(End);
}

/// <summary>Điểm 3D thuần (feet). Tránh kéo Autodesk.Revit.DB.XYZ vào lớp Core.</summary>
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
