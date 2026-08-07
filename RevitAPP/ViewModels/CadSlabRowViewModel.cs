using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.ViewModels;

internal sealed partial class CadSlabRowViewModel : ObservableObject
{
    public CadSlabRowViewModel(CadSlabRegionCandidate region)
    {
        Source = region;
        _thicknessMm = region.EffectiveThicknessMm;
        _offsetMm = region.EffectiveOffsetMm;
        // A slab the drawing labelled is ready to pour; one the drawing left blank is carrying a
        // default and needs a look before it goes into the model.
        _isIncluded = region.Status == CadSlabRegionStatus.Ready;
    }

    public CadSlabRegionCandidate Source { get; }

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private double _thicknessMm;

    [ObservableProperty]
    private double _offsetMm;

    public int Number => Source.Id;
    public int CellCount => Source.CellIds.Count;
    public int HoleCount => Source.Holes.Count;
    public int AbsorbedStripCount => Source.AbsorbedStripCount;
    public string AreaLabel => $"{Source.AreaM2:0.00}";
    public string DetectedThicknessLabel =>
        Source.DetectedThicknessMm is null ? "—" : $"{Source.DetectedThicknessMm:0.#}";
    public string DetectedElevationLabel =>
        Source.DetectedElevationMm is null ? "—" : $"{Source.DetectedElevationMm / 1000.0:+0.000;-0.000;+0.000}";
    public string MatchedText => Source.MatchedText;
    public bool IsLowered => Source.IsLowered;

    public string Status => IsManualOverride ? "ManualOverride" : Source.Status.ToString();

    public bool IsManualOverride =>
        Math.Abs(ThicknessMm - Source.EffectiveThicknessMm) > 0.5
        || Math.Abs(OffsetMm - Source.EffectiveOffsetMm) > 0.5;

    public bool IsValid => Source.CanCreate
                           && Finite(ThicknessMm) && ThicknessMm is >= 30 and <= 2000
                           && Finite(OffsetMm) && Math.Abs(OffsetMm) <= 100_000;

    public CadSlabRegionCandidate Region => Source with
    {
        EffectiveThicknessMm = ThicknessMm,
        EffectiveOffsetMm = OffsetMm,
        IsManualOverride = IsManualOverride
    };

    public void ResetToDetected()
    {
        ThicknessMm = Source.EffectiveThicknessMm;
        OffsetMm = Source.EffectiveOffsetMm;
    }

    partial void OnThicknessMmChanged(double value) => NotifyDerived();
    partial void OnOffsetMmChanged(double value) => NotifyDerived();

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsManualOverride));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Region));
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
