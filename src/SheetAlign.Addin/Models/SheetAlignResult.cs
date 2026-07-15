namespace SheetAlign.Addin.Models;

/// <summary>Một sheet bị bỏ qua khi căn chỉnh, kèm lý do.</summary>
public readonly record struct SheetAlignSkip(string SheetName, string Reason);

/// <summary>Kết quả căn chỉnh viewport theo lưới trục.</summary>
public sealed class SheetAlignResult
{
    public int UpdatedCount { get; set; }
    public List<SheetAlignSkip> Skipped { get; } = new();
}
