using System.Collections.ObjectModel;
using BeamRebar.Core.Models;
using BeamRebar.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeamRebar.Addin.ViewModels;

/// <summary>
///     ViewModel dialog Quick Setting — bind phẳng các trường cấu hình thép (item 1–7, 12 trong UI).
///     Tải mặc định từ <see cref="QuickSettingFactory"/>, validate qua <see cref="QuickSettingValidator"/>,
///     trả <see cref="Result"/> khi user bấm "Create Rebars Immediately".
/// </summary>
public sealed partial class QuickSettingViewModel : ObservableObject
{
    public QuickSettingViewModel() : this(QuickSettingFactory.CreateDefault())
    {
    }

    public QuickSettingViewModel(QuickSettingModel model)
    {
        Diameters = new ObservableCollection<RebarDiameter>(RebarDiameter.Standard);
        Counts = new ObservableCollection<int>(Enumerable.Range(1, 10));
        LoadFrom(model);
    }

    public ObservableCollection<RebarDiameter> Diameters { get; }
    public ObservableCollection<int> Counts { get; }

    /// <summary>Kết quả khi user xác nhận — null nếu huỷ.</summary>
    public QuickSettingModel? Result { get; private set; }

    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty] private string _validationMessage = string.Empty;

    // ===== Item 2: Main Top =====
    [ObservableProperty] private int _mainTopCount;
    [ObservableProperty] private RebarDiameter _mainTopDiameter;

    // ===== Item 6: Main Bottom =====
    [ObservableProperty] private int _mainBottomCount;
    [ObservableProperty] private RebarDiameter _mainBottomDiameter;

    // ===== Item 1: Top Additional (layer 1) =====
    [ObservableProperty] private bool _topAddEnabled;
    [ObservableProperty] private int _topAddCount;
    [ObservableProperty] private RebarDiameter _topAddDiameter;

    // ===== Item 3: Top Additional layer 2 =====
    [ObservableProperty] private bool _topAdd2Enabled;
    [ObservableProperty] private int _topAdd2Count;
    [ObservableProperty] private RebarDiameter _topAdd2Diameter;

    // ===== Item 7: Bottom Additional (layer 1) =====
    [ObservableProperty] private bool _botAddEnabled;
    [ObservableProperty] private int _botAddCount;
    [ObservableProperty] private RebarDiameter _botAddDiameter;

    // ===== Item 4: Bottom Additional layer 2 =====
    [ObservableProperty] private bool _botAdd2Enabled;
    [ObservableProperty] private int _botAdd2Count;
    [ObservableProperty] private RebarDiameter _botAdd2Diameter;

    // ===== Item 5: Stirrup =====
    [ObservableProperty] private RebarDiameter _stirrupDiameter;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTwoEnds))] private bool _stirrupUniform;
    [ObservableProperty] private double _stirrupSpacingEnd;   // A1
    [ObservableProperty] private double _stirrupSpacingMid;   // A2
    [ObservableProperty] private double _stirrupUniformLength; // L

    public bool IsTwoEnds => !StirrupUniform;

    // ===== Item 12: Anti-bulge =====
    [ObservableProperty] private bool _antiBulgeEnabled;
    [ObservableProperty] private double _antiBulgeThreshold;
    [ObservableProperty] private RebarDiameter _antiBulgeDiameter;
    [ObservableProperty] private double _antiBulgeSpacing;

    // ===== Cover =====
    [ObservableProperty] private double _coverTop;
    [ObservableProperty] private double _coverBottom;
    [ObservableProperty] private double _coverSide;

    private void LoadFrom(QuickSettingModel m)
    {
        MainTopCount = m.MainTop.Count;
        MainTopDiameter = m.MainTop.Diameter;
        MainBottomCount = m.MainBottom.Count;
        MainBottomDiameter = m.MainBottom.Diameter;

        TopAddEnabled = m.TopAdditional.Enabled;
        TopAddCount = m.TopAdditional.Count;
        TopAddDiameter = m.TopAdditional.Diameter;
        TopAdd2Enabled = m.TopAdditionalLayer2.Enabled;
        TopAdd2Count = m.TopAdditionalLayer2.Count;
        TopAdd2Diameter = m.TopAdditionalLayer2.Diameter;

        BotAddEnabled = m.BottomAdditional.Enabled;
        BotAddCount = m.BottomAdditional.Count;
        BotAddDiameter = m.BottomAdditional.Diameter;
        BotAdd2Enabled = m.BottomAdditionalLayer2.Enabled;
        BotAdd2Count = m.BottomAdditionalLayer2.Count;
        BotAdd2Diameter = m.BottomAdditionalLayer2.Diameter;

        StirrupDiameter = m.Stirrup.Diameter;
        StirrupUniform = m.Stirrup.Mode == StirrupMode.Uniform;
        StirrupSpacingEnd = m.Stirrup.SpacingEndMm;
        StirrupSpacingMid = m.Stirrup.SpacingMidMm;
        StirrupUniformLength = m.Stirrup.UniformLengthMm;

        AntiBulgeEnabled = m.AntiBulge.Enabled;
        AntiBulgeThreshold = m.AntiBulge.HeightThresholdMm;
        AntiBulgeDiameter = m.AntiBulge.Diameter;
        AntiBulgeSpacing = m.AntiBulge.SpacingMm;

        CoverTop = m.Cover.TopMm;
        CoverBottom = m.Cover.BottomMm;
        CoverSide = m.Cover.SideMm;
    }

    private QuickSettingModel BuildModel() => new()
    {
        MainTop = new MainBarConfig { Count = MainTopCount, Diameter = MainTopDiameter },
        MainBottom = new MainBarConfig { Count = MainBottomCount, Diameter = MainBottomDiameter },

        TopAdditional = new AdditionalBarConfig { Enabled = TopAddEnabled, Count = TopAddCount, Diameter = TopAddDiameter, Layer = 1 },
        TopAdditionalLayer2 = new AdditionalBarConfig { Enabled = TopAdd2Enabled, Count = TopAdd2Count, Diameter = TopAdd2Diameter, Layer = 2 },
        BottomAdditional = new AdditionalBarConfig { Enabled = BotAddEnabled, Count = BotAddCount, Diameter = BotAddDiameter, Layer = 1 },
        BottomAdditionalLayer2 = new AdditionalBarConfig { Enabled = BotAdd2Enabled, Count = BotAdd2Count, Diameter = BotAdd2Diameter, Layer = 2 },

        Stirrup = new StirrupConfig
        {
            Diameter = StirrupDiameter,
            Mode = StirrupUniform ? StirrupMode.Uniform : StirrupMode.TwoEnds,
            SpacingEndMm = StirrupSpacingEnd,
            SpacingMidMm = StirrupSpacingMid,
            UniformLengthMm = StirrupUniformLength
        },

        AntiBulge = new AntiBulgeConfig
        {
            Enabled = AntiBulgeEnabled,
            HeightThresholdMm = AntiBulgeThreshold,
            Diameter = AntiBulgeDiameter,
            SpacingMm = AntiBulgeSpacing
        },

        Cover = new CoverSettings { TopMm = CoverTop, BottomMm = CoverBottom, SideMm = CoverSide }
    };

    [RelayCommand]
    private void CreateImmediately()
    {
        var model = BuildModel();
        var validation = QuickSettingValidator.Validate(model);
        if (!validation.IsValid)
        {
            ValidationMessage = string.Join("\n", validation.Errors);
            return;
        }

        Result = model;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
