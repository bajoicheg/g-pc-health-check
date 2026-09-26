namespace G.PcHealthCheck;

internal interface IAntivirusSecuritySource
{
    IReadOnlyList<SecurityCenterProduct> ReadSecurityCenterProducts();
    DefenderSecurityObservation ReadDefender();
    KasperskySecurityObservation ReadKaspersky();
}

internal static class AntivirusSecurityCollector
{
    public static Dictionary<string, SecurityControlObservation> Collect(IAntivirusSecuritySource? source = null, DateTime? now = null)
    {
        source ??= new AntivirusSecuritySource();
        var at = now ?? DateTime.Now;
        IReadOnlyList<SecurityCenterProduct> products;
        try { products = source.ReadSecurityCenterProducts(); }
        catch (Exception ex)
        {
            return CollectWithoutWsc(source, at, "WSC product enumeration failed: " + ex.GetType().Name);
        }

        var antivirus = products.Where(x => x.Provider.Equals("Antivirus", StringComparison.OrdinalIgnoreCase)).ToList();
        if (antivirus.Count == 0)
        {
            var result = UnknownSet("No primary provider");
            result["SEC-AV-ACTIVE"] = Observation("SEC-AV-ACTIVE", SecurityControlStatus.Fail,
                [new("Products", "0", "WSC")]);
            return result;
        }

        var active = antivirus.Where(x => IsState(x.ProductState, "On")).ToList();
        if (active.Count > 1)
        {
            var result = UnknownSet("Multiple active antivirus providers");
            result["SEC-AV-ACTIVE"] = Observation("SEC-AV-ACTIVE", SecurityControlStatus.Pass,
                [new("ActiveProducts", string.Join("; ", active.Select(x => x.Name)), "WSC")]);
            return result;
        }

        if (active.Count == 0)
        {
            var knownInactive = antivirus.All(x => IsKnownInactive(x.ProductState));
            var result = UnknownSet("No active primary antivirus provider");
            result["SEC-AV-ACTIVE"] = Observation(
                "SEC-AV-ACTIVE",
                knownInactive ? SecurityControlStatus.Fail : SecurityControlStatus.Unknown,
                antivirus.Select(x => new SecurityEvidence("ProductState", x.Name + ":" + x.ProductState, x.Source)).ToList());
            return result;
        }

        var primary = active[0];
        var primaryKind = ProviderKind(primary.Name);
        var output = UnknownSet("Provider-specific state unavailable");
        output["SEC-AV-ACTIVE"] = Observation("SEC-AV-ACTIVE", SecurityControlStatus.Pass,
            [new("PrimaryProduct", primary.Name, primary.Source), new("ProductState", primary.ProductState, primary.Source)]);
        output["SEC-AV-PLATFORM"] = Observation(
            "SEC-AV-PLATFORM",
            IsState(primary.ProductState, "Expired") ? SecurityControlStatus.Fail : SecurityControlStatus.Unknown,
            [new("PrimaryProduct", primary.Name, primary.Source)]);

        if (primaryKind == AntivirusProviderKind.Defender)
        {
            DefenderSecurityObservation defender;
            try { defender = source.ReadDefender(); }
            catch (Exception ex)
            {
                return MergeActive(output, "Defender read failed: " + ex.GetType().Name);
            }
            output["SEC-AV-DEFINITIONS"] = DefinitionObservation(primary, defender.SignatureAgeDays, null, at, defender.Source);
            output["SEC-AV-RTP"] = DefenderRtp(defender);
            output["SEC-AV-TAMPER"] = BooleanObservation("SEC-AV-TAMPER", defender.TamperProtected, "TamperProtected", defender.Source);
            if (!string.IsNullOrWhiteSpace(defender.PlatformVersion))
                output["SEC-AV-PLATFORM"] = Observation("SEC-AV-PLATFORM", SecurityControlStatus.Unknown,
                    [new("PlatformVersion", defender.PlatformVersion!, defender.Source)]);
        }
        else if (primaryKind == AntivirusProviderKind.Kaspersky)
        {
            KasperskySecurityObservation kaspersky;
            try { kaspersky = source.ReadKaspersky(); }
            catch (Exception ex)
            {
                return MergeActive(output, "Kaspersky read failed: " + ex.GetType().Name);
            }
            output["SEC-AV-DEFINITIONS"] = DefinitionObservation(primary, null, kaspersky.DefinitionsUpdatedAt, at, kaspersky.Source);
            output["SEC-AV-RTP"] = BooleanObservation("SEC-AV-RTP", kaspersky.RealTimeProtectionEnabled, "RealTimeProtectionEnabled", kaspersky.Source);
            output["SEC-AV-TAMPER"] = Observation("SEC-AV-TAMPER", SecurityControlStatus.Unknown,
                [new("Provider", primary.Name, primary.Source)]);
            if (!string.IsNullOrWhiteSpace(kaspersky.ProductVersion))
                output["SEC-AV-PLATFORM"] = Observation("SEC-AV-PLATFORM", SecurityControlStatus.Unknown,
                    [new("PlatformVersion", kaspersky.ProductVersion!, kaspersky.Source)]);
        }
        else
        {
            output["SEC-AV-DEFINITIONS"] = DefinitionObservation(primary, null, null, at, primary.Source);
        }

        return output;
    }


    private static Dictionary<string, SecurityControlObservation> CollectWithoutWsc(
        IAntivirusSecuritySource source,
        DateTime now,
        string reason)
    {
        DefenderSecurityObservation? defender = null;
        KasperskySecurityObservation? kaspersky = null;
        try { defender = source.ReadDefender(); } catch { }
        try { kaspersky = source.ReadKaspersky(); } catch { }

        var defenderActive = defender?.AntivirusEnabled == true;
        var kasperskyActive = kaspersky?.RealTimeProtectionEnabled == true;

        if (defenderActive && kasperskyActive)
        {
            var ambiguous = UnknownSet(reason + "; multiple provider-specific sources report active protection");
            ambiguous["SEC-AV-ACTIVE"] = Observation(
                "SEC-AV-ACTIVE",
                SecurityControlStatus.Pass,
                [
                    new("ActiveProducts", "Microsoft Defender Antivirus; Kaspersky Endpoint Security", "Provider fallback"),
                    new("FallbackReason", reason, "Collector")
                ]);
            return ambiguous;
        }

        if (kasperskyActive && kaspersky is not null)
        {
            var primary = new SecurityCenterProduct(
                "Kaspersky Endpoint Security",
                "Antivirus",
                "On",
                kaspersky.DefinitionsUpdatedAt is null ? "Unknown" : "UpToDate",
                null,
                kaspersky.Source);
            var output = UnknownSet(reason);
            output["SEC-AV-ACTIVE"] = Observation(
                "SEC-AV-ACTIVE",
                SecurityControlStatus.Pass,
                [
                    new("PrimaryProduct", primary.Name, primary.Source),
                    new("ProductState", "On", primary.Source),
                    new("FallbackReason", reason, "Collector")
                ]);
            output["SEC-AV-DEFINITIONS"] = DefinitionObservation(primary, null, kaspersky.DefinitionsUpdatedAt, now, kaspersky.Source);
            output["SEC-AV-RTP"] = BooleanObservation("SEC-AV-RTP", kaspersky.RealTimeProtectionEnabled, "RealTimeProtectionEnabled", kaspersky.Source);
            output["SEC-AV-TAMPER"] = Observation("SEC-AV-TAMPER", SecurityControlStatus.Unknown,
                [new("Provider", primary.Name, primary.Source), new("FallbackReason", reason, "Collector")]);
            if (!string.IsNullOrWhiteSpace(kaspersky.ProductVersion))
                output["SEC-AV-PLATFORM"] = Observation("SEC-AV-PLATFORM", SecurityControlStatus.Unknown,
                    [new("PlatformVersion", kaspersky.ProductVersion!, kaspersky.Source), new("FallbackReason", reason, "Collector")]);
            return output;
        }

        if (defenderActive && defender is not null)
        {
            var primary = new SecurityCenterProduct(
                "Microsoft Defender Antivirus",
                "Antivirus",
                "On",
                "Unknown",
                null,
                defender.Source);
            var output = UnknownSet(reason);
            output["SEC-AV-ACTIVE"] = Observation(
                "SEC-AV-ACTIVE",
                SecurityControlStatus.Pass,
                [
                    new("PrimaryProduct", primary.Name, primary.Source),
                    new("ProductState", "On", primary.Source),
                    new("FallbackReason", reason, "Collector")
                ]);
            output["SEC-AV-DEFINITIONS"] = DefinitionObservation(primary, defender.SignatureAgeDays, null, now, defender.Source);
            output["SEC-AV-RTP"] = DefenderRtp(defender);
            output["SEC-AV-TAMPER"] = BooleanObservation("SEC-AV-TAMPER", defender.TamperProtected, "TamperProtected", defender.Source);
            if (!string.IsNullOrWhiteSpace(defender.PlatformVersion))
                output["SEC-AV-PLATFORM"] = Observation("SEC-AV-PLATFORM", SecurityControlStatus.Unknown,
                    [new("PlatformVersion", defender.PlatformVersion!, defender.Source), new("FallbackReason", reason, "Collector")]);
            return output;
        }

        return UnknownSet(reason + "; provider-specific fallback did not prove active protection");
    }

    private static Dictionary<string, SecurityControlObservation> MergeActive(
        Dictionary<string, SecurityControlObservation> current, string reason)
    {
        foreach (var id in new[] { "SEC-AV-PLATFORM", "SEC-AV-DEFINITIONS", "SEC-AV-RTP", "SEC-AV-TAMPER" })
            current[id] = Observation(id, SecurityControlStatus.Unknown, [new("CollectionWarning", reason, "Collector")]);
        return current;
    }

    private static SecurityControlObservation DefinitionObservation(
        SecurityCenterProduct primary,
        int? ageDays,
        DateTime? updatedAt,
        DateTime now,
        string source)
    {
        if (IsState(primary.SignatureState, "OutOfDate"))
            return Observation("SEC-AV-DEFINITIONS", SecurityControlStatus.Fail,
                [new("SignatureState", primary.SignatureState, primary.Source)]);

        int? age = ageDays;
        if (age is null && updatedAt is DateTime stamp)
            age = Math.Max(0, (int)Math.Floor((now - stamp).TotalDays));

        var status = age switch
        {
            <= 2 when age is not null => SecurityControlStatus.Pass,
            >= 3 and <= 7 => SecurityControlStatus.Warn,
            > 7 => SecurityControlStatus.Fail,
            _ when IsState(primary.SignatureState, "UpToDate") => SecurityControlStatus.Pass,
            _ => SecurityControlStatus.Unknown
        };
        var evidence = new List<SecurityEvidence>
        {
            new("SignatureState", primary.SignatureState, primary.Source)
        };
        if (age is int days) evidence.Add(new("SignatureAgeDays", days.ToString(), source));
        if (updatedAt is DateTime date) evidence.Add(new("DefinitionsUpdatedAt", date.ToString("O"), source));
        return Observation("SEC-AV-DEFINITIONS", status, evidence);
    }

    private static SecurityControlObservation DefenderRtp(DefenderSecurityObservation value)
    {
        SecurityControlStatus status;
        if (value.RealTimeProtectionEnabled is null) status = SecurityControlStatus.Unknown;
        else if (value.RealTimeProtectionEnabled == false) status = SecurityControlStatus.Fail;
        else if (value.BehaviorMonitorEnabled == false || value.IoavProtectionEnabled == false) status = SecurityControlStatus.Warn;
        else status = SecurityControlStatus.Pass;
        return Observation("SEC-AV-RTP", status,
            [
                new("RealTimeProtectionEnabled", Flag(value.RealTimeProtectionEnabled), value.Source),
                new("BehaviorMonitorEnabled", Flag(value.BehaviorMonitorEnabled), value.Source),
                new("IoavProtectionEnabled", Flag(value.IoavProtectionEnabled), value.Source)
            ]);
    }

    private static SecurityControlObservation BooleanObservation(string id, bool? value, string key, string source)
        => Observation(id, value switch
        {
            true => SecurityControlStatus.Pass,
            false => SecurityControlStatus.Fail,
            null => SecurityControlStatus.Unknown
        }, [new(key, Flag(value), source)]);

    private static Dictionary<string, SecurityControlObservation> UnknownSet(string reason)
        => new(StringComparer.Ordinal)
        {
            ["SEC-AV-ACTIVE"] = Observation("SEC-AV-ACTIVE", SecurityControlStatus.Unknown, [new("Reason", reason, "Collector")]),
            ["SEC-AV-PLATFORM"] = Observation("SEC-AV-PLATFORM", SecurityControlStatus.Unknown, [new("Reason", reason, "Collector")]),
            ["SEC-AV-DEFINITIONS"] = Observation("SEC-AV-DEFINITIONS", SecurityControlStatus.Unknown, [new("Reason", reason, "Collector")]),
            ["SEC-AV-RTP"] = Observation("SEC-AV-RTP", SecurityControlStatus.Unknown, [new("Reason", reason, "Collector")]),
            ["SEC-AV-TAMPER"] = Observation("SEC-AV-TAMPER", SecurityControlStatus.Unknown, [new("Reason", reason, "Collector")])
        };

    private static SecurityControlObservation Observation(string id, SecurityControlStatus status, IReadOnlyList<SecurityEvidence> evidence)
        => new(id, status, evidence, id);

    private static bool IsKnownInactive(string value)
        => IsState(value, "Off") || IsState(value, "Snoozed") || IsState(value, "Expired");

    private static bool IsState(string actual, string expected)
        => actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string Flag(bool? value) => value switch { true => "true", false => "false", null => "Unknown" };

    private static AntivirusProviderKind ProviderKind(string name)
    {
        if (name.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase)) return AntivirusProviderKind.Kaspersky;
        if (name.Contains("Defender", StringComparison.OrdinalIgnoreCase) || name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return AntivirusProviderKind.Defender;
        return AntivirusProviderKind.Other;
    }

    private enum AntivirusProviderKind { Defender, Kaspersky, Other }
}

internal sealed class AntivirusSecuritySource : IAntivirusSecuritySource
{
    public IReadOnlyList<SecurityCenterProduct> ReadSecurityCenterProducts()
        => WindowsSecurityCenterReader.ReadAntivirusProducts();
    public DefenderSecurityObservation ReadDefender() => DefenderSecurityReader.Read();
    public KasperskySecurityObservation ReadKaspersky() => KasperskySecurityReader.Read();
}
