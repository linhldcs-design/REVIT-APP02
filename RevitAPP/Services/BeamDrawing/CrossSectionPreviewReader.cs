using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Đọc tiết diện THẬT của dầm (kích thước + vị trí thanh thép dọc) tại 1 station để vẽ sơ đồ preview trong dialog.
///     Tọa độ thanh chuẩn hóa [0,1] theo khung tiết diện (X ngang, Y đứng). Bỏ đai (chỉ vẽ thép dọc dạng chấm).
/// </summary>
public sealed class CrossSectionPreviewReader
{
    /// <summary>Preview tại station t∈[0,1] dọc trục dầm. Empty nếu không đọc được.</summary>
    public CrossSectionPreview Read(Document doc, FamilyInstance beam, double t)
    {
        var box = beam.get_BoundingBox(null);
        if (box == null) return CrossSectionPreview.Empty;

        // Trục DỌC dầm = trục ngang (X/Y) DÀI hơn. Bề rộng tiết diện = trục ngang còn lại; chiều cao = Z.
        var beamAlongX = (box.Max.X - box.Min.X) >= (box.Max.Y - box.Min.Y);
        var wFt = beamAlongX ? box.Max.Y - box.Min.Y : box.Max.X - box.Min.X;
        var hFt = box.Max.Z - box.Min.Z;

        var widthMm = wFt * 304.8;
        var heightMm = hFt * 304.8;
        var b = MmParam(beam, "b") ?? MmParam(beam, "Width");
        var h = MmParam(beam, "h") ?? MmParam(beam, "Height") ?? MmParam(beam, "Depth");
        if (b is > 0) widthMm = b.Value;
        if (h is > 0) heightMm = h.Value;
        if (widthMm <= 0 || heightMm <= 0) return CrossSectionPreview.Empty;

        // Chỉ trả KÍCH THƯỚC thật (rộng×cao) để View vẽ khung ĐÚNG TỈ LỆ. Bố trí thép dùng sơ đồ chuẩn ở View
        // (đọc geometry rebar không map sạch sang mặt cắt 2D → lỏm). Cờ có đai để View vẽ khung đai.
        var hasStirrup = HostedRebars(doc, beam.Id).Any(IsStirrup);
        return new CrossSectionPreview(widthMm, heightMm, Array.Empty<PreviewBar>(), hasStirrup);
    }

    private static double? MmParam(Element e, string name)
    {
        var p = e.LookupParameter(name);
        if (p == null || p.StorageType != StorageType.Double) return null;
        return p.AsDouble() * 304.8;
    }

    private static IEnumerable<Rebar> HostedRebars(Document doc, ElementId hostId) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>()
            .Where(r => r.GetHostId() == hostId);

    private static bool IsStirrup(Rebar rebar)
    {
        try
        {
            return rebar.Document.GetElement(rebar.GetShapeId()) is RebarShape { RebarStyle: RebarStyle.StirrupTie };
        }
        catch { return false; }
    }
}
