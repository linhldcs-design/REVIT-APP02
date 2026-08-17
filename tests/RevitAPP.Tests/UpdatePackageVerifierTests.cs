using RevitAPP.Core.Services;
using RevitAPP.Core.Models.Updates;
using System.Text.Json;
using Xunit;

namespace RevitAPP.Tests;

public sealed class UpdatePackageVerifierTests
{
    [Fact]
    public void Manifest_WithInstallerPackage_RemainsBackwardCompatible()
    {
        const string json = """
            {"version":"1.12.1","notes":"patch","packages":{"2025":{"url":"https://example/r25.zip","sha256":"AA"}},"installer":{"url":"https://example/installer.exe","sha256":"BB"}}
            """;

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Equal("1.12.1", manifest.Version);
        Assert.Equal("https://example/installer.exe", manifest.Installer?.Url);
        Assert.Equal("BB", manifest.Installer?.Sha256);
        Assert.True(manifest.Packages.ContainsKey("2025"));
    }

    [Fact]
    public void Manifest_WithoutInstallerPackage_StillDeserializes()
    {
        const string json = """
            {"version":"1.12.0","notes":null,"packages":{"2025":{"url":"https://example/r25.zip","sha256":"AA"}}}
            """;

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Null(manifest.Installer);
    }
    [Theory]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("bad", "1.0.0", false)]
    public void IsNewer_compares_release_versions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdatePackageVerifier.IsNewer(candidate, current));
    }

    [Fact]
    public void VerifySha256_rejects_tampered_package()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "RevitAPP");
            Assert.True(UpdatePackageVerifier.VerifySha256(path,
                "DA8571A1E83E3BB69ECC84DDB9C1D62DB18B6F1846B0E6C9DB173C40688A0367"));
            File.AppendAllText(path, "tampered");
            Assert.False(UpdatePackageVerifier.VerifySha256(path,
                "DA8571A1E83E3BB69ECC84DDB9C1D62DB18B6F1846B0E6C9DB173C40688A0367"));
        }
        finally { File.Delete(path); }
    }
}
