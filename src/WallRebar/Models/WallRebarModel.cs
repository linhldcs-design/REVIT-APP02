namespace WallRebar.Models;

/// <summary>
///     Toàn bộ cấu hình thép tường từ dialog "Wall Rebar". Tường bố trí 2 lưới (mặt A &amp; mặt B), mỗi lưới
///     gồm thanh dọc (vertical, theo chiều cao) + thanh ngang (horizontal, theo chiều dài). Lớp thứ 3 = thép
///     giằng (tie) nối 2 mặt và được tạo theo cấu hình hàng thứ 3, độc lập với
///     <see cref="DrawAdditionalRebar"/>.
///     3 hàng "Ø@spacing" trong dialog map: hàng 1 → <see cref="Vertical"/>, hàng 2 → <see cref="Horizontal"/>,
///     hàng 3 → <see cref="Tie"/>.
/// </summary>
public sealed record WallRebarModel
{
    public CoverSettings Cover { get; init; } = new();

    // --- 3 hàng cấu hình thanh (Cross Section) ---
    public WallLayerConfig Vertical { get; init; } = new() { SpacingMm = 150 };
    public WallLayerConfig Horizontal { get; init; } = new() { SpacingMm = 200 };
    public WallLayerConfig Tie { get; init; } = new() { SpacingMm = 500 };

    // --- Móc đầu thanh dọc (Cross Section: Hook Type trên & dưới) ---
    public HookType TopHookType { get; init; } = HookType.Closed;
    public HookBendDirection TopHookDirection { get; init; } = HookBendDirection.Inward;
    public double TopHookLengthMm { get; init; } = 100;
    public HookType BottomHookType { get; init; } = HookType.Closed;
    public HookBendDirection BottomHookDirection { get; init; } = HookBendDirection.Inward;
    public double BottomHookLengthMm { get; init; } = 200;

    // --- Offset theo chiều cao (Cross Section) ---
    public double TopOffsetMm { get; init; }
    public double BottomOffsetMm { get; init; } = 250;

    // --- Offset theo chiều dài (Longitudinal Section) ---
    public double HorizontalOffsetStartMm { get; init; }
    public double HorizontalOffsetEndMm { get; init; }

    /// <summary>
    ///     Bật thép dọc tăng cường vùng dưới ở giữa các thanh dọc chính trên hai mặt vách.
    ///     Không dùng để bật/tắt thép giằng (tie); tie được điều khiển bởi <see cref="Tie"/>.
    /// </summary>
    public bool DrawAdditionalRebar { get; init; }

    /// <summary>Vẽ thép tăng cường trên mặt trong của vách.</summary>
    public bool DrawAdditionalRebarInterior { get; init; } = true;

    /// <summary>Vẽ thép tăng cường trên mặt ngoài của vách.</summary>
    public bool DrawAdditionalRebarExterior { get; init; } = true;
}
