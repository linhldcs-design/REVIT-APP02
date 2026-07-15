namespace BeamRebarPro.Models;

/// <summary>
///     Cấu hình hoàn chỉnh từ dialog Quick Setting — output của UI, input của orchestrator tạo Rebar.
///     Immutable; gom toàn bộ các nhóm thép + lớp bảo vệ. <see cref="SpanOverrides"/> rỗng → áp chung
///     mọi nhịp; có override → nhịp đó dùng giá trị riêng, field null trong override fallback về đây.
/// </summary>
public sealed record QuickSettingModel
{
    // Thép chủ.
    public MainBarConfig MainTop { get; init; } = new();
    public MainBarConfig MainBottom { get; init; } = new();

    // Thép gia cường (2 lớp top/bottom).
    public AdditionalBarConfig TopAdditional { get; init; } = new();
    public AdditionalBarConfig TopAdditionalLayer2 { get; init; } = new() { Layer = 2 };
    public IReadOnlyList<AdditionalBarConfig> TopAdditionalItems { get; init; } = [];
    public AdditionalBarConfig BottomAdditional { get; init; } = new() { Side = AdditionalBarSide.BottomAtMidspan };
    public AdditionalBarConfig BottomAdditionalLayer2 { get; init; } = new() { Layer = 2, Side = AdditionalBarSide.BottomAtMidspan };
    public IReadOnlyList<AdditionalBarConfig> BottomAdditionalItems { get; init; } = [];

    // Cốt đai.
    public StirrupConfig Stirrup { get; init; } = new();

    // Thép chống phình.
    public AntiBulgeConfig AntiBulge { get; init; } = new();

    public CoverSettings Cover { get; init; } = new();

    /// <summary>Điểm gối/cột nội bộ để chia 1 dầm vật lý xuyên qua nhiều cột thành nhiều nhịp tính toán.</summary>
    public IReadOnlyList<Point3> InternalSupportPoints { get; init; } = [];

    /// <summary>Gối/cột/dầm nội bộ do người dùng chọn, có kèm nửa bề rộng theo trục dầm chính để đai không xuyên vào gối.</summary>
    public IReadOnlyList<SupportInfo> InternalSupports { get; init; } = [];

    /// <summary>Vị trí dầm phụ gác lên dầm chính (feet). Đai dầm chính tránh + tăng cường quanh đây.</summary>
    public IReadOnlyList<Point3> SecondaryBeamPoints { get; init; } = [];

    public IReadOnlyList<SecondaryBeamInfo> SecondaryBeams { get; init; } = [];

    /// <summary>Ghi đè theo từng nhịp (per-span UI). Rỗng → áp cấu hình chung cho mọi nhịp.</summary>
    public IReadOnlyList<SpanRebarOverride> SpanOverrides { get; init; } = [];
}
