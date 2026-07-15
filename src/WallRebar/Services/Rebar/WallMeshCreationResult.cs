namespace WallRebar.Services.Rebar;

/// <summary>Result for one wall face mesh creation, split by bar direction for accurate UI status.</summary>
public sealed record WallMeshCreationResult(int VerticalSetCount, int HorizontalSetCount);
