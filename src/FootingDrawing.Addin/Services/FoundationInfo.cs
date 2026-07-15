using Autodesk.Revit.DB;

namespace FootingDrawing.Addin.Services;

/// <summary>Đọc thông tin phụ trợ của móng (Mark) để đặt tên view + tiêu đề bản vẽ.</summary>
public static class FoundationInfo
{
    /// <summary>Mark của móng (vd "M3"). Rỗng → trả về "".</summary>
    public static string GetMark(Element foundation)
        => foundation.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim() ?? string.Empty;
}
