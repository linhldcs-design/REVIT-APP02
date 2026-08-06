using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.ViewModels;

internal sealed partial class CadBeamRowViewModel : ObservableObject
{
    public CadBeamRowViewModel(CadBeamCandidate candidate)
    {
        Source = candidate;
        _effectiveWidthMm = candidate.EffectiveWidthMm;
        _effectiveHeightMm = candidate.EffectiveHeightMm;
        _mark = candidate.Mark;
        // Width mismatch remains visible but requires an explicit user check before Create.
        _isIncluded = candidate.Status == CadBeamCandidateStatus.Ready;
    }

    public CadBeamCandidate Source { get; }

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private double _effectiveWidthMm;

    [ObservableProperty]
    private double _effectiveHeightMm;

    [ObservableProperty]
    private string _mark;

    public int Number => Source.Id;
    public string GeometryWidthLabel => $"{Source.GeometryWidthMm:0.#}";
    public string TextWidthLabel => Source.TextWidthMm is null ? "—" : $"{Source.TextWidthMm:0.#}";
    public string TextHeightLabel => Source.TextHeightMm is null ? "—" : $"{Source.TextHeightMm:0.#}";
    public string MatchedText => Source.MatchedText;
    public string Status => IsManualOverride ? "ManualOverride" : Source.Status.ToString();
    public bool IsManualOverride =>
        Math.Abs(EffectiveWidthMm - Source.EffectiveWidthMm) > 0.5
        || Math.Abs(EffectiveHeightMm - Source.EffectiveHeightMm) > 0.5
        || !string.Equals(Mark, Source.Mark, StringComparison.Ordinal);
    public bool IsValid => Source.Status is CadBeamCandidateStatus.Ready
                               or CadBeamCandidateStatus.TextWidthMismatch
                           && Finite(EffectiveWidthMm) && EffectiveWidthMm is >= 50 and <= 3000
                           && Finite(EffectiveHeightMm) && EffectiveHeightMm is >= 50 and <= 5000;

    public CadBeamCandidate Candidate => Source with
    {
        EffectiveWidthMm = EffectiveWidthMm,
        EffectiveHeightMm = EffectiveHeightMm,
        Mark = Mark
    };

    public void ResetToDetected()
    {
        EffectiveWidthMm = Source.GeometryWidthMm;
        EffectiveHeightMm = Source.TextHeightMm ?? Source.EffectiveHeightMm;
        Mark = Source.Mark;
    }

    partial void OnEffectiveWidthMmChanged(double value) => NotifyDerived();
    partial void OnEffectiveHeightMmChanged(double value) => NotifyDerived();
    partial void OnMarkChanged(string value) => NotifyDerived();

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsManualOverride));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Candidate));
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
