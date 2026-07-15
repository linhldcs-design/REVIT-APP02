using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class UpdatePackageVerifierTests
{
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
