using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

/// <summary>
/// Decides which CAD line family corresponds to which anchor grid by comparing
/// undirected line angles. Global X/Y is never assumed: both the CAD network and
/// the anchor grids may be skewed.
/// </summary>
public static class CadGridFamilyAssigner
{
    /// <summary>
    /// An assignment is only trusted when it beats the swapped alternative by this
    /// margin (degrees). Below it the two families are too similar to tell apart and
    /// the caller must ask the user instead of guessing.
    /// </summary>
    public const double AmbiguityMarginDegrees = 5.0;

    public static CadGridFamilyAssignment Assign(
        CadGridPoint2 firstCadDirection,
        CadGridPoint2 secondCadDirection,
        CadGridPoint2 firstAnchorDirection,
        CadGridPoint2 secondAnchorDirection)
    {
        var direct =
            AngleBetweenDegrees(firstCadDirection, firstAnchorDirection)
            + AngleBetweenDegrees(secondCadDirection, secondAnchorDirection);
        var swapped =
            AngleBetweenDegrees(firstCadDirection, secondAnchorDirection)
            + AngleBetweenDegrees(secondCadDirection, firstAnchorDirection);

        var isSwapped = swapped < direct;
        var margin = Math.Abs(direct - swapped);
        return new CadGridFamilyAssignment(
            isSwapped,
            margin,
            margin < AmbiguityMarginDegrees);
    }

    /// <summary>
    /// Undirected angle in [0, 90]: a grid line and its reverse are the same line,
    /// so endpoint order in the DWG must not change the result.
    /// </summary>
    public static double AngleBetweenDegrees(CadGridPoint2 left, CadGridPoint2 right)
    {
        var leftLength = Math.Sqrt(left.Xmm * left.Xmm + left.Ymm * left.Ymm);
        var rightLength = Math.Sqrt(right.Xmm * right.Xmm + right.Ymm * right.Ymm);
        if (leftLength <= 0 || rightLength <= 0)
            throw new ArgumentException("Không thể so góc với vector rỗng.");

        var cosine = (left.Xmm * right.Xmm + left.Ymm * right.Ymm)
            / (leftLength * rightLength);
        // Math.Clamp is unavailable on net48 (Revit 2023/2024, AutoCAD 2024).
        cosine = Math.Min(1.0, Math.Abs(cosine));
        return Math.Acos(cosine) * 180.0 / Math.PI;
    }
}

public sealed record CadGridFamilyAssignment(
    bool IsSwapped,
    double MarginDegrees,
    bool IsAmbiguous);
