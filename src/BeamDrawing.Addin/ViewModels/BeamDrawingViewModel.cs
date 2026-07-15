using System.Collections.ObjectModel;
using BeamDrawing.Addin.Services;
using BeamDrawing.Core.Models;
using BeamDrawing.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeamDrawing.Addin.ViewModels;

/// <summary>
///     ViewModel chính của dialog Beam Drawing. Quản lý danh sách setting (CRUD + import/export)
///     và bind từng trường cấu hình. Combobox source lấy từ <see cref="ProjectResources"/>
///     (rỗng ở Phase 3, nạp thật ở Phase 4). Persistence qua <see cref="ISettingStore"/>.
/// </summary>
public sealed partial class BeamDrawingViewModel : ObservableObject
{
    private readonly ISettingStore _store;

    public BeamDrawingViewModel(ProjectResources resources, ISettingStore store)
    {
        Resources = resources;
        _store = store;

        Settings = new ObservableCollection<BeamDrawingSetting>(store.Load());
        LoadIntoFields(SettingFactory.CreateDefault());
        _selectedSetting = Settings.FirstOrDefault();
        if (_selectedSetting != null) LoadIntoFields(_selectedSetting);
    }

    public ProjectResources Resources { get; }
    public ObservableCollection<BeamDrawingSetting> Settings { get; }

    /// <summary>Kết quả khi user bấm OK — null nếu huỷ.</summary>
    public BeamDrawingSetting? Result { get; private set; }

    public event EventHandler<bool>? CloseRequested;

    /// <summary>Yêu cầu View mở dialog chọn sheet; View gọi lại <see cref="ApplyPickedSheet"/>.</summary>
    public event EventHandler? PickSheetRequested;

    [ObservableProperty] private BeamDrawingSetting? _selectedSetting;
    [ObservableProperty] private string _validationMessage = string.Empty;

    // ===== Trường cấu hình (flat để bind XAML) =====
    [ObservableProperty] private string _settingName = string.Empty;

    // Tag sectional
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(T1Label))] private ComboOption? _t1Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(T2Label))] private ComboOption? _t2Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Item4Label))] private ComboOption? _item4Tag;
    [ObservableProperty] private bool _rebarBreakSymbol = true;

    // Tag cross section
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D0Label))] private ComboOption? _d0Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D1Label))] private ComboOption? _d1Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D2Label))] private ComboOption? _d2Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D3Label))] private ComboOption? _d3Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D4Label))] private ComboOption? _d4Tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(D5Label))] private ComboOption? _d5Tag;

    // ===== Nhãn hiển thị trên diagram (Phase 8) =====
    public string T1Label => T1Tag?.Name ?? "T1";
    public string T2Label => T2Tag?.Name ?? "T2";
    public string Item4Label => Item4Tag?.Name ?? "4";
    public string D0Label => D0Tag?.Name ?? "D0";
    public string D1Label => D1Tag?.Name ?? "D1";
    public string D2Label => D2Tag?.Name ?? "D2";
    public string D3Label => D3Tag?.Name ?? "D3";
    public string D4Label => D4Tag?.Name ?? "D4";
    public string D5Label => D5Tag?.Name ?? "D5";

    // View config
    [ObservableProperty] private ComboOption? _sectionalViewTemplate;
    [ObservableProperty] private ComboOption? _crossViewTemplate;
    [ObservableProperty] private ComboOption? _sectionalViewport;
    [ObservableProperty] private ComboOption? _crossViewport;
    [ObservableProperty] private ComboOption? _sectionalSectionType;
    [ObservableProperty] private ComboOption? _crossSectionType;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SectionalScaleLabel))] private int _sectionalScale = 25;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CrossScaleLabel))] private int _crossScale = 25;

    public string SectionalScaleLabel => $"TL 1:{SectionalScale}";
    public string CrossScaleLabel => $"TL 1:{CrossScale}";

    // Annotation
    [ObservableProperty] private bool _spotElevationEnabled = true;
    [ObservableProperty] private ComboOption? _spotElevationType;
    [ObservableProperty] private bool _dimensionEnabled = true;
    [ObservableProperty] private ComboOption? _sectionalDimType;
    [ObservableProperty] private ComboOption? _crossDimType;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpacingFactorLabel))] private int _dimSpacingFactor = 6;
    [ObservableProperty] private ComboOption? _breakLineFamily;

    public string SpacingFactorLabel => $"@{DimSpacingFactor}";

    // Sheet
    [ObservableProperty] private ComboOption? _titleBlock;
    [ObservableProperty] private string _sheetNumber = string.Empty;
    [ObservableProperty] private string _sheetName = string.Empty;

    // Bổ sung khớp bản thương mại
    [ObservableProperty] private ComboOption? _breakLineSymbol;
    [ObservableProperty] private int _spotElevationValue;
    [ObservableProperty] private string _beamDetailViewName = "Beam Detail";
    [ObservableProperty] private string _crossViewName = string.Empty;

    // Checkbox bật/tắt TỪNG NHÓM (header) — độc lập, khớp các ô tick trong bản thương mại
    [ObservableProperty] private bool _rebarTagSectionalEnabled = true;
    [ObservableProperty] private bool _rebarTagCrossEnabled = true;
    [ObservableProperty] private bool _breakLineEnabled = true;

    // Flags
    [ObservableProperty] private bool _longSection;
    [ObservableProperty] private bool _crossSection = true;
    [ObservableProperty] private bool _crossSectionForMultiBeam;
    [ObservableProperty] private bool _pickPillowToDim;
    [ObservableProperty] private bool _createView3D;

    partial void OnSelectedSettingChanged(BeamDrawingSetting? value)
    {
        if (value != null) LoadIntoFields(value);
    }

    // ===== CRUD commands =====
    [RelayCommand]
    private void AddSetting()
    {
        var setting = BuildFromFields();
        if (!Validate(setting)) return;
        if (Settings.Any(s => s.Name == setting.Name))
        {
            ValidationMessage = $"Đã tồn tại setting tên '{setting.Name}'. Dùng Update để ghi đè.";
            return;
        }
        Settings.Add(setting);
        SelectedSetting = setting;
        Persist();
    }

    [RelayCommand]
    private void UpdateSetting()
    {
        var setting = BuildFromFields();
        if (!Validate(setting)) return;
        var index = Settings.ToList().FindIndex(s => s.Name == setting.Name);
        if (index < 0) { ValidationMessage = "Chưa có setting tên này để Update. Dùng Add."; return; }
        Settings[index] = setting;
        SelectedSetting = setting;
        Persist();
    }

    [RelayCommand]
    private void DeleteSetting()
    {
        if (SelectedSetting == null) return;
        Settings.Remove(SelectedSetting);
        SelectedSetting = Settings.FirstOrDefault();
        Persist();
    }

    [RelayCommand]
    private void LoadSetting()
    {
        if (SelectedSetting != null) LoadIntoFields(SelectedSetting);
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedSetting == null) return;
        var i = Settings.IndexOf(SelectedSetting);
        if (i <= 0) return;
        Settings.Move(i, i - 1);
        Persist();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedSetting == null) return;
        var i = Settings.IndexOf(SelectedSetting);
        if (i < 0 || i >= Settings.Count - 1) return;
        Settings.Move(i, i + 1);
        Persist();
    }

    [RelayCommand]
    private void ExportSetting()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Beam Drawing settings (*.json)|*.json",
            FileName = "beam-drawing-settings.json"
        };
        if (dialog.ShowDialog() != true) return;
        _store.Export(Settings, dialog.FileName);
        ValidationMessage = $"Đã export {Settings.Count} setting.";
    }

    [RelayCommand]
    private void ImportSetting()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Beam Drawing settings (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = _store.Import(dialog.FileName);
            foreach (var setting in imported)
            {
                var index = Settings.ToList().FindIndex(s => s.Name == setting.Name);
                if (index >= 0) Settings[index] = setting;
                else Settings.Add(setting);
            }
            Persist();
            ValidationMessage = $"Đã import {imported.Count} setting.";
        }
        catch (Exception ex)
        {
            ValidationMessage = "Import lỗi: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Ok()
    {
        var setting = BuildFromFields();
        if (!Validate(setting)) return;
        Result = setting;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Nút 🔍 cạnh Sheet Number — yêu cầu View mở dialog chọn sheet có sẵn.</summary>
    [RelayCommand]
    private void PickSheet() => PickSheetRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>View gọi sau khi user chọn sheet trong dialog — điền số + tên sheet.</summary>
    public void ApplyPickedSheet(SheetOption sheet)
    {
        SheetNumber = sheet.Number;
        SheetName = sheet.Name;
    }

    // ===== Mapping field <-> model =====
    private BeamDrawingSetting BuildFromFields() => new()
    {
        Name = SettingName.Trim(),
        TagMapping = new RebarTagMapping
        {
            SectionalEnabled = RebarTagSectionalEnabled,
            CrossEnabled = RebarTagCrossEnabled,
            T1TagTypeName = T1Tag?.Name,
            T2TagTypeName = T2Tag?.Name,
            Item4TagTypeName = Item4Tag?.Name,
            RebarBreakSymbol = RebarBreakSymbol,
            D0TagTypeName = D0Tag?.Name,
            D1TagTypeName = D1Tag?.Name,
            D2TagTypeName = D2Tag?.Name,
            D3TagTypeName = D3Tag?.Name,
            D4TagTypeName = D4Tag?.Name,
            D5TagTypeName = D5Tag?.Name
        },
        Sectional = new ViewConfig
        {
            ViewTemplateName = SectionalViewTemplate?.Name,
            ViewportTypeName = SectionalViewport?.Name,
            SectionTypeName = SectionalSectionType?.Name,
            Scale = SectionalScale
        },
        CrossSection = new ViewConfig
        {
            ViewTemplateName = CrossViewTemplate?.Name,
            ViewportTypeName = CrossViewport?.Name,
            SectionTypeName = CrossSectionType?.Name,
            Scale = CrossScale
        },
        SpotElevation = new SpotElevationConfig
        {
            Enabled = SpotElevationEnabled,
            SpotTypeName = SpotElevationType?.Name,
            Offset = SpotElevationValue
        },
        Dimension = new DimensionConfig
        {
            Enabled = DimensionEnabled,
            SectionalDimTypeName = SectionalDimType?.Name,
            CrossSectionDimTypeName = CrossDimType?.Name,
            SpacingFactor = DimSpacingFactor
        },
        BreakLine = new BreakLineConfig
        {
            Enabled = BreakLineEnabled,
            BreakLineFamilyName = BreakLineFamily?.Name
        },
        TitleBlockName = TitleBlock?.Name,
        SheetNumber = SheetNumber,
        SheetName = SheetName,
        Flags = new BeamDrawingFlags
        {
            LongSection = LongSection,
            CrossSection = CrossSection,
            CrossSectionForMultiBeam = CrossSectionForMultiBeam,
            PickPillowToDim = PickPillowToDim,
            CreateView3D = CreateView3D
        }
    };

    private void LoadIntoFields(BeamDrawingSetting s)
    {
        SettingName = s.Name;
        RebarTagSectionalEnabled = s.TagMapping.SectionalEnabled;
        RebarTagCrossEnabled = s.TagMapping.CrossEnabled;
        T1Tag = Find(Resources.RebarTagTypes, s.TagMapping.T1TagTypeName);
        T2Tag = Find(Resources.RebarTagTypes, s.TagMapping.T2TagTypeName);
        Item4Tag = Find(Resources.RebarTagTypes, s.TagMapping.Item4TagTypeName);
        RebarBreakSymbol = s.TagMapping.RebarBreakSymbol;
        D0Tag = Find(Resources.RebarTagTypes, s.TagMapping.D0TagTypeName);
        D1Tag = Find(Resources.RebarTagTypes, s.TagMapping.D1TagTypeName);
        D2Tag = Find(Resources.RebarTagTypes, s.TagMapping.D2TagTypeName);
        D3Tag = Find(Resources.RebarTagTypes, s.TagMapping.D3TagTypeName);
        D4Tag = Find(Resources.RebarTagTypes, s.TagMapping.D4TagTypeName);
        D5Tag = Find(Resources.RebarTagTypes, s.TagMapping.D5TagTypeName);
        SectionalViewTemplate = Find(Resources.ViewTemplates, s.Sectional.ViewTemplateName);
        CrossViewTemplate = Find(Resources.ViewTemplates, s.CrossSection.ViewTemplateName);
        SectionalViewport = Find(Resources.ViewportTypes, s.Sectional.ViewportTypeName);
        CrossViewport = Find(Resources.ViewportTypes, s.CrossSection.ViewportTypeName);
        SectionalSectionType = Find(Resources.SectionTypes, s.Sectional.SectionTypeName);
        CrossSectionType = Find(Resources.SectionTypes, s.CrossSection.SectionTypeName);
        SectionalScale = s.Sectional.Scale;
        CrossScale = s.CrossSection.Scale;
        SpotElevationEnabled = s.SpotElevation.Enabled;
        SpotElevationType = Find(Resources.SpotElevationTypes, s.SpotElevation.SpotTypeName);
        SpotElevationValue = (int)s.SpotElevation.Offset;
        DimensionEnabled = s.Dimension.Enabled;
        SectionalDimType = Find(Resources.DimensionTypes, s.Dimension.SectionalDimTypeName);
        CrossDimType = Find(Resources.DimensionTypes, s.Dimension.CrossSectionDimTypeName);
        DimSpacingFactor = s.Dimension.SpacingFactor;
        BreakLineEnabled = s.BreakLine.Enabled;
        BreakLineFamily = Find(Resources.BreakLineFamilies, s.BreakLine.BreakLineFamilyName);
        TitleBlock = Find(Resources.TitleBlocks, s.TitleBlockName);
        SheetNumber = s.SheetNumber ?? string.Empty;
        SheetName = s.SheetName ?? string.Empty;
        LongSection = s.Flags.LongSection;
        CrossSection = s.Flags.CrossSection;
        CrossSectionForMultiBeam = s.Flags.CrossSectionForMultiBeam;
        PickPillowToDim = s.Flags.PickPillowToDim;
        CreateView3D = s.Flags.CreateView3D;
    }

    private static ComboOption? Find(IReadOnlyList<ComboOption> options, string? name)
        => name == null ? null : options.FirstOrDefault(o => o.Name == name);

    private bool Validate(BeamDrawingSetting setting)
    {
        var result = SettingValidator.Validate(setting);
        ValidationMessage = result.IsValid ? string.Empty : string.Join("\n", result.Errors);
        return result.IsValid;
    }

    private void Persist() => _store.Save(Settings);
}
