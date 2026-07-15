using Autodesk.Revit.DB;

namespace BeamRebarPro;

/// <summary>
///     Shim ElementId.Value (long) cho breaking change o Revit 2024: truoc do dung IntegerValue (int).
///     Dung .ToValue() de chay ca R23 (net48, IntegerValue) lan R24+ (Value).
/// </summary>
internal static class ElementIdCompat
{
    public static long ToValue(this ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>Tao ElementId tu long. R23 chi co ctor int; R24+ co ctor long.</summary>
    public static ElementId Create(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int)value);
#endif
    }
}
