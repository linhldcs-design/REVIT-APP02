using BeamRebar.Core.Models;
using BeamRebar.Core.Services;
using Xunit;

namespace BeamRebar.Core.Tests;

public class SpanModelBuilderTests
{
    private static readonly BeamSection Section = new(300, 600);

    private static BeamSegment Segment(double x0, double x1)
        => new(new Point3(x0, 0, 0), new Point3(x1, 0, 0), Section, TopElevationFeet: 0, BottomElevationFeet: -2);

    [Fact]
    public void Single_beam_yields_one_span_two_end_supports()
    {
        var result = SpanModelBuilder.Build([Segment(0, 8.2)]); // ~2500mm

        Assert.True(result.Run.IsSingleSpan);
        Assert.Single(result.Run.Spans);
        Assert.Equal(2, result.Run.Supports.Count);
        Assert.All(result.Run.Supports, s => Assert.True(s.IsEnd));
    }

    [Fact]
    public void Three_joined_beams_yield_three_spans_four_supports()
    {
        var result = SpanModelBuilder.Build([Segment(0, 8), Segment(8, 16), Segment(16, 24)]);

        Assert.Equal(3, result.Run.Spans.Count);
        Assert.Equal(4, result.Run.Supports.Count);
        // Hai gối đầu/cuối là end, hai gối giữa không.
        Assert.True(result.Run.Supports[0].IsEnd);
        Assert.False(result.Run.Supports[1].IsEnd);
        Assert.False(result.Run.Supports[2].IsEnd);
        Assert.True(result.Run.Supports[3].IsEnd);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Unsorted_segments_are_ordered_along_axis()
    {
        // Cố tình đảo thứ tự — builder phải sắp lại theo trục.
        var result = SpanModelBuilder.Build([Segment(16, 24), Segment(0, 8), Segment(8, 16)]);

        Assert.Equal(3, result.Run.Spans.Count);
        Assert.Equal(0, result.Run.Spans[0].Start.X, 3);
        Assert.Equal(24, result.Run.Spans[2].End.X, 3);
    }

    [Fact]
    public void Gap_between_beams_produces_warning()
    {
        var result = SpanModelBuilder.Build([Segment(0, 8), Segment(10, 18)]); // hở 2ft

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("không nối liền mạch"));
    }

    [Fact]
    public void Empty_input_returns_warning()
    {
        var result = SpanModelBuilder.Build([]);
        Assert.Empty(result.Run.Spans);
        Assert.NotEmpty(result.Warnings);
    }
}
