namespace RevitAPP.Core.Services;

/// <summary>Resolves the physical concrete top/bottom elevation without trusting family control geometry bounds.</summary>
public static class BeamElevationMath
{
    public static (double Top, double Bottom) Resolve(
        double? topParameter, double? bottomParameter, double height,
        double? boundingTop, double? boundingBottom, double axisZ)
    {
        if (topParameter is { } top && bottomParameter is { } bottom &&
            MathCompat.IsFinite(top) && MathCompat.IsFinite(bottom) && top > bottom)
            return (top, bottom);

        if (topParameter is { } validTop && MathCompat.IsFinite(validTop) && height > 0)
            return (validTop, validTop - height);

        if (bottomParameter is { } validBottom && MathCompat.IsFinite(validBottom) && height > 0)
            return (validBottom + height, validBottom);

        if (boundingTop is { } boxTop && boundingBottom is { } boxBottom &&
            MathCompat.IsFinite(boxTop) && MathCompat.IsFinite(boxBottom) && boxTop > boxBottom)
            return (boxTop, boxBottom);

        return (axisZ, axisZ - height);
    }
}
