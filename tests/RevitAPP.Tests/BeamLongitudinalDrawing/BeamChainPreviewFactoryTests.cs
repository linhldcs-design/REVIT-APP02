using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class BeamChainPreviewFactoryTests
{
    [Fact]
    public void Create_Reverse_FlipsSpanAndSupportDirection()
    {
        var chain = new BeamChainModel(
            [new BeamSpanModel(1, 0, new Point3(0,0,0), new Point3(10,0,0), 10, 1, 2)],
            new Point3(0,0,0), new Point3(10,0,0), 10);
        var fingerprint = new RebarStationFingerprint(1, 2, [], [], false);
        var stations = new[]
        {
            new SectionStation(SectionStationKind.LeftSupport, 0, [0], fingerprint, "left"),
            new SectionStation(SectionStationKind.RightSupport, 10, [0], fingerprint, "right")
        };
        var preview = BeamChainPreviewFactory.Create(chain, stations, true);
        Assert.True(preview.IsReversed);
        Assert.Equal(SectionStationKind.LeftSupport, preview.Stations[0].Kind);
        Assert.Equal(SectionStationKind.RightSupport, preview.Stations[1].Kind);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(5, 50)]
    [InlineData(10, 80)]
    public void ProjectX_MapsDistanceIntoPaddedCanvas(double distance, double expected)
    {
        Assert.Equal(expected, BeamChainPreviewFactory.ProjectX(distance, 10, 100, 20), 6);
    }

    [Fact]
    public void Confirmation_InvalidationDisablesGenerateUntilReconfirmed()
    {
        var state = new PreviewConfirmationState();
        Assert.True(state.Confirm(true));
        Assert.True(state.CanGenerate(true, true));
        state.Invalidate();
        Assert.False(state.CanGenerate(true, true));
    }
}
