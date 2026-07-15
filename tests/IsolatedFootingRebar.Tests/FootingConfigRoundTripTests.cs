using System.Text.Json;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingConfigRoundTripTests
{
    [Fact]
    public void FootingRebarModel_RoundTripsThroughSystemTextJson()
    {
        var model = new FootingRebarModel
        {
            MidEnabled = true,
            MidLayers = 3,
            DirXOverride = new Point3(1, 0, 0),
            BottomX = new LayerBarConfig
            {
                Diameter = new RebarDiameter(12),
                UseSpacing = false,
                Count = 9,
                HookLengthMm = 500
            },
            Vertical = new VerticalBarConfig
            {
                Diameter = new RebarDiameter(16),
                SpacingXMm = 175,
                SpacingYMm = 225,
                CountX = 4,
                CountY = 6,
                HookLengthMm = 650
            },
            Horizontal = new HorizontalStirrupConfig
            {
                DiameterX = new RebarDiameter(8),
                DiameterY = new RebarDiameter(8),
                Closed = false,
                HookLengthMm = 120,
                Layers = 4
            }
        };

        var json = JsonSerializer.Serialize(model);
        var roundTrip = JsonSerializer.Deserialize<FootingRebarModel>(json);

        Assert.Equal(model, roundTrip);
    }

    [Fact]
    public void PresetMap_RoundTripsThroughSystemTextJson()
    {
        var presets = new Dictionary<string, FootingRebarModel>
        {
            ["M1"] = new() { DirXOverride = null },
            ["M2"] = new() { DirXOverride = new Point3(0, 1, 0), HorizontalEnabled = false }
        };

        var json = JsonSerializer.Serialize(presets);
        var roundTrip = JsonSerializer.Deserialize<Dictionary<string, FootingRebarModel>>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(presets, roundTrip);
    }
}
