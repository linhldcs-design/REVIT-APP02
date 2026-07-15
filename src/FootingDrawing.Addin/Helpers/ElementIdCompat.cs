using Autodesk.Revit.DB;

namespace FootingDrawing.Addin.Helpers;

internal static class ElementIdCompat
{
    public static ElementId FromLong(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId(checked((int)value));
#endif
    }

    public static long ToLong(this ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
