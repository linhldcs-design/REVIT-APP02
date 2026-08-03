using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Services;
using RevitAPP.Services.DwgExport;

namespace RevitAPP.ViewModels;

public sealed partial class DwgExportViewModel : ObservableObject
{
    private readonly IDwgOutputPathPicker _pathPicker;

    public DwgExportViewModel(
        DwgExportCatalogResult catalog,
        IDwgOutputPathPicker pathPicker,
        bool autoCadAvailable)
    {
        _pathPicker = pathPicker;
        DwgSetups = catalog.Setups;
        DwgVersions = catalog.Versions;
        PrintSets = catalog.PrintSets;
        OutputPath = string.Empty;
        CoreConsoleStatus = autoCadAvailable
            ? "AutoCAD Automation đã sẵn sàng."
            : "Không tìm thấy AutoCAD đầy đủ trên máy.";
        HasFatalError = !autoCadAvailable;
        SelectedDwgSetup = DwgSetups.FirstOrDefault();
        SelectedDwgVersion = DwgVersions.FirstOrDefault(option =>
            option.Value == SelectedDwgSetup?.DefaultVersion) ?? DwgVersions.FirstOrDefault();
        SelectedPrintSet = PrintSets.FirstOrDefault();
        RebuildPreview();
    }

    public IReadOnlyList<DwgSetupOption> DwgSetups { get; }
    public IReadOnlyList<DwgVersionOption> DwgVersions { get; }
    public IReadOnlyList<PrintSetOption> PrintSets { get; }
    public ObservableCollection<DwgSheetPreviewItem> Sheets { get; } = new();
    public DwgExportRequest? Result { get; private set; }
    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DwgSetupOption? _selectedDwgSetup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DwgVersionOption? _selectedDwgVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private PrintSetOption? _selectedPrintSet;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _outputPath = string.Empty;

    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private string _coreConsoleStatus = string.Empty;
    [ObservableProperty] private bool _hasFatalError;

    partial void OnSelectedPrintSetChanged(PrintSetOption? value) => RebuildPreview();

    partial void OnOutputPathChanged(string value) => RefreshValidation();

    partial void OnSelectedDwgSetupChanged(DwgSetupOption? value)
    {
        if (value is null) return;
        SelectedDwgVersion = DwgVersions.FirstOrDefault(option => option.Value == value.DefaultVersion)
                             ?? SelectedDwgVersion;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var suggested = SelectedPrintSet is null ? "Revit-Export.dwg" : $"{SelectedPrintSet.Name}.dwg";
        var selected = _pathPicker.Pick(suggested, OutputPath);
        if (!string.IsNullOrWhiteSpace(selected)) OutputPath = selected;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!CanConfirm()) return;
        Result = new DwgExportRequest(
            SelectedDwgSetup!.Name,
            SelectedDwgVersion!.Value,
            SelectedPrintSet!,
            Path.GetFullPath(OutputPath));
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private bool CanConfirm()
    {
        return string.IsNullOrEmpty(Validate());
    }

    private string Validate()
    {
        if (HasFatalError) return CoreConsoleStatus;
        if (SelectedDwgSetup is null) return "Hãy chọn Export DWG Setup.";
        if (SelectedDwgVersion is null) return "Hãy chọn phiên bản AutoCAD.";
        if (SelectedPrintSet is null) return "Hãy chọn Print Set.";
        if (Sheets.Count == 0) return "Print Set không có sheet.";
        if (Sheets.Any(sheet => !sheet.IsValid)) return "Print Set có mục không hợp lệ; xem cột Kiểm tra.";
        if (!DwgOutputPathPolicy.TryValidateOutputPath(OutputPath, out var pathError)) return pathError;
        return string.Empty;
    }

    private void RebuildPreview()
    {
        Sheets.Clear();
        if (SelectedPrintSet is not null)
            foreach (var sheet in SelectedPrintSet.Sheets) Sheets.Add(sheet);
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        ValidationMessage = Validate();
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
