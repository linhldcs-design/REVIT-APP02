using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

public static class CadGridGeometry
{
    private const double Epsilon = 1e-9;

    public static double Length(CadGridSegment2 segment) => segment.Start.DistanceTo(segment.End);

    public static bool Intersects(
        CadGridSegment2 segment,
        CadGridSelectionBox box,
        double toleranceMm = 1e-6)
    {
        var dx = segment.End.Xmm - segment.Start.Xmm;
        var dy = segment.End.Ymm - segment.Start.Ymm;
        var tMin = 0d;
        var tMax = 1d;

        return Clip(-dx, segment.Start.Xmm - box.MinXmm, ref tMin, ref tMax, toleranceMm)
               && Clip(dx, box.MaxXmm - segment.Start.Xmm, ref tMin, ref tMax, toleranceMm)
               && Clip(-dy, segment.Start.Ymm - box.MinYmm, ref tMin, ref tMax, toleranceMm)
               && Clip(dy, box.MaxYmm - segment.Start.Ymm, ref tMin, ref tMax, toleranceMm);
    }

    public static bool TryIntersectBounded(
        CadGridSegment2 first,
        CadGridSegment2 second,
        double toleranceMm,
        out CadGridPoint2 intersection)
    {
        var p = first.Start;
        var r = first.End - first.Start;
        var q = second.Start;
        var s = second.End - second.Start;
        var cross = Cross(r, s);

        if (Math.Abs(cross) <= Epsilon)
        {
            intersection = default;
            return false;
        }

        var qMinusP = q - p;
        var t = Cross(qMinusP, s) / cross;
        var u = Cross(qMinusP, r) / cross;
        var firstLength = Math.Max(Length(first), Epsilon);
        var secondLength = Math.Max(Length(second), Epsilon);
        var firstParameterTolerance = toleranceMm / firstLength;
        var secondParameterTolerance = toleranceMm / secondLength;

        if (t < -firstParameterTolerance || t > 1 + firstParameterTolerance
            || u < -secondParameterTolerance || u > 1 + secondParameterTolerance)
        {
            intersection = default;
            return false;
        }

        intersection = p + r * t;
        return true;
    }

    public static double CanonicalAngleRadians(CadGridSegment2 segment)
    {
        var direction = segment.End - segment.Start;
        var angle = Math.Atan2(direction.Ymm, direction.Xmm);
        if (angle < 0) angle += Math.PI;
        if (angle >= Math.PI) angle -= Math.PI;
        return angle;
    }

    public static double AngleDistanceRadians(double first, double second)
    {
        var difference = Math.Abs(first - second) % Math.PI;
        return Math.Min(difference, Math.PI - difference);
    }

    public static bool AreSameInfiniteLine(
        CadGridSegment2 first,
        CadGridSegment2 second,
        double distanceToleranceMm,
        double angleToleranceRadians)
    {
        if (Length(first) <= Epsilon || Length(second) <= Epsilon) return false;

        var angleDifference = AngleDistanceRadians(
            CanonicalAngleRadians(first),
            CanonicalAngleRadians(second));
        if (angleDifference > angleToleranceRadians) return false;

        var firstMidpoint = (first.Start + first.End) * 0.5;
        var secondMidpoint = (second.Start + second.End) * 0.5;
        return DistanceToInfiniteLine(secondMidpoint, first) <= distanceToleranceMm
               && DistanceToInfiniteLine(firstMidpoint, second) <= distanceToleranceMm;
    }

    public static double ParameterOnSegment(CadGridPoint2 point, CadGridSegment2 segment)
    {
        var direction = segment.End - segment.Start;
        var denominator = Dot(direction, direction);
        if (denominator <= Epsilon) return 0;
        return Dot(point - segment.Start, direction) / denominator;
    }

    private static double DistanceToInfiniteLine(CadGridPoint2 point, CadGridSegment2 line)
    {
        var direction = line.End - line.Start;
        return Math.Abs(Cross(point - line.Start, direction)) / Math.Max(Length(line), Epsilon);
    }

    private static double Cross(CadGridPoint2 first, CadGridPoint2 second) =>
        first.Xmm * second.Ymm - first.Ymm * second.Xmm;

    private static double Dot(CadGridPoint2 first, CadGridPoint2 second) =>
        first.Xmm * second.Xmm + first.Ymm * second.Ymm;

    private static bool Clip(
        double denominator,
        double numerator,
        ref double tMin,
        ref double tMax,
        double tolerance)
    {
        if (Math.Abs(denominator) <= Epsilon) return numerator >= -tolerance;

        var ratio = numerator / denominator;
        if (denominator < 0)
        {
            if (ratio > tMax + tolerance) return false;
            if (ratio > tMin) tMin = ratio;
        }
        else
        {
            if (ratio < tMin - tolerance) return false;
            if (ratio < tMax) tMax = ratio;
        }

        return true;
    }
}
