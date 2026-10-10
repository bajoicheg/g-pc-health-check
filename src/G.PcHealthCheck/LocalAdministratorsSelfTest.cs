using Microsoft.Win32;

namespace G.PcHealthCheck;

internal static class LocalAdministratorsSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Local administrators self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Local administrators self-test: FAIL — {name}: {ex.Message}"); }
        }

        Test("glob language is anchored case-insensitive and deterministic", () =>
        {
            Require(GlobMatcher.IsMatch("DOMAIN\\adm-*", "domain\\ADM-alex"), "* failed.");
            Require(GlobMatcher.IsMatch("DOMAIN\\adm-????", "DOMAIN\\adm-john"), "? failed.");
            Require(!GlobMatcher.IsMatch("DOMAIN\\adm-????", "DOMAIN\\adm-alex1"), "? length drifted.");
            Require(!GlobMatcher.IsMatch("adm-*", "DOMAIN\\adm-alex"), "Matcher must be whole-string.");
            Require(GlobMatcher.IsMatch("DOMAIN\\adm-[x]", "domain\\ADM-[x]"), "Non-wildcard characters must be literal.");
            Require(!GlobMatcher.IsMatch("DOMAIN\\adm-[x]", "DOMAIN\\adm-x"), "Character-class semantics must not appear.");
            Require(GlobMatcher.IsMatch("LOCAL\\%USERNAME%", "local\\%username%"), "Environment syntax must remain literal text.");
            Require(!GlobMatcher.IsMatch("LOCAL\\%USERNAME%", "LOCAL\\alice"), "Environment expansion must not occur.");
        });

        Test("machine REG_MULTI_SZ policy normalizes blanks and duplicates", () =>
        {
            var raw = new LocalAdminPolicyReadResult(
                true,
                true,
                RegistryValueKind.MultiString,
                new[] { " DOMAIN\\adm-* ", "", "domain\\ADM-*", " SID:S-1-5-21-*-500 ", "   " },
                "Synthetic HKLM");
            var policy = LocalAdministratorsPolicy.Load(new FakePolicySource(raw));
            Require(policy.Configured && policy.Readable, "Valid REG_MULTI_SZ must be configured/readable.");
            Require(policy.Patterns.Count == 2, "Blanks/duplicates must be removed.");
            Require(policy.Patterns[0] == "DOMAIN\\adm-*", "Patterns must be trimmed without rewriting identity text.");
            Require(policy.Patterns[1] == "SID:S-1-5-21-*-500", "SID rule normalization drifted.");
        });

        Test("missing wrong-type unreadable and explicitly empty policy remain distinct", () =>
        {
            var missing = LocalAdministratorsPolicy.Load(new FakePolicySource(new(false, true, null, null, "Synthetic")));
            Require(!missing.Configured && missing.Readable, "Missing policy must be unconfigured, not unreadable.");

            var wrongType = LocalAdministratorsPolicy.Load(new FakePolicySource(new(true, true, RegistryValueKind.String, "DOMAIN\\admin", "Synthetic")));
            Require(wrongType.Configured && !wrongType.Readable, "Wrong Registry type must be invalid/Unknown.");

            var unreadable = LocalAdministratorsPolicy.Load(new FakePolicySource(new(true, false, null, null, "Synthetic")));
            Require(unreadable.Configured && !unreadable.Readable, "Unreadable policy must remain Unknown.");

            var empty = LocalAdministratorsPolicy.Load(new FakePolicySource(new(true, true, RegistryValueKind.MultiString, Array.Empty<string>(), "Synthetic")));
            Require(empty.Configured && empty.Readable && empty.Patterns.Count == 0, "Explicit empty policy must be a valid permit-nobody list.");
        });

        Test("allowed user local user renamed Administrator and direct group pass", () =>
        {
            var policy = Policy(
                "DOMAIN\\adm-*",
                "LOCAL\\svc-admin",
                "SID:S-1-5-21-*-500",
                "DOMAIN\\Helpdesk Admins");
            var result = LocalAdministratorsCollector.Evaluate(policy,
            [
                Principal("domain\\ADM-alex", "S-1-5-21-1-2-3-1100", "User", "ActiveDirectory", true),
                Principal("PC01\\svc-admin", "S-1-5-21-1-2-3-1101", "User", "Local", true),
                Principal("PC01\\RenamedRoot", "S-1-5-21-111-222-333-500", "User", "Local", true),
                Principal("DOMAIN\\Helpdesk Admins", "S-1-5-21-1-2-3-2100", "Group", "ActiveDirectory", true)
            ], "PC01");

            Require(result.Control.Status == SecurityControlStatus.Pass, "All allowed direct principals must pass.");
            Require(result.Members.All(x => x.Result == "Allowed"), "Allowed member result drifted.");
            Require(result.Members.Any(x => x.Name == "LOCAL\\svc-admin"), "Computer-local prefix must normalize to LOCAL\\.");
            var renamed = result.Members.Single(x => x.Sid?.EndsWith("-500", StringComparison.Ordinal) == true);
            Require(renamed.MatchedRule == "SID:S-1-5-21-*-500", "Renamed built-in Administrator must match by SID wildcard.");
            Require(result.Members.Any(x => x.Type == "Group" && x.Result == "Allowed"), "Allowed direct groups must be accepted without recursive expansion.");
        });

        Test("unresolved name with matching SID rule is allowed", () =>
        {
            var result = LocalAdministratorsCollector.Evaluate(
                Policy("SID:S-1-5-21-*-500", "DOMAIN\\adm-*"),
                [Principal(null, "S-1-5-21-100-200-300-500", "User", "Unknown", false)],
                "PC01");
            Require(result.Control.Status == SecurityControlStatus.Pass, "Matching SID must be sufficient when name resolution fails.");
            Require(result.Members.Single().Result == "Allowed", "SID-matched unresolved member must be Allowed.");
        });

        Test("unresolved name without sufficient evidence is Unknown not Fail", () =>
        {
            var result = LocalAdministratorsCollector.Evaluate(
                Policy("DOMAIN\\adm-*"),
                [Principal(null, "S-1-5-21-100-200-300-1234", "User", "Unknown", false)],
                "PC01");
            Require(result.Control.Status == SecurityControlStatus.Unknown, "Unresolved name with unevaluable name rules must be Unknown.");
            Require(result.Members.Single().Result == "Unknown", "Unresolved member must not become false Unauthorized.");
        });

        Test("unauthorized member wins aggregate over additional Unknown evidence", () =>
        {
            var result = LocalAdministratorsCollector.Evaluate(
                Policy("DOMAIN\\adm-*"),
                [
                    Principal("DOMAIN\\ordinary-user", "S-1-5-21-1-2-3-1300", "User", "ActiveDirectory", true),
                    Principal(null, "S-1-5-21-1-2-3-1400", "User", "Unknown", false)
                ],
                "PC01");
            Require(result.Control.Status == SecurityControlStatus.Fail, "Conclusive unauthorized member must make aggregate Fail.");
            Require(result.Members.Any(x => x.Result == "Unauthorized"), "Unauthorized evidence missing.");
            Require(result.Members.Any(x => x.Result == "Unknown"), "Additional incomplete evidence must be retained.");
        });

        Test("explicit empty policy permits nobody", () =>
        {
            var result = LocalAdministratorsCollector.Evaluate(
                new LocalAdminPolicy(true, true, [], "Synthetic"),
                [Principal("PC01\\LocalAdmin", "S-1-5-21-1-2-3-1500", "User", "Local", true)],
                "PC01");
            Require(result.Control.Status == SecurityControlStatus.Fail, "Configured empty list plus an actual member must fail.");
        });

        Test("Administrators discovery is SID-based and control remains weighted critical", () =>
        {
            Require(LocalAdministratorsWindowsSource.AdministratorsSid == "S-1-5-32-544", "Administrators group must be discovered by well-known SID.");
            var descriptor = SecurityControlCatalog.Find("SEC-LOCAL-ADMINS");
            Require(descriptor is not null && descriptor.Weight == 8 && descriptor.FailBandCap == SecurityBand.Low,
                "Local administrators weight/critical cap drifted.");
        });

        Console.WriteLine($"Local administrators self-test: {9 - failures}/9 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static LocalAdminPolicy Policy(params string[] patterns)
        => new(true, true, patterns, "Synthetic machine policy");

    private static LocalAdminPrincipalObservation Principal(string? name, string? sid, string type, string source, bool resolved)
        => new(name, sid, type, source, resolved);

    private sealed class FakePolicySource(LocalAdminPolicyReadResult value) : ILocalAdminPolicySource
    {
        public LocalAdminPolicyReadResult Read() => value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
