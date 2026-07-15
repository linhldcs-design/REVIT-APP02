using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Cho người dùng pick một line trong view để chỉ định hướng thép chính (phương X). Trả vector hướng
///     (chiếu lên mặt phẳng XY, chuẩn hóa) hoặc null nếu hủy. Chạy trong API context (qua ExternalEvent).
/// </summary>
public sealed class DirectionLinePicker
{
    public Point3? PickDirection(UIDocument uiDocument)
    {
        try
        {
            var reference = uiDocument.Selection.PickObject(
                ObjectType.Element, "Chọn một line/cạnh để chỉ định hướng thép chính (phương X).");
            var element = uiDocument.Document.GetElement(reference);

            var curve = (element?.Location as LocationCurve)?.Curve
                        ?? (element?.GetGeometryObjectFromReference(reference) as Curve);
            if (curve is null) return null;

            var dir = curve.GetEndPoint(1) - curve.GetEndPoint(0);
            var planar = new XYZ(dir.X, dir.Y, 0);
            if (planar.GetLength() < 1e-6) return null;

            planar = planar.Normalize();
            return new Point3(planar.X, planar.Y, planar.Z);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}
