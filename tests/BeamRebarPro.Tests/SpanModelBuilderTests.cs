using BeamRebarPro.Models;
using BeamRebarPro.Services;
using Xunit;

namespace BeamRebarPro.Tests;

public class SpanModelBuilderTests
{
    private static BeamSegment Seg(double x0, double x1, double w = 300, double h = 600)
        => new(new Point3(x0, 0, 0), new Point3(x1, 0, 0), new BeamSection(w, h), 0, -h / 304.8);

    [Fact]
    public void SingleSegment_ProducesOneSpanTwoSupports()
    {
        var run = SpanModelBuilder.Build([Seg(0, 10)]);

        Assert.Single(run.Spans);
        Assert.Equal(2, run.Supports.Count);
        Assert.True(run.IsSingleSpan);
        Assert.All(run.Supports, s => Assert.True(s.IsEnd));
    }

    [Fact]
    public void ThreeContinuousSegments_ProduceThreeSpansFourSupports()
    {
        var run = SpanModelBuilder.Build([Seg(0, 10), Seg(10, 18), Seg(18, 30)]);

        Assert.Equal(3, run.Spans.Count);
        Assert.Equal(4, run.Supports.Count);
        Assert.False(run.IsSingleSpan);
        // Hai gối biên IsEnd, hai gối giữa không.
        Assert.True(run.Supports[0].IsEnd);
        Assert.True(run.Supports[^1].IsEnd);
        Assert.False(run.Supports[1].IsEnd);
        Assert.False(run.Supports[2].IsEnd);
    }

    [Fact]
    public void SingleLongSegment_WithInternalSupports_ProducesMultipleSpans()
    {
        var run = SpanModelBuilder.Build(
            [Seg(0, 30)],
            [new Point3(10, 0, 0), new Point3(18, 0, 0)]);

        Assert.Equal(3, run.Spans.Count);
        Assert.Equal(4, run.Supports.Count);
        Assert.Equal(0, run.Spans[0].Start.X);
        Assert.Equal(10, run.Spans[0].End.X, precision: 6);
        Assert.Equal(18, run.Spans[1].End.X, precision: 6);
        Assert.Equal(30, run.Spans[2].End.X);
        Assert.False(run.IsSingleSpan);
        Assert.False(run.Supports[1].IsEnd);
        Assert.False(run.Supports[2].IsEnd);
    }

    [Fact]
    public void OutOfOrderSegments_AreSortedAlongAxis()
    {
        // Đưa vào lộn xộn; builder phải sắp theo trục → span 0 bắt đầu tại x=0.
        var run = SpanModelBuilder.Build([Seg(18, 30), Seg(0, 10), Seg(10, 18)]);

        Assert.Equal(0, run.Spans[0].Start.X);
        Assert.Equal(30, run.Spans[^1].End.X);
    }

    [Fact]
    public void GapBetweenSegments_EmitsContinuityWarning()
    {
        // Khe 200mm > dung sai 50mm → cảnh báo không nối liền.
        var run = SpanModelBuilder.Build([Seg(0, 10), Seg(10.7, 18)]);

        Assert.Contains(run.Warnings, w => w.Contains("không nối liền"));
    }

    [Fact]
    public void SectionChange_EmitsWarning()
    {
        var run = SpanModelBuilder.Build([Seg(0, 10, w: 300), Seg(10, 18, w: 400)]);

        Assert.Contains(run.Warnings, w => w.Contains("Tiết diện đổi"));
    }

    [Fact]
    public void EmptyInput_ReturnsWarningNoSpans()
    {
        var run = SpanModelBuilder.Build([]);

        Assert.Empty(run.Spans);
        Assert.NotEmpty(run.Warnings);
    }
}
