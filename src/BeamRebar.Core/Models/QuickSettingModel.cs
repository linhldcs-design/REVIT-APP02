namespace BeamRebar.Core.Models;

/// <summary>
///     Cấu hình hoàn chỉnh từ dialog Quick Setting — output của UI (Phase 3), input của orchestrator
///     tạo Rebar (Phase 4). Immutable; gom toàn bộ các nhóm thép + lớp bảo vệ.
/// </summary>
public sealed record QuickSettingModel
{
    // Thép chủ (item 2, 6).
    public MainBarConfig MainTop { get; init; } = new();
    public MainBarConfig MainBottom { get; init; } = new();

    // Thép gia cường (item 1, 3, 4, 7).
    public AdditionalBarConfig TopAdditional { get; init; } = new();
    public AdditionalBarConfig TopAdditionalLayer2 { get; init; } = new() { Layer = 2 };
    public AdditionalBarConfig BottomAdditional { get; init; } = new();
    public AdditionalBarConfig BottomAdditionalLayer2 { get; init; } = new() { Layer = 2 };

    // Cốt đai (item 5).
    public StirrupConfig Stirrup { get; init; } = new();

    // Thép chống phình (item 12).
    public AntiBulgeConfig AntiBulge { get; init; } = new();

    public CoverSettings Cover { get; init; } = new();
}
