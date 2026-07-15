using Autodesk.Revit.DB;

namespace BeamDrawing.Addin.Models;

/// <summary>
///     Kết quả sinh bản vẽ cho toàn bộ dầm đã chọn: id các view, sheet, và cảnh báo (vd thiếu family,
///     fallback section type). Dùng để hiển thị TaskDialog tổng kết cuối lệnh.
/// </summary>
public sealed class BeamDrawingResult
{
    public List<ElementId> SectionViewIds { get; } = [];
    public List<ElementId> CrossSectionViewIds { get; } = [];
    public ElementId? SheetId { get; set; }
    public List<string> Warnings { get; } = [];

    public int TotalViews => SectionViewIds.Count + CrossSectionViewIds.Count;
}
