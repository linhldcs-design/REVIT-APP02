namespace RevitAPP.Core.Models.Updates;

public sealed record UpdateManifest(
    string Version,
    string? Notes,
    IReadOnlyDictionary<string, UpdatePackage> Packages);

public sealed record UpdatePackage(string Url, string Sha256);
