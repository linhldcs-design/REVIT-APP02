using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using RevitAPP.Services.CadStructure;

namespace RevitAPP.ViewModels;

internal enum ModelFromCadMode
{
    Grid,
    Column
}

internal sealed record CadModelPreviewData(
    CadStructureTransferPackage Package,
    CadStructureAnalysis Analysis,
    CadGridPreview GridPreview,
    CadStructurePoint2 AnchorPreviewMm,
    IReadOnlyList<CadColumnCandidate> ColumnsPreview);

internal sealed partial class ModelFromCadViewModel : ObservableObject
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 20.0;
    private const double ZoomStep = 1.25;

    private readonly Func<CadModelPreviewData?>? _reselect;
    private bool _suppressItemNotifications;

    public ModelFromCadViewModel(
        CadModelPreviewData data,
        CadColumnProjectOptions options,
        Func<CadModelPreviewData?>? reselect = null)
    {
        Data = data;
        _reselect = reselect;
        GridAxes = new ObservableCollection<CadGridAxisViewModel>(
            data.GridPreview.Axes.Select(axis => new CadGridAxisViewModel(axis)));
        Columns = new ObservableCollection<CadColumnRowViewModel>(
            data.ColumnsPreview.Select(column => new CadColumnRowViewModel(column)));
        Families = new ObservableCollection<CadColumnFamilyOption>(options.Families);
        Levels = new ObservableCollection<CadColumnLevelOption>(options.Levels);

        foreach (var axis in GridAxes) axis.PropertyChanged += OnItemChanged;
        foreach (var column in Columns) column.PropertyChanged += OnItemChanged;

        SelectedFamily = Families.FirstOrDefault(option =>
                             option.DisplayName.Contains("Concrete Column", StringComparison.OrdinalIgnoreCase))
                         ?? Families.FirstOrDefault();
        SelectedBaseLevel = Levels.FirstOrDefault();
        SelectedTopLevel = Levels.Skip(1).FirstOrDefault() ?? Levels.FirstOrDefault();
    }

    public CadModelPreviewData Data { get; private set; }
    public ObservableCollection<CadGridAxisViewModel> GridAxes { get; }
    public ObservableCollection<CadColumnRowViewModel> Columns { get; }
    public ObservableCollection<CadColumnFamilyOption> Families { get; }
    public ObservableCollection<CadColumnLevelOption> Levels { get; }
    public ObservableCollection<string> WidthParameters { get; } = new();
    public ObservableCollection<string> HeightParameters { get; } = new();

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler? RenderRequested;
    public event EventHandler? ReselectCompleted;

    [ObservableProperty]
    private int _activeTabIndex;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private bool _showGridOverlay = true;

    [ObservableProperty]
    private bool _showColumnLabels = true;

    [ObservableProperty]
    private int _previewModeIndex;

    [ObservableProperty]
    private CadColumnFamilyOption? _selectedFamily;

    [ObservableProperty]
    private string? _selectedWidthParameter;

    [ObservableProperty]
    private string? _selectedHeightParameter;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedBaseLevel;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedTopLevel;

    [ObservableProperty]
    private string _baseOffsetText = "0";

    [ObservableProperty]
    private string _topOffsetText = "0";

    [ObservableProperty]
    private string _rotationText = "0";

    [ObservableProperty]
    private CadColumnRowViewModel? _selectedColumn;

    public ModelFromCadMode SelectedMode => ActiveTabIndex == 0
        ? ModelFromCadMode.Grid
        : ModelFromCadMode.Column;

    public IReadOnlyList<CadGridPreviewAxis> SelectedGridAxes =>
        GridAxes.Where(axis => axis.IsSelected)
            .Select(axis => axis.Axis with { SuggestedName = axis.Name })
            .ToArray();

    public IReadOnlyList<CadColumnCandidate> SelectedColumns =>
        Columns.Where(column => column.IsIncluded)
            .Select(column => column.Candidate)
            .ToArray();

    public bool HasCadData => !string.IsNullOrWhiteSpace(Data.Package.SelectionId);

    public string SourceLabel => HasCadData
        ? $"{Data.Package.SourceDrawing} - AutoCAD {Data.Package.AutoCadVersion}"
          + $" - {Data.Package.CreatedUtc.ToLocalTime():HH:mm dd/MM/yyyy}"
        : "Chưa chọn dữ liệu CAD - chọn tab rồi bấm Select From CAD.";

    public string SummaryLabel => SelectedMode == ModelFromCadMode.Grid
        ? $"Chọn {SelectedGridAxes.Count}/{GridAxes.Count} Grid"
        : $"Chọn {SelectedColumns.Count}/{Columns.Count} cột";

    public string CreateButtonText => SelectedMode == ModelFromCadMode.Grid
        ? "Tạo Grid"
        : "Tạo Column";

    public bool CanAccept => SelectedMode == ModelFromCadMode.Grid
        ? SelectedGridAxes.Count > 0 && RotationValid
        : SelectedColumns.Count > 0 && ColumnSettingsValid;

    public bool RotationValid => TryNumber(RotationText, out _);

    public bool ColumnSettingsValid =>
        SelectedFamily is not null
        && !string.IsNullOrWhiteSpace(SelectedWidthParameter)
        && !string.IsNullOrWhiteSpace(SelectedHeightParameter)
        && !string.Equals(SelectedWidthParameter, SelectedHeightParameter,
            StringComparison.OrdinalIgnoreCase)
        && SelectedBaseLevel is not null
        && SelectedTopLevel is not null
        && TryNumber(BaseOffsetText, out _)
        && TryNumber(TopOffsetText, out _)
        && RotationValid
        && (SelectedTopLevel.Elevation * 304.8 + ParseNumber(TopOffsetText))
           > (SelectedBaseLevel.Elevation * 304.8 + ParseNumber(BaseOffsetText));

    public double BaseOffsetMm => ParseNumber(BaseOffsetText);
    public double TopOffsetMm => ParseNumber(TopOffsetText);
    public double RotationDegrees => ParseNumber(RotationText);

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaximumZoom, Zoom * ZoomStep);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinimumZoom, Zoom / ZoomStep);

    [RelayCommand]
    private void ZoomToFit()
    {
        Zoom = 1.0;
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectAllColumns()
    {
        SetColumnSelection(true);
    }

    [RelayCommand]
    private void SelectNoColumns()
    {
        SetColumnSelection(false);
    }

    [RelayCommand]
    private void SelectAllGrids()
    {
        SetGridSelection(_ => true);
    }

    [RelayCommand]
    private void SelectNoGrids()
    {
        SetGridSelection(_ => false);
    }

    [RelayCommand]
    private void SelectGridFamilies()
    {
        SetGridSelection(axis => !axis.IsSkew);
    }

    [RelayCommand]
    private void Reselect()
    {
        var replacement = _reselect?.Invoke();
        if (replacement is null) return;
        Data = replacement;

        foreach (var axis in GridAxes) axis.PropertyChanged -= OnItemChanged;
        foreach (var row in Columns) row.PropertyChanged -= OnItemChanged;
        GridAxes.Clear();
        Columns.Clear();
        foreach (var axis in replacement.GridPreview.Axes)
        {
            var item = new CadGridAxisViewModel(axis);
            item.PropertyChanged += OnItemChanged;
            GridAxes.Add(item);
        }
        foreach (var column in replacement.ColumnsPreview)
        {
            var item = new CadColumnRowViewModel(column);
            item.PropertyChanged += OnItemChanged;
            Columns.Add(item);
        }

        Zoom = 1.0;
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(HasCadData));
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
        ReselectCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    partial void OnSelectedFamilyChanged(CadColumnFamilyOption? value)
    {
        WidthParameters.Clear();
        HeightParameters.Clear();
        if (value is not null)
        {
            foreach (var parameter in value.LengthParameters)
            {
                WidthParameters.Add(parameter);
                HeightParameters.Add(parameter);
            }
            SelectedWidthParameter = FindParameter(value.LengthParameters, "b", "Width", "Chiều rộng")
                                     ?? value.LengthParameters.FirstOrDefault();
            SelectedHeightParameter = FindParameter(value.LengthParameters, "h", "Height", "Depth", "Chiều cao")
                                      ?? value.LengthParameters.Skip(1).FirstOrDefault()
                                      ?? value.LengthParameters.FirstOrDefault();
        }
        NotifyState();
    }

    partial void OnActiveTabIndexChanged(int value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnZoomChanged(double value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowGridOverlayChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowColumnLabelsChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnPreviewModeIndexChanged(int value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedWidthParameterChanged(string? value) => NotifyState();
    partial void OnSelectedHeightParameterChanged(string? value) => NotifyState();
    partial void OnSelectedBaseLevelChanged(CadColumnLevelOption? value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnSelectedTopLevelChanged(CadColumnLevelOption? value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnBaseOffsetTextChanged(string value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnTopOffsetTextChanged(string value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnRotationTextChanged(string value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressItemNotifications) return;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetColumnSelection(bool included)
    {
        _suppressItemNotifications = true;
        try
        {
            foreach (var row in Columns) row.IsIncluded = included;
        }
        finally
        {
            _suppressItemNotifications = false;
        }
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetGridSelection(Func<CadGridAxisViewModel, bool> selector)
    {
        _suppressItemNotifications = true;
        try
        {
            foreach (var axis in GridAxes) axis.IsSelected = selector(axis);
        }
        finally
        {
            _suppressItemNotifications = false;
        }
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(SelectedGridAxes));
        OnPropertyChanged(nameof(SelectedColumns));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(CreateButtonText));
        OnPropertyChanged(nameof(ColumnSettingsValid));
        OnPropertyChanged(nameof(RotationValid));
        OnPropertyChanged(nameof(CanAccept));
        AcceptCommand.NotifyCanExecuteChanged();
    }

    private static string? FindParameter(IEnumerable<string> names, params string[] candidates) =>
        candidates.Select(candidate => names.FirstOrDefault(name =>
                string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);

    private static bool TryNumber(string text, out double value)
    {
        var parsed = double.TryParse(text, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.CurrentCulture, out value)
                     || double.TryParse(text, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out value);
        return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static double ParseNumber(string text) => TryNumber(text, out var value) ? value : 0.0;
}
