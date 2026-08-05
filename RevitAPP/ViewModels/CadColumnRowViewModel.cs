using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.ViewModels;

public sealed partial class CadColumnRowViewModel : ObservableObject
{
    public CadColumnRowViewModel(CadColumnCandidate candidate)
    {
        Candidate = candidate;
        // Loose LINE rectangles can also be rooms, footings, or drafting details.
        // Closed polylines and block candidates carry a source path and are safer defaults.
        _isIncluded = !string.IsNullOrWhiteSpace(candidate.SourcePath);
    }

    public CadColumnCandidate Candidate { get; }
    public int Number => Candidate.Id;
    public string WidthLabel => $"{Candidate.WidthMm:0.#}";
    public string HeightLabel => $"{Candidate.HeightMm:0.#}";
    public string AngleLabel => $"{Candidate.AngleDegrees:0.#}°";
    public string Text => string.IsNullOrWhiteSpace(Candidate.SourceText)
        ? Candidate.SourcePath
        : Candidate.SourceText!;
    public string Layer => Candidate.Layer;
    public string Status => string.IsNullOrWhiteSpace(Candidate.SourcePath)
        ? "Confirm"
        : "Ready";

    [ObservableProperty]
    private bool _isIncluded;
}
