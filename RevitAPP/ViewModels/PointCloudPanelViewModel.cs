using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models;
using RevitAPP.Services.PointCloud;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using RevitColor = Autodesk.Revit.DB.Color;

namespace RevitAPP.ViewModels;

/// <summary>
///     ViewModel cho dockable panel điều khiển hiển thị Point Cloud (modeless).
///     Mọi thao tác Revit API đi qua <see cref="PointCloudExternalEventHandler" />.
/// </summary>
public sealed partial class PointCloudPanelViewModel : ObservableObject
{
    private readonly ExternalEvent _externalEvent;
    private readonly PointCloudExternalEventHandler _handler;
    private readonly IPointCloudDisplayService _service;
    private readonly PointCloudRenderController _renderController;
    private readonly UIApplication _uiApplication;
    private readonly DispatcherTimer _renderStateDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public PointCloudPanelViewModel(
        IPointCloudDisplayService service,
        PointCloudExternalEventHandler handler,
        ExternalEvent externalEvent,
        PointCloudRenderController renderController,
        UIApplication uiApplication)
    {
        _service = service;
        _handler = handler;
        _externalEvent = externalEvent;
        _renderController = renderController;
        _uiApplication = uiApplication;

        ColorModes = PointCloudColorModeItem.All;
        _selectedColorMode = ColorModes[0];
        _renderStateDebounceTimer.Tick += (_, _) =>
        {
            _renderStateDebounceTimer.Stop();
            PushRenderState();
        };
        Refresh();
    }

    public IReadOnlyList<PointCloudColorModeItem> ColorModes { get; }
    public ObservableCollection<PointCloudInfo> PointClouds { get; } = new();
    public ObservableCollection<ScanItem> Scans { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPointClouds))]
    [NotifyPropertyChangedFor(nameof(CanEditSelected))]
    [NotifyCanExecuteChangedFor(nameof(ApplyColorModeCommand))]
    private PointCloudInfo? _selectedPointCloud;

    [ObservableProperty] private PointCloudColorModeItem _selectedColorMode;
    [ObservableProperty] private Color _fixedColor = System.Windows.Media.Colors.OrangeRed;
    [ObservableProperty] private string _statusMessage = "";

    // ===== Custom render (DirectContext3D) — slider như hình =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdjustSliders))]
    private bool _customRenderEnabled;

    [ObservableProperty] private double _pointSize = 3;        // 1..100 (px → quy đổi feet)
    [ObservableProperty] private double _brightness;          // -100..100
    [ObservableProperty] private double _transparency;        // 0..100
    [ObservableProperty] private bool _useOriginalColor = true;

    /// <summary>Slider chỉ tác dụng khi custom render đang bật.</summary>
    public bool CanAdjustSliders => CustomRenderEnabled;

    public bool HasPointClouds => PointClouds.Count > 0;
    public bool IsFixedColorMode => SelectedColorMode.Mode == PointCloudColorModeOption.FixedColor;

    /// <summary>Instance đang chọn có cho phép override không (chỉ .rcp/.rcs hỗ trợ).</summary>
    public bool CanEditSelected => SelectedPointCloud?.SupportsOverrides == true;

    partial void OnSelectedColorModeChanged(PointCloudColorModeItem value) => OnPropertyChanged(nameof(IsFixedColorMode));

    partial void OnSelectedPointCloudChanged(PointCloudInfo? value)
    {
        LoadScans(value);
        if (value is { SupportsOverrides: false })
        {
            StatusMessage = "Point Cloud này không hỗ trợ đổi hiển thị (chỉ .rcp/.rcs hỗ trợ).";
            return;
        }

        StatusMessage = "";
        SyncColorModeFromView(value);
    }

    /// <summary>Đọc color mode hiện tại của instance trong view để combo phản ánh đúng trạng thái.</summary>
    private void SyncColorModeFromView(PointCloudInfo? info)
    {
        if (info == null) return;
        Run(app =>
        {
            if (!TryGetGraphicalView(app, out _, out var view)) return;
            var current = _service.GetColorMode(view, info.InstanceId);
            var match = ColorModes.FirstOrDefault(m => m.Mode == current);
            if (match != null) SelectedColorMode = match;
        });
    }

    /// <summary>Nạp lại danh sách point cloud từ document hiện tại (gọi khi panel mở).</summary>
    [RelayCommand]
    public void Refresh()
    {
        var document = _uiApplication.ActiveUIDocument?.Document;
        PointClouds.Clear();
        Scans.Clear();
        if (document == null)
        {
            StatusMessage = "Chưa mở dự án.";
            return;
        }

        foreach (var info in _service.GetPointClouds(document))
            PointClouds.Add(info);

        OnPropertyChanged(nameof(HasPointClouds));
        if (!HasPointClouds)
        {
            StatusMessage = "Chưa có Point Cloud trong dự án.";
            return;
        }

        // Gán selection sau cùng — setter tự đặt StatusMessage (rỗng hoặc cảnh báo không hỗ trợ).
        SelectedPointCloud = PointClouds.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void ApplyColorMode()
    {
        var instance = SelectedPointCloud;
        if (instance is not { SupportsOverrides: true }) return;

        var mode = SelectedColorMode.Mode;
        RevitColor? fixedColor = mode == PointCloudColorModeOption.FixedColor
            ? new RevitColor(FixedColor.R, FixedColor.G, FixedColor.B)
            : null;

        Run(app =>
        {
            if (!TryGetGraphicalView(app, out var document, out var view))
            {
                SetStatus("View hiện tại không hỗ trợ override point cloud (mở mặt bằng/3D).");
                return;
            }

            var ok = _service.SetColorMode(document, view, instance.InstanceId, mode, fixedColor);
            SetStatus(ok ? "Đã áp chế độ màu." : "Áp chế độ màu thất bại.");
        });
    }

    [RelayCommand]
    private void ToggleScan(ScanItem? scan)
    {
        var instance = SelectedPointCloud;
        if (instance == null || scan == null) return;

        var requested = scan.IsVisible;
        Run(app =>
        {
            if (!TryGetGraphicalView(app, out var document, out var view))
            {
                scan.IsVisible = !requested; // revert: UI không được nói dối model
                SetStatus("View hiện tại không hỗ trợ override point cloud.");
                return;
            }

            var ok = _service.SetScanVisibility(document, view, instance.InstanceId, scan.Name, requested);
            if (!ok) scan.IsVisible = !requested;
            SetStatus(ok ? $"Đã cập nhật scan '{scan.Name}'." : "Cập nhật scan thất bại.");
        });
    }

    /// <summary>Lấy document + view đồ họa hợp lệ; false nếu view hiện tại không nhận override (schedule/sheet…).</summary>
    private static bool TryGetGraphicalView(UIApplication app, out Document document, out View view)
    {
        document = null!;
        view = null!;
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc?.ActiveGraphicalView == null) return false;
        document = uiDoc.Document;
        view = uiDoc.ActiveGraphicalView;
        return true;
    }

    // ===== Custom render commands + slider wiring =====

    /// <summary>Bật/tắt custom render (DirectContext3D). Khi bật → đọc điểm + ẩn cloud gốc.</summary>
    [RelayCommand]
    private void ToggleCustomRender()
    {
        var instance = SelectedPointCloud;
        if (instance == null) return;
        var enabling = !CustomRenderEnabled;

        Run(app =>
        {
            if (!TryGetGraphicalView(app, out var document, out var view))
            {
                SetStatus("Cần mở view 3D để dùng custom render.");
                return;
            }

            if (view.ViewType != ViewType.ThreeD)
            {
                SetStatus("Custom render chỉ hoạt động trong view 3D.");
                return;
            }

            if (enabling)
            {
                // Học từ Qbitec: nạp lại settings đã lưu cho view này (nếu có) trước khi render.
                var saved = _renderController.LoadSavedState(view);
                if (saved != null) ApplyStateToSliders(saved);

                var ok = _renderController.Enable(document, view, instance.InstanceId, BuildState(), Density);
                CustomRenderEnabled = ok;
                SetStatus(ok ? "Đã bật custom render." : "Bật custom render thất bại (không đọc được điểm).");
            }
            else
            {
                _renderController.Disable(view);
                CustomRenderEnabled = false;
                SetStatus("Đã tắt custom render.");
            }

            app.ActiveUIDocument?.RefreshActiveView();
        });
    }

    /// <summary>Đặt slider về mặc định + cập nhật render.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        var def = PointCloudRenderState.Default;
        PointSize = 3;
        Brightness = def.Brightness;
        Transparency = def.Transparency;
        UseOriginalColor = def.UseOriginalColor;
        _renderStateDebounceTimer.Stop();
        PushRenderState();
    }

    private bool _suppressPush; // chặn save khi đang nạp state từ storage

    partial void OnPointSizeChanged(double value) => QueueRenderStatePush();
    partial void OnBrightnessChanged(double value) => QueueRenderStatePush();
    partial void OnTransparencyChanged(double value) => QueueRenderStatePush();
    partial void OnUseOriginalColorChanged(bool value) => PushRenderState();

    private void QueueRenderStatePush()
    {
        if (_suppressPush || !CustomRenderEnabled) return;
        _renderStateDebounceTimer.Stop();
        _renderStateDebounceTimer.Start();
    }

    /// <summary>Áp state đã lưu lên slider mà không trigger save ngược lại.</summary>
    private void ApplyStateToSliders(PointCloudRenderState state)
    {
        _renderStateDebounceTimer.Stop();
        _suppressPush = true;
        try
        {
            PointSize = Math.Round(state.PointSizeFeet / 0.0328);
            Brightness = state.Brightness;
            Transparency = state.Transparency;
            UseOriginalColor = state.UseOriginalColor;
        }
        finally
        {
            _suppressPush = false;
        }
    }

    /// <summary>Đẩy state slider hiện tại vào render server + refresh view (nếu đang bật).</summary>
    private void PushRenderState()
    {
        if (_suppressPush || !CustomRenderEnabled) return;
        Run(app =>
        {
            if (!TryGetGraphicalView(app, out _, out var view)) return;
            _renderController.UpdateState(view, BuildState());
            app.ActiveUIDocument?.RefreshActiveView();
        });
    }

    /// <summary>Quy đổi slider → PointCloudRenderState. PointSize px → feet (~1px ≈ 10mm).</summary>
    private PointCloudRenderState BuildState() => new()
    {
        PointSizeFeet = PointSize * 0.0328,            // ~10mm/px (1ft = 304.8mm)
        Brightness = (int)Brightness,
        Transparency = (int)Transparency,
        UseOriginalColor = UseOriginalColor,
        FixedColor = PackRgba(FixedColor)
    };

    private const double Density = 0.05; // averageDistance feet — nhỏ = dày (gần file gốc)

    private static uint PackRgba(Color c) => (uint)(0xFF << 24 | c.B << 16 | c.G << 8 | c.R);

    private void LoadScans(PointCloudInfo? info)
    {
        Scans.Clear();
        if (info == null) return;
        foreach (var name in info.Scans)
            Scans.Add(new ScanItem(name));
    }

    /// <summary>Đẩy hành động vào ExternalEvent để chạy trong Revit API context.</summary>
    private void Run(Action<UIApplication> action)
    {
        _handler.PendingAction = action;
        _externalEvent.Raise();
    }

    private void SetStatus(string message) => StatusMessage = message;

    /// <summary>Một scan trong point cloud kèm trạng thái hiển thị (binding cho list).</summary>
    public sealed partial class ScanItem : ObservableObject
    {
        public ScanItem(string name) => Name = name;

        public string Name { get; }

        [ObservableProperty] private bool _isVisible = true;
    }
}
