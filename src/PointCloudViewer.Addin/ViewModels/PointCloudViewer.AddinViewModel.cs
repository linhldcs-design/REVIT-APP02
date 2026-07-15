using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PointCloudViewer.Addin.Services;
using PointCloudViewer.Core.Models;
using Serilog;

namespace PointCloudViewer.Addin.ViewModels;

public sealed class PointCloudViewer_AddinViewModel : ObservableObject
{
    private readonly PointCloudSettingsStore _settingsStore;
    private readonly PointCloudRenderCoordinator _renderCoordinator;
    private readonly ILogger _logger;
    private VisualizationMode _mode;
    private double _pointSize;
    private double _brightness;
    private double _contrast;
    private double _transparency;
    private double _xRayContrast;
    private string _status = string.Empty;

    public PointCloudViewer_AddinViewModel(
        PointCloudSettingsStore settingsStore,
        PointCloudRenderCoordinator renderCoordinator,
        ILogger logger)
    {
        _settingsStore = settingsStore;
        _renderCoordinator = renderCoordinator;
        _logger = logger;

        SetModeCommand = new RelayCommand<VisualizationMode>(SetMode);
        ResetCommand = new RelayCommand(Reset);
        RequestUpdateCommand = new RelayCommand(RequestUpdate);

        _settingsStore.SettingsChanged += OnSettingsChanged;
        Load(_settingsStore.Current);
    }

    public RelayCommand<VisualizationMode> SetModeCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand RequestUpdateCommand { get; }

    public VisualizationMode Mode
    {
        get => _mode;
        private set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsRgbMode));
                OnPropertyChanged(nameof(IsNormalMode));
                OnPropertyChanged(nameof(IsXRayMode));
                OnPropertyChanged(nameof(IsColorMapMode));
                OnPropertyChanged(nameof(UsesBrightnessContrast));
                OnPropertyChanged(nameof(UsesXRayContrast));
            }
        }
    }

    public double PointSize
    {
        get => _pointSize;
        set => UpdateSetting(ref _pointSize, value, settings => settings.PointSize = value);
    }

    public double Brightness
    {
        get => _brightness;
        set => UpdateSetting(ref _brightness, value, settings => settings.Brightness = value);
    }

    public double Contrast
    {
        get => _contrast;
        set => UpdateSetting(ref _contrast, value, settings => settings.Contrast = value);
    }

    public double Transparency
    {
        get => _transparency;
        set => UpdateSetting(ref _transparency, value, settings => settings.Transparency = value);
    }

    public double XRayContrast
    {
        get => _xRayContrast;
        set => UpdateSetting(ref _xRayContrast, value, settings => settings.XRayContrast = value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsRgbMode => Mode == VisualizationMode.Rgb;
    public bool IsNormalMode => Mode == VisualizationMode.Normal;
    public bool IsXRayMode => Mode == VisualizationMode.XRay;
    public bool IsColorMapMode => Mode == VisualizationMode.ColorMap;
    public bool UsesBrightnessContrast => Mode == VisualizationMode.Rgb || Mode == VisualizationMode.ColorMap;
    public bool UsesXRayContrast => Mode == VisualizationMode.XRay;

    public double MinPointSize => RenderSettingLimits.MinPointSize;
    public double MaxPointSize => RenderSettingLimits.MaxPointSize;
    public double MinBrightness => RenderSettingLimits.MinBrightness;
    public double MaxBrightness => RenderSettingLimits.MaxBrightness;
    public double MinContrast => RenderSettingLimits.MinContrast;
    public double MaxContrast => RenderSettingLimits.MaxContrast;
    public double MinTransparency => RenderSettingLimits.MinTransparency;
    public double MaxTransparency => RenderSettingLimits.MaxTransparency;
    public double MinXRayContrast => RenderSettingLimits.MinXRayContrast;
    public double MaxXRayContrast => RenderSettingLimits.MaxXRayContrast;

    private void SetMode(VisualizationMode mode)
    {
        _settingsStore.Update(settings => settings.Mode = mode);
    }

    private void Reset()
    {
        _settingsStore.Reset();
        _logger.Information("Point cloud settings reset to defaults");
    }

    private void RequestUpdate()
    {
        _renderCoordinator.RequestUpdate();
        Status = _renderCoordinator.Status;
    }

    private void UpdateSetting(ref double field, double value, Action<PointCloudRenderSettings> apply)
    {
        if (SetProperty(ref field, value))
        {
            _settingsStore.Update(apply);
        }
    }

    private void OnSettingsChanged(object? sender, RenderSettingsChangedEventArgs args)
    {
        Load(args.Current);
        Status = _renderCoordinator.Status;
    }

    private void Load(PointCloudRenderSettings settings)
    {
        Mode = settings.Mode;
        SetProperty(ref _pointSize, settings.PointSize, nameof(PointSize));
        SetProperty(ref _brightness, settings.Brightness, nameof(Brightness));
        SetProperty(ref _contrast, settings.Contrast, nameof(Contrast));
        SetProperty(ref _transparency, settings.Transparency, nameof(Transparency));
        SetProperty(ref _xRayContrast, settings.XRayContrast, nameof(XRayContrast));
        Status = _renderCoordinator.Status;
    }
}
