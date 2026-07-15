namespace IsolatedFootingRebar.Models;

/// <summary>
///     Cấu hình hoàn chỉnh từ dialog "Isolated Footing" — output của UI, input của orchestrator tạo Rebar.
///     Immutable; gom toàn bộ các lớp thép móng đơn. Mỗi lớp có cờ bật/tắt (khớp checkbox trên tab) +
///     cấu hình theo 2 phương X/Y.
/// </summary>
public sealed record FootingRebarModel
{
    // --- Lưới đáy (Bottom Layer) ---
    public bool BottomEnabled { get; init; } = true;
    public LayerBarConfig BottomX { get; init; } = new();
    public LayerBarConfig BottomY { get; init; } = new();

    // --- Lưới trên (Top Layer) ---
    public bool TopEnabled { get; init; } = true;
    public LayerBarConfig TopX { get; init; } = new() { HookLengthMm = 400 };
    public LayerBarConfig TopY { get; init; } = new() { HookLengthMm = 400 };

    // --- Lớp giữa (Mid Layer) — có thể nhiều lớp theo chiều cao ---
    public bool MidEnabled { get; init; }
    public int MidLayers { get; init; } = 2;
    public LayerBarConfig MidX { get; init; } = new() { SpacingMm = 200, HookLengthMm = 200 };
    public LayerBarConfig MidY { get; init; } = new() { SpacingMm = 200, HookLengthMm = 200 };

    // --- Thép đứng gia cường cổ móng (Vertical Reinforced) ---
    public bool VerticalEnabled { get; init; }
    public VerticalBarConfig Vertical { get; init; } = new();

    // --- Đai ngang quanh cổ móng (Horizontal Reinforced) ---
    public bool HorizontalEnabled { get; init; } = true;
    public HorizontalStirrupConfig Horizontal { get; init; } = new();

    public CoverSettings Cover { get; init; } = new();

    /// <summary>Hướng thép chính phương X (đơn vị feet, mặt phẳng XY). null → lấy theo hướng family móng.
    ///     Người dùng "Pick line to specify direction of main Rebar" sẽ ghi đè field này.</summary>
    public Point3? DirXOverride { get; init; }
}

/// <summary>Thanh kê đỡ lưới trên (bar chair) — rải theo bước/số lượng 2 phương; kích thước ghế tùy chỉnh.</summary>
public sealed record VerticalBarConfig
{
    public RebarDiameter Diameter { get; init; } = new(6);
    public bool UseSpacing { get; init; } = true;
    public double SpacingXMm { get; init; } = 200;
    public double SpacingYMm { get; init; } = 200;
    public int CountX { get; init; } = 5;
    public int CountY { get; init; } = 5;

    /// <summary>Chiều dài đoạn chân nằm ngang mỗi đầu ghế (tựa lên lưới đáy), mm.</summary>
    public double HookLengthMm { get; init; } = 200;

    /// <summary>Bề ngang đỉnh ghế (khoảng cách 2 thân đứng), mm. 0 → tự suy theo chiều cao.</summary>
    public double WidthMm { get; init; } = 300;
}

/// <summary>Đai ngang quanh cổ móng: kín (closed) hoặc hở (segmented), nhiều lớp theo chiều cao.</summary>
public sealed record HorizontalStirrupConfig
{
    public RebarDiameter DiameterX { get; init; } = new(6);
    public RebarDiameter DiameterY { get; init; } = new(6);
    public bool Closed { get; init; } = true;
    public bool HookEnabled { get; init; } = true;
    public double HookLengthMm { get; init; } = 100;
    public int Layers { get; init; } = 1;
}
