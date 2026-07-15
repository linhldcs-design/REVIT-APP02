using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Hình học móng đọc từ Revit, biểu diễn thuần (feet) để tính section box không phụ thuộc Revit API.
///     Center = tâm đế (XY), Z tại đáy đế. CutDirection = phương cắt (đơn vị, XY). Bao đủ móng + cổ + cột.
/// </summary>
public sealed record FootingSectionGeometry(
    Point3 Center,
    double WidthFeet,       // bề rộng đế theo phương cắt (ngang MC)
    double TopZFeet,        // đỉnh section (bao đỉnh cột)
    double BottomZFeet,     // đáy section (đáy đế)
    Point3 CutDirection,    // phương cắt trên mặt phẳng ngang (đã normalize, Z=0)
    string Mark,            // Mark móng (đặt tên view / title)
    double? ViewBottomZFeet = null,
    double? ViewTopZFeet = null);
