using System.Collections.ObjectModel;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WallRebar.Models;
using WallRebar.Services;

namespace WallRebar.ViewModels;

/// <summary>
///     ViewModel cho dialog modeless "Wall Rebar" — bám sát screenshot: Configuration (preset) + Cover Setting
///     + Cross Section (3 hàng Ø@spacing, Hook Type trên/dưới, Top/Bottom Offset) + Longitudinal Section
///     (Horizontal Offset Start/End, Draw Additional Rebar). Ghi document qua ExternalEvent.
/// </summary>
public sealed partial class WallRebarViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly ExternalEvent _externalEvent;
    private readonly WallRebarHandler _handler;
    private readonly Dispatcher _dispatcher;

    public event Action? RequestClose;

    public WallRebarViewModel(ILogger logger)
    {
        _logger = logger;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _handler = new WallRebarHandler { OnCompleted = OnRebarCompleted };
        _externalEvent = ExternalEvent.Create(_handler);
        RefreshPresets();
    }

    // --- Cover Setting (mm) ---
    [ObservableProperty] private double _coverTopBottomMm = 25;
    [ObservableProperty] private double _coverLeftRightMm = 25;
    [ObservableProperty] private double _coverStartEndMm = 25;

    // --- Cross Section: Hook trên/dưới ---
    [ObservableProperty] private HookType _topHookType = HookType.Closed;
    [ObservableProperty] private HookBendDirection _topHookDirection = HookBendDirection.Inward;
    [ObservableProperty] private double _topHookLengthMm = 100;
    [ObservableProperty] private HookType _bottomHookType = HookType.Closed;
    [ObservableProperty] private HookBendDirection _bottomHookDirection = HookBendDirection.Inward;
    [ObservableProperty] private double _bottomHookLengthMm = 200;
    [ObservableProperty] private double _topOffsetMm;
    [ObservableProperty] private double _bottomOffsetMm = 250;

    // --- Cross Section: 3 hàng Ø@spacing (Vertical / Horizontal / Tie) ---
    [ObservableProperty] private int _verticalDiameterMm = 6;
    [ObservableProperty] private double _verticalSpacingMm = 150;
    [ObservableProperty] private int _horizontalDiameterMm = 6;
    [ObservableProperty] private double _horizontalSpacingMm = 200;
    [ObservableProperty] private int _tieDiameterMm = 6;
    [ObservableProperty] private double _tieSpacingMm = 500;

    // --- Longitudinal Section ---
    [ObservableProperty] private double _horizontalOffsetStartMm;
    [ObservableProperty] private double _horizontalOffsetEndMm;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdditionalRebarPreview))]
    private bool _drawAdditionalRebar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdditionalRebarPreview))]
    private bool _drawAdditionalRebarInterior = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdditionalRebarPreview))]
    private bool _drawAdditionalRebarExterior = true;

    [ObservableProperty] private double _wallLengthMm = 5600;
    [ObservableProperty] private double _wallHeightMm = 1800;

    // --- Preset ---
    public ObservableCollection<string> PresetNames { get; } = [];
    [ObservableProperty] private string? _selectedPreset;
    [ObservableProperty] private string _presetNameInput = string.Empty;

    [ObservableProperty] private string _statusMessage =
        "Cấu hình thép tường rồi bấm Create. Đơn vị mm.";

    // Chặn bấm Create nhiều lần khi ExternalEvent chưa xử lý xong (mỗi raise bị Revit xếp hàng).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

    public IReadOnlyList<int> DiameterOptions { get; } = RebarDiameter.Standard.Select(d => d.Millimeters).ToList();
    public IReadOnlyList<HookType> HookTypeOptions { get; } = [HookType.Closed, HookType.Half, HookType.Straight];
    public IReadOnlyList<HookBendDirection> HookDirectionOptions { get; } =
        [HookBendDirection.Inward, HookBendDirection.Outward];
    public bool ShowAdditionalRebarPreview =>
        DrawAdditionalRebar && (DrawAdditionalRebarInterior || DrawAdditionalRebarExterior);

    private Wall? _wall;

    public void SetWall(Wall wall)
    {
        _wall = wall;
        _handler.Wall = wall;
        if (new WallGeometryReader().TryRead(wall, out var geometry, out _))
        {
            WallLengthMm = Math.Round(geometry.LengthFeet * 304.8);
            WallHeightMm = Math.Round(geometry.HeightFeet * 304.8);
        }

        StatusMessage = $"Tường \"{wall.Name}\" — cấu hình rồi bấm Create.";
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        if (_wall is null)
        {
            StatusMessage = "Chưa chọn tường.";
            return;
        }

        IsBusy = true;
        _handler.Model = BuildModel();
        StatusMessage = "Đang tạo thép…";
        _externalEvent.Raise();
    }

    private bool CanCreate() => !IsBusy;

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(PresetNameInput) ? SelectedPreset : PresetNameInput;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Nhập tên cấu hình để lưu.";
            return;
        }

        WallConfigStore.Save(name, BuildModel());
        RefreshPresets();
        SelectedPreset = name;
        StatusMessage = $"Đã lưu cấu hình \"{name}\".";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset)) return;
        var name = SelectedPreset;
        WallConfigStore.Delete(name);
        SelectedPreset = null;
        PresetNameInput = string.Empty;
        RefreshPresets();
        StatusMessage = $"Đã xoá cấu hình \"{name}\".";
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    partial void OnSelectedPresetChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        PresetNameInput = value;
        var model = WallConfigStore.Load(value);
        if (model is not null) LoadModel(model);
    }

    partial void OnDrawAdditionalRebarChanged(bool value)
    {
        StatusMessage = value
            ? "Đã bật hiển thị thép tăng cường."
            : "Đã tắt hiển thị thép tăng cường.";
    }

    private void RefreshPresets()
    {
        PresetNames.Clear();
        foreach (var name in WallConfigStore.Names())
            PresetNames.Add(name);
    }

    private WallRebarModel BuildModel() => new()
    {
        Cover = new CoverSettings
        {
            TopBottomMm = CoverTopBottomMm,
            LeftRightMm = CoverLeftRightMm,
            StartEndMm = CoverStartEndMm
        },
        Vertical = new WallLayerConfig { Diameter = new RebarDiameter(VerticalDiameterMm), SpacingMm = VerticalSpacingMm },
        Horizontal = new WallLayerConfig { Diameter = new RebarDiameter(HorizontalDiameterMm), SpacingMm = HorizontalSpacingMm },
        Tie = new WallLayerConfig { Diameter = new RebarDiameter(TieDiameterMm), SpacingMm = TieSpacingMm },
        TopHookType = TopHookType,
        TopHookDirection = TopHookDirection,
        TopHookLengthMm = TopHookLengthMm,
        BottomHookType = BottomHookType,
        BottomHookDirection = BottomHookDirection,
        BottomHookLengthMm = BottomHookLengthMm,
        TopOffsetMm = TopOffsetMm,
        BottomOffsetMm = BottomOffsetMm,
        HorizontalOffsetStartMm = HorizontalOffsetStartMm,
        HorizontalOffsetEndMm = HorizontalOffsetEndMm,
        DrawAdditionalRebar = DrawAdditionalRebar,
        DrawAdditionalRebarInterior = DrawAdditionalRebarInterior,
        DrawAdditionalRebarExterior = DrawAdditionalRebarExterior
    };

    private void LoadModel(WallRebarModel m)
    {
        CoverTopBottomMm = m.Cover.TopBottomMm;
        CoverLeftRightMm = m.Cover.LeftRightMm;
        CoverStartEndMm = m.Cover.StartEndMm;
        VerticalDiameterMm = m.Vertical.Diameter.Millimeters;
        VerticalSpacingMm = m.Vertical.SpacingMm;
        HorizontalDiameterMm = m.Horizontal.Diameter.Millimeters;
        HorizontalSpacingMm = m.Horizontal.SpacingMm;
        TieDiameterMm = m.Tie.Diameter.Millimeters;
        TieSpacingMm = m.Tie.SpacingMm;
        TopHookType = m.TopHookType;
        TopHookDirection = m.TopHookDirection;
        TopHookLengthMm = m.TopHookLengthMm;
        BottomHookType = m.BottomHookType;
        BottomHookDirection = m.BottomHookDirection;
        BottomHookLengthMm = m.BottomHookLengthMm;
        TopOffsetMm = m.TopOffsetMm;
        BottomOffsetMm = m.BottomOffsetMm;
        HorizontalOffsetStartMm = m.HorizontalOffsetStartMm;
        HorizontalOffsetEndMm = m.HorizontalOffsetEndMm;
        DrawAdditionalRebar = m.DrawAdditionalRebar;
        DrawAdditionalRebarInterior = m.DrawAdditionalRebarInterior;
        DrawAdditionalRebarExterior = m.DrawAdditionalRebarExterior;
    }

    private void OnRebarCompleted(RebarCreationResult result) => RunOnUi(() => ApplyRebarCompleted(result));

    private void ApplyRebarCompleted(RebarCreationResult result)
    {
        IsBusy = false;
        var msg = result.Succeeded
            ? $"Đã tạo: {result.VerticalBarCount} set thép dọc, {result.HorizontalBarCount} set thép ngang, {result.TieCount} thép giằng."
            : "Không tạo được thép nào.";
        if (result.Warnings.Count > 0)
            msg += $" ({result.Warnings.Count} warning: {result.Warnings[0]})";
        StatusMessage = msg;
        if (result.Warnings.Count > 0)
            _logger.Warning("Wall rebar warnings: {Warnings}", string.Join(" | ", result.Warnings));
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
