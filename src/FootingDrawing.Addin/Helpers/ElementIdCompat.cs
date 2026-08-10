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

    // ToLong is not declared here: Nice3point.Revit.Extensions supplies one for every release
    // the add-in targets, and a second extension of the same name leaves every call ambiguous.
}
