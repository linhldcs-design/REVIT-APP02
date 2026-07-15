namespace BeamRebar.Core.Models;

/// <summary>
///     Dầm liên tục đã mô hình hoá: chuỗi nhịp sắp xếp theo trục + danh sách gối (kể cả hai đầu mút).
///     1 dầm 1 nhịp → 1 span, 2 gối đầu/cuối. 3 dầm nối → 3 span, 4 gối.
/// </summary>
public sealed record BeamRun(
    IReadOnlyList<Span> Spans,
    IReadOnlyList<Support> Supports)
{
    public double TotalLengthFeet => Spans.Sum(s => s.LengthFeet);
    public bool IsSingleSpan => Spans.Count == 1;
}
