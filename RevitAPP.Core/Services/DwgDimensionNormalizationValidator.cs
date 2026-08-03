namespace RevitAPP.Core.Services;

public static class DwgDimensionNormalizationValidator
{
    public static void EnsureCadCoverage(
        string sheetNumber,
        string viewName,
        int sourceDimensionCount,
        int cadCandidateCount,
        int normalizedDimensionCount)
    {
        if (sourceDimensionCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDimensionCount));
        if (cadCandidateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(cadCandidateCount));
        if (sourceDimensionCount > 0 && cadCandidateCount == 0)
            throw new InvalidOperationException(
                $"Không tìm thấy DIM CAD sau EXPORTLAYOUT trên sheet {sheetNumber}, view {viewName} " +
                $"dù Revit báo {sourceDimensionCount} Dimension.");
        if (normalizedDimensionCount != cadCandidateCount)
            throw new InvalidOperationException(
                $"Không normalize đủ DIM CAD sau EXPORTLAYOUT trên sheet {sheetNumber}, " +
                $"view {viewName}: ứng viên={cadCandidateCount}, đã xử lý={normalizedDimensionCount}.");
    }
}
