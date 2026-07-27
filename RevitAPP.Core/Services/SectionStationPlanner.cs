using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class SectionStationPlanner
{
    public static IReadOnlyList<SectionStation> Plan(
        BeamChainModel chain,
        IReadOnlyList<BeamSpanSectionProfile> profiles,
        bool reduceUniformSpans,
        RebarFingerprintTolerance? tolerance = null)
    {
        if (chain == null) throw new ArgumentNullException(nameof(chain));
        if (profiles == null) throw new ArgumentNullException(nameof(profiles));
        if (chain.Spans.Count != profiles.Count)
            throw new ArgumentException("Mỗi span phải có đúng một profile.", nameof(profiles));

        var comparisonTolerance = tolerance ?? RebarFingerprintTolerance.Default;
        var result = new List<SectionStation>();
        var cumulativeStarts = new double[chain.Spans.Count];
        var reducible = new bool[chain.Spans.Count];
        var cumulative = 0d;

        for (var index = 0; index < chain.Spans.Count; index++)
        {
            var span = chain.Spans[index];
            var profile = profiles[index];
            if (profile.SourceId != span.SourceId)
                throw new ArgumentException($"Profile của span {span.SourceId} không đúng thứ tự.", nameof(profiles));

            cumulativeStarts[index] = cumulative;
            var uniform = RebarFingerprintComparer.AreEquivalent(
                              profile.LeftSupport, profile.MidSpan, comparisonTolerance) &&
                          RebarFingerprintComparer.AreEquivalent(
                              profile.MidSpan, profile.RightSupport, comparisonTolerance);
            reducible[index] = reduceUniformSpans && uniform && IsSimpleUniformProfile(profile);
            if (reducible[index])
            {
                if (index > 0 && result.Any(station => station.Kind == SectionStationKind.RightSupport &&
                                                       Math.Abs(station.ChainDistanceFeet - cumulative) <= comparisonTolerance.SectionFeet))
                    MergeOrAddSharedSupport(result, new SectionStation(SectionStationKind.LeftSupport, cumulative,
                        [index], profile.LeftSupport, "Left boundary of reduced uniform span."), comparisonTolerance);
                result.Add(new SectionStation(SectionStationKind.MidSpan,
                    cumulative + span.LengthFeet * 0.5, [index], profile.MidSpan,
                    "Reduced uniform span: no additional reinforcement and exactly one equivalent stirrup zone."));
                cumulative += span.LengthFeet;
                continue;
            }

            var left = new SectionStation(SectionStationKind.LeftSupport, cumulative,
                [index], profile.LeftSupport, "Left support of span.");
            MergeOrAddSharedSupport(result, left, comparisonTolerance);
            result.Add(new SectionStation(SectionStationKind.MidSpan,
                cumulative + span.LengthFeet * 0.5, [index], profile.MidSpan, "Mid-span station."));
            result.Add(new SectionStation(SectionStationKind.RightSupport,
                cumulative + span.LengthFeet, [index], profile.RightSupport, "Right support of span."));
            cumulative += span.LengthFeet;
        }

        // Một transition khác fingerprint luôn cần hai lát cắt phía trái/phải, kể cả span riêng lẻ đủ điều kiện rút gọn.
        for (var index = 0; index < chain.Spans.Count - 1; index++)
        {
            var boundary = cumulativeStarts[index] + chain.Spans[index].LengthFeet;
            var leftFingerprint = profiles[index].RightSupport;
            var rightFingerprint = profiles[index + 1].LeftSupport;
            if (RebarFingerprintComparer.AreEquivalent(leftFingerprint, rightFingerprint, comparisonTolerance))
                continue;

            var probe = Math.Min(50.0 / 304.8,
                Math.Min(chain.Spans[index].LengthFeet, chain.Spans[index + 1].LengthFeet) * 0.05);
            result.RemoveAll(station => Math.Abs(station.ChainDistanceFeet - boundary) < 1e-9 &&
                                        station.SourceSpanIndices.Contains(index));
            result.RemoveAll(station => Math.Abs(station.ChainDistanceFeet - boundary) < 1e-9 &&
                                        station.SourceSpanIndices.Contains(index + 1));
            EnsureStation(result, new SectionStation(SectionStationKind.RightSupport, boundary - probe,
                [index], leftFingerprint, "Right side of reinforcement transition."));
            EnsureStation(result, new SectionStation(SectionStationKind.LeftSupport, boundary + probe,
                [index + 1], rightFingerprint, "Left side of reinforcement transition."));
        }

        return result.OrderBy(station => station.ChainDistanceFeet)
            .ThenBy(station => station.Kind)
            .ToList();
    }

    private static bool IsSimpleUniformProfile(BeamSpanSectionProfile profile)
    {
        var fingerprints = new[] { profile.LeftSupport, profile.MidSpan, profile.RightSupport };
        return fingerprints.All(item => !item.HasAdditionalReinforcement && item.StirrupZones.Count == 1);
    }

    private static void EnsureStation(List<SectionStation> result, SectionStation candidate)
    {
        var exists = result.Any(item => item.Kind == candidate.Kind &&
                                        Math.Abs(item.ChainDistanceFeet - candidate.ChainDistanceFeet) < 1e-9 &&
                                        item.SourceSpanIndices.SequenceEqual(candidate.SourceSpanIndices));
        if (!exists) result.Add(candidate);
    }

    private static void MergeOrAddSharedSupport(
        List<SectionStation> result,
        SectionStation candidate,
        RebarFingerprintTolerance tolerance)
    {
        var previousIndex = result.FindLastIndex(station =>
            station.Kind == SectionStationKind.RightSupport &&
            Math.Abs(station.ChainDistanceFeet - candidate.ChainDistanceFeet) <= tolerance.SectionFeet);
        if (previousIndex < 0)
        {
            result.Add(candidate);
            return;
        }

        var previous = result[previousIndex];
        if (!RebarFingerprintComparer.AreEquivalent(previous.Fingerprint, candidate.Fingerprint, tolerance))
        {
            result.Add(candidate);
            return;
        }

        result[previousIndex] = previous with
        {
            Kind = SectionStationKind.SharedSupport,
            SourceSpanIndices = previous.SourceSpanIndices.Concat(candidate.SourceSpanIndices).Distinct().ToList(),
            Reason = "Shared support: equivalent fingerprints on both adjacent spans."
        };
    }
}
