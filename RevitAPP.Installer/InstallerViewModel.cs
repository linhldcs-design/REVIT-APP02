using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models.Updates;
using RevitAPP.Core.Services;
using RevitAPP.Licensing;

namespace RevitAPP.Installer;

public sealed partial class InstallerViewModel : ObservableObject
{
    private const string ManifestUrl = "https://github.com/linhldcs-design/REVIT-APP02/releases/latest/download/latest.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public ObservableCollection<string> RevitYears { get; } = new();
    [ObservableProperty] private string? _selectedRevitYear;
    [ObservableProperty] private string _licenseStatus = "Chưa kiểm tra bản quyền";
    [ObservableProperty] private string _statusText = "Sẵn sàng.";
    [ObservableProperty] private string _installedRevitText = string.Empty;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateNotice = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotBusy))] private bool _isBusy;
    public bool IsNotBusy => !IsBusy;

    public InstallerViewModel()
    {
        foreach (var year in new[] { "2022", "2023", "2024", "2025", "2026", "2027" }) RevitYears.Add(year);
        SelectedRevitYear = RevitYears.FirstOrDefault(year => IsRevitInstalled(year)) ?? "2025";
        _ = RefreshLicenseAsync();
        RefreshInstalledStatus();
        _ = AutoCheckUpdatesAsync();
    }

    partial void OnSelectedRevitYearChanged(string? value) => RefreshInstalledStatus();

    [RelayCommand]
    private async Task SignInAsync()
    {
        IsBusy = true;
        try { ApplyLicense(await LicenseService.Instance.SignInAsync()); }
        catch (Exception ex) { LicenseStatus = "Đăng nhập lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SelectedRevitYear)) return;
        IsBusy = true;
        try
        {
            StatusText = "Đang kiểm tra GitHub Releases...";
            var json = await Http.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new InvalidDataException("Manifest không hợp lệ.");
            var installed = InstalledVersion(SelectedRevitYear);
            if (!manifest.Packages.ContainsKey(SelectedRevitYear))
            {
                UpdateAvailable = false;
                StatusText = $"GitHub có bản {manifest.Version}, nhưng chưa có gói cho Revit {SelectedRevitYear}.";
                return;
            }
            UpdateAvailable = installed != null && UpdatePackageVerifier.IsNewer(manifest.Version, installed);
            UpdateNotice = UpdateAvailable
                ? $"CÓ PHIÊN BẢN MỚI: {installed}  →  {manifest.Version} (Revit {SelectedRevitYear})"
                : string.Empty;
            StatusText = installed == null
                ? $"Có thể cài RevitAPP {manifest.Version} cho Revit {SelectedRevitYear}."
                : UpdateAvailable
                    ? $"Có bản mới {manifest.Version}; máy đang dùng {installed}."
                    : $"RevitAPP {installed} đang là bản mới nhất.";
        }
        catch (Exception ex) { StatusText = "Không kiểm tra được cập nhật: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task AutoCheckUpdatesAsync()
    {
        await Task.Delay(1000);
        await CheckUpdatesAsync();
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SelectedRevitYear)) return;
        var state = await LicenseService.Instance.GetStateAsync();
        ApplyLicense(state);
        if (state.Status != RevitAPP.Licensing.LicenseStatus.Valid) { StatusText = "Hãy đăng nhập license hợp lệ trước khi cài."; return; }
        if (Process.GetProcessesByName("Revit").Length > 0) { StatusText = "Hãy đóng tất cả Revit trước khi cài/cập nhật."; return; }

        IsBusy = true;
        try
        {
            StatusText = "Đang kiểm tra phiên bản mới...";
            var json = await Http.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new InvalidDataException("Manifest không hợp lệ.");
            if (!manifest.Packages.TryGetValue(SelectedRevitYear, out var package))
                throw new InvalidOperationException($"Không có gói cho Revit {SelectedRevitYear}.");
            var temp = Path.Combine(Path.GetTempPath(), "RevitAPP-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var zip = Path.Combine(temp, "package.zip");
            StatusText = $"Đang tải RevitAPP {manifest.Version}...";
            await File.WriteAllBytesAsync(zip, await Http.GetByteArrayAsync(package.Url));
            if (!UpdatePackageVerifier.VerifySha256(zip, package.Sha256)) throw new InvalidDataException("SHA-256 không khớp.");
            var extract = Path.Combine(temp, "extract");
            ZipFile.ExtractToDirectory(zip, extract);
            var payload = Directory.Exists(Path.Combine(extract, "RevitAPP")) ? Path.Combine(extract, "RevitAPP") : extract;
            InstallPayload(payload, SelectedRevitYear, manifest.Version);
            Directory.Delete(temp, true);
            StatusText = $"Đã cài RevitAPP {manifest.Version} cho Revit {SelectedRevitYear}.";
        }
        catch (Exception ex) { StatusText = "Cài đặt thất bại: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Uninstall()
    {
        if (Process.GetProcessesByName("Revit").Length > 0) { StatusText = "Hãy đóng tất cả Revit trước khi gỡ."; return; }
        var year = SelectedRevitYear ?? "2025";
        var root = AddinsRoot(year);
        var folder = Path.Combine(root, "RevitAPP");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        var manifest = Path.Combine(root, "RevitAPP.addin");
        if (File.Exists(manifest)) File.Delete(manifest);
        StatusText = $"Đã gỡ RevitAPP khỏi Revit {year}. License và preset vẫn được giữ lại.";
    }

    private async Task RefreshLicenseAsync()
    {
        try { ApplyLicense(await LicenseService.Instance.GetStateAsync()); }
        catch (Exception ex) { LicenseStatus = "Không kiểm tra được: " + ex.Message; }
    }

    private void ApplyLicense(LicenseState state) => LicenseStatus = state.Status == RevitAPP.Licensing.LicenseStatus.Valid
        ? $"Đã kích hoạt: {state.Email} · Hết hạn {state.Expiry}"
        : "Chưa có license hợp lệ: " + (state.Reason ?? state.Status.ToString());

    private void RefreshInstalledStatus()
    {
        var year = SelectedRevitYear ?? "2025";
        InstalledRevitText = IsRevitInstalled(year)
            ? $"Revit {year} đã cài trên máy"
            : $"Không phát hiện Revit {year} trên máy";
        var installed = InstalledVersion(year);
        StatusText = installed == null ? "Chưa cài RevitAPP." : "Đã cài phiên bản " + installed;
    }

    private static string? InstalledVersion(string year)
    {
        var marker = Path.Combine(AddinsRoot(year), "RevitAPP", "installed-version.txt");
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    private static bool IsRevitInstalled(string year) =>
        Directory.Exists($@"C:\Program Files\Autodesk\Revit {year}");

    private static void InstallPayload(string payload, string year, string version)
    {
        var root = AddinsRoot(year);
        var target = Path.Combine(root, "RevitAPP");
        Directory.CreateDirectory(target);
        foreach (var source in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payload, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
        File.WriteAllText(Path.Combine(target, "installed-version.txt"), version);
        File.WriteAllText(Path.Combine(root, "RevitAPP.addin"), AddinManifest());
    }

    private static string AddinsRoot(string year) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "Revit", "Addins", year);

    private static string AddinManifest() => """
        <?xml version="1.0" encoding="utf-8"?>
        <RevitAddIns><AddIn Type="Application"><Name>RevitAPP</Name><Assembly>RevitAPP\RevitAPP.dll</Assembly>
        <AddInId>F28E7DC5-77FF-43A7-A49C-60807974727D</AddInId><FullClassName>RevitAPP.Application</FullClassName>
        <VendorId>Development</VendorId><VendorDescription>RevitAPP</VendorDescription></AddIn></RevitAddIns>
        """;
}
