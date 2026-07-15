using Autodesk.Revit.DB;

namespace RevitAPP.Helpers;

/// <summary>
///     Shim cho breaking change của <see cref="ElementId" /> giữa các phiên bản Revit.
/// </summary>
public static class ElementIdHelper
{
    /// <summary>
    ///     Lấy giá trị số của ElementId dưới dạng <see cref="long" />.
    /// </summary>
    // Multi-version: ElementId đổi từ IntegerValue (int) sang Value (long) ở Revit 2024.
    public static long ToValue(this ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>
    ///     Tạo ElementId từ giá trị số. R23 chỉ có ctor int; R24+ có ctor long.
    /// </summary>
    public static ElementId Create(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int)value);
#endif
    }
}
