using System.Security.Cryptography;

namespace RevitAPP.Core.Services;

public static class UpdatePackageVerifier
{
    public static bool IsNewer(string candidate, string current)
    {
        return TryVersion(candidate, out var next) && TryVersion(current, out var installed) && next > installed;
    }

    public static bool VerifySha256(string path, string expected)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(expected)) return false;
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryVersion(string value, out Version version)
    {
        var normalized = (value ?? string.Empty).Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(normalized, out version!);
    }
}
