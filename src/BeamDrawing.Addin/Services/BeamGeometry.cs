using Autodesk.Revit.DB;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Dữ liệu hình học của một dầm thẳng đã đọc xong — đủ để tính plane mặt cắt và sinh view.
///     Toạ độ ở đơn vị nội bộ Revit (feet); Section ở mm.
/// </summary>
public sealed record BeamGeometry(
    FamilyInstance Instance,
    Line LocationLine,
    XYZ Direction,
    BeamSection Section,
    double TopElevationFeet,
    double BottomElevationFeet)
{
    public XYZ Start => LocationLine.GetEndPoint(0);
    public XYZ End => LocationLine.GetEndPoint(1);
    public double LengthFeet => LocationLine.Length;
}
