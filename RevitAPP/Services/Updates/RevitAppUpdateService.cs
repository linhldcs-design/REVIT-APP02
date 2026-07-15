using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using RevitAPP.Core.Models.Updates;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.Updates;

public sealed class RevitAppUpdateService
{
    public const string ManifestUrl = "https://github.com/linhldcs-design/REVIT-APP02/releases/latest/download/latest.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<UpdateCheckResult> CheckAndStageAsync(string revitYear, CancellationToken token = default)
    {
        using var manifestResponse = await Client.GetAsync(ManifestUrl, token);
        manifestResponse.EnsureSuccessStatusCode();
        var json = await manifestResponse.Content.ReadAsStringAsync();
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions())
                       ?? throw new InvalidDataException("Manifest cập nhật không hợp lệ.");
        var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        if (!UpdatePackageVerifier.IsNewer(manifest.Version, current))
            return new UpdateCheckResult(false, false, current, manifest.Version, manifest.Notes, null);

        if (!manifest.Packages.TryGetValue(revitYear, out var package))
            throw new InvalidOperationException($"Bản {manifest.Version} không có gói Revit {revitYear}.");

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitAPP", "Updates", manifest.Version, revitYear);
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "package.zip");
        using (var response = await Client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync();
            using var output = File.Create(zipPath);
            await input.CopyToAsync(output);
        }

        if (!UpdatePackageVerifier.VerifySha256(zipPath, package.Sha256))
        {
            File.Delete(zipPath);
            throw new InvalidDataException("SHA-256 của gói cập nhật không khớp.");
        }

        var payload = Path.Combine(root, "payload");
        if (Directory.Exists(payload)) Directory.Delete(payload, true);
        ZipFile.ExtractToDirectory(zipPath, payload);
        var pending = new PendingUpdate(manifest.Version, payload, InstallDirectory(), revitYear);
        var pendingPath = Path.Combine(root, "pending.json");
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(pending, JsonOptions()));
        return new UpdateCheckResult(true, true, current, manifest.Version, manifest.Notes, pendingPath);
    }

    public static bool LaunchInstaller(string pendingPath)
    {
        var updater = Path.Combine(InstallDirectory(), "RevitAPP.Updater.exe");
        if (!File.Exists(updater) || !File.Exists(pendingPath)) return false;
        Process.Start(new ProcessStartInfo(updater, $"--pending \"{pendingPath}\" --wait-pid {Process.GetCurrentProcess().Id}")
        {
            UseShellExecute = true
        });
        return true;
    }

    private static string InstallDirectory() => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
}

public sealed record UpdateCheckResult(bool UpdateAvailable, bool Staged, string CurrentVersion,
    string LatestVersion, string? Notes, string? PendingPath);

public sealed record PendingUpdate(string Version, string PayloadDirectory, string InstallDirectory, string RevitYear);
