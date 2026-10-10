using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace G.PcHealthCheck;

internal static class SecurityLocalizationCoverageSelfTest
{
    private static readonly Regex Cyrillic = new("[А-Яа-яЁё]", RegexOptions.CultureInvariant);

    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security localization coverage self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security localization coverage self-test: FAIL — {name}: {ex.GetBaseException().Message}"); }
        }

        Test("Security resource key sets are identical and non-empty in Russian and English", () =>
        {
            var manager = new ResourceManager("G.PcHealthCheck.Resources.SecurityStrings", typeof(AppLocalization).Assembly);
            var ru = Read(manager, CultureInfo.GetCultureInfo("ru-RU"));
            var en = Read(manager, CultureInfo.GetCultureInfo("en-US"));
            Require(ru.Count >= 50, $"Security resource set is unexpectedly small: {ru.Count} keys.");
            Require(ru.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(en.Keys),
                "Russian and English Security resource key sets differ.");
            foreach (var key in ru.Keys)
            {
                Require(!string.IsNullOrWhiteSpace(ru[key]), "Blank Russian Security resource: " + key);
                Require(!string.IsNullOrWhiteSpace(en[key]), "Blank English Security resource: " + key);
                Require(AppLocalization.TextForCulture("ru", key) != key, "Russian Security resource does not resolve: " + key);
                Require(AppLocalization.TextForCulture("en", key) != key, "English Security resource does not resolve: " + key);
            }
        });

        Test("English Security operator framing contains no Cyrillic", () =>
        {
            var manager = new ResourceManager("G.PcHealthCheck.Resources.SecurityStrings", typeof(AppLocalization).Assembly);
            var en = Read(manager, CultureInfo.GetCultureInfo("en-US"));
            var leaks = en.Where(x => Cyrillic.IsMatch(x.Value)).Select(x => x.Key + "=" + x.Value).ToList();
            Require(leaks.Count == 0, "English Security resources contain Cyrillic framing: " + string.Join(" | ", leaks.Take(8)));

            var plan = new SecurityHardeningPlan
            {
                Actions =
                [
                    new("SecurityUpdateAvDefinitions", SecurityHardeningActionState.NeedsUac, "DefinitionsStale", SecurityPrimaryProvider.Defender, true, "PrimaryAvDefinitions"),
                    new("SecurityEnablePrimaryRtp", SecurityHardeningActionState.Unavailable, "AlreadySatisfied", SecurityPrimaryProvider.Defender, true, "DefenderRealtimeProtection"),
                    new("SecurityEnableWindowsFirewall", SecurityHardeningActionState.BlockedByPolicy, "BlockedByPolicy", SecurityPrimaryProvider.Defender, true, "WindowsFirewallProfiles")
                ]
            };
            var confirmation = SecurityHardeningPresentation.BuildConfirmation(plan, "en");
            Require(!Cyrillic.IsMatch(confirmation), "English hardening confirmation contains Cyrillic operator framing.");
        });

        Test("stable Security control and hardening action IDs are language independent", () =>
        {
            var controls = SecurityControlCatalog.All.Select(x => x.Id).ToArray();
            var actions = SecurityHardeningActionRegistry.All.Select(x => x.Id).ToArray();
            Require(controls.Length == 16 && controls.All(x => x.StartsWith("SEC-", StringComparison.Ordinal)),
                "Security control stable-ID contract drifted.");
            Require(actions.SequenceEqual(new[]
            {
                "SecurityUpdateAvDefinitions",
                "SecurityEnablePrimaryRtp",
                "SecurityEnableWindowsFirewall"
            }, StringComparer.Ordinal), "Security hardening stable-ID contract drifted.");
            foreach (var language in new[] { "ru", "en" })
            {
                foreach (var id in controls)
                    Require(id == SecurityControlCatalog.Find(id)?.Id, "Security control ID changed with presentation language: " + language);
                foreach (var id in actions)
                    Require(id == SecurityHardeningActionRegistry.Find(id)?.Id, "Security hardening action ID changed with presentation language: " + language);
            }
        });

        Console.WriteLine($"Security localization coverage self-test: {3 - failures}/3 passed.");
        return failures == 0 ? 0 : 246;
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: true)
            ?? throw new InvalidOperationException("Security resource set is unavailable for " + culture.Name);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key && entry.Value is string value && key.StartsWith("Security.", StringComparison.Ordinal))
                result[key] = value;
        }
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
