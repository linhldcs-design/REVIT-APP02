namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>Phân loại thanh thép dọc để vẽ leader minh họa đúng nhóm.</summary>
public enum PreviewBarRole { MainTop, MainBottom, Reinforce }

/// <summary>1 chấm thép trên sơ đồ preview, tọa độ chuẩn hóa [0,1] trong tiết diện (X trái→phải, Y đáy→đỉnh).</summary>
public sealed record PreviewBar(double X, double Y, double DiameterMm, PreviewBarRole Role);

/// <summary>
///     Tiết diện preview 1 station (GỐI/NHỊP) đọc từ dầm thật: kích thước + thanh thép dọc (đã phân nhóm) +
///     có đai hay không. Tọa độ [0,1] để View vẽ theo khung tỉ lệ đúng. Vẽ thép chủ/tăng cường + đai vuông + leader.
/// </summary>
public sealed record CrossSectionPreview(
    double WidthMm,
    double HeightMm,
    IReadOnlyList<PreviewBar> Bars,
    bool HasStirrup)
{
    public static CrossSectionPreview Empty { get; } = new(0, 0, Array.Empty<PreviewBar>(), false);
    public bool HasData => WidthMm > 0 && HeightMm > 0;
}
