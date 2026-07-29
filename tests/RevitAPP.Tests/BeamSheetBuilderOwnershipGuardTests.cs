using System.Runtime.CompilerServices;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamSheetBuilderOwnershipGuardTests
{
    [Fact]
    public void ArrangeCrossViewports_DoesNotRecoverMovableOwnershipFromWholeSheet()
    {
        var source = File.ReadAllText(SourcePath());

        Assert.DoesNotContain("CollectBeamClustersOnSheet", source);
        Assert.DoesNotContain("beamGroups = allGroups", source);
        Assert.Contains("byId.TryGetValue(placement.ViewportId", source);
        Assert.Contains("Đã giữ viewport mới trên sheet để người dùng chỉnh tay", source);
    }

    private static string SourcePath([CallerFilePath] string testFile = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFile)
            ?? throw new InvalidOperationException("Không xác định được thư mục test.");
        return Path.GetFullPath(Path.Combine(
            testsDirectory, "..", "..", "RevitAPP", "Services", "BeamDrawing", "SheetBuilder.cs"));
    }
}
