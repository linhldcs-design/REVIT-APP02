namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

public enum SectionStationKind
{
    LeftSupport,
    MidSpan,
    RightSupport,
    SharedSupport
}

/// <summary>Mặt cắt cần tạo dọc theo chuỗi dầm.</summary>
public sealed record SectionStation(
    SectionStationKind Kind,
    double ChainDistanceFeet,
    IReadOnlyList<int> SourceSpanIndices,
    RebarStationFingerprint Fingerprint,
    string Reason);

/// <summary>Ba mẫu thép nghiệp vụ của một nhịp: gối trái, giữa nhịp, gối phải.</summary>
public sealed record BeamSpanSectionProfile(
    long SourceId,
    RebarStationFingerprint LeftSupport,
    RebarStationFingerprint MidSpan,
    RebarStationFingerprint RightSupport);
