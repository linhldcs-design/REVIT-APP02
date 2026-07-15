namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Annotation đúng cấu trúc mặt cắt ngang mẫu: thép dọc dùng Multi-Rebar Annotation,
///     thép đai dùng Independent Rebar Tag; cấu hình riêng cho GỐI và NHỊP.
/// </summary>
public sealed record CrossAnnotationConfig(
    string? EndLongitudinalMraTypeName,
    string? EndStirrupTagTypeName,
    string? MidLongitudinalMraTypeName,
    string? MidStirrupTagTypeName,
    string? EndReinforceL1MraTypeName = null,   // rebar tag thép tăng cường lớp 1 (1 cây) — GỐI
    string? MidReinforceL1MraTypeName = null,   // rebar tag thép tăng cường lớp 1 (1 cây) — NHỊP
    string? EndReinforceL2MraTypeName = null,   // MRA type thép tăng cường lớp 2 (≥2 cây) — GỐI
    string? MidReinforceL2MraTypeName = null)   // MRA type thép tăng cường lớp 2 (≥2 cây) — NHỊP
{
    public static CrossAnnotationConfig Empty { get; } = new(null, null, null, null);
}
