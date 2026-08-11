using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.ViewModels;

internal sealed partial class CadWallRowViewModel : ObservableObject
{
    public CadWallRowViewModel(CadWallCandidate wall)
    {
        Source = wall;
        _thicknessMm = wall.EffectiveThicknessMm;
        _isIncluded = wall.CanCreate;
    }

    public CadWallCandidate Source { get; }

    [ObservableProperty]
    private bool _isIncluded;

    /// <summary>
    /// The thickness to build at. It starts at what the reader measured between the faces, and
    /// the user can type over it for a wall the drawing left ambiguous.
    /// </summary>
    [ObservableProperty]
    private double _thicknessMm;

    public int Number => Source.Id;

    public double LengthMm => Source.LengthMm;

    public string DrawnAs => Source.Source switch
    {
        CadWallSource.Rectangle => "Rectangle",
        _ => "2 line"
    };

    public string StatusLabel => Source.Status switch
    {
        CadWallCandidateStatus.Ready => "Ready",
        CadWallCandidateStatus.ThicknessOutOfRange => "Bề dày ngoài dải",
        CadWallCandidateStatus.TooShort => "Quá ngắn",
        _ => Source.Status.ToString()
    };

    /// <summary>
    /// The candidate as the user left it, with any thickness typed over the measured one.
    /// </summary>
    public CadWallCandidate ToCandidate() =>
        Math.Abs(ThicknessMm - Source.ThicknessMm) < 0.5
            ? Source
            : Source with { OverrideThicknessMm = ThicknessMm };
}

/// <summary>
/// A layer offered to the user, with how much of the scan sits on it.
///
/// A wall and a beam are drawn identically, so only the layer says which is which. The reader
/// ticks the boxes it thinks are walls; the user has the last word.
/// </summary>
internal sealed partial class CadLayerRowViewModel : ObservableObject
{
    public CadLayerRowViewModel(CadLayerTally tally)
    {
        Source = tally;
        _isWall = tally.SuggestedAsWall;
    }

    public CadLayerTally Source { get; }

    [ObservableProperty]
    private bool _isWall;

    public string Layer => Source.Layer;

    public int SegmentCount => Source.SegmentCount;

    public string Label => $"{Source.Layer}  ({Source.SegmentCount})";
}
