using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public class TcvnRebarCalculatorTests
{
    private static FloorRebarConfig Config(
        double mainDia = 20, int barsX = 3, int barsY = 4, double stirrupDia = 8,
        double sEnd = 100, double sMid = 200, double confine = 0, double beamDepth = 0)
        => new(mainDia, barsX, barsY, stirrupDia, sEnd, sMid, confine, beamDepth);

    [Fact]
    public void BuildStirrupLoop_ReturnsClosedRectangleAtCenterline()
    {
        var section = new ColumnSection(400, 600);

        var loop = TcvnRebarCalculator.BuildStirrupLoop(section, coverMm: 25, stirrupDiameterMm: 8);

        // Loop = (400 - 50 - 8) × (600 - 50 - 8) = 342 × 542 → half = 171 × 271
        Assert.Equal(4, loop.Count);
        Assert.All(loop, p => Assert.Equal(171, Math.Abs(p.Xmm), 6));
        Assert.All(loop, p => Assert.Equal(271, Math.Abs(p.Ymm), 6));
    }

    [Fact]
    public void BuildStirrupLoop_ThrowsWhenCoverExceedsSection()
    {
        var section = new ColumnSection(50, 600);
        Assert.Throws<ArgumentException>(() =>
            TcvnRebarCalculator.BuildStirrupLoop(section, coverMm: 25, stirrupDiameterMm: 8));
    }

    [Fact]
    public void BuildMainBarPositions_TotalIsPerimeterFormula_AndInsideCore()
    {
        var section = new ColumnSection(400, 600);
        var config = Config(mainDia: 20, barsX: 3, barsY: 4, stirrupDia: 8);

        var points = TcvnRebarCalculator.BuildMainBarPositions(section, config, coverMm: 25);

        // 2*BarsX + 2*BarsY - 4 = 6 + 8 - 4 = 10
        Assert.Equal(10, points.Count);
        // inset = 25 + 8 + 10 = 43 → hw = 157, hh = 257
        Assert.All(points, p => Assert.True(Math.Abs(p.Xmm) <= 157 + 1e-6));
        Assert.All(points, p => Assert.True(Math.Abs(p.Ymm) <= 257 + 1e-6));
        // No duplicate positions
        var distinct = points.Select(p => (Math.Round(p.Xmm, 3), Math.Round(p.Ymm, 3))).Distinct().Count();
        Assert.Equal(points.Count, distinct);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 1)]
    public void BuildMainBarPositions_ThrowsWhenBarsBelowTwo(int barsX, int barsY)
    {
        var section = new ColumnSection(400, 600);
        Assert.Throws<ArgumentException>(() =>
            TcvnRebarCalculator.BuildMainBarPositions(section, Config(barsX: barsX, barsY: barsY), coverMm: 25));
    }

    [Fact]
    public void BuildMainBarPositions_ThrowsWhenSectionTooSmall()
    {
        var section = new ColumnSection(400, 400);
        // mainDia 400 → inset = 25 + 8 + 200 = 233 > 200 → hw negative
        Assert.Throws<ArgumentException>(() =>
            TcvnRebarCalculator.BuildMainBarPositions(section, Config(mainDia: 400), coverMm: 25));
    }

    [Fact]
    public void ComputeZones_NormalColumn_ThreeZonesCoverFullHeight()
    {
        var section = new ColumnSection(400, 600);
        var config = Config(sEnd: 100, sMid: 200);

        var zones = TcvnRebarCalculator.ComputeZones(clearHeightMm: 3000, section, config);

        // l0 = max(3000/6=500, max(400,600)=600, 450) = 600
        Assert.Equal(0, zones.Bottom.StartElevationMm, 6);
        Assert.Equal(600, zones.Bottom.LengthMm, 6);
        Assert.Equal(600, zones.Middle.StartElevationMm, 6);
        Assert.Equal(1800, zones.Middle.LengthMm, 6);
        Assert.Equal(2400, zones.Top.StartElevationMm, 6);
        Assert.Equal(600, zones.Top.LengthMm, 6);
        Assert.Equal(3000, zones.Bottom.LengthMm + zones.Middle.LengthMm + zones.Top.LengthMm, 6);

        // counts = floor(len/spacing)+1
        Assert.Equal(7, zones.Bottom.Count);   // 600/100 + 1
        Assert.Equal(10, zones.Middle.Count);  // 1800/200 + 1
        Assert.Equal(7, zones.Top.Count);
    }

    [Fact]
    public void ComputeZones_ShortColumn_HasEmptyMiddleZone()
    {
        var section = new ColumnSection(400, 600);
        var zones = TcvnRebarCalculator.ComputeZones(clearHeightMm: 800, section, Config());

        // 2*l0(600) >= 800 → l0 clamped to 400, middle empty
        Assert.Equal(400, zones.Bottom.LengthMm, 6);
        Assert.Equal(0, zones.Middle.LengthMm, 6);
        Assert.Equal(0, zones.Middle.Count);
        Assert.Equal(400, zones.Top.StartElevationMm, 6);
    }

    [Fact]
    public void ComputeZones_UsesExplicitConfineZoneLength()
    {
        var section = new ColumnSection(400, 600);
        var config = Config(confine: 500);

        var zones = TcvnRebarCalculator.ComputeZones(clearHeightMm: 3000, section, config);

        Assert.Equal(500, zones.Bottom.LengthMm, 6);
        Assert.Equal(2000, zones.Middle.LengthMm, 6);
    }

    [Theory]
    [InlineData(20, 40, 800)]
    [InlineData(16, 30, 480)]
    public void LapLength_IsFactorTimesDiameter(double dia, double factor, double expected)
        => Assert.Equal(expected, TcvnRebarCalculator.LapLengthMm(dia, factor), 6);

    [Fact]
    public void ComputeZones_ThrowsOnNonPositiveSpacing()
    {
        var section = new ColumnSection(400, 600);
        Assert.Throws<ArgumentException>(() =>
            TcvnRebarCalculator.ComputeZones(3000, section, Config(sEnd: 0)));
    }

    [Fact]
    public void ComputeZones_BeamDepth_StirrupsStopBelowBeam()
    {
        var section = new ColumnSection(400, 600);
        // clear 3600, beam 600 → vùng đặt đai = 3000 (đai dừng dưới đáy dầm)
        var zones = TcvnRebarCalculator.ComputeZones(3600, section, Config(beamDepth: 600));

        var placeHeight = zones.Top.StartElevationMm + zones.Top.LengthMm;
        Assert.Equal(3000, placeHeight, 6);
        Assert.True(placeHeight < 3600, "Đai phải dừng dưới đáy dầm, không chạy hết chiều cao tầng.");
        Assert.Equal(2400, zones.Top.StartElevationMm, 6);
    }

    [Fact]
    public void ComputeZones_ThrowsWhenBeamDeeperThanStorey()
    {
        var section = new ColumnSection(400, 600);
        Assert.Throws<ArgumentException>(() =>
            TcvnRebarCalculator.ComputeZones(3000, section, Config(beamDepth: 3000)));
    }

    [Fact]
    public void MainBarSpan_NoStagger_AllBarsSameSpan()
    {
        var even = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, false, false, stagger: false, barIndex: 0);
        var odd = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, false, false, stagger: false, barIndex: 1);

        Assert.Equal((0d, 3800d), even);
        Assert.Equal(even, odd);
    }

    [Fact]
    public void MainBarSpan_Stagger_OddBarsShiftedByUniformShift()
    {
        var even = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, false, false, stagger: true, barIndex: 0);
        var odd = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, false, false, stagger: true, barIndex: 1);

        Assert.Equal((0d, 3800d), even);          // splice tại 3000..3800
        Assert.Equal((800d, 4600d), odd);         // splice tại 3800..4600 → so le
    }

    [Fact]
    public void MainBarSpan_Stagger_UsesUniformShiftNotPerBarLap()
    {
        // Thanh phụ Ø nhỏ: lap riêng 480 nhưng dịch so le ĐỒNG ĐỀU 800 → đáy = 800 (gom đúng cao độ)
        var odd = TcvnRebarCalculator.MainBarSpan(0, 3000, 480, 800, 0, false, false, stagger: true, barIndex: 1);
        Assert.Equal(800d, odd.BottomMm, 6);
        Assert.Equal(3000 + 800 + 480, odd.TopMm, 6);
    }

    [Fact]
    public void MainBarSpan_LapZoneOffset_RaisesSpliceAboveFloor()
    {
        // offset 1500 (giữa cột H=3000), thanh chẵn không so le
        var even = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 1500, false, false, stagger: false, barIndex: 0);
        Assert.Equal(1500d, even.BottomMm, 6);            // chân nâng lên giữa tầng
        Assert.Equal(3000 + 1500 + 800, even.TopMm, 6);   // đỉnh nối tại giữa tầng trên + lap
    }

    [Fact]
    public void MainBarSpan_BottomStorey_StartsAtBaseEvenWhenStaggered()
    {
        var odd = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, isBottomStorey: true, isTopStorey: false,
            stagger: true, barIndex: 1);
        Assert.Equal(0d, odd.BottomMm, 6);        // chân cột dưới cùng không nhấc lên
        Assert.Equal(4600d, odd.TopMm, 6);
    }

    [Fact]
    public void MainBarSpan_TopStorey_EndsAtTopNoExtension()
    {
        var odd = TcvnRebarCalculator.MainBarSpan(0, 3000, 800, 800, 0, isBottomStorey: false, isTopStorey: true,
            stagger: true, barIndex: 1);
        Assert.Equal(800d, odd.BottomMm, 6);
        Assert.Equal(3000d, odd.TopMm, 6);        // tầng trên cùng không kéo dài thêm
    }

    [Fact]
    public void BuildClassifiedMainBars_HasFourCorners()
    {
        var section = new ColumnSection(400, 600);
        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(section, Config(barsX: 3, barsY: 4), coverMm: 25);

        Assert.Equal(10, bars.Count);
        Assert.Equal(4, bars.Count(b => b.IsCorner));
    }

    [Fact]
    public void BuildClassifiedMainBars_DistributionBarUsesSmallerDiameter()
    {
        var section = new ColumnSection(400, 600);
        var config = Config(mainDia: 20, barsX: 3, barsY: 4) with { UseDistributionBar = true, DistributionBarDiameterMm = 12 };

        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(section, config, coverMm: 25);

        Assert.All(bars.Where(b => b.IsCorner), b => Assert.Equal(20, b.DiameterMm));
        Assert.All(bars.Where(b => !b.IsCorner), b => Assert.Equal(12, b.DiameterMm));
    }

    [Fact]
    public void ReinforcementArea_TenBarsD20_Is31_42Cm2()
    {
        var section = new ColumnSection(400, 600);
        var area = TcvnRebarCalculator.ReinforcementAreaCm2(section, Config(mainDia: 20, barsX: 3, barsY: 4), 25);
        // 10 × π/4 × 20² = 3141.59 mm² = 31.42 cm²
        Assert.Equal(31.42, area, 1);
    }

    [Fact]
    public void ReinforcementRatio_IsAreaOverGrossPercent()
    {
        var section = new ColumnSection(400, 600);
        var ratio = TcvnRebarCalculator.ReinforcementRatioPercent(section, Config(mainDia: 20, barsX: 3, barsY: 4), 25);
        // 3141.59 / 240000 × 100 = 1.309%
        Assert.Equal(1.309, ratio, 2);
    }

    [Fact]
    public void TopHookLegEnd_BendsTowardCenterAlongLargerAxis()
    {
        // |x| < |y| → bẻ theo Y về tâm
        var a = TcvnRebarCalculator.TopHookLegEnd(new Point2D(157, 257), 150);
        Assert.Equal(157, a.Xmm, 6);
        Assert.Equal(107, a.Ymm, 6);

        // |x| >= |y| → bẻ theo X về tâm
        var b = TcvnRebarCalculator.TopHookLegEnd(new Point2D(157, 0), 150);
        Assert.Equal(7, b.Xmm, 6);
        Assert.Equal(0, b.Ymm, 6);
    }

    [Fact]
    public void SectionStepOffset_ComputesCornerShift()
    {
        var lower = new ColumnSection(400, 600);
        var upper = new ColumnSection(300, 600);
        var e = TcvnRebarCalculator.SectionStepOffsetMm(lower, Config(), upper, Config(), 25);
        Assert.Equal(50, e, 6); // (400-300)/2
    }

    [Theory]
    [InlineData(0, true, SectionTransition.None)]
    [InlineData(50, true, SectionTransition.Crank)]
    [InlineData(100, true, SectionTransition.Dowel)]
    [InlineData(50, false, SectionTransition.Dowel)]
    public void ClassifyTransition_FollowsThresholdAndLayout(double e, bool sameLayout, SectionTransition expected)
        => Assert.Equal(expected, TcvnRebarCalculator.ClassifyTransition(e, sameLayout, new SectionTransitionOptions()));

    [Fact]
    public void ClampBarIntoSection_PullsBarInsideSmallerColumn()
    {
        var upper = new ColumnSection(300, 600);
        // inset = 25+8+10 = 43 → maxX = 150-43 = 107
        var clamped = TcvnRebarCalculator.ClampBarIntoSection(new Point2D(157, 100), upper, Config(), 25);
        Assert.Equal(107, clamped.Xmm, 6);   // bị kéo vào trong theo X
        Assert.Equal(100, clamped.Ymm, 6);   // Y trong giới hạn → giữ nguyên
    }

    [Fact]
    public void CrosstieXPositions_ExcludesCorners()
    {
        var section = new ColumnSection(400, 600);
        var xs = TcvnRebarCalculator.CrosstieXPositions(section, Config(barsX: 3, barsY: 4), 25);
        Assert.Single(xs);          // Cx=3 → 1 thanh giữa
        Assert.Equal(0, xs[0], 6);  // giữa cạnh → x = 0
    }

    [Fact]
    public void CrosstieYPositions_ExcludesCorners()
    {
        var section = new ColumnSection(400, 600);
        var ys = TcvnRebarCalculator.CrosstieYPositions(section, Config(barsX: 3, barsY: 4), 25);
        Assert.Equal(2, ys.Count);  // Cy=4 → 2 thanh giữa
        // inset 43 → hh = 257; vị trí i=1,2 trong 4 đoạn: -257 + (514/3)*{1,2}
        Assert.All(ys, y => Assert.True(Math.Abs(y) < 257));
    }
}
