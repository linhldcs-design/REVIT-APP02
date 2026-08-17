using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Xunit;

namespace BeamRebarPro.Tests;

/// <summary>
/// Khoá cách phân vùng và đếm đai. Số đai ở đây phải bằng số đai builder tạo trong mô hình, nếu không
/// bản xem trước sẽ báo sai khối lượng thép.
/// </summary>
public sealed class BeamRebarStirrupFactoryTests
{
    private const double WidthMm = 300;
    private const double HeightMm = 600;
    private const double TopElevationMm = 3000;

    private static readonly BeamCoverMm Cover = new(TopMm: 25, BottomMm: 25, SideMm: 25);

    private static PureSpanFrame Frame(double lengthMm = 6000) => new(
        new GeometryPoint3D(0, 0, TopElevationMm - HeightMm),
        new GeometryPoint3D(lengthMm, 0, TopElevationMm - HeightMm),
        WidthMm, HeightMm, TopElevationMm);

    [Fact]
    public void Zones_Uniform_CoversWholeSpanWithSingleSpacing()
    {
        var zones = BeamRebarStirrupFactory.Zones(6000, spacingEndMm: 200, spacingMidMm: 250, twoEnds: false);

        var zone = Assert.Single(zones);
        Assert.Equal(0, zone.FromMm);
        Assert.Equal(6000, zone.ToMm);
        Assert.Equal(200, zone.SpacingMm);
    }

    [Fact]
    public void Zones_TwoEnds_SplitsIntoQuartersByDefault()
    {
        var zones = BeamRebarStirrupFactory.Zones(8000, 150, 200, twoEnds: true);

        Assert.Equal(3, zones.Count);
        Assert.Equal((0, 2000, 150), (zones[0].FromMm, zones[0].ToMm, zones[0].SpacingMm));
        Assert.Equal((2000, 6000, 200), (zones[1].FromMm, zones[1].ToMm, zones[1].SpacingMm));
        Assert.Equal((6000, 8000, 150), (zones[2].FromMm, zones[2].ToMm, zones[2].SpacingMm));
    }

    [Fact]
    public void Zones_DenseZonesLongerThanSpan_ShrinkProportionally()
    {
        // Hai vùng dày cộng lại vượt nhịp: phải co lại chứ không chồng lên nhau.
        var zones = BeamRebarStirrupFactory.Zones(
            4000, 150, 200, twoEnds: true, endZoneStartMm: 3000, endZoneEndMm: 3000);

        Assert.Equal(2000, zones[0].ToMm, 9);
        Assert.Equal(2000, zones[2].FromMm, 9);
        Assert.Equal(0, zones[1].LengthMm, 9);
    }

    [Fact]
    public void Zones_ZeroLengthSpan_ProducesNothing()
    {
        Assert.Empty(BeamRebarStirrupFactory.Zones(0, 150, 200, twoEnds: true));
    }

    [Fact]
    public void MainStirrups_UniformSpan_PlacesBarAtBothEnds()
    {
        var zones = BeamRebarStirrupFactory.Zones(6000, 200, 200, twoEnds: false);

        var stations = BeamRebarStirrupFactory.MainStirrupStations(zones, []);

        Assert.Equal(31, stations.Count); // 30 khoảng 200mm.
        Assert.Equal(0, stations[0].StationMm, 9);
        Assert.Equal(6000, stations[^1].StationMm, 9);
    }

    [Fact]
    public void MainStirrups_TwoEnds_UsesTighterSpacingNearSupports()
    {
        var zones = BeamRebarStirrupFactory.Zones(8000, spacingEndMm: 100, spacingMidMm: 200, twoEnds: true);

        var stations = BeamRebarStirrupFactory.MainStirrupStations(zones, []);

        Assert.All(stations.Where(s => s.Zone == "End1"), s => Assert.InRange(s.StationMm, 0, 2000));
        Assert.Contains(stations, s => s.Zone == "Mid");
        Assert.Contains(stations, s => s.Zone == "End2");
    }

    [Fact]
    public void SecondaryRanges_BeamNearMidspan_ReservesGapOnBothSides()
    {
        var ranges = BeamRebarStirrupFactory.SecondaryRanges(
            [(3000, 100)], spanLengthMm: 6000, stirrupDiameterMm: 8);

        var range = Assert.Single(ranges);
        // Mép dầm phụ 3000±100, lùi thêm 50 + 8/2 = 54 → cụm trái kết thúc tại 2846.
        Assert.Equal(2846, range.LeftClusterEndMm, 9);
        Assert.Equal(2846 - 150, range.LeftClusterStartMm, 9);
        Assert.Equal(3154, range.RightClusterStartMm, 9);
        Assert.Equal(3154 + 150, range.RightClusterEndMm, 9);
    }

    [Fact]
    public void SecondaryRanges_BeamTooCloseToSupport_IsSkipped()
    {
        // Cụm tăng cường sẽ tràn khỏi nhịp — bỏ qua thay vì tạo đai ngoài dầm.
        var ranges = BeamRebarStirrupFactory.SecondaryRanges([(80, 100)], 6000, 8);

        Assert.Empty(ranges);
    }

    [Fact]
    public void SecondaryRanges_StationOutsideSpan_IsSkipped()
    {
        Assert.Empty(BeamRebarStirrupFactory.SecondaryRanges([(-50, 100), (7000, 100)], 6000, 8));
    }

    [Fact]
    public void SecondaryCluster_PlacesFourBarsPerSide()
    {
        var range = BeamRebarStirrupFactory.SecondaryRanges([(3000, 100)], 6000, 8)[0];

        var stations = BeamRebarStirrupFactory.SecondaryClusterStations(range);

        Assert.Equal(8, stations.Count); // 4 đai mỗi cụm, 3 khoảng 50mm.
        Assert.Equal(range.LeftClusterStartMm, stations[0], 9);
        Assert.Equal(range.RightClusterEndMm, stations[^1], 9);
    }

    [Fact]
    public void SubtractBlocked_RemovesSecondaryBeamZoneFromMainRun()
    {
        var zone = new BeamStirrupZone(0, 6000, 200, "Uniform");
        var blocked = BeamRebarStirrupFactory.SecondaryRanges([(3000, 100)], 6000, 8);

        var segments = BeamRebarStirrupFactory.SubtractBlocked(zone, blocked);

        Assert.Equal(2, segments.Count);
        Assert.Equal((0, 2696), segments[0]);
        Assert.Equal((3304, 6000), segments[1]);
    }

    [Fact]
    public void SubtractBlocked_NoSecondaryBeams_KeepsZoneIntact()
    {
        var segments = BeamRebarStirrupFactory.SubtractBlocked(new BeamStirrupZone(0, 6000, 200, "Uniform"), []);

        Assert.Equal((0d, 6000d), Assert.Single(segments));
    }

    [Fact]
    public void MainStirrups_AroundSecondaryBeam_LeavesNoBarInsideBlockedZone()
    {
        var zones = BeamRebarStirrupFactory.Zones(6000, 200, 200, twoEnds: false);
        var blocked = BeamRebarStirrupFactory.SecondaryRanges([(3000, 100)], 6000, 8);

        var stations = BeamRebarStirrupFactory.MainStirrupStations(zones, blocked);

        Assert.DoesNotContain(stations, s =>
            s.StationMm > blocked[0].LeftClusterStartMm + 1e-6 &&
            s.StationMm < blocked[0].RightClusterEndMm - 1e-6);
    }

    [Fact]
    public void ClosedProfile_FormsRectangleInsideCover()
    {
        var points = BeamRebarStirrupFactory.ClosedProfile(Frame(), Cover, diameterMm: 8, stationMm: 1000);

        Assert.Equal(4, points.Count);
        // Bề rộng tim đai: 300 − 2×25 − 8 = 242mm.
        Assert.Equal(242, Math.Abs(points[1].Ymm - points[0].Ymm), 9);
        // Chiều cao tim đai: 600 − 2×25 − 8 = 542mm.
        Assert.Equal(542, Math.Abs(points[0].Zmm - points[3].Zmm), 9);
        Assert.All(points, p => Assert.Equal(1000, p.Xmm, 9));
    }

    [Fact]
    public void NarrowProfile_HugsSelectedMainBars()
    {
        var points = BeamRebarStirrupFactory.NarrowProfile(
            Frame(), Cover, 8, stationMm: 1000, leftLateralMm: -50, rightLateralMm: 50);

        Assert.Equal(4, points.Count);
        Assert.Equal(100, Math.Abs(points[1].Ymm - points[0].Ymm), 9);
    }

    [Fact]
    public void CHookProfile_IsSingleVerticalBar()
    {
        var points = BeamRebarStirrupFactory.CHookProfile(Frame(), Cover, 8, stationMm: 1000, lateralMm: 0);

        Assert.Equal(2, points.Count);
        Assert.Equal(points[0].Ymm, points[1].Ymm, 9);
        Assert.True(points[0].Zmm > points[1].Zmm);
    }

    [Fact]
    public void MainBarLateral_ThreeBars_SpreadsAcrossUsableWidth()
    {
        Assert.Equal(-200, BeamRebarStirrupFactory.MainBarLateralMm(0, 3, 200), 9);
        Assert.Equal(0, BeamRebarStirrupFactory.MainBarLateralMm(1, 3, 200), 9);
        Assert.Equal(200, BeamRebarStirrupFactory.MainBarLateralMm(2, 3, 200), 9);
    }

    [Fact]
    public void MainBarLateral_SingleBar_SitsAtCentre()
    {
        Assert.Equal(0, BeamRebarStirrupFactory.MainBarLateralMm(0, 1, 200), 9);
    }

    [Fact]
    public void PathBudget_ReasonableConfiguration_IsAccepted()
    {
        BeamRebarStirrupFactory.GuardPathBudget(5000);
    }

    [Fact]
    public void PathBudget_ExcessiveConfiguration_IsRejectedWithActionableMessage()
    {
        // Bước đai quá nhỏ sinh hàng chục nghìn thanh và treo giao diện — phải chặn trước khi dựng.
        var ex = Assert.Throws<ArgumentException>(() =>
            BeamRebarStirrupFactory.GuardPathBudget(BeamRebarStirrupFactory.MaxPaths + 1));

        Assert.Contains("bước đai", ex.Message);
    }
}
