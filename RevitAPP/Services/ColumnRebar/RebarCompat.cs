using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Bao boc Rebar.CreateFromCurves cho breaking change o Revit 2027:
///     RebarHookOrientation (2 tham so) bi bo, thay bang BarTerminationsData. Giu 1 diem #if duy nhat.
/// </summary>
internal static class RebarCompat
{
    public static Rebar? CreateFromCurves(
        Document document,
        RebarStyle style,
        RebarBarType type,
        RebarHookType? startHook,
        RebarHookType? endHook,
        Element host,
        XYZ normal,
        IList<Curve> curves)
    {
#if REVIT2027_OR_GREATER
        var terminations = new BarTerminationsData(document);
        return Rebar.CreateFromCurves(
            document, style, type, host, normal, curves, terminations, true, true);
#else
        return Rebar.CreateFromCurves(
            document, style, type, startHook, endHook, host,
            normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
#endif
    }
}
