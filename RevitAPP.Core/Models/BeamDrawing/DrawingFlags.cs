namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>Các checkbox dưới form BIMSpeed.</summary>
public sealed record DrawingFlags(
    bool LongSection,
    bool CrossSection,
    bool CrossSectionForMultiBeam,
    bool PickPillowToDim,
    bool CreateView3D,
    string? LongSectionViewName = null,
    string? CrossSectionViewName = null);
