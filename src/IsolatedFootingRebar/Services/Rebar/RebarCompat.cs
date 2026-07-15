using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Bao boc Rebar.CreateFromCurves cho breaking change o Revit 2027:
///     RebarHookOrientation (2 tham so) bi bo, thay bang BarTerminationsData. Giu 1 diem #if duy nhat.
/// </summary>
internal static class RebarCompat
{
    public static Autodesk.Revit.DB.Structure.Rebar CreateFromCurves(
        Document document, RebarStyle style, RebarBarType barType,
        RebarHookType? startHook, RebarHookType? endHook, Element host,
        XYZ normal, IList<Curve> curves,
        bool right, bool useExistingShapeIfPossible)
    {
#if REVIT2027_OR_GREATER
        var terminations = new BarTerminationsData(document);
        return Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
            document, style, barType, host, normal, curves, terminations,
            useExistingShapeIfPossible, createNewShape: true);
#else
        var orient = right ? RebarHookOrientation.Right : RebarHookOrientation.Left;
        return Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
            document, style, barType, startHook, endHook, host,
            normal, curves, orient, orient,
            useExistingShapeIfPossible: useExistingShapeIfPossible, createNewShape: true);
#endif
    }
}
