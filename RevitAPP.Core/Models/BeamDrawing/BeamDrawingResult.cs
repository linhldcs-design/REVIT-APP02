namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Kết quả sinh bản vẽ dầm. Dùng id kiểu long (thuần) để độc lập Revit API — lớp Revit map từ ElementId.Value.
/// </summary>
public sealed class BeamDrawingResult
{
    public List<long> SectionViewIds { get; } = new();
    public List<long> CrossSectionViewIds { get; } = new();
    public long? SheetId { get; set; }
    public List<string> Warnings { get; } = new();

    public int TotalViews => SectionViewIds.Count + CrossSectionViewIds.Count;
}
