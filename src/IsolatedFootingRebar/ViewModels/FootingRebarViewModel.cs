using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;
using Serilog;

namespace IsolatedFootingRebar.ViewModels;

/// <summary>
///     ViewModel cho dialog modeless "Isolated Footing" — bám sát ảnh: 6 tab (Common + Bottom/Top/Mid/
///     Vertical/Horizontal) + preset bar. Ghi document qua ExternalEvent (KHÔNG gọi Transaction trực tiếp).
/// </summary>
public sealed partial class FootingRebarViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly ExternalEvent _externalEvent;
    private readonly FootingRebarHandler _handler;
    private readonly Dispatcher _dispatcher;

    public event Action? RequestClose;

    public FootingRebarViewModel(ILogger logger)
    {
        _logger = logger;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _handler = new FootingRebarHandler
        {
            OnCompleted = OnRebarCompleted,
            OnDirectionPicked = OnDirectionPicked
        };
        _externalEvent = ExternalEvent.Create(_handler);
        RefreshPresets();
    }

    // --- Common Settings (cover theo ảnh) ---
    [ObservableProperty] private double _bottomCoverMm = 185;
    [ObservableProperty] private double _topCoverMm = 35;
    [ObservableProperty] private double _sideCoverMm = 35;

    // --- Tab enable (checkbox trên tab) ---
    [ObservableProperty] private bool _bottomEnabled = true;
    [ObservableProperty] private bool _topEnabled = true;
    [ObservableProperty] private bool _midEnabled;
    [ObservableProperty] private bool _verticalEnabled;
    [ObservableProperty] private bool _horizontalEnabled = true;

    // --- Lưới đáy / trên / giữa: mỗi tab có X + Y ---
    public DirectionViewModel BottomX { get; } = new();
    public DirectionViewModel BottomY { get; } = new();
    public DirectionViewModel TopX { get; } = new() { HookLengthMm = 400 };
    public DirectionViewModel TopY { get; } = new() { HookLengthMm = 400 };
    public DirectionViewModel MidX { get; } = new() { SpacingMm = 200, HookLengthMm = 200 };
    public DirectionViewModel MidY { get; } = new() { SpacingMm = 200, HookLengthMm = 200 };
    [ObservableProperty] private int _midLayers = 2;

    // --- Vertical Reinforced (thép đứng cổ móng) ---
    [ObservableProperty] private int _verticalDiameterMm = 6;
    [ObservableProperty] private bool _verticalUseSpacing = true;
    [ObservableProperty] private double _verticalSpacingXMm = 200;
    [ObservableProperty] private double _verticalSpacingYMm = 200;
    [ObservableProperty] private int _verticalCountX = 5;
    [ObservableProperty] private int _verticalCountY = 5;
    [ObservableProperty] private double _verticalHookLengthMm = 200;
    [ObservableProperty] private double _verticalWidthMm = 300;

    // --- Horizontal Reinforced (đai ngang cổ móng) ---
    [ObservableProperty] private int _horizontalDiameterMm = 6;
    [ObservableProperty] private bool _horizontalClosed = true;
    [ObservableProperty] private bool _horizontalHookEnabled = true;
    [ObservableProperty] private double _horizontalHookLengthMm = 100;
    [ObservableProperty] private int _horizontalLayers = 1;

    // --- Preset ---
    public ObservableCollection<string> PresetNames { get; } = [];
    [ObservableProperty] private string? _selectedPreset;
    [ObservableProperty] private string _presetNameInput = string.Empty;

    [ObservableProperty] private string _statusMessage =
        "Cấu hình thép móng rồi bấm Create. Đường kính bằng mm.";

    public IReadOnlyList<int> DiameterOptions { get; } = RebarDiameter.Standard.Select(d => d.Millimeters).ToList();

    private Element? _foundation;

    public void SetFoundation(Element foundation)
    {
        _foundation = foundation;
        _handler.Foundation = foundation;
        StatusMessage = $"Móng \"{foundation.Name}\" — cấu hình rồi bấm Create.";
    }

    [RelayCommand]
    private void Create()
    {
        if (_foundation is null)
        {
            StatusMessage = "Chưa chọn móng.";
            return;
        }

        _handler.Request = FootingRequest.CreateRebar;
        _handler.Model = BuildModel();
        StatusMessage = "Đang tạo thép…";
        _externalEvent.Raise();
    }

    [RelayCommand]
    private void PickDirection()
    {
        _handler.Request = FootingRequest.PickDirectionLine;
        StatusMessage = "Chọn line hướng thép chính trong Revit…";
        _externalEvent.Raise();
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(PresetNameInput) ? SelectedPreset : PresetNameInput;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Nhập tên cấu hình để lưu.";
            return;
        }

        FootingConfigStore.Save(name, BuildModel());
        RefreshPresets();
        SelectedPreset = name;
        StatusMessage = $"Đã lưu cấu hình \"{name}\".";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset)) return;
        var name = SelectedPreset;
        FootingConfigStore.Delete(name);
        RefreshPresets();
        StatusMessage = $"Đã xoá cấu hình \"{name}\".";
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    partial void OnSelectedPresetChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var model = FootingConfigStore.Load(value);
        if (model is not null) LoadModel(model);
    }

    private void RefreshPresets()
    {
        PresetNames.Clear();
        foreach (var name in FootingConfigStore.Names())
            PresetNames.Add(name);
    }

    private FootingRebarModel BuildModel() => new()
    {
        Cover = new CoverSettings { BottomMm = BottomCoverMm, TopMm = TopCoverMm, SideMm = SideCoverMm },
        BottomEnabled = BottomEnabled,
        BottomX = BottomX.ToConfig(),
        BottomY = BottomY.ToConfig(),
        TopEnabled = TopEnabled,
        TopX = TopX.ToConfig(),
        TopY = TopY.ToConfig(),
        MidEnabled = MidEnabled,
        MidLayers = MidLayers,
        MidX = MidX.ToConfig(),
        MidY = MidY.ToConfig(),
        VerticalEnabled = VerticalEnabled,
        Vertical = new VerticalBarConfig
        {
            Diameter = new RebarDiameter(VerticalDiameterMm),
            UseSpacing = VerticalUseSpacing,
            SpacingXMm = VerticalSpacingXMm,
            SpacingYMm = VerticalSpacingYMm,
            CountX = VerticalCountX,
            CountY = VerticalCountY,
            HookLengthMm = VerticalHookLengthMm,
            WidthMm = VerticalWidthMm
        },
        HorizontalEnabled = HorizontalEnabled,
        Horizontal = new HorizontalStirrupConfig
        {
            DiameterX = new RebarDiameter(HorizontalDiameterMm),
            DiameterY = new RebarDiameter(HorizontalDiameterMm),
            Closed = HorizontalClosed,
            HookEnabled = HorizontalHookEnabled,
            HookLengthMm = HorizontalHookLengthMm,
            Layers = HorizontalLayers
        },
        DirXOverride = _dirXOverride
    };

    private Point3? _dirXOverride;

    private void LoadModel(FootingRebarModel m)
    {
        BottomCoverMm = m.Cover.BottomMm; TopCoverMm = m.Cover.TopMm; SideCoverMm = m.Cover.SideMm;
        BottomEnabled = m.BottomEnabled; BottomX.Load(m.BottomX); BottomY.Load(m.BottomY);
        TopEnabled = m.TopEnabled; TopX.Load(m.TopX); TopY.Load(m.TopY);
        MidEnabled = m.MidEnabled; MidLayers = m.MidLayers; MidX.Load(m.MidX); MidY.Load(m.MidY);
        VerticalEnabled = m.VerticalEnabled; VerticalDiameterMm = m.Vertical.Diameter.Millimeters;
        VerticalUseSpacing = m.Vertical.UseSpacing; VerticalSpacingXMm = m.Vertical.SpacingXMm;
        VerticalSpacingYMm = m.Vertical.SpacingYMm; VerticalCountX = m.Vertical.CountX;
        VerticalCountY = m.Vertical.CountY; VerticalHookLengthMm = m.Vertical.HookLengthMm;
        VerticalWidthMm = m.Vertical.WidthMm;
        HorizontalEnabled = m.HorizontalEnabled; HorizontalDiameterMm = m.Horizontal.DiameterX.Millimeters;
        HorizontalClosed = m.Horizontal.Closed; HorizontalHookEnabled = m.Horizontal.HookEnabled;
        HorizontalHookLengthMm = m.Horizontal.HookLengthMm;
        HorizontalLayers = m.Horizontal.Layers;
        _dirXOverride = m.DirXOverride;
    }

    // Handler.Execute chạy trên main thread của Revit (cùng UI thread với dialog modeless) → cập nhật
    // binding trực tiếp an toàn, không cần marshal Dispatcher.
    private void OnRebarCompleted(RebarCreationResult result)
        => RunOnUi(() => ApplyRebarCompleted(result));

    private void ApplyRebarCompleted(RebarCreationResult result)
    {
        var msg = result.Succeeded
            ? $"Đã tạo: {result.MeshCount} lưới, {result.VerticalCount} thép đứng, {result.StirrupCount} đai."
            : "Không tạo được thép nào.";
        if (result.Warnings.Count > 0)
            msg += $" ({result.Warnings.Count} warning: {result.Warnings[0]})";
        StatusMessage = msg;
        if (result.Warnings.Count > 0)
            _logger.Warning("Footing rebar warnings: {Warnings}", string.Join(" | ", result.Warnings));
    }

    private void OnDirectionPicked(Point3? dir)
        => RunOnUi(() => ApplyDirectionPicked(dir));

    private void ApplyDirectionPicked(Point3? dir)
    {
        _dirXOverride = dir;
        StatusMessage = dir is null
            ? "Không nhận được hướng — dùng hướng mặc định của móng."
            : "Đã đặt hướng thép chính theo line đã chọn.";
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
