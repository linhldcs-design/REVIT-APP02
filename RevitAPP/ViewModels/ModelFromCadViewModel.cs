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
    Column,
    Beam
}

internal sealed record CadModelPreviewData(
    CadStructureTransferPackage Package,
    CadStructureAnalysis Analysis,
    CadGridPreview GridPreview,
    CadStructurePoint2 AnchorPreviewMm,
    IReadOnlyList<CadColumnCandidate> ColumnsPreview);

internal sealed record CadBeamPreviewData(
    CadStructureTransferPackage Package,
    CadBeamAnalysis Analysis);

internal sealed partial class ModelFromCadViewModel : ObservableObject
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 20.0;
    private const double ZoomStep = 1.25;

    private readonly Func<CadModelPreviewData?>? _reselect;
    private readonly Func<CadStructureTransferPackage, CadBeamAnalysisOptions, CadBeamPreviewData?>? _selectBeam;
    private bool _suppressItemNotifications;

    public ModelFromCadViewModel(
        CadModelPreviewData data,
        CadColumnProjectOptions options,
        Func<CadModelPreviewData?>? reselect = null,
        Func<CadStructureTransferPackage, CadBeamAnalysisOptions, CadBeamPreviewData?>? selectBeam = null)
    {
        Data = data;
        _reselect = reselect;
        _selectBeam = selectBeam;
        GridAxes = new ObservableCollection<CadGridAxisViewModel>(
            data.GridPreview.Axes.Select(axis => new CadGridAxisViewModel(axis)));
        Columns = new ObservableCollection<CadColumnRowViewModel>(
            data.ColumnsPreview.Select(column => new CadColumnRowViewModel(column)));
        Families = new ObservableCollection<CadColumnFamilyOption>(options.Families);
        BeamFamilies = new ObservableCollection<CadBeamFamilyOption>(options.BeamFamilies);
        Levels = new ObservableCollection<CadColumnLevelOption>(options.Levels);

        foreach (var axis in GridAxes) axis.PropertyChanged += OnItemChanged;
        foreach (var column in Columns) column.PropertyChanged += OnItemChanged;

        SelectedFamily = Families.FirstOrDefault(option =>
                             option.DisplayName.Contains("Concrete Column", StringComparison.OrdinalIgnoreCase))
                         ?? Families.FirstOrDefault();
        SelectedBaseLevel = Levels.FirstOrDefault();
        SelectedTopLevel = Levels.Skip(1).FirstOrDefault() ?? Levels.FirstOrDefault();
        SelectedBeamFamily = BeamFamilies.FirstOrDefault(option =>
                                 option.DisplayName.Contains("Concrete", StringComparison.OrdinalIgnoreCase))
                             ?? BeamFamilies.FirstOrDefault();
        SelectedBeamLevel = Levels.FirstOrDefault();
    }

    public CadModelPreviewData Data { get; private set; }
    public ObservableCollection<CadGridAxisViewModel> GridAxes { get; }
    public ObservableCollection<CadColumnRowViewModel> Columns { get; }
    public ObservableCollection<CadBeamRowViewModel> Beams { get; } = new();
    public ObservableCollection<CadColumnFamilyOption> Families { get; }
    public ObservableCollection<CadColumnLevelOption> Levels { get; }
    public ObservableCollection<CadBeamFamilyOption> BeamFamilies { get; }
    public ObservableCollection<string> BeamWidthParameters { get; } = new();
    public ObservableCollection<string> BeamHeightParameters { get; } = new();
    public ObservableCollection<string> WidthParameters { get; } = new();
    public ObservableCollection<string> HeightParameters { get; } = new();

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler? RenderRequested;
    public event EventHandler? ReselectCompleted;
    public event EventHandler? FitRequested;

    public CadBeamPreviewData? BeamData { get; private set; }

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

    [ObservableProperty]
    private int _beamPreviewModeIndex;

    [ObservableProperty]
    private bool _showBeamGridOverlay = true;

    [ObservableProperty]
    private bool _showBeamLabels = true;

    [ObservableProperty]
    private CadBeamFamilyOption? _selectedBeamFamily;

    [ObservableProperty]
    private string? _selectedBeamWidthParameter;

    [ObservableProperty]
    private string? _selectedBeamHeightParameter;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedBeamLevel;

    [ObservableProperty]
    private string _beamZOffsetText = "0";

    [ObservableProperty]
    private string _minimumBeamLineText = "500";

    [ObservableProperty]
    private string _gapJoinText = "300";

    [ObservableProperty]
    private string _textSearchDistanceText = "2000";

    [ObservableProperty]
    private string _maximumRunGapText = string.Empty;

    [ObservableProperty]
    private CadBeamRowViewModel? _selectedBeam;

    [ObservableProperty]
    private bool _beamAnalysisDirty;

    public ModelFromCadMode SelectedMode => ActiveTabIndex switch
    {
        0 => ModelFromCadMode.Grid,
        1 => ModelFromCadMode.Column,
        _ => ModelFromCadMode.Beam
    };

    public IReadOnlyList<CadGridPreviewAxis> SelectedGridAxes =>
        GridAxes.Where(axis => axis.IsSelected)
            .Select(axis => axis.Axis with { SuggestedName = axis.Name })
            .ToArray();

    public IReadOnlyList<CadColumnCandidate> SelectedColumns =>
        Columns.Where(column => column.IsIncluded)
            .Select(column => column.Candidate)
            .ToArray();

    public IReadOnlyList<CadBeamCandidate> SelectedBeams =>
        Beams.Where(beam => beam.IsIncluded && beam.IsValid)
            .Select(beam => beam.Candidate)
            .ToArray();

    public bool HasCadData => !string.IsNullOrWhiteSpace(Data.Package.SelectionId);

    public string SourceLabel => HasCadData
        ? $"{Data.Package.SourceDrawing} - AutoCAD {Data.Package.AutoCadVersion}"
          + $" - {Data.Package.CreatedUtc.ToLocalTime():HH:mm dd/MM/yyyy}"
        : "Chưa chọn dữ liệu CAD - chọn tab rồi bấm Select From CAD.";

    public string SummaryLabel => SelectedMode switch
    {
        ModelFromCadMode.Grid => $"Chọn {SelectedGridAxes.Count}/{GridAxes.Count} Grid",
        ModelFromCadMode.Column => $"Chọn {SelectedColumns.Count}/{Columns.Count} cột",
        _ => $"Chọn {SelectedBeams.Count}/{Beams.Count} dầm"
             + (BeamAnalysisDirty ? " — cần Apply/Re-analyze" : string.Empty)
    };

    public string CreateButtonText => SelectedMode switch
    {
        ModelFromCadMode.Grid => "Tạo Grid",
        ModelFromCadMode.Column => "Tạo Column",
        _ => "Tạo Beam"
    };

    public bool CanAccept => SelectedMode switch
    {
        ModelFromCadMode.Grid => SelectedGridAxes.Count > 0 && RotationValid,
        ModelFromCadMode.Column => SelectedColumns.Count > 0 && ColumnSettingsValid,
        _ => SelectedBeams.Count > 0 && BeamSettingsValid
    };

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
    public double BeamZOffsetMm => ParseNumber(BeamZOffsetText);
    public double GapJoinMm => ParseNumber(GapJoinText);
    public double MinimumBeamLineMm => ParseNumber(MinimumBeamLineText);
    public double TextSearchDistanceMm => ParseNumber(TextSearchDistanceText);
    // Blank means no limit, so collinear stretches of the same section stay one beam however far
    // apart they sit. A number splits them once the break exceeds it.
    public double MaximumRunGapMm =>
        TryNumber(MaximumRunGapText, out var value) ? value : double.MaxValue;
    public bool CanSelectBeamLines => HasCadData && SelectedGridAxes.Count > 0 && BeamAnalysisSettingsValid;
    public bool BeamAnalysisSettingsValid =>
        TryNumber(MinimumBeamLineText, out var minLine) && minLine >= 0
        && TryNumber(GapJoinText, out var gap) && gap is >= 0 and <= 2000
        && TryNumber(TextSearchDistanceText, out var textSearch) && textSearch >= 0
        && (string.IsNullOrWhiteSpace(MaximumRunGapText)
            || (TryNumber(MaximumRunGapText, out var runGap) && runGap >= 0));
    public bool BeamSettingsValid =>
        BeamAnalysisSettingsValid
        && !BeamAnalysisDirty
        && SelectedBeamFamily is not null
        && !string.IsNullOrWhiteSpace(SelectedBeamWidthParameter)
        && !string.IsNullOrWhiteSpace(SelectedBeamHeightParameter)
        && !string.Equals(SelectedBeamWidthParameter, SelectedBeamHeightParameter,
            StringComparison.OrdinalIgnoreCase)
        && SelectedBeamLevel is not null
        && TryNumber(BeamZOffsetText, out _)
        && RotationValid
        && Beams.Where(beam => beam.IsIncluded).All(beam => beam.IsValid);

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaximumZoom, Zoom * ZoomStep);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinimumZoom, Zoom / ZoomStep);

    [RelayCommand]
    private void ZoomToFit()
    {
        Zoom = 1.0;
        FitRequested?.Invoke(this, EventArgs.Empty);
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
    private void SelectAllBeams() => SetBeamSelection(true);

    [RelayCommand]
    private void SelectNoBeams() => SetBeamSelection(false);

    [RelayCommand]
    private void ResetSelectedBeamSection()
    {
        SelectedBeam?.ResetToDetected();
    }

    [RelayCommand(CanExecute = nameof(CanSelectBeamLines))]
    private void SelectBeamLines()
    {
        var replacement = _selectBeam?.Invoke(GridPackageForBeam(), BeamOptions());
        if (replacement is null) return;
        SetBeamData(replacement);
    }

    [RelayCommand(CanExecute = nameof(CanApplyBeamAnalysis))]
    private void ApplyBeamAnalysis()
    {
        if (BeamData is null) return;
        var analysis = CadBeamAnalyzer.Analyze(
            BeamData.Package, GridPackageForBeam().Segments, BeamOptions());
        SetBeamData(new CadBeamPreviewData(BeamData.Package, analysis));
    }

    private bool CanApplyBeamAnalysis() => BeamData is not null
                                           && BeamAnalysisSettingsValid
                                           && BeamAnalysisDirty;

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
        foreach (var row in Beams) row.PropertyChanged -= OnItemChanged;
        GridAxes.Clear();
        Columns.Clear();
        Beams.Clear();
        BeamData = null;
        BeamAnalysisDirty = false;
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

    private void SetBeamData(CadBeamPreviewData replacement)
    {
        foreach (var row in Beams) row.PropertyChanged -= OnItemChanged;
        Beams.Clear();
        BeamData = replacement;
        BeamAnalysisDirty = false;
        foreach (var candidate in replacement.Analysis.Beams)
        {
            var row = new CadBeamRowViewModel(candidate);
            row.PropertyChanged += OnItemChanged;
            Beams.Add(row);
        }
        SelectedBeam = Beams.FirstOrDefault();
        Zoom = 1.0;
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

    partial void OnSelectedBeamFamilyChanged(CadBeamFamilyOption? value)
    {
        BeamWidthParameters.Clear();
        BeamHeightParameters.Clear();
        if (value is not null)
        {
            foreach (var parameter in value.LengthParameters)
            {
                BeamWidthParameters.Add(parameter);
                BeamHeightParameters.Add(parameter);
            }
            SelectedBeamWidthParameter = FindParameter(value.LengthParameters, "b", "Width", "Chiều rộng")
                                         ?? value.LengthParameters.FirstOrDefault();
            SelectedBeamHeightParameter = FindParameter(value.LengthParameters, "h", "Height", "Depth", "Chiều cao")
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
    partial void OnBeamPreviewModeIndexChanged(int value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowBeamGridOverlayChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowBeamLabelsChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedWidthParameterChanged(string? value) => NotifyState();
    partial void OnSelectedHeightParameterChanged(string? value) => NotifyState();
    partial void OnSelectedBeamWidthParameterChanged(string? value) => NotifyState();
    partial void OnSelectedBeamHeightParameterChanged(string? value) => NotifyState();
    partial void OnSelectedBeamLevelChanged(CadColumnLevelOption? value) => NotifyState();
    partial void OnSelectedBeamChanged(CadBeamRowViewModel? value) => RenderRequested?.Invoke(this, EventArgs.Empty);
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
    partial void OnBeamZOffsetTextChanged(string value) => NotifyState();
    partial void OnMinimumBeamLineTextChanged(string value) => NotifyBeamAnalysisSettings();
    partial void OnGapJoinTextChanged(string value) => NotifyBeamAnalysisSettings();
    partial void OnTextSearchDistanceTextChanged(string value) => NotifyBeamAnalysisSettings();
    partial void OnMaximumRunGapTextChanged(string value) => NotifyBeamAnalysisSettings();
    partial void OnBeamAnalysisDirtyChanged(bool value) => NotifyState();

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

    private void SetBeamSelection(bool included)
    {
        _suppressItemNotifications = true;
        try
        {
            foreach (var row in Beams) row.IsIncluded = included && row.IsValid;
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
        OnPropertyChanged(nameof(SelectedBeams));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(CreateButtonText));
        OnPropertyChanged(nameof(ColumnSettingsValid));
        OnPropertyChanged(nameof(BeamSettingsValid));
        OnPropertyChanged(nameof(BeamAnalysisSettingsValid));
        OnPropertyChanged(nameof(BeamAnalysisDirty));
        OnPropertyChanged(nameof(CanSelectBeamLines));
        OnPropertyChanged(nameof(RotationValid));
        OnPropertyChanged(nameof(CanAccept));
        AcceptCommand.NotifyCanExecuteChanged();
        SelectBeamLinesCommand.NotifyCanExecuteChanged();
        ApplyBeamAnalysisCommand.NotifyCanExecuteChanged();
    }

    private void NotifyBeamAnalysisSettings()
    {
        if (BeamData is not null) BeamAnalysisDirty = true;
        NotifyState();
    }

    private CadBeamAnalysisOptions BeamOptions() => new(
        MinimumLineLengthMm: MinimumBeamLineMm,
        GapJoinToleranceMm: GapJoinMm,
        TextSearchDistanceMm: TextSearchDistanceMm,
        MaximumRunGapMm: MaximumRunGapMm);

    private CadStructureTransferPackage GridPackageForBeam()
    {
        var selectedIds = GridAxes.Where(axis => axis.IsSelected)
            .Select(axis => axis.Axis.Id).ToHashSet();
        return Data.Package with
        {
            Segments = Data.Package.Segments
                .Where(segment => selectedIds.Contains(segment.Id))
                .ToArray()
        };
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
