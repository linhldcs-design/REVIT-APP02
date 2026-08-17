using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Xunit;

namespace BeamRebarPro.Tests;

/// <summary>
/// Khoá hình học thép dọc. Số liệu kỳ vọng tính tay theo đúng công thức builder đang dùng, nên sai
/// lệch ở đây báo hiệu preview và thép thật đã tách nhau.
/// </summary>
public sealed class BeamRebarLongitudinalFactoryTests
{
    private const double WidthMm = 300;
    private const double HeightMm = 600;
    private const double TopElevationMm = 3000;
    private const double SpanLengthMm = 6000;

    private static readonly BeamCoverMm Cover = new(TopMm: 25, BottomMm: 25, SideMm: 25);

    private static PureSpanFrame Frame() => new(
        new GeometryPoint3D(0, 0, TopElevationMm - HeightMm),
        new GeometryPoint3D(SpanLengthMm, 0, TopElevationMm - HeightMm),
        WidthMm, HeightMm, TopElevationMm);

    [Fact]
    public void Vertical_TopBar_SitsInsideStirrupBelowTopFace()
    {
        // cover 25 + đai 8 + nửa thanh 16/2 = 41mm dưới mặt trên.
        var (vertical, usableHalf) = BeamRebarLongitudinalFactory.Vertical(
            Frame(), Cover, barDiameterMm: 16, stirrupDiameterMm: 8, atTop: true);

        Assert.Equal(-41, vertical, 9);
        // 300/2 − (25+8) − 16/2 = 109mm.
        Assert.Equal(109, usableHalf, 9);
    }

    [Fact]
    public void Vertical_BottomBar_MeasuredUpFromBottomFace()
    {
        var (vertical, _) = BeamRebarLongitudinalFactory.Vertical(
            Frame(), Cover, barDiameterMm: 16, stirrupDiameterMm: 8, atTop: false);

        // −(600 − (25+8) − 8) = −559mm.
        Assert.Equal(-559, vertical, 9);
    }

    [Fact]
    public void Vertical_Layer2Offset_PushesBarDeeperIntoSection()
    {
        var (layer1, _) = BeamRebarLongitudinalFactory.Vertical(
            Frame(), Cover, 16, 8, atTop: true);
        var (layer2, _) = BeamRebarLongitudinalFactory.Vertical(
            Frame(), Cover, 16, 8, atTop: true, extraVerticalOffsetMm: 30);

        Assert.Equal(layer1 - 30, layer2, 9);
    }

    [Fact]
    public void Vertical_NarrowSection_ClampsUsableHalfToZero()
    {
        // Tiết diện hẹp hơn tổng cover: không được trả số âm (thanh sẽ lật ra ngoài).
        var narrow = new PureSpanFrame(
            new GeometryPoint3D(0, 0, 0), new GeometryPoint3D(SpanLengthMm, 0, 0),
            widthMm: 60, heightMm: HeightMm, topElevationMm: TopElevationMm);

        var (_, usableHalf) = BeamRebarLongitudinalFactory.Vertical(narrow, Cover, 32, 8, atTop: true);

        Assert.Equal(0, usableHalf);
    }

    [Fact]
    public void LateralOffsets_ThreeBars_SpanFullUsableWidth()
    {
        var offsets = BeamRebarLongitudinalFactory.LateralOffsetsMm(3, usableHalfMm: 200);

        Assert.Equal([-200, 0, 200], offsets);
    }

    [Fact]
    public void LateralOffsets_SingleBar_SitsAtSectionCentre()
    {
        Assert.Equal([0d], BeamRebarLongitudinalFactory.LateralOffsetsMm(1, usableHalfMm: 200));
    }

    [Fact]
    public void LateralOffsets_NoBars_ProducesNothing()
    {
        Assert.Empty(BeamRebarLongitudinalFactory.LateralOffsetsMm(0, usableHalfMm: 200));
    }

    [Fact]
    public void GapOffsets_TwoAddedBars_SitBetweenThreeMainBars()
    {
        // Thanh chủ tại −200, 0, +200 → tâm hai khe là −100 và +100.
        var offsets = BeamRebarLongitudinalFactory.GapOffsetsMm(
            positionInSection: "0,1", mainBarCount: 3, addCount: 2, usableHalfMm: 200);

        Assert.Equal([-100d, 100d], offsets);
    }

    [Fact]
    public void GapOffsets_FewMainBars_FallsBackToInteriorSpacing()
    {
        // Chỉ 2 thanh chủ thì không có khe giữa để xen — chia đều trong lòng.
        var offsets = BeamRebarLongitudinalFactory.GapOffsetsMm("0,1", mainBarCount: 2, addCount: 2, usableHalfMm: 300);

        Assert.Equal([-100d, 100d], offsets);
    }

    [Fact]
    public void GapOffsets_RequestBeyondAvailableGaps_FallsBackToInteriorSpacing()
    {
        // 3 thanh chủ có 2 khe, nhưng yêu cầu 3 cây gia cường → không xen được, chia đều.
        var offsets = BeamRebarLongitudinalFactory.GapOffsetsMm("0,1,5", mainBarCount: 3, addCount: 3, usableHalfMm: 200);

        Assert.Equal([-100d, 0d, 100d], offsets);
    }

    [Fact]
    public void GapOffsets_MalformedPositionText_DoesNotThrow()
    {
        // Chuỗi vị trí đến từ ô nhập của người dùng; ký tự rác phải bị bỏ qua chứ không làm hỏng lệnh.
        var offsets = BeamRebarLongitudinalFactory.GapOffsetsMm("a,,b", mainBarCount: 3, addCount: 2, usableHalfMm: 200);

        Assert.Equal(2, offsets.Count);
    }

    [Fact]
    public void LateralOffsets_Layer2_IgnoresGapsAndSpreadsFullWidth()
    {
        // Thép lớp 2 rải đều suốt bề rộng thay vì xen khe.
        var offsets = BeamRebarLongitudinalFactory.LateralOffsetsMm(
            2, usableHalfMm: 200, positionInSection: "0,1", mainBarCount: 3, spreadAcrossFullWidth: true);

        Assert.Equal([-200d, 200d], offsets);
    }

    [Fact]
    public void LateralOffsets_Layer1_UsesGapsBetweenMainBars()
    {
        var offsets = BeamRebarLongitudinalFactory.LateralOffsetsMm(
            2, usableHalfMm: 200, positionInSection: "0,1", mainBarCount: 3, spreadAcrossFullWidth: false);

        Assert.Equal([-100d, 100d], offsets);
    }

    [Fact]
    public void ClampSegment_FullLengthBar_PullsBackFromBeamEnds()
    {
        var (startT, endT) = BeamRebarLongitudinalFactory.ClampSegmentInsideHost(Frame(), Cover, 0, 1);

        Assert.Equal(25.0 / SpanLengthMm, startT, 9);
        Assert.Equal(1 - 25.0 / SpanLengthMm, endT, 9);
    }

    [Fact]
    public void ClampSegment_InteriorBar_KeepsOriginalExtent()
    {
        var (startT, endT) = BeamRebarLongitudinalFactory.ClampSegmentInsideHost(Frame(), Cover, 0.25, 0.75);

        Assert.Equal(0.25, startT, 9);
        Assert.Equal(0.75, endT, 9);
    }

    [Fact]
    public void ClampSegment_ShortSpan_CapsInsetAtFivePercent()
    {
        var shortFrame = new PureSpanFrame(
            new GeometryPoint3D(0, 0, 0), new GeometryPoint3D(200, 0, 0),
            WidthMm, HeightMm, TopElevationMm);

        var (startT, _) = BeamRebarLongitudinalFactory.ClampSegmentInsideHost(shortFrame, Cover, 0, 1);

        Assert.Equal(0.05, startT, 9);
    }

    [Fact]
    public void Polyline_StraightBar_HasTwoPoints()
    {
        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, lateralMm: 0, verticalMm: -41, BarBendDirection.None);

        Assert.Equal(2, points.Count);
        Assert.Equal(TopElevationMm - 41, points[0].Zmm, 9);
    }

    [Fact]
    public void Polyline_BendAtStart_AddsLeadingVertexBelowBar()
    {
        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, 0, -41, BarBendDirection.Down, startBendMm: 300);

        Assert.Equal(3, points.Count);
        Assert.Equal(points[1].Zmm - 300, points[0].Zmm, 9);
        Assert.Equal(points[0].Xmm, points[1].Xmm, 9);
    }

    [Fact]
    public void Polyline_BendAtBothEnds_AddsVertexEachSide()
    {
        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, 0, -41, BarBendDirection.Down, startBendMm: 300, endBendMm: 300);

        Assert.Equal(4, points.Count);
        Assert.Equal(points[^2].Zmm - 300, points[^1].Zmm, 9);
    }

    [Fact]
    public void Polyline_ZeroBendLength_StaysStraight()
    {
        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, 0, -41, BarBendDirection.Down, startBendMm: 0, endBendMm: 0);

        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void Polyline_BendLongerThanSection_IsCappedAtSectionDepth()
    {
        var maxBend = BeamRebarLongitudinalFactory.MaxBendLengthMm(Frame(), Cover, barDiameterMm: 16);

        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, 0, -41, BarBendDirection.Down, startBendMm: 5000, maxBendMm: maxBend);

        // 600 − 25 − 25 − 16 = 534mm.
        Assert.Equal(534, maxBend, 9);
        Assert.Equal(points[1].Zmm - 534, points[0].Zmm, 9);
    }

    [Fact]
    public void Polyline_BendUp_RaisesVertexAboveBar()
    {
        // Thép gia cường bẻ ngược phía so với thép chủ dưới — hướng phải theo tham số, không tự suy.
        var points = BeamRebarLongitudinalFactory.BuildPolyline(
            Frame(), 0, 1, 0, -559, BarBendDirection.Up, startBendMm: 200);

        Assert.Equal(points[1].Zmm + 200, points[0].Zmm, 9);
    }
}
