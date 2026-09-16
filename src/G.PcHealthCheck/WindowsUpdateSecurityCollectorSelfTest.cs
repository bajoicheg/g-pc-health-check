namespace G.PcHealthCheck;

internal static class WindowsUpdateSecurityCollectorSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Test(string name, Action body)
        {
            try { body(); Console.WriteLine($"Windows Update security self-test: PASS — {name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Windows Update security self-test: FAIL — {name}: {ex.Message}"); }
        }

        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local);

        Test("fresh history with no pending qualifying updates passes", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(now.AddDays(-20), 0, false, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Pass, "Fresh update posture must pass.");
        });

        Test("pending qualifying update warns immediately", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(now.AddDays(-10), 2, false, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Warn, "Pending qualifying update must warn.");
        });

        Test("46 to 60 day history warns", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(now.AddDays(-50), 0, false, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Warn, "46-60 day update age must warn.");
        });

        Test("older than 60 days fails", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(now.AddDays(-61), 0, false, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Fail, ">60 day update age must fail.");
        });

        Test("missing trustworthy history is unknown", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(null, 0, false, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Unknown, "Missing qualifying history must be Unknown.");
        });

        Test("source failure is unknown, never healthy", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new ThrowingSource(), now);
            Require(result.Status == SecurityControlStatus.Unknown, "WUA failure must be Unknown.");
        });

        Test("pending reboot is preserved as evidence without inventing update failure", () =>
        {
            var result = WindowsUpdateSecurityCollector.Collect(new FakeSource(new(now.AddDays(-5), 0, true, "ManagedService", "WUA")), now);
            Require(result.Status == SecurityControlStatus.Pass, "Generic pending reboot alone must not imply stale OS updates.");
            Require(result.Evidence.Any(x => x.Key == "PendingReboot" && x.Value == "true"), "Pending reboot evidence missing.");
            Require(result.Evidence.Any(x => x.Key == "UpdateService" && x.Value == "ManagedService"), "Configured service evidence missing.");
        });

        Console.WriteLine($"Windows Update security self-test: {7 - failures}/7 passed.");
        return failures == 0 ? 0 : 1;
    }

    private sealed class FakeSource(WindowsUpdateSecurityObservation value) : IWindowsUpdateSecuritySource
    {
        public WindowsUpdateSecurityObservation Read() => value;
    }

    private sealed class ThrowingSource : IWindowsUpdateSecuritySource
    {
        public WindowsUpdateSecurityObservation Read() => throw new InvalidOperationException("synthetic WUA failure");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
