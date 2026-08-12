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
    Beam,
    Slab,
    Wall
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

internal sealed record CadWallPreviewData(
    CadStructureTransferPackage Package,
    CadWallAnalysis Analysis);

internal sealed record CadSlabPreviewData(
    CadStructureTransferPackage Package,
    IReadOnlyList<CadHatchRegion> Hatches,
    CadSlabAnalysis Analysis)
{
    /// <summary>
    /// Marks the user picked to say which bays stay open, kept in drawing units beside the scan so
    /// re-analysing after a settings change does not need them picked again.
    /// </summary>
    public IReadOnlyList<CadStructureSegment> OpeningOutlines { get; init; } =
        Array.Empty<CadStructureSegment>();
}

internal sealed partial class ModelFromCadViewModel : ObservableObject
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 20.0;
    private const double ZoomStep = 1.25;

    private readonly Func<CadModelPreviewData?>? _reselect;
    private readonly Func<CadStructureTransferPackage, CadBeamAnalysisOptions, CadBeamPreviewData?>? _selectBeam;
    private readonly Func<CadStructureTransferPackage, CadSlabAnalysisOptions, CadSlabPreviewData?>? _selectSlab;
    private readonly Func<CadStructureTransferPackage, IReadOnlyList<CadStructureSegment>?>? _selectOpeningOutlines;
    private readonly Func<CadStructureTransferPackage, IReadOnlyList<CadHatchRegion>?>? _selectHatchRegions;
    private readonly Func<CadStructureTransferPackage, CadWallAnalysisOptions, CadWallPreviewData?>? _selectWall;
    private bool _suppressItemNotifications;

    public ModelFromCadViewModel(
        CadModelPreviewData data,
        CadColumnProjectOptions options,
        Func<CadModelPreviewData?>? reselect = null,
        Func<CadStructureTransferPackage, CadBeamAnalysisOptions, CadBeamPreviewData?>? selectBeam = null,
        Func<CadStructureTransferPackage, CadSlabAnalysisOptions, CadSlabPreviewData?>? selectSlab = null,
        Func<CadStructureTransferPackage, IReadOnlyList<CadStructureSegment>?>? selectOpeningOutlines = null,
        Func<CadStructureTransferPackage, IReadOnlyList<CadHatchRegion>?>? selectHatchRegions = null,
        Func<CadStructureTransferPackage, CadWallAnalysisOptions, CadWallPreviewData?>? selectWall = null)
    {
        Data = data;
        _reselect = reselect;
        _selectBeam = selectBeam;
        _selectSlab = selectSlab;
        _selectOpeningOutlines = selectOpeningOutlines;
        _selectHatchRegions = selectHatchRegions;
        _selectWall = selectWall;
        GridAxes = new ObservableCollection<CadGridAxisViewModel>(
            data.GridPreview.Axes.Select(axis => new CadGridAxisViewModel(axis)));
        Columns = new ObservableCollection<CadColumnRowViewModel>(
            data.ColumnsPreview.Select(column => new CadColumnRowViewModel(column)));
        Families = new ObservableCollection<CadColumnFamilyOption>(options.Families);
        BeamFamilies = new ObservableCollection<CadBeamFamilyOption>(options.BeamFamilies);
        SlabTypes = new ObservableCollection<CadSlabTypeOption>(options.SlabTypes);
        Levels = new ObservableCollection<CadColumnLevelOption>(options.Levels);
        Slabs = new ObservableCollection<CadSlabRowViewModel>();
        Walls = new ObservableCollection<CadWallRowViewModel>();
        WallLayers = new ObservableCollection<CadLayerRowViewModel>();
        WallTypes = new ObservableCollection<CadWallTypeOption>(options.WallTypes);

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
        SelectedSlabType = SlabTypes.FirstOrDefault(option =>
                               option.Name.Contains("Concrete", StringComparison.OrdinalIgnoreCase))
                           ?? SlabTypes.FirstOrDefault();
        SelectedSlabLevel = Levels.FirstOrDefault();
        SelectedWallType = WallTypes.FirstOrDefault();
        SelectedWallBaseLevel = Levels.FirstOrDefault();
        SelectedWallTopLevel = Levels.Skip(1).FirstOrDefault() ?? Levels.FirstOrDefault();
    }

    public CadModelPreviewData Data { get; private set; }
    public ObservableCollection<CadGridAxisViewModel> GridAxes { get; }
    public ObservableCollection<CadColumnRowViewModel> Columns { get; }
    public ObservableCollection<CadBeamRowViewModel> Beams { get; } = new();
    public ObservableCollection<CadColumnFamilyOption> Families { get; }
    public ObservableCollection<CadColumnLevelOption> Levels { get; }
    public ObservableCollection<CadBeamFamilyOption> BeamFamilies { get; }
    public ObservableCollection<CadSlabTypeOption> SlabTypes { get; }
    public ObservableCollection<CadSlabRowViewModel> Slabs { get; }
    public ObservableCollection<CadWallRowViewModel> Walls { get; }
    public ObservableCollection<CadLayerRowViewModel> WallLayers { get; }
    public ObservableCollection<CadWallTypeOption> WallTypes { get; }
    public ObservableCollection<string> BeamWidthParameters { get; } = new();
    public ObservableCollection<string> BeamHeightParameters { get; } = new();
    public ObservableCollection<string> WidthParameters { get; } = new();
    public ObservableCollection<string> HeightParameters { get; } = new();

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler? RenderRequested;
    public event EventHandler? ReselectCompleted;
    public event EventHandler? FitRequested;

    public CadBeamPreviewData? BeamData { get; private set; }
    public CadSlabPreviewData? SlabData { get; private set; }
    public CadWallPreviewData? WallData { get; private set; }

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
    private CadSlabTypeOption? _selectedSlabType;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedSlabLevel;

    [ObservableProperty]
    private CadWallTypeOption? _selectedWallType;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedWallBaseLevel;

    [ObservableProperty]
    private CadColumnLevelOption? _selectedWallTopLevel;

    // What the user says a wall can be. Both are typed before the scan, because they decide what
    // is picked up at all: a drawing carries beams drawn exactly like walls.
    [ObservableProperty]
    private string _minimumWallThicknessText = "100";

    [ObservableProperty]
    private string _maximumWallThicknessText = "400";

    [ObservableProperty]
    private string _minimumWallLengthText = "300";

    [ObservableProperty]
    private string _wallLengthRatioText = "3";

    [ObservableProperty]
    private string _wallBaseOffsetText = "0";

    /// <summary>
    /// Whether the layers or the limits have changed since the walls were last read, so the
    /// review no longer shows what the settings would give.
    /// </summary>
    [ObservableProperty]
    private bool _wallAnalysisDirty;

    [ObservableProperty]
    private bool _showWallLabels = true;

    [ObservableProperty]
    private bool _showWallGridOverlay = true;

    [ObservableProperty]
    private int _wallPreviewModeIndex;

    [ObservableProperty]
    private CadWallRowViewModel? _selectedWall;

    [ObservableProperty]
    private string _vertexSnapText = "20";

    [ObservableProperty]
    private string _minimumSlabLineText = "200";

    [ObservableProperty]
    private string _minimumSlabAreaText = "1";

    [ObservableProperty]
    private string _minimumOpeningAreaText = "0.05";

    [ObservableProperty]
    private string _beamStripWidthText = "500";

    [ObservableProperty]
    private string _hatchJoinDistanceText = "500";

    [ObservableProperty]
    private string _defaultSlabThicknessText = "100";

    [ObservableProperty]
    private string _defaultSlabOffsetText = "0";

    [ObservableProperty]
    private string _loweredSlabOffsetText = "-50";

    [ObservableProperty]
    private bool _overrideSlabThickness;

    [ObservableProperty]
    private bool _overrideSlabElevation;

    [ObservableProperty]
    private CadSlabRowViewModel? _selectedSlab;

    [ObservableProperty]
    private bool _slabAnalysisDirty;

    [ObservableProperty]
    private int _slabPreviewModeIndex;

    [ObservableProperty]
    private bool _showSlabGridOverlay = true;

    [ObservableProperty]
    private bool _showSlabLabels = true;

    [ObservableProperty]
    private CadBeamRowViewModel? _selectedBeam;

    [ObservableProperty]
    private bool _beamAnalysisDirty;

    public ModelFromCadMode SelectedMode => ActiveTabIndex switch
    {
        0 => ModelFromCadMode.Grid,
        1 => ModelFromCadMode.Column,
        2 => ModelFromCadMode.Beam,
        3 => ModelFromCadMode.Slab,
        _ => ModelFromCadMode.Wall
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

    public IReadOnlyList<CadWallCandidate> SelectedWalls =>
        Walls.Where(wall => wall.IsIncluded && wall.IsValid)
            .Select(wall => wall.ToCandidate())
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
        ModelFromCadMode.Slab => $"Chọn {SelectedSlabs.Count}/{Slabs.Count} sàn"
             + (SlabAnalysisDirty ? " — cần Apply/Re-analyze" : string.Empty)
             + SlabWarningLabel,
        ModelFromCadMode.Wall => $"Chọn {SelectedWalls.Count}/{Walls.Count} tường"
             + (WallAnalysisDirty ? " — cần Apply/Re-analyze" : string.Empty)
             + WallWarningLabel,
        _ => $"Chọn {SelectedBeams.Count}/{Beams.Count} dầm"
             + (BeamAnalysisDirty ? " — cần Apply/Re-analyze" : string.Empty)
             + BeamWarningLabel
    };

    private string SlabWarningLabel =>
        SlabData is null || SlabData.Analysis.Warnings.Count == 0
            ? string.Empty
            : "  |  " + string.Join("  |  ", SlabData.Analysis.Warnings);

    // Boundaries the analyzer could not turn into beams stay grey in the preview and would be
    // missing from the model, so the reason belongs next to the count rather than in a log.
    private string BeamWarningLabel =>
        BeamData is null || BeamData.Analysis.Warnings.Count == 0
            ? string.Empty
            : "  |  " + string.Join("  |  ", BeamData.Analysis.Warnings);

    public string CreateButtonText => SelectedMode switch
    {
        ModelFromCadMode.Grid => "Tạo Grid",
        ModelFromCadMode.Column => "Tạo Column",
        ModelFromCadMode.Slab => "Tạo Sàn",
        ModelFromCadMode.Wall => "Tạo Wall",
        _ => "Tạo Beam"
    };

    public bool CanAccept => SelectedMode switch
    {
        ModelFromCadMode.Grid => SelectedGridAxes.Count > 0 && RotationValid,
        ModelFromCadMode.Column => SelectedColumns.Count > 0 && ColumnSettingsValid,
        ModelFromCadMode.Slab => SelectedSlabs.Count > 0 && SlabSettingsValid,
        ModelFromCadMode.Wall => SelectedWalls.Count > 0 && WallCreateSettingsValid,
        _ => SelectedBeams.Count > 0 && BeamSettingsValid
    };

    public IReadOnlyList<CadSlabRegionCandidate> SelectedSlabs =>
        Slabs.Where(slab => slab.IsIncluded && slab.IsValid)
            .Select(slab => slab.Region)
            .ToArray();

    public bool CanSelectSlabLines =>
        HasCadData && SelectedGridAxes.Count > 0 && SlabAnalysisSettingsValid;

    public bool SlabAnalysisSettingsValid =>
        TryNumber(VertexSnapText, out var snap) && snap is >= 0 and <= 500
        && TryNumber(MinimumSlabLineText, out var minLine) && minLine >= 0
        && TryNumber(MinimumSlabAreaText, out var area) && area >= 0
        && TryNumber(MinimumOpeningAreaText, out var openingArea) && openingArea >= 0
        && TryNumber(BeamStripWidthText, out var strip) && strip >= 0
        && TryNumber(HatchJoinDistanceText, out var hatchJoin) && hatchJoin >= 0
        && TryNumber(DefaultSlabThicknessText, out var thickness) && thickness is >= 30 and <= 2000
        && TryNumber(DefaultSlabOffsetText, out _)
        && TryNumber(LoweredSlabOffsetText, out _);

    public bool SlabSettingsValid =>
        SlabAnalysisSettingsValid
        && !SlabAnalysisDirty
        && SelectedSlabType is not null
        && SelectedSlabLevel is not null
        && RotationValid;

    private CadSlabAnalysisOptions SlabOptions() => SlabOptionsWith(
        SlabData?.OpeningOutlines ?? Array.Empty<CadStructureSegment>());

    private CadSlabAnalysisOptions SlabOptionsWith(IReadOnlyList<CadStructureSegment> marks) =>
        BaseSlabOptions() with { OpeningOutlinesMm = marks };

    private CadSlabAnalysisOptions BaseSlabOptions() => new(
        VertexSnapToleranceMm: ParseNumber(VertexSnapText),
        MinimumLineLengthMm: ParseNumber(MinimumSlabLineText),
        MinimumRegionAreaM2: ParseNumber(MinimumSlabAreaText),
        MinimumOpeningAreaM2: ParseNumber(MinimumOpeningAreaText),
        MaximumBeamStripWidthMm: ParseNumber(BeamStripWidthText),
        HatchJoinDistanceMm: ParseNumber(HatchJoinDistanceText),
        DefaultThicknessMm: ParseNumber(DefaultSlabThicknessText),
        DefaultOffsetMm: ParseNumber(DefaultSlabOffsetText),
        LoweredDefaultOffsetMm: ParseNumber(LoweredSlabOffsetText),
        OverrideThickness: OverrideSlabThickness,
        OverrideElevation: OverrideSlabElevation);

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

    [RelayCommand(CanExecute = nameof(CanSelectSlabLines))]
    private void SelectSlabLines()
    {
        var replacement = _selectSlab?.Invoke(GridPackageForBeam(), SlabOptions());
        if (replacement is null) return;
        SetSlabData(replacement);
    }

    [RelayCommand(CanExecute = nameof(CanSelectOpeningOutlines))]
    private void SelectOpeningOutlines()
    {
        if (SlabData is null || _selectOpeningOutlines is null) return;
        var marks = _selectOpeningOutlines(SlabData.Package);
        if (marks is null) return;
        var analysis = CadSlabAnalyzer.Analyze(
            SlabData.Package, SlabData.Hatches, SlabOptionsWith(marks));
        SetSlabData(SlabData with { Analysis = analysis, OpeningOutlines = marks });
    }

    private bool CanSelectOpeningOutlines() => SlabData is not null && SlabAnalysisSettingsValid;

    /// <summary>
    /// Replaces the shaded areas read from the slab selection with the ones the user picks. The
    /// slab window takes in every hatch it covers, including ones belonging to another part of the
    /// plan; picking says which ones are the lowered pours.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSelectHatchRegions))]
    private void SelectHatchRegions()
    {
        if (SlabData is null || _selectHatchRegions is null) return;
        var picked = _selectHatchRegions(SlabData.Package);
        if (picked is null) return;
        var analysis = CadSlabAnalyzer.Analyze(SlabData.Package, picked, SlabOptions());
        SetSlabData(SlabData with { Analysis = analysis, Hatches = picked });
    }

    private bool CanSelectHatchRegions() => SlabData is not null && SlabAnalysisSettingsValid;

    [RelayCommand(CanExecute = nameof(CanApplySlabAnalysis))]
    private void ApplySlabAnalysis()
    {
        if (SlabData is null) return;
        var analysis = CadSlabAnalyzer.Analyze(SlabData.Package, SlabData.Hatches, SlabOptions());
        SetSlabData(SlabData with { Analysis = analysis });
    }

    private bool CanApplySlabAnalysis() => SlabData is not null
                                           && SlabAnalysisSettingsValid
                                           && SlabAnalysisDirty;

    [RelayCommand]
    private void SelectAllSlabs() => SetSlabSelection(true);

    [RelayCommand]
    private void ClearSlabSelection() => SetSlabSelection(false);

    [RelayCommand]
    private void ResetSlabsToDetected()
    {
        _suppressItemNotifications = true;
        foreach (var slab in Slabs) slab.ResetToDetected();
        _suppressItemNotifications = false;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetSlabSelection(bool included)
    {
        _suppressItemNotifications = true;
        foreach (var slab in Slabs)
            slab.IsIncluded = included && slab.IsValid;
        _suppressItemNotifications = false;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private CadWallAnalysisOptions WallOptions() => new(
        MinimumThicknessMm: ParseNumber(MinimumWallThicknessText),
        MaximumThicknessMm: ParseNumber(MaximumWallThicknessText),
        MinimumLengthMm: ParseNumber(MinimumWallLengthText),
        MinimumLengthRatio: ParseNumber(WallLengthRatioText))
    {
        WallLayers = WallLayers.Where(layer => layer.IsWall)
            .Select(layer => layer.Layer)
            .ToArray()
    };

    public bool WallSettingsValid =>
        TryNumber(MinimumWallThicknessText, out var minimum)
        && TryNumber(MaximumWallThicknessText, out var maximum)
        && TryNumber(MinimumWallLengthText, out var minimumLength)
        && TryNumber(WallLengthRatioText, out var ratio)
        && TryNumber(WallBaseOffsetText, out _)
        && minimum > 0
        && maximum >= minimum
        && minimumLength >= 0
        && ratio >= 1;

    public bool WallCreateSettingsValid =>
        SelectedWallType is not null
        && SelectedWallBaseLevel is not null
        && SelectedWallTopLevel is not null
        && SelectedWallTopLevel.Level.Id != SelectedWallBaseLevel.Level.Id
        && SelectedWallTopLevel.Elevation > SelectedWallBaseLevel.Elevation
        && WallSettingsValid
        && !WallAnalysisDirty
        && RotationValid
        && (SelectedWallTopLevel.Elevation - SelectedWallBaseLevel.Elevation) * 304.8
           > WallBaseOffsetMm
        && Walls.Where(wall => wall.IsIncluded).All(wall => wall.IsValid);

    public double WallBaseOffsetMm => ParseNumber(WallBaseOffsetText);

    public string WallWarningLabel =>
        WallData is null || WallData.Analysis.Warnings.Count == 0
            ? string.Empty
            : "  |  " + string.Join("  |  ", WallData.Analysis.Warnings);

    [RelayCommand(CanExecute = nameof(CanSelectWallLines))]
    private void SelectWallLines()
    {
        var replacement = _selectWall?.Invoke(GridPackageForBeam(), WallOptions());
        if (replacement is null) return;
        SetWallData(replacement);
    }

    private bool CanSelectWallLines() =>
        HasCadData && SelectedGridAxes.Count > 0 && WallSettingsValid;

    [RelayCommand(CanExecute = nameof(CanApplyWallAnalysis))]
    private void ApplyWallAnalysis()
    {
        if (WallData is null) return;
        var analysis = CadWallAnalyzer.Analyze(WallData.Package, WallOptions());
        if (analysis.Error is not null) return;
        SetWallData(WallData with { Analysis = analysis }, keepLayers: true);
    }

    private bool CanApplyWallAnalysis() =>
        WallData is not null && WallSettingsValid && WallAnalysisDirty;

    [RelayCommand]
    private void SelectAllWalls()
    {
        _suppressItemNotifications = true;
        foreach (var wall in Walls) wall.IsIncluded = wall.IsValid;
        _suppressItemNotifications = false;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearWallSelection()
    {
        _suppressItemNotifications = true;
        foreach (var wall in Walls) wall.IsIncluded = false;
        _suppressItemNotifications = false;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetWallData(CadWallPreviewData replacement, bool keepLayers = false)
    {
        var thicknessOverrides = keepLayers
            ? Walls.Where(row => Math.Abs(row.ThicknessMm - row.Source.ThicknessMm) >= 0.5)
                .GroupBy(WallIdentity)
                .ToDictionary(group => group.Key, group => group.Last().ThicknessMm)
            : new Dictionary<string, double>();
        var inclusionOverrides = keepLayers
            ? Walls.GroupBy(WallIdentity)
                .ToDictionary(group => group.Key, group => group.Last().IsIncluded)
            : new Dictionary<string, bool>();
        var selectedIdentity = keepLayers && SelectedWall is not null
            ? WallIdentity(SelectedWall)
            : null;
        foreach (var row in Walls) row.PropertyChanged -= OnItemChanged;
        Walls.Clear();
        WallData = replacement;
        WallAnalysisDirty = false;

        // A fresh scan brings its own layers; re-analysing the same scan keeps the ticks the user
        // has already made, or every Apply would undo their choice.
        if (!keepLayers)
        {
            foreach (var row in WallLayers) row.PropertyChanged -= OnItemChanged;
            WallLayers.Clear();
            foreach (var tally in replacement.Analysis.Layers)
            {
                var row = new CadLayerRowViewModel(tally);
                row.PropertyChanged += OnItemChanged;
                WallLayers.Add(row);
            }
        }

        foreach (var wall in replacement.Analysis.Walls)
        {
            var row = new CadWallRowViewModel(wall);
            if (thicknessOverrides.TryGetValue(WallIdentity(row), out var thicknessMm))
                row.ThicknessMm = thicknessMm;
            if (inclusionOverrides.TryGetValue(WallIdentity(row), out var isIncluded))
                row.IsIncluded = isIncluded;
            row.PropertyChanged += OnItemChanged;
            Walls.Add(row);
        }

        // The first pass intentionally returns only layer tallies. Suggested wall layers are
        // already ticked, so enable Apply immediately to run the required second pass.
        if (!keepLayers && Walls.Count == 0 && WallLayers.Any(layer => layer.IsWall))
            WallAnalysisDirty = true;
        SelectedWall = selectedIdentity is null
            ? Walls.FirstOrDefault()
            : Walls.FirstOrDefault(row => WallIdentity(row) == selectedIdentity)
              ?? Walls.FirstOrDefault();
        Zoom = 1.0;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
        ReselectCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static string WallIdentity(CadWallRowViewModel row) =>
        string.Join(",", row.Source.SourceSegmentIds.OrderBy(id => id));

    private void SetSlabData(CadSlabPreviewData replacement)
    {
        foreach (var row in Slabs) row.PropertyChanged -= OnItemChanged;
        Slabs.Clear();
        SlabData = replacement;
        SlabAnalysisDirty = false;
        foreach (var region in replacement.Analysis.Regions)
        {
            var row = new CadSlabRowViewModel(region);
            row.PropertyChanged += OnItemChanged;
            Slabs.Add(row);
        }
        SelectedSlab = Slabs.FirstOrDefault();
        Zoom = 1.0;
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
        ReselectCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySlabAnalysisSettings()
    {
        if (SlabData is not null) SlabAnalysisDirty = true;
        NotifyState();
    }

    partial void OnVertexSnapTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnMinimumSlabLineTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnMinimumSlabAreaTextChanged(string value) => NotifySlabAnalysisSettings();

    partial void OnMinimumOpeningAreaTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnBeamStripWidthTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnHatchJoinDistanceTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnDefaultSlabThicknessTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnDefaultSlabOffsetTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnLoweredSlabOffsetTextChanged(string value) => NotifySlabAnalysisSettings();
    partial void OnOverrideSlabThicknessChanged(bool value) => NotifySlabAnalysisSettings();
    partial void OnOverrideSlabElevationChanged(bool value) => NotifySlabAnalysisSettings();
    partial void OnSelectedSlabTypeChanged(CadSlabTypeOption? value) => NotifyState();
    partial void OnSelectedSlabLevelChanged(CadColumnLevelOption? value) => NotifyState();
    partial void OnSelectedSlabChanged(CadSlabRowViewModel? value) =>
        RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnSlabPreviewModeIndexChanged(int value) =>
        RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowSlabGridOverlayChanged(bool value) =>
        RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowSlabLabelsChanged(bool value) =>
        RenderRequested?.Invoke(this, EventArgs.Empty);

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
        foreach (var row in Walls) row.PropertyChanged -= OnItemChanged;
        foreach (var row in WallLayers) row.PropertyChanged -= OnItemChanged;
        GridAxes.Clear();
        Columns.Clear();
        Beams.Clear();
        Walls.Clear();
        WallLayers.Clear();
        BeamData = null;
        WallData = null;
        SelectedWall = null;
        BeamAnalysisDirty = false;
        WallAnalysisDirty = false;
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
    partial void OnWallPreviewModeIndexChanged(int value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowWallGridOverlayChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnShowWallLabelsChanged(bool value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedWallChanged(CadWallRowViewModel? value) => RenderRequested?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedWallTypeChanged(CadWallTypeOption? value) => NotifyState();
    partial void OnSelectedWallBaseLevelChanged(CadColumnLevelOption? value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnSelectedWallTopLevelChanged(CadColumnLevelOption? value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnWallBaseOffsetTextChanged(string value)
    {
        NotifyState();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnMinimumWallThicknessTextChanged(string value) => NotifyWallAnalysisSettings();
    partial void OnMaximumWallThicknessTextChanged(string value) => NotifyWallAnalysisSettings();
    partial void OnMinimumWallLengthTextChanged(string value) => NotifyWallAnalysisSettings();
    partial void OnWallLengthRatioTextChanged(string value) => NotifyWallAnalysisSettings();
    partial void OnWallAnalysisDirtyChanged(bool value) => NotifyState();
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
        if (sender is CadLayerRowViewModel) WallAnalysisDirty = WallData is not null;
        if (sender is CadWallRowViewModel
            && e.PropertyName == nameof(CadWallRowViewModel.ThicknessMm))
            WallAnalysisDirty = WallData is not null;
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
        OnPropertyChanged(nameof(SelectedSlabs));
        OnPropertyChanged(nameof(SlabSettingsValid));
        OnPropertyChanged(nameof(SlabAnalysisSettingsValid));
        OnPropertyChanged(nameof(SlabAnalysisDirty));
        OnPropertyChanged(nameof(CanSelectSlabLines));
        OnPropertyChanged(nameof(SelectedWalls));
        OnPropertyChanged(nameof(WallSettingsValid));
        OnPropertyChanged(nameof(WallCreateSettingsValid));
        OnPropertyChanged(nameof(WallAnalysisDirty));
        OnPropertyChanged(nameof(WallWarningLabel));
        AcceptCommand.NotifyCanExecuteChanged();
        SelectBeamLinesCommand.NotifyCanExecuteChanged();
        ApplyBeamAnalysisCommand.NotifyCanExecuteChanged();
        SelectSlabLinesCommand.NotifyCanExecuteChanged();
        ApplySlabAnalysisCommand.NotifyCanExecuteChanged();
        SelectOpeningOutlinesCommand.NotifyCanExecuteChanged();
        SelectHatchRegionsCommand.NotifyCanExecuteChanged();
        SelectWallLinesCommand.NotifyCanExecuteChanged();
        ApplyWallAnalysisCommand.NotifyCanExecuteChanged();
    }

    private void NotifyWallAnalysisSettings()
    {
        if (WallData is not null) WallAnalysisDirty = true;
        NotifyState();
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
