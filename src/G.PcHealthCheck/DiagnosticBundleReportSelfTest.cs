using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class DiagnosticBundleReportSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }

        Test("missing source is warning not healthy", () =>
        {
            var snapshot = BundleFixture();
            snapshot.Events = Result<IncidentSnapshot>(DiagnosticBundleCategory.Events, "Unavailable", true);
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("События", StringComparison.Ordinal) && html.Contains("Недоступно", StringComparison.Ordinal));
            Require(!html.Contains("События: проблем нет", StringComparison.OrdinalIgnoreCase));
        });

        Test("HTML encodes workstation strings", () =>
        {
            var snapshot = BundleFixture("<script>x</script>&");
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(!html.Contains("<script>x</script>", StringComparison.Ordinal));
            Require(html.Contains("&lt;script&gt;x&lt;/script&gt;&amp;", StringComparison.Ordinal));
        });

        Test("manifest distinguishes NotRequested unavailable and cancelled", () =>
        {
            var snapshot = BundleFixture();
            snapshot.Events = Result<IncidentSnapshot>(DiagnosticBundleCategory.Events, "Unavailable", true);
            snapshot.Storage = Result<DiskDetailsSnapshot>(DiagnosticBundleCategory.Storage, "Cancelled", true);
            using var document = JsonDocument.Parse(DiagnosticBundleReport.ManifestJson(snapshot));
            var states = document.RootElement.GetProperty("Sources").EnumerateArray().ToDictionary(
                item => item.GetProperty("Category").GetString() ?? "",
                item => item.GetProperty("State").GetString() ?? "",
                StringComparer.Ordinal);
            Require(states["Events"] == "Unavailable");
            Require(states["Storage"] == "Cancelled");
            Require(states["Performance"] == "NotRequested");
        });

        Test("manifest is UTF-8 JSON with exact metadata", () =>
        {
            var snapshot = BundleFixture();
            var bytes = Encoding.UTF8.GetBytes(DiagnosticBundleReport.ManifestJson(snapshot));
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            Require(root.GetProperty("SchemaVersion").GetInt32() == snapshot.SchemaVersion);
            Require(root.GetProperty("ApplicationVersion").GetString() == "9.8.7");
            Require(root.GetProperty("Mode").GetString() == "Quick");
            Require(root.GetProperty("ComputerName").GetString() == "PC<01>");
        });

        Test("summary contains version mode context and source matrix", () =>
        {
            var summary = DiagnosticBundleReport.Summary(BundleFixture());
            Require(summary.Contains("9.8.7", StringComparison.Ordinal));
            Require(summary.Contains("Быстрый", StringComparison.Ordinal));
            Require(summary.Contains(@"DOMAIN\tech", StringComparison.Ordinal));
            Require(summary.Contains("Источники", StringComparison.Ordinal));
            Require(summary.Contains("Health Check", StringComparison.Ordinal));
            Require(summary.Contains("События", StringComparison.Ordinal));
        });

        Test("event highlights use only collected warning levels and group evidence", () =>
        {
            var snapshot = BundleFixture();
            var highlights = DiagnosticBundleCore.EventHighlights(snapshot.Events.Payload!);
            Require(highlights.Count == 2);
            Require(highlights.All(item => item.Level is >= 1 and <= 3));
            Require(highlights.Single(item => item.EventId == 42).Count == 2);
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("не доказывает причину", StringComparison.OrdinalIgnoreCase));
            Require(!html.Contains("Причина:", StringComparison.OrdinalIgnoreCase));
        });

        Test("process highlights rank available working sets only", () =>
        {
            var snapshot = BundleFixture();
            var rows = DiagnosticBundleCore.ProcessWorkingSetHighlights(snapshot.Processes.Payload!, 10);
            Require(rows.Count == 2);
            Require(rows[0].WorkingSetBytes == 900 && rows[1].WorkingSetBytes == 100);
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("Крупнейшие рабочие наборы", StringComparison.Ordinal));
            Require(!html.Contains("плохой процесс", StringComparison.OrdinalIgnoreCase));
        });

        Test("endpoint summary counts evidence and includes reachability caveat", () =>
        {
            var snapshot = BundleFixture();
            var counts = DiagnosticBundleCore.EndpointCounts(snapshot.Endpoints.Payload!);
            Require(counts.Total == 3 && counts.TcpListeners == 1 && counts.TcpEstablished == 1 && counts.UdpBindings == 1);
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("не доказывают доступность извне", StringComparison.OrdinalIgnoreCase));
        });

        Test("Extended report preserves performance markers", () =>
        {
            var snapshot = BundleFixture(mode: DiagnosticBundleMode.Extended);
            var html = DiagnosticBundleReport.Html(snapshot);
            Require(html.Contains("Сеанс производительности", StringComparison.Ordinal));
            Require(html.Contains("зависание &lt;окна&gt;", StringComparison.Ordinal));
            Require(html.Contains("performance.html", StringComparison.Ordinal));
        });

        Test("Quick report contains no invented performance section", () =>
        {
            var html = DiagnosticBundleReport.Html(BundleFixture());
            Require(!html.Contains("Сеанс производительности", StringComparison.Ordinal));
            Require(!html.Contains("performance.html", StringComparison.Ordinal));
        });

        Test("saving never overwrites and keeps folder before zip", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "g-pc-bundle-report-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var first = DiagnosticBundleReport.Save(BundleFixture(), root, createZip: true);
                var second = DiagnosticBundleReport.Save(BundleFixture(), root, createZip: true);
                Require(first.Folder != second.Folder);
                Require(Directory.Exists(first.Folder) && Directory.Exists(second.Folder));
                Require(first.Zip is not null && File.Exists(first.Zip));
                Require(second.Zip is not null && File.Exists(second.Zip));
                Require(File.Exists(Path.Combine(first.Folder, "summary.html")));
                Require(File.Exists(Path.Combine(first.Folder, "manifest.json")));
                Require(File.Exists(Path.Combine(first.Folder, "health.json")));
                Require(File.Exists(Path.Combine(first.Folder, "processes.json")));
                using var archive = ZipFile.OpenRead(first.Zip!);
                Require(archive.Entries.Any(entry => entry.FullName == "summary.html"));
                Require(archive.Entries.Any(entry => entry.FullName == "manifest.json"));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        });

        Console.WriteLine($"Diagnostic bundle report regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 250;
    }

    private static DiagnosticBundleSnapshot BundleFixture(string processName = "alpha.exe",
        DiagnosticBundleMode mode = DiagnosticBundleMode.Quick)
    {
        var started = DateTimeOffset.Parse("2026-01-01T10:00:00+03:00");
        var finished = started.AddMinutes(1);
        var options = DiagnosticBundleCore.DefaultOptions(mode);
        var data = new DiagnosticData
        {
            System = new SystemInfo { ComputerName = "PC<01>", UserName = "tech" },
            CollectionWarnings = []
        };
        var assessment = new ScanResult
        {
            Data = data,
            Assessment = new Assessment
            {
                Score = 88,
                Status = "WARN",
                CoveragePercent = 91,
                CoverageStatus = "HIGH",
                Findings =
                [
                    new Finding { Severity = "WARN", Category = "Память", Title = "Synthetic warning", Value = "91", Recommendation = "Проверить evidence." }
                ]
            }
        };
        var processes = new ProcessReviewSnapshot
        {
            State = "Complete",
            StartedAt = started,
            FinishedAt = finished,
            Processes =
            [
                new ProcessReviewEntry { Pid = 10, Name = processName, WorkingSetBytes = 900 },
                new ProcessReviewEntry { Pid = 11, Name = "beta.exe", WorkingSetBytes = 100 },
                new ProcessReviewEntry { Pid = 12, Name = "unknown.exe", WorkingSetBytes = null }
            ]
        };
        var endpoints = new EndpointSnapshot
        {
            State = "Complete",
            StartedAt = started,
            FinishedAt = finished,
            Tables =
            [
                new EndpointTable
                {
                    Name = "TCP4", State = "Complete", Rows =
                    [
                        new EndpointRow { Table = "TCP4", Protocol = "TCP", Family = "IPv4", LocalAddress = "0.0.0.0", LocalPort = 80, Pid = 10, State = "LISTEN", ProcessName = processName },
                        new EndpointRow { Table = "TCP4", Protocol = "TCP", Family = "IPv4", LocalAddress = "192.0.2.10", LocalPort = 50000, RemoteAddress = "198.51.100.10", RemotePort = 443, Pid = 11, State = "ESTABLISHED", ProcessName = "beta.exe" }
                    ]
                },
                new EndpointTable
                {
                    Name = "UDP4", State = "Complete", Rows =
                    [
                        new EndpointRow { Table = "UDP4", Protocol = "UDP", Family = "IPv4", LocalAddress = "0.0.0.0", LocalPort = 5353, Pid = 12, State = "BOUND", ProcessName = "unknown.exe" }
                    ]
                }
            ]
        };
        var events = new IncidentSnapshot
        {
            State = "Complete",
            StartedAt = started,
            FinishedAt = finished,
            Window = new IncidentWindow(started.AddHours(-1), started, 1000),
            Logs =
            [
                new IncidentLogResult
                {
                    Log = "Application", State = "Complete", Events =
                    [
                        new IncidentEvent { Log = "Application", Timestamp = started.AddMinutes(-2), EventId = 42, Level = 3, Provider = "Demo", Message = "warning one" },
                        new IncidentEvent { Log = "Application", Timestamp = started.AddMinutes(-1), EventId = 42, Level = 3, Provider = "Demo", Message = "warning two" },
                        new IncidentEvent { Log = "Application", Timestamp = started, EventId = 100, Level = 4, Provider = "Info", Message = "information" }
                    ]
                },
                new IncidentLogResult
                {
                    Log = "System", State = "Complete", Events =
                    [
                        new IncidentEvent { Log = "System", Timestamp = started.AddMinutes(-3), EventId = 7, Level = 2, Provider = "Kernel", Message = "error" }
                    ]
                }
            ]
        };
        var storage = new DiskDetailsSnapshot
        {
            Outcome = "Completed",
            StartedAt = started,
            FinishedAt = finished,
            Disks =
            [
                new PhysicalDiskDetail { DeviceId = "0", Name = "Disk & 1", Health = 1, Firmware = "FW<1>" }
            ]
        };
        var performance = new PerformanceSessionSnapshot
        {
            Options = new PerformanceSessionOptions(60, 2),
            Outcome = "Completed",
            StartedAt = started,
            FinishedAt = finished,
            ElapsedMs = 60000,
            Samples =
            [
                new PerformanceSample(2000, 2, started.AddSeconds(2), new PerformanceReading(10, 20, 5, 0, []))
            ],
            Markers = [new PerformanceMarker(5000, "зависание <окна>")]
        };

        return new DiagnosticBundleSnapshot
        {
            SchemaVersion = 1,
            ApplicationVersion = "9.8.7",
            ComputerName = "PC<01>",
            Options = options,
            ExecutionContext = new ExecutionContextInfo
            {
                CapturedAt = started,
                ProcessAccount = @"DOMAIN\tech",
                ProcessSid = "S-1-5-21-test",
                ProcessProfile = @"C:\Users\tech",
                SessionId = 1,
                IsElevated = false,
                HasAdministratorToken = false,
                AdministratorMember = false,
                SessionAccount = @"DOMAIN\tech",
                SessionSid = "S-1-5-21-test",
                SessionProfile = @"C:\Users\tech",
                ProfileSource = "Synthetic"
            },
            StartedAt = started,
            FinishedAt = finished,
            Outcome = "Complete",
            Health = Result(DiagnosticBundleCategory.Health, "Complete", true, new DiagnosticBundleHealthPayload(data, assessment)),
            Processes = Result(DiagnosticBundleCategory.Processes, "Complete", true, processes),
            Endpoints = Result(DiagnosticBundleCategory.Endpoints, "Complete", true, endpoints),
            Events = Result(DiagnosticBundleCategory.Events, "Complete", true, events),
            Storage = Result(DiagnosticBundleCategory.Storage, "Complete", true, storage),
            Performance = mode == DiagnosticBundleMode.Extended
                ? Result(DiagnosticBundleCategory.Performance, "Complete", true, performance)
                : Result<PerformanceSessionSnapshot>(DiagnosticBundleCategory.Performance, "NotRequested", false)
        };
    }

    private static BundleSourceResult<T> Result<T>(DiagnosticBundleCategory category, string state, bool requested, T? payload = null)
        where T : class
        => new()
        {
            Category = category,
            State = state,
            Requested = requested,
            StartedAt = requested ? DateTimeOffset.Parse("2026-01-01T10:00:00+03:00") : default,
            FinishedAt = requested ? DateTimeOffset.Parse("2026-01-01T10:01:00+03:00") : default,
            Payload = payload
        };

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected diagnostic bundle report invariant was not satisfied.");
    }
}
