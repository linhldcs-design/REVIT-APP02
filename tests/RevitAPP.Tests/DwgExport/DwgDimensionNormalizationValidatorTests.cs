using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgDimensionNormalizationValidatorTests
{
    [Theory]
    [InlineData(12, 12, 12)]
    [InlineData(12, 46, 46)]
    [InlineData(5, 4, 4)]
    public void EnsureCadCoverage_WhenEveryCadCandidateIsNormalized_DoesNotThrow(
        int source,
        int candidates,
        int normalized) =>
        DwgDimensionNormalizationValidator.EnsureCadCoverage(
            "KC-06-00", "Mặt bằng", source, candidates, normalized);

    [Fact]
    public void EnsureCadCoverage_WhenCadCandidatesAreMissing_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DwgDimensionNormalizationValidator.EnsureCadCoverage("KC-06-00", "Mặt bằng", 12, 11, 10));

        Assert.Contains("KC-06-00", exception.Message);
        Assert.Contains("ứng viên=11, đã xử lý=10", exception.Message);
    }

    [Fact]
    public void EnsureCadCoverage_WhenRevitHasDimensionsButCadHasNone_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DwgDimensionNormalizationValidator.EnsureCadCoverage("KC-06-00", "Mặt bằng", 1, 0, 0));
    }
}
