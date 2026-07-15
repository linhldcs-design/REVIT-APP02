namespace RevitAPP.Core.Models;

/// <summary>
///     Một loại thanh thép (RebarBarType) trong dự án, dạng thuần (không phụ thuộc Revit API).
///     <paramref name="BarTypeId"/> giữ giá trị long của ElementId; lớp Revit chuyển đổi ngược lại.
/// </summary>
public sealed record RebarBarTypeOption(long BarTypeId, string Name, double DiameterMm)
{
    /// <summary>Nhãn hiển thị trên combo: "Ø{đường kính} – {tên}".</summary>
    public string Display => $"Ø{DiameterMm:0.#} – {Name}";

    public override string ToString() => Display;
}
