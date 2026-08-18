namespace IsolatedFootingRebar.Models;

public enum FootingPreviewBarKind
{
    BottomX,
    BottomY,
    TopX,
    TopY,
    MidX,
    MidY,
    Chair,
    Horizontal
}

public readonly record struct PreviewPoint3D(double Xmm, double Ymm, double Zmm);

public sealed record FootingPreviewPath(
    FootingPreviewBarKind Kind,
    double DiameterMm,
    IReadOnlyList<PreviewPoint3D> Points,
    bool IsClosed = false);

public sealed record FootingPreviewTriangle(PreviewPoint3D A, PreviewPoint3D B, PreviewPoint3D C);

public readonly record struct FootingPreviewEdge(PreviewPoint3D A, PreviewPoint3D B);

public sealed record FootingRebarPreviewPlan(
    IReadOnlyList<FootingPreviewPath> Paths,
    IReadOnlyList<FootingPreviewTriangle> Concrete,
    string? ValidationMessage = null)
{
    public static FootingRebarPreviewPlan Empty(string? message = null) => new([], [], message);
    public bool IsEmpty => Paths.Count == 0 && Concrete.Count == 0;
}
