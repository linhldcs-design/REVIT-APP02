using RevitAPP.Core.Services;
using Xunit;

namespace BeamRebarPro.Tests;

/// <summary>
/// Khoá ngữ nghĩa rải thanh của Revit. Preview dựng lại cả bó thép bằng chính phép tính này, nên sai
/// ở đây là preview hiện sai số đai/số thanh so với mô hình thật.
/// </summary>
public sealed class RebarLayoutMathTests
{
    [Fact]
    public void MaximumSpacing_DivisibleLength_PlacesBarAtBothEnds()
    {
        var stations = RebarLayoutMath.MaximumSpacingStations(1000, 250);

        Assert.Equal([0, 250, 500, 750, 1000], stations);
    }

    [Fact]
    public void MaximumSpacing_IndivisibleLength_ShrinksSpacingToCoverWholeRun()
    {
        // Bước yêu cầu 300 không chia chẵn 1000. Revit co bước xuống 250 để phủ hết đoạn,
        // KHÔNG giữ bước 300 rồi bỏ hở 100mm ở cuối (đó sẽ là đai thiếu ở mép gối).
        var stations = RebarLayoutMath.MaximumSpacingStations(1000, 300);

        Assert.Equal([0, 250, 500, 750, 1000], stations);
        Assert.All(stations.Zip(stations.Skip(1), (a, b) => b - a), gap => Assert.True(gap <= 300));
    }

    [Fact]
    public void MaximumSpacing_RunShorterThanSpacing_PlacesOnlyEndBars()
    {
        var stations = RebarLayoutMath.MaximumSpacingStations(200, 300);

        Assert.Equal([0, 200], stations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void MaximumSpacing_EmptyRun_PlacesNothing(double arrayLengthMm)
    {
        Assert.Empty(RebarLayoutMath.MaximumSpacingStations(arrayLengthMm, 150));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-150)]
    public void MaximumSpacing_NonPositiveSpacing_Throws(double spacingMm)
    {
        Assert.Throws<ArgumentException>(() => RebarLayoutMath.MaximumSpacingStations(1000, spacingMm));
    }

    [Fact]
    public void MaximumSpacing_LastStationExactlyAtRunEnd()
    {
        var stations = RebarLayoutMath.MaximumSpacingStations(3175, 150);

        Assert.Equal(3175, stations[^1], 6);
        Assert.Equal(0, stations[0]);
    }

    [Fact]
    public void FixedNumber_SpreadsBarsEvenlyAcrossRun()
    {
        var offsets = RebarLayoutMath.FixedNumberOffsets(4, 300);

        Assert.Equal([0, 100, 200, 300], offsets);
    }

    [Fact]
    public void FixedNumber_SingleBar_SitsAtOrigin()
    {
        // Builder đặt cây đơn vào giữa tiết diện thay vì mép, nên offset phải là 0.
        Assert.Equal([0d], RebarLayoutMath.FixedNumberOffsets(1, 300));
    }

    [Fact]
    public void FixedNumber_NoBars_PlacesNothing()
    {
        Assert.Empty(RebarLayoutMath.FixedNumberOffsets(0, 300));
    }

    [Fact]
    public void FixedNumber_ZeroWidthRun_StacksBarsAtOrigin()
    {
        // Tiết diện hẹp tới mức không còn bề rộng khả dụng: vẫn đủ số thanh, không chia cho 0.
        var offsets = RebarLayoutMath.FixedNumberOffsets(3, 0);

        Assert.Equal([0d, 0d, 0d], offsets);
    }
}
