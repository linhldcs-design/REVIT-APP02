namespace BeamRebar.Addin.Models;

/// <summary>Kết quả tạo thép: tổng số thanh/đai đã tạo + cảnh báo gom từ các creator.</summary>
public sealed record RebarCreationResult(
    int LongitudinalCount,
    int StirrupCount,
    int AntiBulgeCount,
    IReadOnlyList<string> Warnings)
{
    public int Total => LongitudinalCount + StirrupCount + AntiBulgeCount;
}
