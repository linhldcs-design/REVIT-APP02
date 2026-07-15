using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.ViewModels;

/// <summary>ViewModel form Beam Drawing v2: resource combos, preset CRUD và cấu hình sectional/cross riêng.</summary>
public sealed partial class BeamDrawingViewModel : ObservableObject
{
    public const string DefaultOption = "(Mặc định)";

    private readonly BeamDrawingPresetStore _presetStore;

    public BeamDrawingViewModel(ProjectResources resources, BeamDrawingPresetStore? presetStore = null)
    {
        _presetStore = presetStore ?? new BeamDrawingPresetStore();

        RebarTagOptions = Prepend(resources.RebarTagTypeNames);
        MultiRebarAnnotationTypeOptions = Prepend(resources.MultiRebarAnnotationTypeNames);
        SpotTypeOptions = Prepend(resources.SpotElevationTypeNames);
        DimensionTypeOptions = Prepend(resources.DimensionTypeNames);
        SectionTypeOptions = Prepend(resources.SectionTypeNames);
        ViewTemplateOptions = Prepend(resources.ViewTemplateNames);
        ViewportTypeOptions = Prepend(resources.ViewportTypeNames);
        BreakLineOptions = Prepend(resources.BreakLineFamilyNames);
        TitleBlockOptions = Prepend(resources.TitleBlockNames);
        ExistingSheetOptions = new ObservableCollection<ProjectSheetOption>(resources.ExistingSheets);

        var stored = _presetStore.Load();
        foreach (var preset in stored) Presets.Add(preset);

        if (Presets.Count > 0) SelectedPreset = Presets[0];
        else
        {
            LoadIntoFields(BeamDrawingSettingFactory.CreateDefault());
            ApplyLiveProjectDefaults();
        }
    }

    /// <summary>Preview tiết diện thật (dầm đầu tiên pick) — GỐI &amp; NHỊP. Command gán; View vẽ chấm thép theo tỉ lệ.</summary>
    public CrossSectionPreview SupportPreview { get; set; } = CrossSectionPreview.Empty;
    public CrossSectionPreview MidSpanPreview { get; set; } = CrossSectionPreview.Empty;

    public ObservableCollection<BeamDrawingSetting> Presets { get; } = new();
    public ObservableCollection<string> RebarTagOptions { get; }
    public ObservableCollection<string> MultiRebarAnnotationTypeOptions { get; }
    public ObservableCollection<string> SpotTypeOptions { get; }
    public ObservableCollection<string> DimensionTypeOptions { get; }
    public ObservableCollection<string> SectionTypeOptions { get; }
    public ObservableCollection<string> ViewTemplateOptions { get; }
    public ObservableCollection<string> ViewportTypeOptions { get; }
    public ObservableCollection<string> BreakLineOptions { get; }
    public ObservableCollection<string> TitleBlockOptions { get; }
    public ObservableCollection<ProjectSheetOption> ExistingSheetOptions { get; }

    [ObservableProperty] private BeamDrawingSetting? _selectedPreset;
    [ObservableProperty] private string _settingName = string.Empty;

    [ObservableProperty] private string _selectedT1Tag = DefaultOption;
    [ObservableProperty] private string _selectedT2Tag = DefaultOption;
    [ObservableProperty] private string _selectedMidTag = DefaultOption;
    [ObservableProperty] private string _selectedD0Tag = DefaultOption;
    [ObservableProperty] private string _selectedD1Tag = DefaultOption;
    [ObservableProperty] private string _selectedD2Tag = DefaultOption;
    [ObservableProperty] private string _selectedD3Tag = DefaultOption;
    [ObservableProperty] private string _selectedD4Tag = DefaultOption;
    [ObservableProperty] private string _selectedD5Tag = DefaultOption;
    [ObservableProperty] private bool _rebarBreakSymbol = true;
    [ObservableProperty] private string _selectedEndLongitudinalMraType = DefaultOption;
    [ObservableProperty] private string _selectedEndStirrupTagType = DefaultOption;
    [ObservableProperty] private string _selectedMidLongitudinalMraType = DefaultOption;
    [ObservableProperty] private string _selectedMidStirrupTagType = DefaultOption;
    [ObservableProperty] private string _selectedEndReinforceL1MraType = DefaultOption; // tag tăng cường L1 (1 cây) — GỐI
    [ObservableProperty] private string _selectedMidReinforceL1MraType = DefaultOption; // tag tăng cường L1 (1 cây) — NHỊP
    [ObservableProperty] private string _selectedEndReinforceL2MraType = DefaultOption; // MRA tăng cường L2 (≥2 cây) — GỐI
    [ObservableProperty] private string _selectedMidReinforceL2MraType = DefaultOption; // MRA tăng cường L2 (≥2 cây) — NHỊP

    [ObservableProperty] private bool _spotEnabled = true;
    [ObservableProperty] private string _selectedSpotType = DefaultOption;
    [ObservableProperty] private string _spotOffsetText = "0";

    [ObservableProperty] private bool _dimensionEnabled = true;
    [ObservableProperty] private string _selectedSectionalDimType = DefaultOption;
    [ObservableProperty] private string _selectedCrossDimType = DefaultOption;
    [ObservableProperty] private string _spacingFactorText = "6";
    [ObservableProperty] private string _distanceToSideBeamText = "200";
    [ObservableProperty] private string _distanceToBotFaceText = "200";

    [ObservableProperty] private string _sectionalScaleText = "25";
    [ObservableProperty] private string _crossScaleText = "25";
    [ObservableProperty] private string _selectedSectionalSectionType = DefaultOption;
    [ObservableProperty] private string _selectedCrossSectionType = DefaultOption;
    [ObservableProperty] private string _selectedSectionalViewTemplate = DefaultOption;
    [ObservableProperty] private string _selectedCrossViewTemplate = DefaultOption;
    [ObservableProperty] private string _selectedSectionalViewport = DefaultOption;
    [ObservableProperty] private string _selectedCrossViewport = DefaultOption;

    [ObservableProperty] private bool _breakLineEnabled = true;
    [ObservableProperty] private string _selectedBreakLine = DefaultOption;
    [ObservableProperty] private string _selectedTitleBlock = DefaultOption;
    [ObservableProperty] private string _sheetNumber = string.Empty;
    [ObservableProperty] private string _sheetName = string.Empty;
    [ObservableProperty] private ProjectSheetOption? _selectedExistingSheet;

    [ObservableProperty] private bool _longSection;
    [ObservableProperty] private string _longSectionViewName = string.Empty;
    [ObservableProperty] private bool _crossSection = true;
    [ObservableProperty] private string _crossSectionViewName = string.Empty;
    [ObservableProperty] private bool _crossSectionForMultiBeam;
    [ObservableProperty] private bool _pickPillowToDim;
    [ObservableProperty] private bool _createView3D;

    public BeamDrawingSetting? Result { get; private set; }
    public event EventHandler<bool>? CloseRequested;

    partial void OnSelectedPresetChanged(BeamDrawingSetting? value)
    {
        if (value != null) LoadIntoFields(value);
    }

    partial void OnSelectedExistingSheetChanged(ProjectSheetOption? value)
    {
        if (value == null) return;
        SheetNumber = value.Number;
        SheetName = value.Name;
    }

    [RelayCommand]
    private void AddPreset()
    {
        if (!TryBuildSetting(requireName: true, out var setting, out var errors))
        {
            ShowValidation(errors);
            return;
        }

        if (Presets.Any(p => string.Equals(p.SettingName, setting.SettingName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowValidation(["Tên setting đã tồn tại."]);
            return;
        }

        Presets.Add(setting);
        SelectedPreset = setting;
        SavePresets();
    }

    [RelayCommand]
    private void UpdatePreset()
    {
        if (SelectedPreset == null)
        {
            ShowValidation(["Hãy chọn một setting để cập nhật."]);
            return;
        }

        if (!TryBuildSetting(requireName: true, out var setting, out var errors))
        {
            ShowValidation(errors);
            return;
        }

        var index = Presets.IndexOf(SelectedPreset);
        if (Presets.Where((_, i) => i != index)
            .Any(p => string.Equals(p.SettingName, setting.SettingName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowValidation(["Tên setting đã tồn tại."]);
            return;
        }

        Presets[index] = setting;
        SelectedPreset = setting;
        SavePresets();
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        var index = Presets.IndexOf(SelectedPreset);
        Presets.Remove(SelectedPreset);
        SelectedPreset = Presets.Count == 0 ? null : Presets[Math.Min(index, Presets.Count - 1)];
        SavePresets();
    }

    [RelayCommand]
    private void LoadPresets()
    {
        var dialog = new OpenFileDialog { Filter = "Beam Drawing preset (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        var imported = _presetStore.Import(dialog.FileName);
        if (imported.Count == 0)
        {
            ShowValidation(["File preset không hợp lệ hoặc không có setting."]);
            return;
        }

        Presets.Clear();
        foreach (var preset in imported) Presets.Add(preset);
        SelectedPreset = Presets[0];
        SavePresets();
    }

    [RelayCommand]
    private void ExportPresets()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Beam Drawing preset (*.json)|*.json",
            FileName = "beam-drawing-presets.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog() == true) _presetStore.Export(dialog.FileName, Presets);
    }

    [RelayCommand]
    private void MovePresetUp() => MoveSelectedPreset(-1);

    [RelayCommand]
    private void MovePresetDown() => MoveSelectedPreset(1);

    [RelayCommand]
    private void Ok()
    {
        if (!TryBuildSetting(requireName: false, out var setting, out var errors))
        {
            ShowValidation(errors);
            return;
        }

        Result = setting;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private bool TryBuildSetting(bool requireName, out BeamDrawingSetting setting, out List<string> errors)
    {
        errors = new List<string>();
        var defaults = BeamDrawingSettingFactory.CreateDefault();

        var crossScale = ParseInt(CrossScaleText, "Tỉ lệ Cross Section", errors, defaults.CrossSection.Scale);
        var spacing = ParseInt(SpacingFactorText, "DIM spacing factor", errors, defaults.Dim.SpacingFactor);
        var side = ParseDouble(DistanceToSideBeamText, "Distance DIM to side beam", errors,
            defaults.Dim.DistanceToSideBeamMm);
        var bottom = ParseDouble(DistanceToBotFaceText, "Distance DIM to bot face", errors,
            defaults.Dim.DistanceToBotFaceMm);
        var spotOffset = ParseDouble(SpotOffsetText, "Spot elevation offset", errors, defaults.Spot.OffsetMm);

        if (requireName && string.IsNullOrWhiteSpace(SettingName)) errors.Add("Setting Name không được để trống.");
        if (SelectedExistingSheet == null) errors.Add("Hãy chọn một Sheet có sẵn trong project.");

        setting = new BeamDrawingSetting(
            SettingName: NullIfWhiteSpace(SettingName),
            Sectional: defaults.Sectional,
            CrossSection: new PerViewConfig(crossScale, Normalize(SelectedCrossSectionType),
                Normalize(SelectedCrossViewTemplate), Normalize(SelectedCrossViewport)),
            Tags: new RebarTagSet(
                null, null, null,
                Normalize(SelectedD0Tag), Normalize(SelectedD1Tag), Normalize(SelectedD2Tag),
                Normalize(SelectedD3Tag), Normalize(SelectedD4Tag), Normalize(SelectedD5Tag), false),
            Spot: new SpotElevationConfig(SpotEnabled, Normalize(SelectedSpotType), spotOffset),
            Dim: new DimensionConfig(DimensionEnabled, Normalize(SelectedSectionalDimType),
                Normalize(SelectedCrossDimType), spacing, side, bottom),
            BreakLine: BreakLineEnabled,
            BreakLineFamilyName: Normalize(SelectedBreakLine),
            Sheet: new SheetConfig(SheetNumber.Trim(), SheetName.Trim(), Normalize(SelectedTitleBlock)),
            Flags: new DrawingFlags(
                LongSection: false,
                CrossSection: true,
                CrossSectionForMultiBeam: false,
                PickPillowToDim: false,
                CreateView3D: false,
                LongSectionViewName: null,
                CrossSectionViewName: NullIfWhiteSpace(CrossSectionViewName)),
            CrossAnnotation: new CrossAnnotationConfig(
                Normalize(SelectedEndLongitudinalMraType),
                Normalize(SelectedEndStirrupTagType),
                Normalize(SelectedMidLongitudinalMraType),
                Normalize(SelectedMidStirrupTagType),
                Normalize(SelectedEndReinforceL1MraType),
                Normalize(SelectedMidReinforceL1MraType),
                Normalize(SelectedEndReinforceL2MraType),
                Normalize(SelectedMidReinforceL2MraType)));

        errors.AddRange(BeamDrawingSettingValidator.Validate(setting));
        return errors.Count == 0;
    }

    private void LoadIntoFields(BeamDrawingSetting setting)
    {
        SettingName = setting.SettingName ?? string.Empty;
        SectionalScaleText = setting.Sectional.Scale.ToString();
        CrossScaleText = setting.CrossSection.Scale.ToString();
        SelectedSectionalSectionType = ToOption(setting.Sectional.SectionTypeName);
        SelectedCrossSectionType = ToOption(setting.CrossSection.SectionTypeName);
        SelectedSectionalViewTemplate = ToOption(setting.Sectional.ViewTemplateName);
        SelectedCrossViewTemplate = ToOption(setting.CrossSection.ViewTemplateName);
        SelectedSectionalViewport = ToOption(setting.Sectional.ViewportTypeName);
        SelectedCrossViewport = ToOption(setting.CrossSection.ViewportTypeName);

        SelectedT1Tag = ToOption(setting.Tags.T1);
        SelectedT2Tag = ToOption(setting.Tags.T2);
        SelectedMidTag = ToOption(setting.Tags.MidItem);
        SelectedD0Tag = ToOption(setting.Tags.D0);
        SelectedD1Tag = ToOption(setting.Tags.D1);
        SelectedD2Tag = ToOption(setting.Tags.D2);
        SelectedD3Tag = ToOption(setting.Tags.D3);
        SelectedD4Tag = ToOption(setting.Tags.D4);
        SelectedD5Tag = ToOption(setting.Tags.D5);
        RebarBreakSymbol = setting.Tags.RebarBreakSymbol;
        var crossAnnotation = setting.CrossAnnotation ?? CrossAnnotationConfig.Empty;
        SelectedEndLongitudinalMraType = ToOption(crossAnnotation.EndLongitudinalMraTypeName);
        SelectedEndStirrupTagType = ToOption(crossAnnotation.EndStirrupTagTypeName ?? setting.Tags.D4);
        SelectedMidLongitudinalMraType = ToOption(crossAnnotation.MidLongitudinalMraTypeName);
        SelectedMidStirrupTagType = ToOption(crossAnnotation.MidStirrupTagTypeName ?? setting.Tags.D2);
        SelectedEndReinforceL1MraType = ToOption(crossAnnotation.EndReinforceL1MraTypeName);
        SelectedMidReinforceL1MraType = ToOption(crossAnnotation.MidReinforceL1MraTypeName);
        SelectedEndReinforceL2MraType = ToOption(crossAnnotation.EndReinforceL2MraTypeName);
        SelectedMidReinforceL2MraType = ToOption(crossAnnotation.MidReinforceL2MraTypeName);

        SpotEnabled = setting.Spot.Enabled;
        SelectedSpotType = ToOption(setting.Spot.TypeName);
        SpotOffsetText = setting.Spot.OffsetMm.ToString("0.###");
        DimensionEnabled = setting.Dim.Enabled;
        SelectedSectionalDimType = ToOption(setting.Dim.SectionalDimTypeName);
        SelectedCrossDimType = ToOption(setting.Dim.CrossDimTypeName);
        SpacingFactorText = setting.Dim.SpacingFactor.ToString();
        DistanceToSideBeamText = setting.Dim.DistanceToSideBeamMm.ToString("0.###");
        DistanceToBotFaceText = setting.Dim.DistanceToBotFaceMm.ToString("0.###");

        BreakLineEnabled = setting.BreakLine;
        SelectedBreakLine = ToOption(setting.BreakLineFamilyName);
        SelectedTitleBlock = ToOption(setting.Sheet.TitleBlockName);
        SheetNumber = setting.Sheet.Number;
        SheetName = setting.Sheet.Name;
        SelectedExistingSheet = ExistingSheetOptions.FirstOrDefault(sheet =>
            string.Equals(sheet.Number, setting.Sheet.Number, StringComparison.OrdinalIgnoreCase));

        LongSection = setting.Flags.LongSection;
        LongSectionViewName = setting.Flags.LongSectionViewName ?? string.Empty;
        CrossSection = setting.Flags.CrossSection;
        CrossSectionViewName = setting.Flags.CrossSectionViewName ?? string.Empty;
        CrossSectionForMultiBeam = setting.Flags.CrossSectionForMultiBeam;
        PickPillowToDim = setting.Flags.PickPillowToDim;
        CreateView3D = setting.Flags.CreateView3D;

        // Migrate preset tạo trước khi có CrossAnnotationConfig/MCP-derived resource defaults.
        if (crossAnnotation == CrossAnnotationConfig.Empty)
        {
            SelectedEndLongitudinalMraType = FindOption(
                MultiRebarAnnotationTypeOptions, "BS-A2_SL & DK (MCN)-P");
            SelectedMidLongitudinalMraType = SelectedEndLongitudinalMraType;
            SelectedEndStirrupTagType = FindOption(RebarTagOptions, "A2_P_RT_DK&KC_BOT");
            SelectedMidStirrupTagType = SelectedEndStirrupTagType;
        }
        if (string.IsNullOrWhiteSpace(setting.Dim.CrossDimTypeName))
            SelectedCrossDimType = FindOption(DimensionTypeOptions, "@BS-Dim A2");
        if (string.IsNullOrWhiteSpace(setting.Spot.TypeName))
            SelectedSpotType = FindOption(SpotTypeOptions, "BS-2-Cao độ mặt đứng");
        if (string.IsNullOrWhiteSpace(setting.BreakLineFamilyName))
            SelectedBreakLine = FindOption(BreakLineOptions, "@BS-Break Line _Nhieu ty le: 1-25");
    }

    private void MoveSelectedPreset(int delta)
    {
        if (SelectedPreset == null) return;
        var oldIndex = Presets.IndexOf(SelectedPreset);
        var newIndex = oldIndex + delta;
        if (newIndex < 0 || newIndex >= Presets.Count) return;
        Presets.Move(oldIndex, newIndex);
        SavePresets();
    }

    private void SavePresets() => _presetStore.Save(Presets);

    /// <summary>Default học trực tiếp từ view mẫu DS1-01 - 4 qua Revit MCP; chỉ áp khi chưa có preset.</summary>
    private void ApplyLiveProjectDefaults()
    {
        SelectedEndLongitudinalMraType = FindOption(
            MultiRebarAnnotationTypeOptions, "BS-A2_SL & DK (MCN)-P");
        SelectedMidLongitudinalMraType = SelectedEndLongitudinalMraType;
        SelectedEndStirrupTagType = FindOption(RebarTagOptions, "A2_P_RT_DK&KC_BOT");
        SelectedMidStirrupTagType = SelectedEndStirrupTagType;
        SelectedCrossDimType = FindOption(DimensionTypeOptions, "@BS-Dim A2");
        SelectedSpotType = FindOption(SpotTypeOptions, "BS-2-Cao độ mặt đứng");
        SelectedBreakLine = FindOption(BreakLineOptions, "@BS-Break Line _Nhieu ty le: 1-25");
    }

    private static string FindOption(IEnumerable<string> options, string expected) =>
        options.FirstOrDefault(option => option.Contains(expected, StringComparison.OrdinalIgnoreCase))
        ?? DefaultOption;

    private static int ParseInt(string value, string label, List<string> errors, int fallback)
    {
        if (int.TryParse(value?.Trim(), out var result)) return result;
        errors.Add($"{label} phải là số nguyên.");
        return fallback;
    }

    private static double ParseDouble(string value, string label, List<string> errors, double fallback)
    {
        if (double.TryParse(value?.Trim(), out var result)) return result;
        errors.Add($"{label} phải là số.");
        return fallback;
    }

    private static ObservableCollection<string> Prepend(IReadOnlyList<string> names)
    {
        var result = new ObservableCollection<string> { DefaultOption };
        foreach (var name in names) result.Add(name);
        return result;
    }

    private static string ToOption(string? value) => string.IsNullOrWhiteSpace(value) ? DefaultOption : value;
    private static string? Normalize(string? value) => value == DefaultOption ? null : NullIfWhiteSpace(value);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ShowValidation(IEnumerable<string> errors) =>
        MessageBox.Show(string.Join("\n", errors), "Cấu hình chưa hợp lệ",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
