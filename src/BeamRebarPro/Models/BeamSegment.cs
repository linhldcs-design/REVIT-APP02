namespace BeamRebarPro.Models;

/// <summary>
///     Một dầm vật lý đã đọc geometry: trục (feet) + tiết diện + cao độ mặt trên/dưới THẬT (lấy từ
///     bounding box). <see cref="LateralOffsetFeet"/> = lệch ngang từ location line tới tâm khối bê tông
///     (theo phương ngang tiết diện) — bù justification của dầm để thép căn đúng giữa. Input để gom
///     thành <see cref="BeamRun"/>.
/// </summary>
public sealed record BeamSegment(
    Point3 Start,
    Point3 End,
    BeamSection Section,
    double TopElevationFeet,
    double BottomElevationFeet,
    double LateralOffsetFeet = 0)
{
    public double LengthFeet => Start.DistanceTo(End);
}
