using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgOutputPathPolicyTests
{
    [Fact]
    public void SuggestedDirectory_CloudModelPath_FallsBackToDocuments()
    {
        var fallback = Path.Combine(Path.GetTempPath(), "Documents");

        var actual = DwgOutputPathPolicy.ResolveSuggestedDirectory(
            @"Autodesk Docs:\PHAM GIA\Nha Van Phong.rvt",
            fallback);

        Assert.Equal(fallback, actual);
    }

    [Fact]
    public void SuggestedDirectory_LocalModelPath_UsesModelDirectory()
    {
        var actual = DwgOutputPathPolicy.ResolveSuggestedDirectory(
            @"C:\Projects\Nha Van Phong.rvt",
            @"C:\Users\Admin\Documents");

        Assert.Equal(@"C:\Projects", actual);
    }

    [Fact]
    public void ExistingInitialDirectory_CloudPseudoPath_ReturnsNull()
    {
        var actual = DwgOutputPathPolicy.GetExistingInitialDirectory(
            @"Autodesk Docs:\PHAM GIA\Nha Van Phong-Model.dwg");

        Assert.Null(actual);
    }

    [Fact]
    public void ExistingInitialDirectory_LocalPath_ReturnsDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(root, DwgOutputPathPolicy.GetExistingInitialDirectory(Path.Combine(root, "output.dwg")));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public void ValidateOutputPath_CloudPseudoPath_IsRejected()
    {
        var valid = DwgOutputPathPolicy.TryValidateOutputPath(
            @"Autodesk Docs:\PHAM GIA\output.dwg",
            out var error);

        Assert.False(valid);
        Assert.Contains("Autodesk Docs", error);
    }
}
