namespace BeamRebarPro.Models;

/// <summary>
///     Dầm liên tục đã mô hình hoá: chuỗi nhịp sắp theo trục + danh sách gối (kể cả hai đầu mút).
///     1 dầm 1 nhịp → 1 span, 2 gối; 3 dầm nối → 3 span, 4 gối. <see cref="Warnings"/> gom cảnh báo
///     phát sinh khi gom segment (tiết diện đổi, gối lệch...).
/// </summary>
public sealed record BeamRun(
    IReadOnlyList<Span> Spans,
    IReadOnlyList<Support> Supports,
    IReadOnlyList<string> Warnings)
{
    public double TotalLengthFeet => Spans.Sum(s => s.LengthFeet);
    public bool IsSingleSpan => Spans.Count == 1;
}
