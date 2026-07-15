using RevitAPP.Core.Models;
using Xunit;

namespace RevitAPP.Tests;

public class PointCloudInfoTests
{
    [Fact]
    public void Display_PlainName_WhenSupportsOverrides()
    {
        var info = new PointCloudInfo(123, "Scan A", true, new[] { "s1" }, Array.Empty<string>());
        Assert.Equal("Scan A", info.Display);
    }

    [Fact]
    public void Display_FlagsUnsupported_WhenNoOverrides()
    {
        var info = new PointCloudInfo(123, "Scan A", false, Array.Empty<string>(), Array.Empty<string>());
        Assert.Contains("không hỗ trợ override", info.Display);
    }

    [Fact]
    public void EmptyScansAndRegions_AreAllowed()
    {
        var info = new PointCloudInfo(1, "Empty", true, Array.Empty<string>(), Array.Empty<string>());
        Assert.Empty(info.Scans);
        Assert.Empty(info.Regions);
    }

    [Fact]
    public void InstanceId_PreservesLongValue()
    {
        const long bigId = 4_294_967_300; // > int.MaxValue → đảm bảo dùng long
        var info = new PointCloudInfo(bigId, "Big", true, Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal(bigId, info.InstanceId);
    }
}
