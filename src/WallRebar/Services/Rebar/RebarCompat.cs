using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Bao boc Rebar.CreateFromCurves cho breaking change o Revit 2027:
///     RebarHookOrientation (2 tham so start/end) da bi bo, thay bang BarTerminationsData.
///     Giu 1 diem #if duy nhat de 3 creator goi chung.
/// </summary>
internal static class RebarCompat
{
    /// <summary>
    ///     Tao Rebar tu curves. hookAtStart/hookAtEnd: huong moc (chi dung cho R23–R26); R27 dung
    ///     BarTerminationsData mac dinh (thang, khong moc) — WallRebar tu tao hinh dang moc bang curves nen
    ///     khong phu thuoc hook terminations cua API.
    /// </summary>
    public static Autodesk.Revit.DB.Structure.Rebar CreateFromCurves(
        Document document,
        RebarBarType barType,
        Element host,
        XYZ normal,
        IList<Curve> curves,
        RebarHookOrientationCompat hookAtStart,
        RebarHookOrientationCompat hookAtEnd,
        bool useExistingShapeIfPossible = false,
        RebarHookType? startHookType = null,
        RebarHookType? endHookType = null)
    {
#if REVIT2027_OR_GREATER
        // R27: hook orientation/type set qua BarTerminationsData. WallRebar tu tao hinh dang moc bang
        // curves (Closed shape) nen mac dinh terminations la du.
        var terminations = new BarTerminationsData(document);
        return Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
            document, RebarStyle.Standard, barType, host,
            normal, curves, terminations,
            useExistingShapeIfPossible, createNewShape: true);
#else
        var start = hookAtStart == RebarHookOrientationCompat.Left
            ? RebarHookOrientation.Left : RebarHookOrientation.Right;
        var end = hookAtEnd == RebarHookOrientationCompat.Left
            ? RebarHookOrientation.Left : RebarHookOrientation.Right;
        return Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
            document, RebarStyle.Standard, barType, startHookType, endHookType, host,
            normal, curves, start, end,
            useExistingShapeIfPossible: useExistingShapeIfPossible, createNewShape: true);
#endif
    }
}

/// <summary>Huong moc doc lap voi API (khong ton tai RebarHookOrientation o R27).</summary>
internal enum RebarHookOrientationCompat
{
    Left,
    Right
}
