using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using Microsoft.Win32;

namespace RevitAPP.ViewModels;

public sealed partial class BeamLongitudinalDrawingViewModel : ObservableObject
{
    private readonly IReadOnlyList<BeamSpanInput> _sourceSpans;
    private readonly IReadOnlyDictionary<long, BeamSpanSectionProfile> _profilesBySourceId;
    private BeamChainModel _chain;
    private IReadOnlyList<SectionStation> _stations;
    private readonly PreviewConfirmationState _confirmation = new();
    private readonly LongitudinalDrawingPresetStore _presetStore;

    public BeamLongitudinalDrawingViewModel(LongitudinalProjectResources resources,
        IReadOnlyList<BeamSpanInput> sourceSpans, IReadOnlyList<BeamSpanSectionProfile> profiles, BeamChainModel chain,
        LongitudinalDrawingPresetStore? presetStore = null)
    {
        Resources = resources;
        _sourceSpans = sourceSpans;
        _profilesBySourceId = profiles.ToDictionary(profile => profile.SourceId);
        _chain = chain;
        _stations = PlanStations(chain);
        _presetStore = presetStore ?? new LongitudinalDrawingPresetStore();
        Preview = BeamChainPreviewFactory.Create(chain, _stations);
        LoadDefaults();
        foreach (var preset in _presetStore.Load()) Presets.Add(preset);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(CanGenerate)) OnPropertyChanged(nameof(CanGenerate));
        };
    }

    public LongitudinalProjectResources Resources { get; }
    public ObservableCollection<LongitudinalDrawingSetting> Presets { get; } = [];
    public event EventHandler<bool>? CloseRequested;
    public LongitudinalDrawingReviewResult? Result { get; private set; }

    [ObservableProperty] private BeamChainPreviewModel _preview;
    [ObservableProperty] private bool _isPreviewConfirmed;
    [ObservableProperty] private string _validationMessage = "Hãy kiểm tra trục, nhịp, gối và vị trí cắt rồi xác nhận preview.";
    [ObservableProperty] private string _settingName = string.Empty;
    [ObservableProperty] private string _scaleText = "25";
    [ObservableProperty] private string _selectedDimensionType = string.Empty;
    [ObservableProperty] private string _selectedLongitudinalTag = string.Empty;
    [ObservableProperty] private string _selectedStirrupTag = string.Empty;
    [ObservableProperty] private string _selectedDetailComponent = string.Empty;
    [ObservableProperty] private string _selectedCrossBreakLine = string.Empty;
    [ObservableProperty] private string _selectedViewportType = string.Empty;
    [ObservableProperty] private string _selectedCrossViewportType = string.Empty;
    [ObservableProperty] private string _selectedLongitudinalSectionType = string.Empty;
    [ObservableProperty] private string _selectedCrossSectionType = string.Empty;
    [ObservableProperty] private string _selectedSpotElevationType = string.Empty;
    [ObservableProperty] private string _selectedLongitudinalViewTemplate = string.Empty;
    [ObservableProperty] private string _selectedCrossViewTemplate = string.Empty;
    [ObservableProperty] private string _selectedCrossSupportLongitudinalMra = string.Empty;
    [ObservableProperty] private string _selectedCrossSupportStirrupTag = string.Empty;
    [ObservableProperty] private string _selectedCrossMidLongitudinalMra = string.Empty;
    [ObservableProperty] private string _selectedCrossMidStirrupTag = string.Empty;
    [ObservableProperty] private string _selectedCrossSupportReinforceL1Tag = string.Empty;
    [ObservableProperty] private string _selectedCrossMidReinforceL1Tag = string.Empty;
    [ObservableProperty] private string _selectedCrossSupportReinforceL2Mra = string.Empty;
    [ObservableProperty] private string _selectedCrossMidReinforceL2Mra = string.Empty;
    [ObservableProperty] private ProjectSheetOption? _selectedTargetSheet;
    [ObservableProperty] private string _sheetNumber = "KC-001";
    [ObservableProperty] private string _sheetName = "CHI TIẾT THÉP DẦM";
    [ObservableProperty] private string _annotationOffsetText = "200";
    [ObservableProperty] private string _endpointToleranceText = "5";
    [ObservableProperty] private string _alignmentToleranceText = "10";
    [ObservableProperty] private LongitudinalDrawingSetting? _selectedPreset;

    public bool CanGenerate => _confirmation.CanGenerate(TryBuildSetting(out _, out _), Preview.IsValid);

    [RelayCommand]
    private void ConfirmPreview()
    {
        IsPreviewConfirmed = _confirmation.Confirm(Preview.IsValid);
        ValidationMessage = IsPreviewConfirmed
            ? "Preview đã xác nhận. Có thể tạo bản vẽ khi cấu hình hợp lệ."
            : "Preview không hợp lệ.";
        OnPropertyChanged(nameof(CanGenerate));
    }

    [RelayCommand]
    private void ReverseDirection()
    {
        Preview = BeamChainPreviewFactory.Create(_chain, _stations, !Preview.IsReversed);
        _confirmation.Invalidate();
        IsPreviewConfirmed = false;
        ValidationMessage = "Đã đảo hướng. Hãy kiểm tra và xác nhận lại preview.";
        OnPropertyChanged(nameof(CanGenerate));
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (!TryBuildSetting(out var setting, out var errors) || string.IsNullOrWhiteSpace(SettingName))
        {
            ValidationMessage = errors.Count > 0 ? string.Join("\n", errors) : "Nhập tên preset trước khi lưu.";
            return;
        }
        setting = setting with { SettingName = SettingName.Trim() };
        var existing = Presets.ToList();
        var index = existing.FindIndex(item => string.Equals(item.SettingName, setting.SettingName,
            StringComparison.OrdinalIgnoreCase));
        if (index >= 0) existing[index] = setting; else existing.Add(setting);
        Presets.Clear(); foreach (var item in existing) Presets.Add(item);
        try
        {
            _presetStore.Save(existing);
            ValidationMessage = $"Đã lưu preset '{setting.SettingName}'.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            ValidationMessage = $"Không thể lưu preset: {exception.Message}";
        }
    }

    [RelayCommand]
    private void LoadPreset()
    {
        if (SelectedPreset == null) return;
        LoadSetting(SelectedPreset);
        ValidationMessage = $"Đã nạp preset '{SelectedPreset.SettingName}'. Hãy xác nhận lại preview nếu tolerance thay đổi.";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        Presets.Remove(SelectedPreset);
        try { _presetStore.Save(Presets); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            ValidationMessage = $"Không thể cập nhật kho preset: {exception.Message}";
            return;
        }
        SelectedPreset = null;
        ValidationMessage = "Đã xóa preset.";
    }

    [RelayCommand]
    private void ImportPresets()
    {
        var dialog = new OpenFileDialog { Filter = "JSON preset (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        if (!_presetStore.TryImport(dialog.FileName, out var imported, out var error))
        {
            ValidationMessage = $"Không thể nhập preset: {error}";
            return;
        }
        Presets.Clear(); foreach (var item in imported) Presets.Add(item);
        try
        {
            _presetStore.Save(Presets);
            ValidationMessage = $"Đã nhập {Presets.Count} preset.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            ValidationMessage = $"Đã đọc file nhưng không thể lưu preset: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ExportPresets()
    {
        var dialog = new SaveFileDialog { Filter = "JSON preset (*.json)|*.json", FileName = "beam-longitudinal-presets.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _presetStore.Export(dialog.FileName, Presets);
            ValidationMessage = $"Đã xuất {Presets.Count} preset.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            ValidationMessage = $"Không thể xuất preset: {exception.Message}";
        }
    }

    [RelayCommand]
    private void Generate()
    {
        var settingValid = TryBuildSetting(out var setting, out var errors);
        if (!CanGenerate || !settingValid)
        {
            ValidationMessage = errors.Count == 0 ? "Phải xác nhận preview trước khi tạo." : string.Join("\n", errors);
            return;
        }
        Result = new LongitudinalDrawingReviewResult(setting, _chain, _stations, Preview.IsReversed);
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand] private void Cancel() => CloseRequested?.Invoke(this, false);

    private void LoadDefaults()
    {
        SelectedDimensionType = First(Resources.DimensionTypes);
        SelectedLongitudinalTag = First(Resources.RebarTagTypes);
        SelectedStirrupTag = First(Resources.RebarTagTypes);
        SelectedDetailComponent = First(Resources.DetailComponentTypes);
        SelectedCrossBreakLine = PreferredTemplate(Resources.DetailComponentTypes,
            "@BS-Break Line _Nhieu ty le: 1-25", "@BS-Break Line", "Break Line");
        SelectedViewportType = First(Resources.ViewportTypes);
        SelectedCrossViewportType = First(Resources.ViewportTypes);
        SelectedLongitudinalSectionType = First(Resources.SectionTypes);
        SelectedCrossSectionType = First(Resources.SectionTypes);
        SelectedSpotElevationType = First(Resources.SpotElevationTypes);
        SelectedLongitudinalViewTemplate = PreferredTemplate(Resources.ViewTemplates,
            "dọc", "doc", "long");
        SelectedCrossViewTemplate = PreferredTemplate(Resources.ViewTemplates,
            "ngang", "cross");
        SelectedCrossSupportLongitudinalMra = PreferredTemplate(Resources.MultiRebarAnnotationTypes,
            "BS-A2_SL & DK (MCN)-P");
        SelectedCrossMidLongitudinalMra = SelectedCrossSupportLongitudinalMra;
        SelectedCrossSupportStirrupTag = PreferredTemplate(Resources.RebarTagTypes,
            "A2_P_RT_DK&KC_BOT");
        SelectedCrossMidStirrupTag = SelectedCrossSupportStirrupTag;
        SelectedCrossSupportReinforceL1Tag = SelectedCrossSupportStirrupTag;
        SelectedCrossMidReinforceL1Tag = SelectedCrossMidStirrupTag;
        SelectedCrossSupportReinforceL2Mra = SelectedCrossSupportLongitudinalMra;
        SelectedCrossMidReinforceL2Mra = SelectedCrossMidLongitudinalMra;
        if (string.Equals(SelectedCrossViewTemplate, SelectedLongitudinalViewTemplate,
                StringComparison.OrdinalIgnoreCase))
            SelectedCrossViewTemplate = Resources.ViewTemplates
                .FirstOrDefault(item => !string.Equals(item, SelectedLongitudinalViewTemplate,
                    StringComparison.OrdinalIgnoreCase)) ?? SelectedCrossViewTemplate;
        SelectedTargetSheet = Resources.ExistingSheets.FirstOrDefault();
    }

    private void LoadSetting(LongitudinalDrawingSetting value)
    {
        SettingName = value.SettingName ?? string.Empty;
        ScaleText = value.Scale.ToString(); SelectedDimensionType = value.DimensionTypeName;
        SelectedLongitudinalTag = value.LongitudinalRebarTagTypeName; SelectedStirrupTag = value.StirrupTagTypeName;
        SelectedDetailComponent = value.DetailComponentTypeName;
        SelectedCrossBreakLine = value.CrossBreakLineTypeName ?? PreferredTemplate(
            Resources.DetailComponentTypes, "@BS-Break Line _Nhieu ty le: 1-25", "@BS-Break Line", "Break Line");
        SelectedViewportType = value.ViewportTypeName;
        SelectedCrossViewportType = value.CrossViewportTypeName ?? value.ViewportTypeName;
        SelectedLongitudinalSectionType = value.LongitudinalSectionTypeName; SelectedCrossSectionType = value.CrossSectionTypeName;
        SelectedSpotElevationType = value.SpotElevationTypeName;
        SelectedLongitudinalViewTemplate = value.ViewTemplateName ?? string.Empty;
        SelectedCrossViewTemplate = value.CrossViewTemplateName ?? PreferredTemplate(Resources.ViewTemplates,
            "ngang", "cross");
        SelectedCrossSupportLongitudinalMra = value.CrossSupportLongitudinalMraTypeName
            ?? PreferredTemplate(Resources.MultiRebarAnnotationTypes, "BS-A2_SL & DK (MCN)-P");
        SelectedCrossMidLongitudinalMra = value.CrossMidLongitudinalMraTypeName
            ?? SelectedCrossSupportLongitudinalMra;
        SelectedCrossSupportStirrupTag = value.CrossSupportStirrupTagTypeName
            ?? PreferredTemplate(Resources.RebarTagTypes, "A2_P_RT_DK&KC_BOT");
        SelectedCrossMidStirrupTag = value.CrossMidStirrupTagTypeName
            ?? SelectedCrossSupportStirrupTag;
        SelectedCrossSupportReinforceL1Tag = value.CrossSupportReinforceL1TagTypeName
            ?? SelectedCrossSupportStirrupTag;
        SelectedCrossMidReinforceL1Tag = value.CrossMidReinforceL1TagTypeName
            ?? SelectedCrossMidStirrupTag;
        SelectedCrossSupportReinforceL2Mra = value.CrossSupportReinforceL2MraTypeName
            ?? SelectedCrossSupportLongitudinalMra;
        SelectedCrossMidReinforceL2Mra = value.CrossMidReinforceL2MraTypeName
            ?? SelectedCrossMidLongitudinalMra;
        if (string.Equals(SelectedCrossViewTemplate, SelectedLongitudinalViewTemplate,
                StringComparison.OrdinalIgnoreCase))
            SelectedCrossViewTemplate = Resources.ViewTemplates
                .FirstOrDefault(item => !string.Equals(item, SelectedLongitudinalViewTemplate,
                    StringComparison.OrdinalIgnoreCase)) ?? SelectedCrossViewTemplate;
        SelectedTargetSheet = Resources.ExistingSheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Number, value.SheetNumber, StringComparison.OrdinalIgnoreCase));
        SheetNumber = SelectedTargetSheet?.Number ?? value.SheetNumber;
        SheetName = SelectedTargetSheet?.Name ?? value.SheetName;
        AnnotationOffsetText = value.AnnotationOffsetMm.ToString();
        EndpointToleranceText = value.EndpointToleranceMm.ToString(); AlignmentToleranceText = value.AlignmentToleranceMm.ToString();
    }

    partial void OnEndpointToleranceTextChanged(string value) => RebuildPreview();
    partial void OnAlignmentToleranceTextChanged(string value) => RebuildPreview();

    private void RebuildPreview()
    {
        InvalidatePreviewConfirmation();
        if (!double.TryParse(EndpointToleranceText, out var endpointMm) || endpointMm < 0 ||
            !double.TryParse(AlignmentToleranceText, out var alignmentMm) || alignmentMm < 0)
        {
            ValidationMessage = "Tolerance không hợp lệ; preview chưa thể tính lại.";
            return;
        }

        var result = BeamChainBuilder.Build(_sourceSpans,
            new BeamChainTolerance(endpointMm / 304.8, alignmentMm / 304.8, endpointMm / 304.8));
        if (!result.IsValid || result.Model == null)
        {
            Preview = Preview with { Warnings = result.Errors.Select(error => error.Message).ToList() };
            ValidationMessage = string.Join("\n", result.Errors.Select(error => error.Message));
            return;
        }

        _chain = result.Model;
        _stations = PlanStations(_chain);
        Preview = BeamChainPreviewFactory.Create(_chain, _stations, Preview.IsReversed);
        ValidationMessage = "Tolerance đã thay đổi và preview đã được tính lại. Hãy xác nhận lại.";
    }

    private void InvalidatePreviewConfirmation()
    {
        _confirmation.Invalidate();
        IsPreviewConfirmed = false;
        ValidationMessage = "Tolerance đã thay đổi. Hãy kiểm tra và xác nhận lại preview.";
    }

    private bool TryBuildSetting(out LongitudinalDrawingSetting setting, out List<string> errors)
    {
        errors = [];
        var scale = ParseInt(ScaleText, "Tỷ lệ", errors);
        var offset = ParseDouble(AnnotationOffsetText, "Offset annotation", errors);
        var endpoint = ParseDouble(EndpointToleranceText, "Endpoint tolerance", errors);
        var alignment = ParseDouble(AlignmentToleranceText, "Alignment tolerance", errors);
        setting = new LongitudinalDrawingSetting(SettingName, scale, SelectedDimensionType,
            SelectedLongitudinalTag, SelectedStirrupTag, SelectedDetailComponent, SelectedViewportType,
            SelectedLongitudinalSectionType, SelectedCrossSectionType, SelectedSpotElevationType,
            NullIfEmpty(SelectedLongitudinalViewTemplate), NullIfEmpty(SelectedCrossViewTemplate),
            string.Empty, SheetNumber, SheetName,
            offset, endpoint, alignment,
            NullIfEmpty(SelectedCrossSupportLongitudinalMra),
            NullIfEmpty(SelectedCrossSupportStirrupTag),
            NullIfEmpty(SelectedCrossMidLongitudinalMra),
            NullIfEmpty(SelectedCrossMidStirrupTag),
            NullIfEmpty(SelectedCrossViewportType),
            NullIfEmpty(SelectedCrossSupportReinforceL1Tag),
            NullIfEmpty(SelectedCrossMidReinforceL1Tag),
            NullIfEmpty(SelectedCrossSupportReinforceL2Mra),
            NullIfEmpty(SelectedCrossMidReinforceL2Mra),
            NullIfEmpty(SelectedCrossBreakLine));
        errors.AddRange(LongitudinalDrawingSettingValidator.Validate(setting));
        ValidateResource(SelectedDimensionType, Resources.DimensionTypes, "Dimension Type", errors);
        ValidateResource(SelectedLongitudinalTag, Resources.RebarTagTypes, "Tag thép dọc", errors);
        ValidateResource(SelectedStirrupTag, Resources.RebarTagTypes, "Tag thép đai", errors);
        ValidateResource(SelectedDetailComponent, Resources.DetailComponentTypes, "Detail Component", errors);
        ValidateResource(SelectedCrossBreakLine, Resources.DetailComponentTypes,
            "Detail Item nét cắt MC ngang", errors);
        ValidateResource(SelectedViewportType, Resources.ViewportTypes, "Viewport Type", errors);
        ValidateResource(SelectedCrossViewportType, Resources.ViewportTypes,
            "Viewport Type mặt cắt ngang", errors);
        ValidateResource(SelectedLongitudinalSectionType, Resources.SectionTypes, "Section Type dọc", errors);
        ValidateResource(SelectedCrossSectionType, Resources.SectionTypes, "Section Type ngang", errors);
        ValidateResource(SelectedSpotElevationType, Resources.SpotElevationTypes, "Spot Elevation Type", errors);
        ValidateResource(SelectedLongitudinalViewTemplate, Resources.ViewTemplates,
            "View Template mặt cắt dọc", errors);
        ValidateResource(SelectedCrossViewTemplate, Resources.ViewTemplates,
            "View Template mặt cắt ngang", errors);
        ValidateResource(SelectedCrossSupportLongitudinalMra, Resources.MultiRebarAnnotationTypes,
            "MRA thép dọc MC ngang gối", errors);
        ValidateResource(SelectedCrossMidLongitudinalMra, Resources.MultiRebarAnnotationTypes,
            "MRA thép dọc MC ngang nhịp", errors);
        ValidateResource(SelectedCrossSupportStirrupTag, Resources.RebarTagTypes,
            "Tag thép đai MC ngang gối", errors);
        ValidateResource(SelectedCrossMidStirrupTag, Resources.RebarTagTypes,
            "Tag thép đai MC ngang nhịp", errors);
        ValidateResource(SelectedCrossSupportReinforceL1Tag, Resources.RebarTagTypes,
            "Tag thép tăng cường lớp 1 MC ngang gối", errors);
        ValidateResource(SelectedCrossMidReinforceL1Tag, Resources.RebarTagTypes,
            "Tag thép tăng cường lớp 1 MC ngang nhịp", errors);
        ValidateResource(SelectedCrossSupportReinforceL2Mra, Resources.MultiRebarAnnotationTypes,
            "MRA thép tăng cường lớp 2 MC ngang gối", errors);
        ValidateResource(SelectedCrossMidReinforceL2Mra, Resources.MultiRebarAnnotationTypes,
            "MRA thép tăng cường lớp 2 MC ngang nhịp", errors);
        if (string.Equals(SelectedLongitudinalViewTemplate, SelectedCrossViewTemplate,
                StringComparison.OrdinalIgnoreCase))
            errors.Add("Mặt cắt dọc và mặt cắt ngang phải chọn hai View Template khác nhau.");
        if (SelectedTargetSheet == null || !Resources.ExistingSheets.Contains(SelectedTargetSheet))
            errors.Add("Phải chọn một Sheet có sẵn trong project.");
        return errors.Count == 0;
    }

    partial void OnSelectedTargetSheetChanged(ProjectSheetOption? value)
    {
        SheetNumber = value?.Number ?? string.Empty;
        SheetName = value?.Name ?? string.Empty;
    }

    private static int ParseInt(string text, string label, List<string> errors) =>
        int.TryParse(text, out var value) ? value : AddError<int>(errors, $"{label} không hợp lệ.");
    private static double ParseDouble(string text, string label, List<string> errors) =>
        double.TryParse(text, out var value) ? value : AddError<double>(errors, $"{label} không hợp lệ.");
    private static T AddError<T>(List<string> errors, string value) { errors.Add(value); return default!; }
    private static string First(IReadOnlyList<string> values) => values.FirstOrDefault() ?? string.Empty;
    private static string PreferredTemplate(IReadOnlyList<string> values, params string[] tokens) =>
        values.FirstOrDefault(value => tokens.Any(token =>
            value.Contains(token, StringComparison.OrdinalIgnoreCase))) ?? First(values);
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static void ValidateResource(string value, IReadOnlyList<string> options, string label, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value, StringComparer.Ordinal))
            errors.Add($"{label} không còn tồn tại trong project.");
    }

    private IReadOnlyList<SectionStation> PlanStations(BeamChainModel chain)
    {
        var inputsBySourceId = _sourceSpans.ToDictionary(span => span.SourceId);
        var orderedProfiles = chain.Spans.Select(span =>
        {
            var profile = _profilesBySourceId[span.SourceId];
            var input = inputsBySourceId[span.SourceId];
            var followsInputDirection = span.Start.DistanceTo(input.Start) <= span.Start.DistanceTo(input.End);
            return followsInputDirection
                ? profile
                : profile with { LeftSupport = profile.RightSupport, RightSupport = profile.LeftSupport };
        }).ToList();
        return SectionStationPlanner.Plan(chain, orderedProfiles, reduceUniformSpans: true);
    }
}
