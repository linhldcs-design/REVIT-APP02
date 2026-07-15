using RevitAPP.Core.Models;
using Xunit;

namespace RevitAPP.Tests;

public class PointCloudColorModeOptionTests
{
    [Fact]
    public void All_CoversEveryEnumValue()
    {
        var enumValues = Enum.GetValues<PointCloudColorModeOption>();
        var itemModes = PointCloudColorModeItem.All.Select(i => i.Mode).ToList();

        Assert.Equal(enumValues.Length, PointCloudColorModeItem.All.Count);
        Assert.All(enumValues, mode => Assert.Contains(mode, itemModes));
    }

    [Fact]
    public void All_HasNoDuplicateModes()
    {
        var modes = PointCloudColorModeItem.All.Select(i => i.Mode).ToList();
        Assert.Equal(modes.Count, modes.Distinct().Count());
    }

    [Fact]
    public void All_EveryItemHasDisplayLabel()
    {
        Assert.All(PointCloudColorModeItem.All, item => Assert.False(string.IsNullOrWhiteSpace(item.Display)));
    }

    [Fact]
    public void NoOverride_RepresentsNativeRgb()
    {
        // RGB gốc của Revit = NoOverride (Revit không có enum "RGB" riêng).
        var rgbItem = PointCloudColorModeItem.All.Single(i => i.Mode == PointCloudColorModeOption.NoOverride);
        Assert.Contains("RGB", rgbItem.Display);
    }

    [Fact]
    public void Item_ToString_ReturnsDisplay()
    {
        var item = new PointCloudColorModeItem(PointCloudColorModeOption.Elevation, "Theo cao độ");
        Assert.Equal("Theo cao độ", item.ToString());
    }
}
