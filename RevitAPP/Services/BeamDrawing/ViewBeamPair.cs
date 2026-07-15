using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Cặp view vừa tạo + dầm nguồn + geometry, truyền cho annotator ở T2. IsCross phân biệt mặt cắt ngang.
/// </summary>
public sealed record ViewBeamPair(
    ViewSection View,
    FamilyInstance Beam,
    BeamGeometry Geometry,
    bool IsCross,
    double? Station = null,
    bool? IsSupportZone = null); // true=GỐI, false=NHỊP (phân vùng độc lập với Station t thật, cho dầm nhiều nhịp)

/// <summary>
///     Đặt annotation (rebar tag / dimension / spot elevation) lên các view đã commit. Cài đặt ở Phase 5.
/// </summary>
public interface IBeamAnnotator
{
    void Annotate(Document doc, IReadOnlyList<ViewBeamPair> pairs, BeamDrawingSetting setting, BeamDrawingResult result);
}
