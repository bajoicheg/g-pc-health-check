using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace G.PcHealthCheck;

internal static class PolicyArtifactsSelfTest
{
    public static int Run()
    {
        try
        {
            Require(LocalAdministratorsPolicy.PolicyPath == @"SOFTWARE\Policies\GPCHealthCheck",
                "Local Administrators machine policy Registry path drifted.");
            Require(LocalAdministratorsPolicy.ValueName == "AllowedLocalAdministrators",
                "Local Administrators machine policy value name drifted.");

            var load = typeof(LocalAdministratorsPolicy).GetMethod(
                "Load",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("LocalAdministratorsPolicy.Load is missing.");
            var parameters = load.GetParameters();
            Require(parameters.Length == 1 && parameters[0].ParameterType == typeof(ILocalAdminPolicySource),
                "Runtime policy loader must consume only ILocalAdminPolicySource, never a file/config path.");

            var sourceMethods = typeof(RegistryLocalAdminPolicySource).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Require(sourceMethods.Length == 1 && sourceMethods[0].Name == "Read" && sourceMethods[0].GetParameters().Length == 0,
                "Registry policy source API must remain fixed and read-only.");

            var resources = typeof(PolicyArtifactsSelfTest).Assembly.GetManifestResourceNames();
            Require(!resources.Any(x => x.EndsWith(".admx", StringComparison.OrdinalIgnoreCase)
                                        || x.EndsWith(".adml", StringComparison.OrdinalIgnoreCase)),
                "ADMX/ADML deployment artifacts must not be embedded runtime dependencies.");

            var synthetic = LocalAdministratorsPolicy.Load(new FakeSource(new(
                true,
                true,
                RegistryValueKind.MultiString,
                new[] { @"CONTOSO\adm-*", "SID:S-1-5-21-*-500" },
                "Synthetic Registry")));
            Require(synthetic.Configured && synthetic.Readable && synthetic.Patterns.Count == 2,
                "Registry-backed policy load must remain independent from adjacent files.");

            if (IsDotnetHost())
                RunRepositoryValidator();

            Console.WriteLine("Policy artifacts self-test: 1/1 passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Policy artifacts self-test: FAIL — " + ex.Message);
            return 241;
        }
    }

    private static bool IsDotnetHost()
        => string.Equals(
            Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

    private static void RunRepositoryValidator()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = Path.Combine(repositoryRoot, "tools", "policy", "Test-GPCHealthCheckAdmx.ps1");
        Require(File.Exists(script), "ADMX validator script is missing from the source checkout.");

        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-RepositoryRoot");
        start.ArgumentList.Add(repositoryRoot);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start ADMX validator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("ADMX validator exceeded 30 seconds.");
        }

        var output = stdout.GetAwaiter().GetResult().Trim();
        var error = stderr.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ADMX validator failed with exit code {process.ExitCode}: {(string.IsNullOrWhiteSpace(error) ? output : error)}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var project = Path.Combine(current.FullName, "src", "G.PcHealthCheck", "G.PcHealthCheck.csproj");
            if (File.Exists(project)) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate source repository root for ADMX validation.");
    }

    private sealed class FakeSource(LocalAdminPolicyReadResult value) : ILocalAdminPolicySource
    {
        public LocalAdminPolicyReadResult Read() => value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
