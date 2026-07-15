using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using RevitAPP.Models;

namespace RevitAPP.ViewModels;

/// <summary>ViewModel chính của dialog cấu hình thép cột.</summary>
public sealed partial class ColumnRebarViewModel : ObservableObject
{
    // Kích thước vùng section preview (pixel)
    private const double PreviewWidth = 250;
    private const double PreviewHeight = 200;
    private const double PreviewPadding = 24;

    private bool _canDraw;

    /// <summary>Callback lưu preset (đặt bởi Command layer để tránh VM phụ thuộc trực tiếp Revit Document).</summary>
    public Func<ColumnRebarConfig, bool>? SavePresetCallback { get; set; }

    /// <summary>Callback xoá preset theo tên.</summary>
    public Func<string, bool>? DeletePresetCallback { get; set; }

    public ColumnRebarViewModel(IReadOnlyList<ColumnStackItem> stack, IReadOnlyList<RebarBarTypeOption> barTypes,
        IReadOnlyList<ColumnRebarConfig>? savedPresets = null)
    {
        BarTypes = barTypes;
        var defaultMain = barTypes.FirstOrDefault(b => b.DiameterMm >= 16) ?? barTypes[0];
        var defaultStirrup = barTypes.FirstOrDefault(b => b.DiameterMm is >= 6 and <= 10) ?? barTypes[0];

        Floors = new ObservableCollection<FloorRebarRowViewModel>(
            stack.Select(item => CreateRow(item.Storey, defaultMain, defaultStirrup, item.AutoBeamDepthMm)));
        _selectedFloor = Floors.FirstOrDefault();

        Presets = new ObservableCollection<ColumnRebarConfig>(savedPresets ?? Array.Empty<ColumnRebarConfig>());

        Revalidate();
        UpdateDetail();
    }

    public IReadOnlyList<RebarBarTypeOption> BarTypes { get; }
    public ObservableCollection<FloorRebarRowViewModel> Floors { get; }

    public IReadOnlyList<StirrupTypeOption> StirrupTypeOptions { get; } = new[]
    {
        new StirrupTypeOption(SectionStirrupType.ClosedTie, "Đai kín"),
        new StirrupTypeOption(SectionStirrupType.Crosstie, "Đai + móc chéo"),
        new StirrupTypeOption(SectionStirrupType.Separated, "Đai tách rời")
    };

    // ===== Section preview + thông số thép của tầng đang chọn =====
    public ObservableCollection<SectionPreviewDot> PreviewDots { get; } = new();

    [ObservableProperty] private double _sectionRectLeft;
    [ObservableProperty] private double _sectionRectTop;
    [ObservableProperty] private double _sectionRectWidth;
    [ObservableProperty] private double _sectionRectHeight;
    [ObservableProperty] private double _loopRectLeft;
    [ObservableProperty] private double _loopRectTop;
    [ObservableProperty] private double _loopRectWidth;
    [ObservableProperty] private double _loopRectHeight;
    [ObservableProperty] private string _totalBarText = "";
    [ObservableProperty] private string _areaText = "";
    [ObservableProperty] private string _ratioText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyToAllCommand))]
    private FloorRebarRowViewModel? _selectedFloor;

    partial void OnSelectedFloorChanged(FloorRebarRowViewModel? value) => UpdateDetail();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DrawCommand))]
    private double _coverMm = 25;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DrawCommand))]
    private double _lapFactor = 30;

    [ObservableProperty] private bool _staggerLap = true;
    [ObservableProperty] private LapPosition _lapPosition = LapPosition.NearBottom;
    [ObservableProperty] private double _lapDistanceFromBottomMm = 50;

    // ===== Thép chờ móng (Foundation tab) =====
    [ObservableProperty] private bool _foundationEnabled;
    [ObservableProperty] private double _foundationHmMm = 250;
    [ObservableProperty] private double _foundationLbMm = 200;
    [ObservableProperty] private StarterBendDirection _foundationDirection = StarterBendDirection.Right;
    [ObservableProperty] private bool _foundationSplitBothSides;

    /// <summary>Kết quả khi user xác nhận Vẽ; null nếu huỷ.</summary>
    public IReadOnlyList<StoreyRebarPlan>? Result { get; private set; }

    public RebarLapOptions LapOptions => new(LapFactor, CoverMm, StaggerLap, LapPosition, LapDistanceFromBottomMm);

    public FoundationStarterOptions FoundationOptions =>
        new(FoundationEnabled, FoundationHmMm, FoundationLbMm, FoundationDirection, FoundationSplitBothSides);

    // ===== Rải đai (Stirrup tab) =====
    [ObservableProperty] private double _distanceToFirstStirrupMm;
    [ObservableProperty] private bool _spreadThroughBeam;
    [ObservableProperty] private double _minConfineZoneMm = 450;
    [ObservableProperty] private double _confineClearanceDivisor = 6;
    [ObservableProperty] private bool _reinforceJoint;
    [ObservableProperty] private double _jointStirrupCount = 3;
    [ObservableProperty] private CrosstieDirection _crosstieDirection = CrosstieDirection.X;

    public StirrupSpreadOptions StirrupSpreadOptions =>
        new(DistanceToFirstStirrupMm, SpreadThroughBeam, MinConfineZoneMm, ConfineClearanceDivisor,
            ReinforceJoint, (int)Math.Max(2, JointStirrupCount), CrosstieDirection);

    // ===== Xử lý đầu thép (Settings tab) =====
    [ObservableProperty] private bool _topHookBending = true;
    [ObservableProperty] private double _topHookLengthMm = 150;
    [ObservableProperty] private bool _crankAtLap;
    [ObservableProperty] private double _bendIfOffsetLeMm = 75;
    [ObservableProperty] private double _slopeRatioHdOverE = 6;
    [ObservableProperty] private LargeStepMode _largeStepMode = LargeStepMode.AnchorAtSlab;
    [ObservableProperty] private double _jointAnchorDownMm = 300;

    public SectionTransitionOptions SectionTransitionOptions => new(BendIfOffsetLeMm, SlopeRatioHdOverE, LargeStepMode, JointAnchorDownMm);
    [ObservableProperty] private bool _addPartition = true;

    public ColumnEndOptions ColumnEndOptions => new(TopHookBending, TopHookLengthMm, CrankAtLap);

    /// <summary>Yêu cầu View đóng (true = xác nhận, false = huỷ).</summary>
    public event EventHandler<bool>? CloseRequested;

    private FloorRebarRowViewModel CreateRow(ColumnStorey storey, RebarBarTypeOption main, RebarBarTypeOption stirrup,
        double autoBeamDepthMm = 0)
    {
        var row = new FloorRebarRowViewModel(storey, main, stirrup, autoBeamDepthMm);
        row.PropertyChanged += OnRowChanged;
        return row;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FloorRebarRowViewModel.Error)) return;
        Revalidate();
        if (ReferenceEquals(sender, SelectedFloor)) UpdateDetail();
    }

    /// <summary>Tính lại thông số thép + section preview cho tầng đang chọn.</summary>
    private void UpdateDetail()
    {
        PreviewDots.Clear();
        var floor = SelectedFloor;
        if (floor == null) return;

        var section = floor.Storey.Section;
        var config = floor.ToConfig();

        try
        {
            var bars = TcvnRebarCalculator.BuildClassifiedMainBars(section, config, CoverMm);
            var area = TcvnRebarCalculator.ReinforcementAreaCm2(section, config, CoverMm);
            var ratio = TcvnRebarCalculator.ReinforcementRatioPercent(section, config, CoverMm);
            var loop = TcvnRebarCalculator.BuildStirrupLoop(section, CoverMm, config.StirrupDiameterMm);

            var total = bars.Count;
            var cornerDia = config.MainBarDiameterMm;
            TotalBarText = config.UseDistributionBar
                ? $"4Ø{cornerDia:0.#} + {total - 4}Ø{config.DistributionBarDiameterMm:0.#}"
                : $"{total}Ø{cornerDia:0.#}";
            AreaText = $"{area:0.00} cm²";
            RatioText = $"{ratio:0.000} %";

            // Tỷ lệ vẽ: fit tiết diện vào vùng preview
            var scale = Math.Min((PreviewWidth - 2 * PreviewPadding) / section.WidthMm,
                (PreviewHeight - 2 * PreviewPadding) / section.HeightMm);
            var cx = PreviewWidth / 2d;
            var cy = PreviewHeight / 2d;

            SectionRectWidth = section.WidthMm * scale;
            SectionRectHeight = section.HeightMm * scale;
            SectionRectLeft = cx - SectionRectWidth / 2d;
            SectionRectTop = cy - SectionRectHeight / 2d;

            var loopW = (loop[0].Xmm * -2); // |x| × 2  (loop[0] = (−hw,−hh))
            var loopH = (loop[0].Ymm * -2);
            LoopRectWidth = loopW * scale;
            LoopRectHeight = loopH * scale;
            LoopRectLeft = cx - LoopRectWidth / 2d;
            LoopRectTop = cy - LoopRectHeight / 2d;

            foreach (var bar in bars)
            {
                var size = bar.IsCorner ? 9d : 7d;
                var left = cx + bar.Position.Xmm * scale - size / 2d;
                var top = cy - bar.Position.Ymm * scale - size / 2d; // lật Y (lên trên)
                PreviewDots.Add(new SectionPreviewDot(left, top, size, bar.IsCorner));
            }
        }
        catch (ArgumentException)
        {
            TotalBarText = AreaText = RatioText = "—";
        }
    }

    partial void OnCoverMmChanged(double value)
    {
        Revalidate();
        UpdateDetail();
    }

    partial void OnLapFactorChanged(double value) => Revalidate();

    private void Revalidate()
    {
        var ok = CoverMm >= 0 && LapFactor > 0 && Floors.Count > 0;
        foreach (var floor in Floors)
            ok &= floor.Validate(CoverMm);

        _canDraw = ok;
        DrawCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApplyToAll))]
    private void ApplyToAll()
    {
        if (SelectedFloor == null) return;
        foreach (var floor in Floors)
            if (!ReferenceEquals(floor, SelectedFloor))
                floor.CopyFrom(SelectedFloor);
        Revalidate();
    }

    private bool CanApplyToAll() => SelectedFloor != null && Floors.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDraw))]
    private void Draw()
    {
        Result = Floors.Select(f => new StoreyRebarPlan(
            f.Storey, f.ToConfig(), f.MainBarType, f.StirrupType, f.DistributionBarType)).ToList();
        CloseRequested?.Invoke(this, true);
    }

    private bool CanDraw() => _canDraw;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    // ===== Preset cấu hình (lưu/khôi phục theo tên) =====
    public ObservableCollection<ColumnRebarConfig> Presets { get; }

    // Chặn tự-áp preset khi việc đổi SelectedPreset đến từ thao tác Lưu/Xoá (không phải user chọn).
    private bool _suppressPresetApply;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private ColumnRebarConfig? _selectedPreset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePresetCommand))]
    private string _presetName = "";

    partial void OnSelectedPresetChanged(ColumnRebarConfig? value)
    {
        if (value == null) return;
        PresetName = value.Name;
        // Chọn preset trong danh sách = áp dụng ngay (không cần nút Nạp).
        if (!_suppressPresetApply) ApplyConfig(value);
    }

    /// <summary>Gom toàn bộ trạng thái dialog thành DTO để lưu.</summary>
    private ColumnRebarConfig CaptureConfig(string name) => new(
        name,
        CoverMm, LapFactor, StaggerLap, LapPosition, LapDistanceFromBottomMm,
        FoundationEnabled, FoundationHmMm, FoundationLbMm, FoundationDirection, FoundationSplitBothSides,
        DistanceToFirstStirrupMm, SpreadThroughBeam, MinConfineZoneMm, ConfineClearanceDivisor,
        ReinforceJoint, JointStirrupCount, CrosstieDirection,
        TopHookBending, TopHookLengthMm, CrankAtLap, BendIfOffsetLeMm, SlopeRatioHdOverE,
        LargeStepMode, JointAnchorDownMm, AddPartition,
        Floors.Select(f => new ColumnRebarFloorConfig(
            f.LevelName, f.MainBarType.DiameterMm, f.BarsX, f.BarsY, f.StirrupType.DiameterMm,
            f.SpacingEndMm, f.SpacingMidMm, f.ConfineZoneLenMm,
            f.UseDistributionBar, f.DistributionBarType.DiameterMm, f.StirrupSectionType)).ToList());

    /// <summary>Khôi phục dialog từ DTO đã lưu. Dò lại loại thanh thép theo đường kính.</summary>
    private void ApplyConfig(ColumnRebarConfig c)
    {
        CoverMm = c.CoverMm;
        LapFactor = c.LapFactor;
        StaggerLap = c.StaggerLap;
        LapPosition = c.LapPosition;
        LapDistanceFromBottomMm = c.LapDistanceFromBottomMm;

        FoundationEnabled = c.FoundationEnabled;
        FoundationHmMm = c.FoundationHmMm;
        FoundationLbMm = c.FoundationLbMm;
        FoundationDirection = c.FoundationDirection;
        FoundationSplitBothSides = c.FoundationSplitBothSides;

        DistanceToFirstStirrupMm = c.DistanceToFirstStirrupMm;
        SpreadThroughBeam = c.SpreadThroughBeam;
        MinConfineZoneMm = c.MinConfineZoneMm;
        ConfineClearanceDivisor = c.ConfineClearanceDivisor;
        ReinforceJoint = c.ReinforceJoint;
        JointStirrupCount = c.JointStirrupCount;
        CrosstieDirection = c.CrosstieDirection;

        TopHookBending = c.TopHookBending;
        TopHookLengthMm = c.TopHookLengthMm;
        CrankAtLap = c.CrankAtLap;
        BendIfOffsetLeMm = c.BendIfOffsetLeMm;
        SlopeRatioHdOverE = c.SlopeRatioHdOverE;
        LargeStepMode = c.LargeStepMode;
        JointAnchorDownMm = c.JointAnchorDownMm;
        AddPartition = c.AddPartition;

        // Áp cấu hình từng tầng theo tên tầng; tầng không có trong preset giữ nguyên.
        foreach (var floor in Floors)
        {
            var saved = c.Floors.FirstOrDefault(f =>
                string.Equals(f.LevelName, floor.LevelName, StringComparison.OrdinalIgnoreCase));
            if (saved == null) continue;

            floor.MainBarType = ResolveBarType(saved.MainBarDiameterMm, floor.MainBarType);
            floor.BarsX = saved.BarsX;
            floor.BarsY = saved.BarsY;
            floor.StirrupType = ResolveBarType(saved.StirrupDiameterMm, floor.StirrupType);
            floor.SpacingEndMm = saved.SpacingEndMm;
            floor.SpacingMidMm = saved.SpacingMidMm;
            floor.ConfineZoneLenMm = saved.ConfineZoneLenMm;
            floor.UseDistributionBar = saved.UseDistributionBar;
            floor.DistributionBarType = ResolveBarType(saved.DistributionBarDiameterMm, floor.DistributionBarType);
            floor.StirrupSectionType = saved.StirrupSectionType;
        }

        Revalidate();
        UpdateDetail();
    }

    /// <summary>Dò loại thanh có đường kính gần nhất; fallback về loại hiện tại nếu không có.</summary>
    private RebarBarTypeOption ResolveBarType(double diameterMm, RebarBarTypeOption fallback)
    {
        var exact = BarTypes.FirstOrDefault(b => Math.Abs(b.DiameterMm - diameterMm) < 0.01);
        if (exact != null) return exact;
        return BarTypes.OrderBy(b => Math.Abs(b.DiameterMm - diameterMm)).FirstOrDefault() ?? fallback;
    }

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        var name = PresetName.Trim();
        var config = CaptureConfig(name);
        if (SavePresetCallback?.Invoke(config) != true) return;

        var existing = Presets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Presets.Remove(existing);
        Presets.Add(config);

        // Chọn preset vừa lưu nhưng không tự áp lại (giá trị đang hiển thị chính là nó).
        _suppressPresetApply = true;
        SelectedPreset = config;
        _suppressPresetApply = false;
    }

    private bool CanSavePreset() => !string.IsNullOrWhiteSpace(PresetName);

    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private void DeletePreset()
    {
        var preset = SelectedPreset;
        if (preset == null || DeletePresetCallback?.Invoke(preset.Name) != true) return;
        Presets.Remove(preset);
        SelectedPreset = null;
        PresetName = "";
    }

    private bool CanUsePreset() => SelectedPreset != null;
}
