using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Licensing;
using Serilog;

namespace RevitAPP.ViewModels;

/// <summary>
///     ViewModel cho dialog License: hien trang thai, dang nhap Google, dang xuat.
///     Dung chung <see cref="LicenseService.Instance"/> voi 4 MCP tool ve thep.
/// </summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly LicenseService _service = LicenseService.Instance;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    [ObservableProperty] private string _statusText = "Dang kiem tra...";
    [ObservableProperty] private string _emailText = string.Empty;
    [ObservableProperty] private bool _isSignedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    public LicenseViewModel()
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Dang mo trinh duyet de dang nhap Google...";
        try
        {
            var state = await _service.SignInAsync();
            Apply(state);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "License sign-in failed");
            StatusText = "Loi dang nhap: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SignOut()
    {
        _service.SignOut();
        IsSignedIn = false;
        EmailText = string.Empty;
        StatusText = "Da dang xuat. Vui long dang nhap de su dung cong cu ve thep.";
    }

    private async Task RefreshAsync()
    {
        try
        {
            var state = await _service.GetStateAsync();
            Apply(state);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "License state check failed");
            StatusText = "Khong kiem tra duoc license: " + ex.Message;
        }
    }

    private void Apply(LicenseState state)
    {
        void Set()
        {
            IsSignedIn = state.Status == LicenseStatus.Valid;
            EmailText = state.Email ?? string.Empty;
            StatusText = state.Status switch
            {
                LicenseStatus.Valid => $"Da kich hoat • {state.Email} • Het han {state.Expiry}",
                LicenseStatus.Expired => "License het han: " + state.Reason,
                LicenseStatus.Denied => "Chua duoc cap quyen: " + state.Reason,
                _ => "Chua dang nhap. Bam \"Dang nhap Google\" de kich hoat."
            };
        }

        if (_dispatcher.CheckAccess()) Set();
        else _dispatcher.Invoke(Set);
    }
}
