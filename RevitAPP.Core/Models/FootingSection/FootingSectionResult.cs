namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Kết quả sinh bản vẽ mặt cắt móng. Id kiểu long (thuần) để độc lập Revit API — lớp Revit map từ ElementId.Value.
/// </summary>
public sealed class FootingSectionResult
{
    public long? SectionViewId { get; set; }
    public long? SheetId { get; set; }
    public long? ViewportId { get; set; }
    public List<string> Warnings { get; } = new();
}
