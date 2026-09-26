using Microsoft.Win32;
using System.Security;

namespace G.PcHealthCheck;

internal sealed record LocalAdminPolicyReadResult(
    bool ValuePresent,
    bool Readable,
    RegistryValueKind? Kind,
    object? Value,
    string Source);

internal interface ILocalAdminPolicySource
{
    LocalAdminPolicyReadResult Read();
}

internal static class LocalAdministratorsPolicy
{
    internal const string PolicyPath = @"SOFTWARE\Policies\GPCHealthCheck";
    internal const string ValueName = "AllowedLocalAdministrators";
    internal const string SourceName = "HKLM machine policy / Group Policy";

    public static LocalAdminPolicy Load(ILocalAdminPolicySource? source = null)
    {
        source ??= new RegistryLocalAdminPolicySource();
        LocalAdminPolicyReadResult raw;
        try { raw = source.Read(); }
        catch
        {
            return new(true, false, [], SourceName);
        }

        if (!raw.ValuePresent)
            return new(false, raw.Readable, [], raw.Source);

        if (!raw.Readable || raw.Kind != RegistryValueKind.MultiString || raw.Value is not string[] values)
            return new(true, false, [], raw.Source);

        var patterns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (seen.Add(trimmed)) patterns.Add(trimmed);
        }

        return new(true, true, patterns, raw.Source);
    }
}

internal sealed class RegistryLocalAdminPolicySource : ILocalAdminPolicySource
{
    public LocalAdminPolicyReadResult Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(LocalAdministratorsPolicy.PolicyPath, writable: false);
            if (key is null)
                return new(false, true, null, null, LocalAdministratorsPolicy.SourceName);

            var present = key.GetValueNames().Any(x => string.Equals(x, LocalAdministratorsPolicy.ValueName, StringComparison.OrdinalIgnoreCase));
            if (!present)
                return new(false, true, null, null, LocalAdministratorsPolicy.SourceName);

            RegistryValueKind kind;
            try { kind = key.GetValueKind(LocalAdministratorsPolicy.ValueName); }
            catch { return new(true, false, null, null, LocalAdministratorsPolicy.SourceName); }

            object? value;
            try
            {
                value = key.GetValue(
                    LocalAdministratorsPolicy.ValueName,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch
            {
                return new(true, false, kind, null, LocalAdministratorsPolicy.SourceName);
            }

            return new(true, true, kind, value, LocalAdministratorsPolicy.SourceName);
        }
        catch (UnauthorizedAccessException)
        {
            return new(true, false, null, null, LocalAdministratorsPolicy.SourceName);
        }
        catch (SecurityException)
        {
            return new(true, false, null, null, LocalAdministratorsPolicy.SourceName);
        }
    }
}
