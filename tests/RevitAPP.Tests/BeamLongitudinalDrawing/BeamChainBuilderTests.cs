using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class BeamChainBuilderTests
{
    [Fact]
    public void Build_UnorderedStraightSpans_ReturnsDeterministicConnectedPath()
    {
        var inputs = new[]
        {
            Span(20, 10, 20),
            Span(10, 0, 10),
            Span(30, 20, 30)
        };

        var result = BeamChainBuilder.Build(inputs, new BeamChainTolerance(0.01, 0.01, 0.01));

        Assert.True(result.IsValid);
        Assert.NotNull(result.Model);
        Assert.Equal(new long[] { 10, 20, 30 }, result.Model.Spans.Select(x => x.SourceId));
        Assert.Equal(30, result.Model.TotalLengthFeet, 6);
        Assert.Equal(0, result.Model.Spans[0].Start.X, 6);
        Assert.Equal(30, result.Model.Spans[^1].End.X, 6);
    }

    [Fact]
    public void Build_ReversedInputEndpoints_OrientsEverySpanAlongPath()
    {
        var inputs = new[]
        {
            Span(2, 20, 10),
            Span(1, 10, 0)
        };

        var result = BeamChainBuilder.Build(inputs, BeamChainTolerance.Default);

        Assert.True(result.IsValid);
        Assert.All(result.Model!.Spans, span => Assert.True(span.End.X > span.Start.X));
    }

    [Fact]
    public void Build_BranchTopology_ReturnsActionableError()
    {
        var inputs = new[]
        {
            Span(1, 0, 10),
            Span(2, 10, 20),
            new BeamSpanInput(3, P(10), new Point3(10, 10, 0), 1, 2)
        };

        var result = BeamChainBuilder.Build(inputs, BeamChainTolerance.Default);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == BeamChainErrorCode.Branch);
    }

    [Fact]
    public void Build_DisconnectedSpans_ReturnsDisconnectedError()
    {
        var result = BeamChainBuilder.Build(
            new[] { Span(1, 0, 10), Span(2, 20, 30) }, BeamChainTolerance.Default);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == BeamChainErrorCode.Disconnected);
    }

    [Fact]
    public void Build_MisalignedSpan_ReturnsAlignmentError()
    {
        var inputs = new[]
        {
            Span(1, 0, 10),
            new BeamSpanInput(2, P(10), new Point3(20, 1, 0), 1, 2)
        };

        var result = BeamChainBuilder.Build(inputs, new BeamChainTolerance(0.01, 0.1, 0.01));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == BeamChainErrorCode.NotCollinear);
    }

    [Fact]
    public void Build_DifferentElevation_ReturnsElevationError()
    {
        var inputs = new[]
        {
            Span(1, 0, 10),
            new BeamSpanInput(2, P(10), new Point3(20, 0, 1), 1, 2)
        };

        var result = BeamChainBuilder.Build(inputs, new BeamChainTolerance(0.01, 0.01, 0.1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == BeamChainErrorCode.DifferentElevation);
    }

    [Fact]
    public void Build_ChainedEndpointTolerance_IsIndependentOfInputOrder()
    {
        var first = new BeamSpanInput(1, new Point3(-10, 0, 0), new Point3(0, 0, 0), 1, 2);
        var second = new BeamSpanInput(2, new Point3(0.009, 0, 0), new Point3(10, 0, 0), 1, 2);
        var third = new BeamSpanInput(3, new Point3(0.018, 0, 0), new Point3(0, 10, 0), 1, 2);
        var tolerance = new BeamChainTolerance(0.01, 0.01, 0.01);

        var forward = BeamChainBuilder.Build([first, second, third], tolerance);
        var reverse = BeamChainBuilder.Build([third, second, first], tolerance);

        Assert.False(forward.IsValid);
        Assert.False(reverse.IsValid);
        Assert.Equal(forward.Errors[0].Code, reverse.Errors[0].Code);
        Assert.Equal(BeamChainErrorCode.Branch, forward.Errors[0].Code);
    }

    private static BeamSpanInput Span(long id, double startX, double endX) =>
        new(id, P(startX), P(endX), 1, 2);

    private static Point3 P(double x) => new(x, 0, 0);
}
