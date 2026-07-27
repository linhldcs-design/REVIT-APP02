namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

public sealed record BeamChainPreviewSpan(
    long SourceId, int DisplayIndex, double StartFeet, double EndFeet, string Label);

public sealed record BeamChainPreviewStation(
    SectionStationKind Kind, double ChainDistanceFeet, string Label);

public sealed record BeamChainPreviewModel(
    IReadOnlyList<BeamChainPreviewSpan> Spans,
    IReadOnlyList<BeamChainPreviewStation> Stations,
    double TotalLengthFeet,
    bool IsReversed,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Spans.Count > 0 && TotalLengthFeet > 0 && Warnings.Count == 0;
}

public sealed class PreviewConfirmationState
{
    private int _revision;
    private int _confirmedRevision = -1;

    public bool IsConfirmed => _confirmedRevision == _revision;
    public void Invalidate() => _revision++;
    public bool Confirm(bool previewValid)
    {
        if (!previewValid) return false;
        _confirmedRevision = _revision;
        return true;
    }

    public bool CanGenerate(bool settingValid, bool previewValid) =>
        settingValid && previewValid && IsConfirmed;
}
