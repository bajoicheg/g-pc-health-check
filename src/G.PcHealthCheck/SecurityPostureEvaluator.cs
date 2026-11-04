namespace G.PcHealthCheck;

internal static class SecurityPostureEvaluator
{
    public static SecurityPostureAssessment Evaluate(SecurityPostureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var controls = SecurityControlCatalog.All.Select(descriptor =>
        {
            var observation = snapshot.ControlObservations.TryGetValue(descriptor.Id, out var observed)
                ? observed
                : new SecurityControlObservation(descriptor.Id, SecurityControlStatus.Unknown, [], descriptor.Id);
            return new SecurityControlResult(
                descriptor.Id,
                observation.Status,
                descriptor.Weight,
                EarnedFraction(observation.Status),
                string.IsNullOrWhiteSpace(observation.GuidanceCode) ? descriptor.Id : observation.GuidanceCode,
                observation.Evidence);
        }).ToList();

        var applicable = controls.Where(x => x.Status != SecurityControlStatus.NotApplicable).ToList();
        var known = applicable.Where(x => IsKnown(x.Status)).ToList();
        var applicableWeight = applicable.Sum(x => x.Weight);
        var knownWeight = known.Sum(x => x.Weight);
        var coverage = applicableWeight == 0
            ? 0
            : (int)Math.Round(knownWeight * 100d / applicableWeight, MidpointRounding.AwayFromZero);
        var earned = known.Sum(x => x.Weight * x.EarnedFraction);
        var score = knownWeight == 0
            ? null
            : (int?)Math.Round(earned * 100d / knownWeight, MidpointRounding.AwayFromZero);

        var rawBand = BandFor(score);
        var overrides = new List<string>();
        var capped = rawBand;
        foreach (var control in controls.Where(x => x.Status == SecurityControlStatus.Fail))
        {
            var descriptor = SecurityControlCatalog.Find(control.Id);
            if (descriptor?.FailBandCap is not SecurityBand cap) continue;
            overrides.Add(control.Id);
            capped = NoBetterThan(capped, cap);
        }

        var displayBand = coverage < 80 ? SecurityBand.AssessmentIncomplete : capped;
        return new SecurityPostureAssessment
        {
            Score = score,
            CoveragePercent = coverage,
            RawBand = rawBand,
            DisplayBand = displayBand,
            CriticalOverrides = overrides,
            Controls = controls,
            Supplemental = snapshot.Supplemental.ToList(),
            CollectionWarnings = snapshot.CollectionWarnings.ToList()
        };
    }

    internal static bool IsKnown(SecurityControlStatus status)
        => status is SecurityControlStatus.Pass or SecurityControlStatus.Warn or SecurityControlStatus.Fail;

    internal static double EarnedFraction(SecurityControlStatus status)
        => status switch
        {
            SecurityControlStatus.Pass => 1d,
            SecurityControlStatus.Warn => 0.5d,
            SecurityControlStatus.Fail => 0d,
            _ => 0d
        };

    internal static SecurityBand BandFor(int? score)
        => score switch
        {
            null => SecurityBand.AssessmentIncomplete,
            >= 90 => SecurityBand.High,
            >= 75 => SecurityBand.Good,
            >= 50 => SecurityBand.NeedsAttention,
            _ => SecurityBand.Low
        };

    internal static SecurityBand NoBetterThan(SecurityBand current, SecurityBand cap)
    {
        if (current == SecurityBand.AssessmentIncomplete) return current;
        return BandSeverity(current) >= BandSeverity(cap) ? current : cap;
    }

    private static int BandSeverity(SecurityBand band)
        => band switch
        {
            SecurityBand.High => 0,
            SecurityBand.Good => 1,
            SecurityBand.NeedsAttention => 2,
            SecurityBand.Low => 3,
            SecurityBand.AssessmentIncomplete => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(band))
        };
}
