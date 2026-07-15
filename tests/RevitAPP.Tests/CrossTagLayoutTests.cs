using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public class CrossTagLayoutTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(800)]
    public void TagYsFromBeamBounds_AnchorsOutsideBeamAndSpreadsDescending(double beamHeightMm)
    {
        var beamTop = 10.0;
        var beamBottom = beamTop - beamHeightMm / 304.8;

        var slots = CrossTagLayout.TagYsFromBeamBounds(4, beamTop, beamBottom);

        Assert.Equal(beamTop + 20.0 / 304.8, slots[0], 6);
        Assert.Equal(beamBottom - 50.0 / 304.8, slots[^1], 6);
        Assert.All(slots.Zip(slots.Skip(1)), pair => Assert.True(pair.First > pair.Second));
    }

    [Fact]
    public void TagYsFromBeamBounds_IntermediateSlotsAdaptWhenBeamHeightChanges()
    {
        var shortBeam = CrossTagLayout.TagYsFromBeamBounds(4, 2.0, 2.0 - 400.0 / 304.8);
        var tallBeam = CrossTagLayout.TagYsFromBeamBounds(4, 2.0, 2.0 - 800.0 / 304.8);

        Assert.True(tallBeam[1] < shortBeam[1]);
        Assert.True(tallBeam[2] < shortBeam[2]);
    }

    [Fact]
    public void TagHeadLocal_AllSameX_AtCropMaxPlusOffset()
    {
        var a = CrossTagLayout.TagHeadLocal(0, 5, 0, 2.953, 1.64);
        var b = CrossTagLayout.TagHeadLocal(4, 5, 0, 2.953, 1.64);

        Assert.Equal(1.64 + CrossTagLayout.TagColumnOffsetXFeet, a.X, 6);
        Assert.Equal(a.X, b.X, 6);
    }

    [Fact]
    public void TagHeadLocal_SpreadsTopToBottom_WithinMargins()
    {
        // Khớp mẫu DK1-15: crop cao 2.953, 5 tag → trên cùng ~2.526, dưới cùng ~0.427.
        var top = CrossTagLayout.TagHeadLocal(0, 5, 0, 2.953, 1.64);
        var bottom = CrossTagLayout.TagHeadLocal(4, 5, 0, 2.953, 1.64);

        Assert.Equal(2.953 - CrossTagLayout.VerticalMarginFeet, top.Y, 3);
        Assert.Equal(CrossTagLayout.VerticalMarginFeet, bottom.Y, 3);
        Assert.True(top.Y > bottom.Y); // index 0 trên cùng
    }

    [Fact]
    public void TagHeadLocal_SingleTag_Centered()
    {
        var only = CrossTagLayout.TagHeadLocal(0, 1, 0, 2.0, 1.0);
        Assert.Equal(1.0, only.Y, 6); // (top+bottom)/2 = (2-0.427 + 0+0.427)/2 = 1.0
    }

    [Fact]
    public void SpreadNoOverlap_EnforcesMinGap_KeepsDescendingOrder()
    {
        // 2 thép sát nhau (0.1 < minGap) → phải giãn ra ≥ 0.5.
        var y = CrossTagLayout.SpreadNoOverlap(new[] { 2.0, 1.9, 1.0, 0.4 });

        for (var i = 1; i < y.Length; i++)
            Assert.True(y[i - 1] - y[i] >= CrossTagLayout.MinTagGapFeet - 1e-9, $"gap {i} quá nhỏ");
    }

    [Fact]
    public void SpreadNoOverlap_AlreadySpaced_Unchanged()
    {
        var input = new[] { 2.2, 1.57, 1.05, 0.43 }; // giống đích DK2-1, gap đều >0.5
        var y = CrossTagLayout.SpreadNoOverlap(input);
        for (var i = 0; i < input.Length; i++) Assert.Equal(input[i], y[i], 6);
    }

    [Fact]
    public void SpreadNoOverlap_KeepsCenter()
    {
        var input = new[] { 1.2, 1.1, 1.0 }; // chụm, tâm = 1.1
        var y = CrossTagLayout.SpreadNoOverlap(input);
        Assert.Equal(1.1, (y[0] + y[2]) * 0.5, 6);
    }

    [Fact]
    public void SpreadClampedToCrop_EnforcesGap_StaysInsideCrop()
    {
        // Thép chụm trong crop ĐỦ RỘNG (cao 3.0 như đích DK2-1 cao 2.625+).
        var y = CrossTagLayout.SpreadClampedToCrop(new[] { 1.02, 0.99, 0.91, 0.46 }, 0, 3.0);

        for (var i = 1; i < y.Length; i++)
            Assert.True(y[i - 1] - y[i] >= CrossTagLayout.MinTagGapFeet - 1e-9, $"gap {i} nhỏ");
        Assert.True(y[^1] >= CrossTagLayout.VerticalMarginFeet - 1e-9, "tràn dưới crop");
        Assert.True(y[0] <= 3.0 - CrossTagLayout.VerticalMarginFeet + 1e-9, "vượt đỉnh crop");
    }

    [Fact]
    public void SpreadClampedToCrop_TightCrop_FillsEvenly()
    {
        // Crop quá hẹp cho 4 tag với minGap 0.5 (vùng khả dụng < 1.5) → rải đều lấp đầy.
        var y = CrossTagLayout.SpreadClampedToCrop(new[] { 1.0, 0.9, 0.8, 0.7 }, 0, 1.5);
        Assert.Equal(1.5 - CrossTagLayout.VerticalMarginFeet, y[0], 3);
        Assert.Equal(CrossTagLayout.VerticalMarginFeet, y[^1], 3);
    }
}
