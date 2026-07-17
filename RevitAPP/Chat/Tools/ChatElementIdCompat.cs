using Autodesk.Revit.DB;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tạo ElementId từ giá trị long, tương thích đa version Revit (API đổi kiểu id ở R2024).
/// </summary>
public static class ChatElementIdCompat
{
    public static ElementId Create(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int)value);
#endif
    }

    public static long Value(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
