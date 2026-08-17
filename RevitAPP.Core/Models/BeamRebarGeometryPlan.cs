namespace RevitAPP.Core.Models;

/// <summary>Loại thanh thép trong dầm. Quyết định màu và cách vẽ ở preview.</summary>
public enum BeamRebarPathKind
{
    MainTop,
    MainBottom,
    AdditionalTop,
    AdditionalBottom,
    /// <summary>Đai chính. Vùng đai dày/thưa phân biệt qua <see cref="BeamRebarPath.Zone"/>.</summary>
    Stirrup,
    /// <summary>Đai tăng cường quanh vị trí dầm phụ gác lên dầm chính.</summary>
    StirrupSecondary,
    AdditionalStirrupClosed,
    AdditionalStirrupCHook,
    /// <summary>Đai C giữ thép gia cường lớp 2.</summary>
    Layer2Tie,
    AntiBulgeBar,
    AntiBulgeTie
}

/// <summary>Khối bê tông vẽ nền cho thép, dạng khung x-ray.</summary>
public enum BeamRebarContextKind
{
    Beam,
    Column,
    CrossBeam
}

/// <summary>
/// Đường tim một thanh thép. Các điểm liên tiếp tạo thành đoạn thẳng — khớp cách builder dựng curve.
/// Toạ độ mm trong hệ toạ độ mô hình.
/// </summary>
public sealed record BeamRebarPath(
    int SpanIndex,
    BeamRebarPathKind Kind,
    double DiameterMm,
    IReadOnlyList<GeometryPoint3D> Points,
    int Layer = 1,
    /// <summary>Nhóm vùng để preview tô màu/gom nhóm, vd "End1", "Mid", "End2".</summary>
    string? Zone = null,
    /// <summary>Đai kín: khi vẽ phải nối điểm cuối về điểm đầu.</summary>
    bool IsClosedLoop = false);

/// <summary>
/// Khối bê tông chạy theo tuyến, mô tả bằng tâm tiết diện hai đầu. Dầm nằm ngang chạy theo tuyến nên
/// biểu diễn này tự nhiên hơn kiểu tâm + góc xoay dùng cho cột.
/// </summary>
public sealed record BeamRebarContextVolume(
    BeamRebarContextKind Kind,
    GeometryPoint3D StartCenterMm,
    GeometryPoint3D EndCenterMm,
    double WidthMm,
    double HeightMm);

/// <summary>
/// Mô tả đầy đủ, độc lập Revit, của toàn bộ thép trong một tuyến dầm. Preview và builder đọc chung
/// bản mô tả này nên hình ảnh xem trước khớp với thép được tạo trong mô hình.
/// </summary>
public sealed record BeamRebarGeometryPlan(
    IReadOnlyList<BeamRebarContextVolume> Context,
    IReadOnlyList<BeamRebarPath> Paths,
    /// <summary>Vị trí gối tính từ đầu tuyến (mm) — để mặt cắt dọc vẽ vạch gối và đánh số nhịp.</summary>
    IReadOnlyList<double> SupportStationsMm,
    double TotalLengthMm)
{
    public static BeamRebarGeometryPlan Empty { get; } = new([], [], [], 0);

    public IEnumerable<BeamRebarPath> Longitudinal => Paths.Where(p => p.Kind
        is BeamRebarPathKind.MainTop or BeamRebarPathKind.MainBottom
        or BeamRebarPathKind.AdditionalTop or BeamRebarPathKind.AdditionalBottom);

    public IEnumerable<BeamRebarPath> Stirrups => Paths.Where(p => p.Kind
        is BeamRebarPathKind.Stirrup or BeamRebarPathKind.StirrupSecondary
        or BeamRebarPathKind.AdditionalStirrupClosed or BeamRebarPathKind.AdditionalStirrupCHook
        or BeamRebarPathKind.Layer2Tie);

    public IEnumerable<BeamRebarPath> AntiBulge => Paths.Where(p => p.Kind
        is BeamRebarPathKind.AntiBulgeBar or BeamRebarPathKind.AntiBulgeTie);

    public bool IsEmpty => Paths.Count == 0 && Context.Count == 0;
}
