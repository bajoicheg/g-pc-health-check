using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class SecurityEvidenceSafetySelfTest
{
    private static readonly string[] SecretMarkers =
    [
        "111111-222222-333333-444444-555555-666666-777777-888888",
        "Synthetic-BIOS-Password!",
        "synthetic-av-token-secret-123"
    ];

    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Security evidence safety self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Security evidence safety self-test: FAIL — {name}: {ex.GetBaseException().Message}"); }
        }

        Test("serialized hardening result excludes raw native output and synthetic secrets", () =>
        {
            var batch = new SecurityHardeningBatchResult
            {
                SessionId = "00000000-1111-2222-3333-444444444444",
                StartedAt = new DateTime(2026, 9, 16, 12, 0, 0),
                FinishedAt = new DateTime(2026, 9, 16, 12, 0, 1),
                Elevated = true,
                Actions =
                [
                    new SecurityActionResult
                    {
                        Id = "SecurityUpdateAvDefinitions",
                        Success = false,
                        Message = "Synthetic bounded public result",
                        Output = string.Join(" | ", SecretMarkers),
                        TargetScope = "PrimaryAvDefinitions"
                    }
                ]
            };
            var json = JsonSerializer.Serialize(batch);
            foreach (var marker in SecretMarkers)
                Require(!json.Contains(marker, StringComparison.Ordinal), "Raw native secret leaked through serialized hardening result: " + marker);

            var output = typeof(SecurityActionResult).GetProperty(nameof(SecurityActionResult.Output))
                ?? throw new InvalidOperationException("SecurityActionResult.Output is missing.");
            Require(output.GetCustomAttribute<JsonIgnoreAttribute>() is not null,
                "Raw Security operation output must be explicitly JsonIgnore at the model boundary.");
        });

        Test("automatic hardening registry remains exact and contains no weakening or generic mutation class", () =>
        {
            var ids = SecurityHardeningActionRegistry.All.Select(x => x.Id).ToArray();
            Require(ids.SequenceEqual(new[]
            {
                "SecurityUpdateAvDefinitions",
                "SecurityEnablePrimaryRtp",
                "SecurityEnableWindowsFirewall"
            }, StringComparer.Ordinal), "Automatic Security hardening registry is not the approved three-action allow-list.");

            var forbidden = new[]
            {
                "Disable", "Exclude", "Exclusion", "Uninstall", "Remove", "Tamper", "Bypass",
                "BitLocker", "RecoveryKey", "Firmware", "Bios", "BootOrder", "LocalAdmin",
                "WindowsUpdate", "InstallUpdate", "Uac", "Tpm", "Vbs", "Hvci", "Command",
                "Registry", "Service", "Principal", "Password", "Token"
            };
            foreach (var id in ids)
                foreach (var fragment in forbidden)
                    Require(!id.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                        $"Forbidden weakening/generic mutation class leaked into hardening registry: {id} ({fragment}).");
            Require(SecurityHardeningActionRegistry.Find("SecurityEnableKasperskyRtp") is null,
                "Kaspersky RTP must remain outside the automatic hardening registry.");
        });

        Test("hardening operation API exposes only fixed typed mutation surfaces", () =>
        {
            var methods = typeof(ISecurityHardeningOperations).GetMethods();
            Require(methods.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                new[] { "EnableDefenderRealtimeProtection", "EnableWindowsFirewall", "UpdateDefinitions" }, StringComparer.Ordinal),
                "Security hardening operations interface drifted.");

            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (method.Name == "EnableWindowsFirewall"
                        && parameter.ParameterType == typeof(IReadOnlyList<string>)
                        && string.Equals(parameter.Name, "fixedProfiles", StringComparison.Ordinal))
                        continue;
                    Require(parameter.ParameterType != typeof(string),
                        $"Security hardening operation exposes a free-form string parameter: {method.Name}.{parameter.Name}");
                    var name = parameter.Name ?? string.Empty;
                    Require(!new[] { "executable", "path", "command", "registry", "service", "principal", "password", "token" }
                        .Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)),
                        $"Security hardening operation exposes a generic mutation parameter: {method.Name}.{name}");
                }
            }
        });

        Console.WriteLine($"Security evidence safety self-test: {3 - failures}/3 passed.");
        return failures == 0 ? 0 : 247;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
