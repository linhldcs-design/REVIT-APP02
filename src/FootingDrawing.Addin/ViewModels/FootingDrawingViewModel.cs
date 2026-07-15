using System.Collections.ObjectModel;
using FootingDrawing.Addin.Services;
using FootingDrawing.Core.Models;
using FootingDrawing.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FootingDrawing.Addin.ViewModels;

/// <summary>
///     ViewModel dialog Bản Vẽ Móng: quản lý danh sách setting (CRUD + import/export JSON) và bind
///     từng trường cấu hình. Nguyên tắc: user tùy chọn linh hoạt — combobox liệt kê đầy đủ, checkbox
///     bật/tắt từng thành phần, KHÔNG auto.
/// </summary>
public sealed partial class FootingDrawingViewModel : ObservableObject
{
    private readonly ISettingStore _store;

    public FootingDrawingViewModel(ProjectResources resources, ISettingStore store)
    {
        Resources = resources;
        _store = store;

        Settings = new ObservableCollection<FootingDrawingSetting>(store.Load());
        LoadIntoFields(SettingFactory.CreateDefault());
        _selectedSetting = Settings.FirstOrDefault();
        if (_selectedSetting != null) LoadIntoFields(_selectedSetting);
    }

    public ProjectResources Resources { get; }
    public ObservableCollection<FootingDrawingSetting> Settings { get; }

    /// <summary>Kết quả khi user bấm OK — null nếu huỷ.</summary>
    public FootingDrawingSetting? Result { get; private set; }

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler? PickSheetRequested;

    [ObservableProperty] private FootingDrawingSetting? _selectedSetting;
    [ObservableProperty] private string _validationMessage = string.Empty;

    // ===== Trường cấu hình (flat để bind XAML) =====
    [ObservableProperty] private string _settingName = string.Empty;

    // Combobox chọn type
    [ObservableProperty] private ComboOption? _dimensionType;
    [ObservableProperty] private ComboOption? _footingRebarTag;
    [ObservableProperty] private ComboOption? _bendingDetailType;
    [ObservableProperty] private ComboOption? _parentPlanView;
    [ObservableProperty] private ComboOption? _calloutType;
    [ObservableProperty] private ComboOption? _viewTemplate;
    [ObservableProperty] private ComboOption? _viewportType;

    // Sheet đích
    [ObservableProperty] private string _sheetNumber = string.Empty;
    [ObservableProperty] private string _sheetName = string.Empty;

    // Checkbox tùy chọn linh hoạt (độc lập)
    [ObservableProperty] private bool _footingTagEnabled = true;
    [ObservableProperty] private bool _columnTagEnabled = true;
    [ObservableProperty] private bool _bendingDetailEnabled = true;
    [ObservableProperty] private bool _titleEnabled = true;
    [ObservableProperty] private bool _dimOverallEnabled = true;
    [ObservableProperty] private bool _dimBaseEnabled = true;
    [ObservableProperty] private bool _dimPedestalEnabled = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScaleLabel))] private int _scale = 25;
    public string ScaleLabel => $"TL 1:{Scale}";

    partial void OnSelectedSettingChanged(FootingDrawingSetting? value)
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
            ValidationMessage = $"Đã tồn tại setting '{setting.Name}'. Dùng Update để ghi đè.";
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
        if (index < 0) { ValidationMessage = "Chưa có setting tên này. Dùng Add."; return; }
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
    private void ExportSetting()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Footing Drawing settings (*.json)|*.json",
            FileName = "footing-drawing-settings.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _store.Export(Settings, dialog.FileName);
            ValidationMessage = $"Đã export {Settings.Count} setting.";
        }
        catch (Exception ex)
        {
            ValidationMessage = "Export lỗi: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ImportSetting()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Footing Drawing settings (*.json)|*.json"
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

    [RelayCommand]
    private void PickSheet() => PickSheetRequested?.Invoke(this, EventArgs.Empty);

    public void ApplyPickedSheet(SheetOption sheet)
    {
        SheetNumber = sheet.Number;
        SheetName = sheet.Name;
    }

    // ===== Mapping field <-> model =====
    private FootingDrawingSetting BuildFromFields() => new()
    {
        Name = SettingName.Trim(),
        DimensionTypeName = DimensionType?.Name,
        FootingRebarTagTypeName = FootingRebarTag?.Name,
        BendingDetailTypeName = BendingDetailType?.Name,
        ParentPlanViewName = ParentPlanView?.Name,
        CalloutTypeName = CalloutType?.Name,
        ViewTemplateName = ViewTemplate?.Name,
        ViewportTypeName = ViewportType?.Name,
        SheetNumber = SheetNumber,
        SheetName = SheetName,
        FootingTagEnabled = FootingTagEnabled,
        ColumnTagEnabled = ColumnTagEnabled,
        BendingDetailEnabled = BendingDetailEnabled,
        TitleEnabled = TitleEnabled,
        DimOverallEnabled = DimOverallEnabled,
        DimBaseEnabled = DimBaseEnabled,
        DimPedestalEnabled = DimPedestalEnabled,
        Scale = Scale
    };

    private void LoadIntoFields(FootingDrawingSetting s)
    {
        SettingName = s.Name;
        DimensionType = Find(Resources.DimensionTypes, s.DimensionTypeName);
        FootingRebarTag = Find(Resources.RebarTagTypes, s.FootingRebarTagTypeName);
        BendingDetailType = Find(Resources.BendingDetailTypes, s.BendingDetailTypeName);
        ParentPlanView = Find(Resources.ParentPlanViews, s.ParentPlanViewName);
        CalloutType = Find(Resources.CalloutTypes, s.CalloutTypeName);
        ViewTemplate = Find(Resources.ViewTemplates, s.ViewTemplateName);
        ViewportType = Find(Resources.ViewportTypes, s.ViewportTypeName);
        SheetNumber = s.SheetNumber ?? string.Empty;
        SheetName = s.SheetName ?? string.Empty;
        FootingTagEnabled = s.FootingTagEnabled;
        ColumnTagEnabled = s.ColumnTagEnabled;
        BendingDetailEnabled = s.BendingDetailEnabled;
        TitleEnabled = s.TitleEnabled;
        DimOverallEnabled = s.DimOverallEnabled;
        DimBaseEnabled = s.DimBaseEnabled;
        DimPedestalEnabled = s.DimPedestalEnabled;
        Scale = s.Scale;
    }

    private static ComboOption? Find(IReadOnlyList<ComboOption> options, string? name)
        => name == null ? null : options.FirstOrDefault(o => o.Name == name);

    private bool Validate(FootingDrawingSetting setting)
    {
        var result = SettingValidator.Validate(setting);
        ValidationMessage = result.IsValid ? string.Empty : string.Join("\n", result.Errors);
        return result.IsValid;
    }

    private void Persist() => _store.Save(Settings);
}
