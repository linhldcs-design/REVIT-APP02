using System.Globalization;
using System.Text;

namespace IsolatedFootingRebar.Services;

/// <summary>Nhận dạng bê tông lót mà không phụ thuộc Revit API để có thể kiểm thử ngoài Revit.</summary>
internal static class BlindingConcreteFilter
{
    private static readonly string[] NameMarkers =
    [
        "be tong lot",
        "bt lot",
        "betong lot",
        "blinding",
        "lean concrete",
        "mud slab"
    ];

    public static bool IsBlindingName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var normalized = Normalize(name);
        return NameMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Dự phòng cho family không gán Material/Subcategory: chỉ nhận một bản mỏng ở đáy, tiếp giáp với
    /// khối móng phía trên và nhô ra rõ ràng theo cả hai phương. Các ngưỡng dùng cùng đơn vị với đầu vào.
    /// </summary>
    public static bool LooksLikeBlindingSlab(
        double candidateBottom,
        double candidateTop,
        double candidateWidthX,
        double candidateWidthY,
        double structuralBottom,
        double structuralTop,
        double structuralWidthX,
        double structuralWidthY,
        double maxThickness,
        double maxVerticalGap,
        double minimumTotalOverhang)
    {
        const double numericTolerance = 1e-9;
        var thickness = candidateTop - candidateBottom;
        var structuralHeight = structuralTop - structuralBottom;
        var verticalGap = structuralBottom - candidateTop;

        return thickness > 0
               && structuralHeight >= thickness * 3
               && thickness <= maxThickness
               && verticalGap >= -maxVerticalGap
               && verticalGap <= maxVerticalGap
               && candidateWidthX + numericTolerance >= structuralWidthX + minimumTotalOverhang
               && candidateWidthY + numericTolerance >= structuralWidthY + minimumTotalOverhang;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

            var normalized = char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ';
            if (normalized == ' ')
            {
                if (previousWasSpace) continue;
                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            builder.Append(normalized);
        }

        return builder.ToString().Trim();
    }
}
