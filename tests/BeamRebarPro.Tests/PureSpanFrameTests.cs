using RevitAPP.Core.Models;
using Xunit;

namespace BeamRebarPro.Tests;

/// <summary>
/// Khoá hệ trục cục bộ của nhịp. Preview đặt thanh bằng chính hệ trục này, nên lệch ở đây là thép
/// xem trước nằm sai chỗ so với thép thật.
/// </summary>
public sealed class PureSpanFrameTests
{
    private const double BeamLengthMm = 6000;
    private const double WidthMm = 300;
    private const double HeightMm = 600;
    private const double TopElevationMm = 3000;

    private static PureSpanFrame AlongXAxis(double lateralOffsetMm = 0) => new(
        new GeometryPoint3D(0, 0, TopElevationMm - HeightMm),
        new GeometryPoint3D(BeamLengthMm, 0, TopElevationMm - HeightMm),
        WidthMm, HeightMm, TopElevationMm, lateralOffsetMm);

    [Fact]
    public void AcrossAxis_ForBeamAlongX_PointsToNegativeY()
    {
        // Dấu này quyết định chiều rải thanh: builder dựng thanh gốc tại -usableHalf rồi rải về phía
        // +Across. Đảo dấu là cả bó thép chạy ra ngoài tiết diện thay vì vào trong.
        // Along × (0,0,1) = (1,0,0) × (0,0,1) = (0,-1,0).
        var frame = AlongXAxis();

        Assert.Equal(0, frame.Across.X, 9);
        Assert.Equal(-1, frame.Across.Y, 9);
        Assert.Equal(0, frame.Across.Z, 9);
    }

    [Fact]
    public void AlongAxis_IsUnitVectorInBeamDirection()
    {
        var frame = AlongXAxis();

        Assert.Equal(1, frame.Along.X, 9);
        Assert.Equal(0, frame.Along.Y, 9);
        Assert.Equal(1, frame.Along.Length, 9);
        Assert.Equal(BeamLengthMm, frame.LengthMm, 9);
    }

    [Fact]
    public void AxisTop_AtMidspan_SitsOnTopFaceCentre()
    {
        var frame = AlongXAxis();

        var mid = frame.AxisTop(0.5);

        Assert.Equal(BeamLengthMm / 2, mid.Xmm, 9);
        Assert.Equal(0, mid.Ymm, 9);
        Assert.Equal(TopElevationMm, mid.Zmm, 9);
    }

    [Fact]
    public void AxisTop_AtEnds_MatchesSpanEndpoints()
    {
        var frame = AlongXAxis();

        Assert.Equal(0, frame.AxisTop(0).Xmm, 9);
        Assert.Equal(BeamLengthMm, frame.AxisTop(1).Xmm, 9);
    }

    [Fact]
    public void LateralOffset_ShiftsAxisAlongAcrossDirection()
    {
        // Bù justification dầm: bỏ sót thì cả bó thép lệch khỏi khối bê tông.
        var frame = AlongXAxis(lateralOffsetMm: 50);

        var mid = frame.AxisTop(0.5);

        Assert.Equal(BeamLengthMm / 2, mid.Xmm, 9);
        Assert.Equal(-50, mid.Ymm, 9); // Across = (0,-1,0) → offset dương đẩy về -Y.
    }

    [Fact]
    public void PointAt_AppliesLateralAndVerticalOffsets()
    {
        var frame = AlongXAxis();

        var point = frame.PointAt(0.5, lateralMm: 100, verticalMm: -75);

        Assert.Equal(BeamLengthMm / 2, point.Xmm, 9);
        Assert.Equal(-100, point.Ymm, 9);
        Assert.Equal(TopElevationMm - 75, point.Zmm, 9);
    }

    [Fact]
    public void PointAtStation_MatchesParametricPoint()
    {
        var frame = AlongXAxis();

        var byStation = frame.PointAtStation(1500, 0, 0);
        var byParameter = frame.PointAt(0.25, 0, 0);

        Assert.Equal(byParameter.Xmm, byStation.Xmm, 9);
        Assert.Equal(byParameter.Ymm, byStation.Ymm, 9);
    }

    [Fact]
    public void ZeroLengthSpan_IsRejected()
    {
        var point = new GeometryPoint3D(0, 0, 0);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PureSpanFrame(point, point, WidthMm, HeightMm, TopElevationMm, spanIndex: 3));

        Assert.Contains("Span 3", ex.Message);
    }

    [Fact]
    public void VerticalBeam_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new PureSpanFrame(
            new GeometryPoint3D(0, 0, 0),
            new GeometryPoint3D(0, 0, BeamLengthMm),
            WidthMm, HeightMm, TopElevationMm, spanIndex: 2));

        Assert.Contains("thẳng đứng", ex.Message);
    }

    [Fact]
    public void SkewedBeam_KeepsAcrossPerpendicularToAlong()
    {
        // Dầm xiên trong mặt bằng vẫn phải có Across vuông góc Along, nếu không tiết diện bị méo.
        var frame = new PureSpanFrame(
            new GeometryPoint3D(0, 0, 0),
            new GeometryPoint3D(3000, 4000, 0),
            WidthMm, HeightMm, TopElevationMm);

        var dot = frame.Along.X * frame.Across.X + frame.Along.Y * frame.Across.Y + frame.Along.Z * frame.Across.Z;

        Assert.Equal(0, dot, 9);
        Assert.Equal(1, frame.Across.Length, 9);
        Assert.Equal(5000, frame.LengthMm, 9);
    }
}
