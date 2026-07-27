using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

/// <summary>Input hình học thuần của một dầm, theo internal unit feet.</summary>
public sealed record BeamSpanInput(
    long SourceId,
    Point3 Start,
    Point3 End,
    double WidthFeet,
    double HeightFeet,
    long HostId = 0);

/// <summary>Một nhịp đã được định hướng theo thứ tự của chuỗi dầm.</summary>
public sealed record BeamSpanModel(
    long SourceId,
    int Index,
    Point3 Start,
    Point3 End,
    double LengthFeet,
    double WidthFeet,
    double HeightFeet,
    long HostId = 0);

/// <summary>Chuỗi dầm hợp lệ là một path duy nhất, đã sắp và định hướng.</summary>
public sealed record BeamChainModel(
    IReadOnlyList<BeamSpanModel> Spans,
    Point3 Start,
    Point3 End,
    double TotalLengthFeet);

public sealed record BeamChainTolerance(
    double EndpointFeet,
    double AlignmentFeet,
    double ElevationFeet)
{
    public static BeamChainTolerance Default { get; } = new(0.01, 0.01, 0.01);
}

public enum BeamChainErrorCode
{
    Empty,
    InvalidGeometry,
    Disconnected,
    Branch,
    Cycle,
    NotCollinear,
    DifferentElevation
}

public sealed record BeamChainError(BeamChainErrorCode Code, string Message, long? SourceId = null);

public sealed record BeamChainBuildResult(BeamChainModel? Model, IReadOnlyList<BeamChainError> Errors)
{
    public bool IsValid => Model != null && Errors.Count == 0;
}
