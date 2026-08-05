namespace RevitAPP.Core.Models.CadGrid;

public sealed record CadGridIntersection(
    int FirstSegmentId,
    int SecondSegmentId,
    CadGridPoint2 Point);

public sealed record CadGridNetworkFamily(
    IReadOnlyList<int> OrderedSegmentIds,
    IReadOnlyList<double> ConsecutiveDistancesMm,
    int ReferenceSegmentId);

public sealed record CadGridNetwork(
    CadGridNetworkFamily FirstFamily,
    CadGridNetworkFamily SecondFamily,
    IReadOnlyList<CadGridIntersection> Intersections,
    IReadOnlyList<int> SkippedSegmentIds);

public sealed record CadGridNetworkAnalysis(
    CadGridNetwork? Network,
    string? Error)
{
    public bool IsValid => Network is not null && string.IsNullOrWhiteSpace(Error);

    public static CadGridNetworkAnalysis Invalid(string error) => new(null, error);

    public static CadGridNetworkAnalysis Valid(CadGridNetwork network) => new(network, null);
}
