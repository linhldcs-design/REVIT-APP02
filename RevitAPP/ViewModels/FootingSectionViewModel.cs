using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.FootingSection;
using RevitAPP.Core.Services;

namespace RevitAPP.ViewModels;

/// <summary>ViewModel form mặt cắt móng: combo tài nguyên, preset CRUD, cấu hình tag/dim/detail item.</summary>
public sealed partial class FootingSectionViewModel : ObservableObject
{
    public const string DefaultOption = "(Mặc định)";

    private readonly FootingSectionPresetStore _presetStore;

    public FootingSectionViewModel(ProjectResources resources, FootingSectionPresetStore? presetStore = null,
        IReadOnlyList<string>? levelNames = null)
    {
        _presetStore = presetStore ?? new FootingSectionPresetStore();

        RebarTagOptions = Prepend(resources.RebarTagTypeNames);
        DimensionTypeOptions = Prepend(resources.DimensionTypeNames);
        SectionTypeOptions = Prepend(resources.SectionTypeNames);
        ViewTemplateOptions = Prepend(resources.ViewTemplateNames);
        ViewportTypeOptions = Prepend(resources.ViewportTypeNames);
        BreakLineOptions = Prepend(resources.BreakLineFamilyNames);
        DirectionOptions = new ObservableCollection<string> { "Phương X", "Phương Y" };
        LevelOptions = Prepend(levelNames ?? []);
        ExistingSheetOptions = new ObservableCollection<ProjectSheetOption>(resources.ExistingSheets);

        foreach (var preset in _presetStore.Load()) Presets.Add(preset);

        if (Presets.Count > 0) SelectedPreset = Presets[0];
        else LoadIntoFields(FootingSectionSettingFactory.CreateDefault());
    }

    public ObservableCollection<FootingSectionSetting> Presets { get; } = new();
    public ObservableCollection<string> RebarTagOptions { get; }
    public ObservableCollection<string> DimensionTypeOptions { get; }
    public ObservableCollection<string> SectionTypeOptions { get; }
    public ObservableCollection<string> ViewTemplateOptions { get; }
    public ObservableCollection<string> ViewportTypeOptions { get; }
    public ObservableCollection<string> BreakLineOptions { get; }
    public ObservableCollection<string> DirectionOptions { get; }
    public ObservableCollection<string> LevelOptions { get; }
    public ObservableCollection<ProjectSheetOption> ExistingSheetOptions { get; }

    [ObservableProperty] private FootingSectionSetting? _selectedPreset;
    [ObservableProperty] private string _settingName = string.Empty;
    [ObservableProperty] private string _scaleText = "25";
    [ObservableProperty] private string _selectedSectionType = DefaultOption;
    [ObservableProperty] private string _selectedViewTemplate = DefaultOption;
    [ObservableProperty] private string _selectedViewport = DefaultOption;
    [ObservableProperty] private string _selectedDirection = "Phương X";
    [ObservableProperty] private string _selectedViewBottomLevel = DefaultOption;
    [ObservableProperty] private string _selectedViewTopLevel = DefaultOption;

    [ObservableProperty] private bool _tagFooting = true;
    [ObservableProperty] private string _selectedFootingTag = DefaultOption;
    [ObservableProperty] private bool _tagStirrup = true;
    [ObservableProperty] private string _selectedStirrupTag = DefaultOption;
    [ObservableProperty] private bool _tagStarter = true;
    [ObservableProperty] private string _selectedStarterTag = DefaultOption;

    [ObservableProperty] private bool _dimEnabled = true;
    [ObservableProperty] private string _selectedDimType = DefaultOption;
    [ObservableProperty] private string _dimOffsetText = "200";

    [ObservableProperty] private bool _breakLineEnabled = true;
    [ObservableProperty] private string _selectedBreakLine = DefaultOption;

    [ObservableProperty] private ProjectSheetOption? _selectedExistingSheet;
    [ObservableProperty] private string _sheetNumber = string.Empty;
    [ObservableProperty] private string _sheetName = string.Empty;

    public FootingSectionSetting? Result { get; private set; }
    public event EventHandler<bool>? CloseRequested;

    partial void OnSelectedPresetChanged(FootingSectionSetting? value)
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
        if (!TryBuildSetting(requireName: true, out var setting, out var errors)) { ShowValidation(errors); return; }
        if (Presets.Any(p => NameEquals(p.SettingName, setting.SettingName)))
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
        if (SelectedPreset == null) { ShowValidation(["Hãy chọn một setting để cập nhật."]); return; }
        if (!TryBuildSetting(requireName: true, out var setting, out var errors)) { ShowValidation(errors); return; }
        var index = Presets.IndexOf(SelectedPreset);
        if (Presets.Where((_, i) => i != index).Any(p => NameEquals(p.SettingName, setting.SettingName)))
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
        var dialog = new OpenFileDialog { Filter = "Footing section preset (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        var imported = _presetStore.Import(dialog.FileName);
        if (imported.Count == 0) { ShowValidation(["File preset không hợp lệ hoặc không có setting."]); return; }
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
            Filter = "Footing section preset (*.json)|*.json",
            FileName = "footing-section-presets.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog() == true) _presetStore.Export(dialog.FileName, Presets);
    }

    [RelayCommand]
    private void Ok()
    {
        if (!TryBuildSetting(requireName: false, out var setting, out var errors)) { ShowValidation(errors); return; }
        Result = setting;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private bool TryBuildSetting(bool requireName, out FootingSectionSetting setting, out List<string> errors)
    {
        errors = new List<string>();
        var defaults = FootingSectionSettingFactory.CreateDefault();
        var scale = ParseInt(ScaleText, "Tỉ lệ", errors, defaults.Scale);
        var dimOffset = ParseDouble(DimOffsetText, "Khoảng cách DIM", errors, defaults.Dim.OffsetMm);

        if (requireName && string.IsNullOrWhiteSpace(SettingName)) errors.Add("Setting Name không được để trống.");
        if (SelectedExistingSheet == null) errors.Add("Hãy chọn một Sheet có sẵn trong project.");

        setting = new FootingSectionSetting(
            SettingName: NullIfWhiteSpace(SettingName),
            Scale: scale,
            SectionTypeName: Normalize(SelectedSectionType),
            ViewTemplateName: Normalize(SelectedViewTemplate),
            ViewportTypeName: Normalize(SelectedViewport),
            Tags: new RebarTagConfig(
                TagFooting, Normalize(SelectedFootingTag),
                TagStirrup, Normalize(SelectedStirrupTag),
                TagStarter, Normalize(SelectedStarterTag)),
            Dim: new FootingDimensionConfig(DimEnabled, Normalize(SelectedDimType), dimOffset),
            BreakLine: new BreakLineConfig(BreakLineEnabled, Normalize(SelectedBreakLine)),
            Sheet: new SheetConfig(SheetNumber.Trim(), SheetName.Trim(), null),
            Flags: new FootingSectionFlags(TagFooting || TagStirrup || TagStarter, DimEnabled, BreakLineEnabled),
            Direction: SelectedDirection == "Phương Y"
                ? FootingSectionDirection.Y
                : FootingSectionDirection.X,
            ViewBottomLevelName: Normalize(SelectedViewBottomLevel),
            ViewTopLevelName: Normalize(SelectedViewTopLevel));

        var bottomIndex = LevelOptions.IndexOf(SelectedViewBottomLevel);
        var topIndex = LevelOptions.IndexOf(SelectedViewTopLevel);
        if (bottomIndex > 0 && topIndex > 0 && bottomIndex >= topIndex)
            errors.Add("Level kết thúc phải cao hơn Level bắt đầu.");

        errors.AddRange(FootingSectionSettingValidator.Validate(setting));
        return errors.Count == 0;
    }

    private void LoadIntoFields(FootingSectionSetting setting)
    {
        // Preset lưu từ schema cũ có thể thiếu (null) các config lồng — fallback về .Empty để không crash.
        var tags = setting.Tags ?? RebarTagConfig.Empty;
        var dim = setting.Dim ?? FootingDimensionConfig.Empty;
        var breakLine = setting.BreakLine ?? BreakLineConfig.Empty;
        var sheet = setting.Sheet;

        SettingName = setting.SettingName ?? string.Empty;
        ScaleText = setting.Scale > 0 ? setting.Scale.ToString() : "25";
        SelectedSectionType = ToOption(setting.SectionTypeName);
        SelectedViewTemplate = ToOption(setting.ViewTemplateName);
        SelectedViewport = ToOption(setting.ViewportTypeName);
        SelectedDirection = setting.Direction == FootingSectionDirection.Y ? "Phương Y" : "Phương X";
        SelectedViewBottomLevel = ToOption(setting.ViewBottomLevelName);
        SelectedViewTopLevel = ToOption(setting.ViewTopLevelName);

        TagFooting = tags.TagFooting;
        SelectedFootingTag = ToOption(tags.FootingBarTagName);
        TagStirrup = tags.TagStirrup;
        SelectedStirrupTag = ToOption(tags.StirrupTagName);
        TagStarter = tags.TagStarter;
        SelectedStarterTag = ToOption(tags.StarterTagName);

        DimEnabled = dim.Enabled;
        SelectedDimType = ToOption(dim.DimTypeName);
        DimOffsetText = dim.OffsetMm.ToString("0.###");

        BreakLineEnabled = breakLine.Enabled;
        SelectedBreakLine = ToOption(breakLine.FamilyName);

        SheetNumber = sheet?.Number ?? string.Empty;
        SheetName = sheet?.Name ?? string.Empty;
        SelectedExistingSheet = ExistingSheetOptions.FirstOrDefault(s =>
            string.Equals(s.Number, sheet?.Number, StringComparison.OrdinalIgnoreCase));
    }

    private void SavePresets() => _presetStore.Save(Presets);

    private static bool NameEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
