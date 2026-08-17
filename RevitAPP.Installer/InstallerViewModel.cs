using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Reflection;
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
    public event EventHandler? RestartInstallerRequested;

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
        if (IsBusy) return;
        IsBusy = true;
        try { ApplyLicense(await LicenseService.Instance.SignInAsync()); }
        catch (Exception ex) { LicenseStatus = "Đăng nhập lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        var year = SelectedRevitYear;
        if (IsBusy || string.IsNullOrWhiteSpace(year)) return;
        IsBusy = true;
        try
        {
            StatusText = "Đang kiểm tra GitHub Releases...";
            var json = await DownloadStringWithRetryAsync(ManifestUrl, "manifest cập nhật");
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new InvalidDataException("Manifest không hợp lệ.");
            if (await TryStartInstallerUpdateAsync(manifest)) return;
            var installed = InstalledVersion(year);
            if (!manifest.Packages.ContainsKey(year))
            {
                UpdateAvailable = false;
                StatusText = $"GitHub có bản {manifest.Version}, nhưng chưa có gói cho Revit {year}.";
                return;
            }
            UpdateAvailable = installed != null && UpdatePackageVerifier.IsNewer(manifest.Version, installed);
            UpdateNotice = UpdateAvailable
                ? $"CÓ PHIÊN BẢN MỚI: {installed}  →  {manifest.Version} (Revit {year})"
                : string.Empty;
            StatusText = installed == null
                ? $"Có thể cài RevitAPP {manifest.Version} cho Revit {year}."
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
        var year = SelectedRevitYear;
        if (IsBusy || string.IsNullOrWhiteSpace(year)) return;
        IsBusy = true;
        string? temp = null;
        try
        {
            StatusText = "Đang xác nhận license...";
            var state = await LicenseService.Instance.GetStateAsync();
            ApplyLicense(state);
            if (state.Status != RevitAPP.Licensing.LicenseStatus.Valid)
                throw new InvalidOperationException("Hãy đăng nhập license hợp lệ trước khi cài.");

            var revitProcesses = Process.GetProcessesByName("Revit");
            if (revitProcesses.Length > 0)
                throw new InvalidOperationException(
                    $"Hãy đóng tất cả Revit trước khi cài/cập nhật (đang chạy PID {string.Join(", ", revitProcesses.Select(process => process.Id))}).");

            StatusText = "Đang kiểm tra phiên bản mới...";
            var json = await DownloadStringWithRetryAsync(ManifestUrl, "manifest cập nhật");
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new InvalidDataException("Manifest không hợp lệ.");
            if (await TryStartInstallerUpdateAsync(manifest)) return;
            if (!manifest.Packages.TryGetValue(year, out var package))
                throw new InvalidOperationException($"Không có gói cho Revit {year}.");
            temp = Path.Combine(Path.GetTempPath(), "RevitAPP-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var zip = Path.Combine(temp, "package.zip");
            StatusText = $"Đang tải RevitAPP {manifest.Version}...";
            await File.WriteAllBytesAsync(zip,
                await DownloadBytesWithRetryAsync(package.Url, $"gói RevitAPP {manifest.Version}"));
            if (!UpdatePackageVerifier.VerifySha256(zip, package.Sha256)) throw new InvalidDataException("SHA-256 không khớp.");
            var extract = Path.Combine(temp, "extract");
            ZipFile.ExtractToDirectory(zip, extract);
            var payload = Directory.Exists(Path.Combine(extract, "RevitAPP")) ? Path.Combine(extract, "RevitAPP") : extract;
            InstallPayload(payload, year, manifest.Version);
            UpdateAvailable = false;
            UpdateNotice = string.Empty;
            StatusText = $"Đã cài RevitAPP {manifest.Version} cho Revit {year}.";
        }
        catch (Exception ex) { StatusText = "Cài đặt thất bại: " + ex.Message; }
        finally
        {
            if (temp is not null && Directory.Exists(temp))
            {
                try { Directory.Delete(temp, true); }
                catch { /* Temp cleanup must not hide the actual update result. */ }
            }
            IsBusy = false;
        }
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
        var staging = Path.Combine(root, ".RevitAPP.install-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(root, ".RevitAPP.backup-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "RevitAPP.addin");
        var manifestStaging = Path.Combine(root, ".RevitAPP.addin.install-" + Guid.NewGuid().ToString("N"));
        var manifestBackup = Path.Combine(root, ".RevitAPP.addin.backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var source in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(staging, Path.GetRelativePath(payload, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            File.WriteAllText(Path.Combine(staging, "installed-version.txt"), version);
            File.WriteAllText(manifestStaging, AddinManifest());

            var targetBackedUp = false;
            var manifestBackedUp = false;
            var newTargetInstalled = false;
            var newManifestInstalled = false;
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    targetBackedUp = true;
                }
                if (File.Exists(manifest))
                {
                    File.Move(manifest, manifestBackup);
                    manifestBackedUp = true;
                }
                Directory.Move(staging, target);
                newTargetInstalled = true;
                File.Move(manifestStaging, manifest);
                newManifestInstalled = true;
            }
            catch
            {
                if (newTargetInstalled && Directory.Exists(target)) Directory.Delete(target, true);
                if (targetBackedUp && Directory.Exists(backup)) Directory.Move(backup, target);
                if (newManifestInstalled && File.Exists(manifest)) File.Delete(manifest);
                if (manifestBackedUp && File.Exists(manifestBackup)) File.Move(manifestBackup, manifest);
                throw;
            }

            if (Directory.Exists(backup))
            {
                try { Directory.Delete(backup, true); }
                catch { /* A complete new target is already live; stale backup cleanup is non-fatal. */ }
            }
            if (File.Exists(manifestBackup))
            {
                try { File.Delete(manifestBackup); }
                catch { /* The new manifest is already live; stale backup cleanup is non-fatal. */ }
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch { /* Staging cleanup must not turn a successful installation into a failure. */ }
            }
            if (File.Exists(manifestStaging))
            {
                try { File.Delete(manifestStaging); }
                catch { /* Manifest staging cleanup is best effort. */ }
            }
        }
    }

    private async Task<bool> TryStartInstallerUpdateAsync(UpdateManifest manifest)
    {
        if (manifest.Installer is null) return false;
        var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        if (!UpdatePackageVerifier.IsNewer(manifest.Version, current)) return false;

        var updateDirectory = Path.Combine(Path.GetTempPath(), "RevitAPP-Installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        try
        {
            var candidate = Path.Combine(updateDirectory, "RevitAPP.Installer.exe");
            StatusText = $"Đang tải Installer {manifest.Version}...";
            await File.WriteAllBytesAsync(candidate,
                await DownloadBytesWithRetryAsync(manifest.Installer.Url, $"Installer {manifest.Version}"));
            if (!UpdatePackageVerifier.VerifySha256(candidate, manifest.Installer.Sha256))
                throw new InvalidDataException("SHA-256 của Installer không khớp.");

            InstallerSelfUpdater.LaunchHelper(candidate, SelfInstaller.InstalledPath, Environment.ProcessId);
            StatusText = $"Installer sẽ tự khởi động lại ở phiên bản {manifest.Version}...";
            RestartInstallerRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            try { Directory.Delete(updateDirectory, true); } catch { }
            throw;
        }
    }

    private async Task<string> DownloadStringWithRetryAsync(string url, string label) =>
        Encoding.UTF8.GetString(await DownloadBytesWithRetryAsync(url, label));

    private async Task<byte[]> DownloadBytesWithRetryAsync(string url, string label)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { return await Http.GetByteArrayAsync(url); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt == 3) break;
                StatusText = $"Tải {label} chưa thành công (lần {attempt}/3), đang thử lại...";
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
        }
        throw new HttpRequestException($"Không tải được {label} sau 3 lần thử.", last);
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
