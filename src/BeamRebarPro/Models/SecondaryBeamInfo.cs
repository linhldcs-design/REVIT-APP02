namespace BeamRebarPro.Models;

/// <summary>
///     Secondary beam station data used to place hanger/tight stirrups around the real beam faces.
/// </summary>
public sealed record SecondaryBeamInfo(Point3 Location, double HalfWidthFeet);
