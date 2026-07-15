namespace FootingDrawing.Core.Models;

/// <summary>
///     Kết quả một lần sinh bản vẽ móng. Chứa id view/sheet/viewport đã tạo + danh sách cảnh báo
///     (thiếu type, reference chưa verify, rebar chưa tag...). Không throw cho warning — chỉ gom lại.
/// </summary>
public sealed class FootingDrawingResult
{
    public long ViewId { get; set; }
    public long SheetId { get; set; }
    public long ViewportId { get; set; }
    public int TagCount { get; set; }
    public int DimensionCount { get; set; }
    public int BendingDetailCount { get; set; }
    public List<string> Warnings { get; } = [];
}
