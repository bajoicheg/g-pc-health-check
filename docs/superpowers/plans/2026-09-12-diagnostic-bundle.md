# Diagnostic Bundle for Service Desk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one explicit read-only workflow that composes existing diagnostics into a coherent Service Desk evidence package with Quick and Extended modes, source-level completeness, privacy controls, and unique HTML/JSON output.

**Architecture:** Add a bundle coordinator and versioned bundle snapshot that wrap existing `DiagnosticsService`, `AssessmentService`, `IncidentEvents`, `ProcessReview`, `EndpointReviewService`, `DiskDetailsService`, and `PerformanceSessionService` rather than duplicating provider logic. Keep orchestration separate from WinForms: the form selects categories/mode and starts/stops the coordinator; report/package code consumes the completed snapshot. Source failures remain independent, and Extended performance runs as an explicit sequential phase after point-in-time collectors.

**Tech Stack:** C# / .NET 8, WinForms, existing WMI/Event Log/IP Helper/storage/performance collectors, `System.Text.Json`, local HTML/ZIP packaging, existing Windows GitHub Actions CI.

**Spec:** `docs/superpowers/specs/2026-09-12-diagnostic-bundle-design.md`

## Global Constraints

- One portable Windows x64 single-file EXE; arbitrary valid executable folder/name remains supported.
- Collection uses the current GUI process rights; no hidden elevation or privileged diagnostic broker.
- No new repair/action command and no changes to UAC, worker allow-list, Temp preview/deletion rules, health thresholds or coverage semantics.
- Quick mode never runs active DNS/TCP probes or timed performance observation.
- Extended mode adds only whole-machine performance observation; initial default is 60 seconds / 2 seconds, selectable duration 30/60 seconds and interval 1/2/5 seconds.
- Unknown/failed/partial telemetry never becomes a healthy value.
- Cancellation is cooperative; completed source results survive and remain exportable.
- No automatic upload, reputation lookup, generic redaction engine, stress workload, process kill, service restart, network reset, startup change, driver install or reboot.
- HTML-encode all workstation strings; JSON is UTF-8; output is unique/create-new and never overwrites prior bundles.
- Hosted Windows CI is not corporate Windows 11 acceptance.

---

## File Structure

Create focused bundle files instead of enlarging existing diagnostic forms:

- `src/G.PcHealthCheck/DiagnosticBundleModels.cs` — enums/categories, source envelopes, bundle snapshot and outcome rules inputs.
- `src/G.PcHealthCheck/DiagnosticBundleCore.cs` — default category sets, deterministic outcome calculation, source-state mapping, highlight selection helpers.
- `src/G.PcHealthCheck/DiagnosticBundleService.cs` — coordinator and injectable collector interfaces/adapters.
- `src/G.PcHealthCheck/DiagnosticBundleReport.cs` — summary HTML, manifest/source JSON serialization and package assembly.
- `src/G.PcHealthCheck/DiagnosticBundleForm.cs` — idle-on-open WinForms workflow and menu attachment.
- `src/G.PcHealthCheck/DiagnosticBundleSelfTest.cs` — pure behavior/regression cases.
- `src/G.PcHealthCheck/DiagnosticBundleIntegrationSelfTest.cs` — safe Windows/native/UI/export cases.

Modify only where needed:

- `src/G.PcHealthCheck/Program.cs` — self-test and menu wiring.
- `src/G.PcHealthCheck/MainForm.cs` only if the existing Analysis menu cannot be reused through the same attachment pattern used by the other tools.
- `src/G.PcHealthCheck/G.PcHealthCheck.csproj` — final version bump to 0.15.0 after feature behavior is green.
- `README.md`, `CHANGELOG.md`, `docs/releases/0.15.0.md`, `docs/releases/0.15.0-validation.md` — final behavior and validation evidence.

Do **not** restructure existing collectors as part of this release unless one cannot be called outside a form. If extraction is required, move only the smallest orchestration method into a non-UI helper and retain identical standalone-tool semantics with regression coverage.

---

### Task 1: Versioned bundle model, category defaults, and completeness semantics

**Files:**
- Create: `src/G.PcHealthCheck/DiagnosticBundleModels.cs`
- Create: `src/G.PcHealthCheck/DiagnosticBundleCore.cs`
- Create: `src/G.PcHealthCheck/DiagnosticBundleSelfTest.cs`
- Modify: `src/G.PcHealthCheck/Program.cs`

**Interfaces:**
- Produces:
  - `internal enum DiagnosticBundleMode { Quick, Extended }`
  - `internal enum DiagnosticBundleCategory { Health, Processes, Endpoints, Events, Storage, Performance }`
  - `internal sealed record DiagnosticBundleOptions(DiagnosticBundleMode Mode, IReadOnlySet<DiagnosticBundleCategory> Categories, int PerformanceSeconds = 60, int PerformanceIntervalSeconds = 2)`
  - `internal sealed class BundleSourceResult<T>` with `Category`, `State`, `StartedAt`, `FinishedAt`, `Requested`, `Payload`, `Warnings`.
  - `internal sealed class DiagnosticBundleSnapshot` with schema/application/computer/context/timestamps/outcome/options/source payloads/cancellation marker.
  - `DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode mode)`
  - `DiagnosticBundleCore.CalculateOutcome(DiagnosticBundleSnapshot snapshot)`
  - `DiagnosticBundleCore.Validate(DiagnosticBundleOptions options)`

- [ ] **Step 1: Write failing defaults/outcome tests**

Add tests that explicitly verify:

```csharp
Test("Quick defaults exclude performance", () =>
{
    var o = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Quick);
    Require(o.Categories.SetEquals(new[] {
        DiagnosticBundleCategory.Health, DiagnosticBundleCategory.Processes,
        DiagnosticBundleCategory.Endpoints, DiagnosticBundleCategory.Events,
        DiagnosticBundleCategory.Storage }), "Quick defaults changed.");
    Require(!o.Categories.Contains(DiagnosticBundleCategory.Performance), "Quick requested timed performance.");
});

Test("Extended defaults include performance 60s 2s", () =>
{
    var o = DiagnosticBundleCore.DefaultOptions(DiagnosticBundleMode.Extended);
    Require(o.Categories.Contains(DiagnosticBundleCategory.Performance)
        && o.PerformanceSeconds == 60 && o.PerformanceIntervalSeconds == 2,
        "Extended defaults wrong.");
});

Test("NotRequested is distinct from unavailable", () =>
{
    var s = BundleWith(
        Source(DiagnosticBundleCategory.Health, "Complete", requested: true, payload: new object()),
        Source(DiagnosticBundleCategory.Events, "NotRequested", requested: false));
    Require(DiagnosticBundleCore.CalculateOutcome(s) == "Complete", "Opt-out reduced completeness.");
});

Test("useful partial bundle remains Partial", () =>
{
    var s = BundleWith(
        Source(DiagnosticBundleCategory.Health, "Complete", true, new object()),
        Source(DiagnosticBundleCategory.Events, "Unavailable", true));
    Require(DiagnosticBundleCore.CalculateOutcome(s) == "Partial", "Useful evidence lost to unavailable source.");
});

Test("cancel before useful evidence is Cancelled", () =>
{
    var s = BundleWith(Source(DiagnosticBundleCategory.Health, "Cancelled", true));
    s.CancellationRequested = true;
    Require(DiagnosticBundleCore.CalculateOutcome(s) == "Cancelled", "Empty cancellation not preserved.");
});
```

Also cover: all requested unavailable => `Unavailable`; cancellation after one completed payload => `Partial`; invalid Extended duration/interval; Performance selected in Quick => validation failure; zero requested categories => validation failure.

- [ ] **Step 2: Run Windows self-test and verify RED**

Run through the normal branch CI or local Quick profile:

```powershell
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick
```

Expected: prior suites green; new bundle suite fails because types/core are missing or throwing. Record exact SHA/run ID; do not interpret RED as release readiness.

- [ ] **Step 3: Implement the minimal models/core**

Implement immutable/copy-safe category inputs: copy the caller set into a new `HashSet<DiagnosticBundleCategory>` in validation/default construction so later UI mutations cannot alter a running session.

Outcome algorithm must be deterministic:

```csharp
public static string CalculateOutcome(DiagnosticBundleSnapshot snapshot)
{
    var requested = snapshot.Sources.Where(x => x.Requested).ToList();
    var useful = requested.Any(x => x.PayloadAvailable);
    if (snapshot.CancellationRequested && !useful) return "Cancelled";
    if (!useful && requested.All(x => x.State is "Unavailable" or "Cancelled")) return "Unavailable";
    if (requested.All(x => x.State == "Complete")) return "Complete";
    return useful ? "Partial" : snapshot.CancellationRequested ? "Cancelled" : "Unavailable";
}
```

`PayloadAvailable` must be explicit on the source envelope or derived only from a non-null typed payload that is valid for that source; do not infer it from `State` alone.

- [ ] **Step 4: Run Quick verification and make the new behavior suite green**

Expected: all Task 1 bundle behavior tests green and all previous suites remain green.

- [ ] **Step 5: Commit**

Commit message:

```text
test/feat: define diagnostic bundle modes and completeness semantics
```

---

### Task 2: Reusable source adapters and coordinator preserving independent failures

**Files:**
- Create: `src/G.PcHealthCheck/DiagnosticBundleService.cs`
- Extend: `src/G.PcHealthCheck/DiagnosticBundleSelfTest.cs`
- Modify existing collector files only if a tiny non-UI extraction is unavoidable.

**Interfaces:**
- Consumes existing APIs:
  - `new DiagnosticsService().CollectAsync(IProgress<string>?, CancellationToken)`
  - `new AssessmentService().Assess(DiagnosticData)`
  - `new ProcessReview(IProcessReviewSource).Collect(int maximum, CancellationToken)`
  - `new IncidentEvents(IIncidentEventSource).Collect(IncidentWindow, CancellationToken)`
  - `EndpointReviewService.Collect(IEndpointSource, ExecutionContextInfo?, CancellationToken, IProgress<string>?)`
  - `DiskDetailsService.Collect(IPhysicalDiskDetailsSource, CancellationToken, int maxDisks = 64, Func<long>? elapsedMs = null)`
  - `new PerformanceSessionService().RunAsync(PerformanceSessionOptions, IPerformanceSessionSource, IPerformanceSessionClock, IProgress<PerformanceSample>?, CancellationToken)`
- Produces:
  - `internal interface IDiagnosticBundleCollector` with one method per category returning the existing typed model.
  - `internal sealed class WindowsDiagnosticBundleCollector : IDiagnosticBundleCollector` composing the current Windows sources.
  - `internal sealed class DiagnosticBundleService` with `Task<DiagnosticBundleSnapshot> CollectAsync(DiagnosticBundleOptions options, IDiagnosticBundleCollector collector, ExecutionContextInfo? context, IProgress<DiagnosticBundleProgress>? progress, IProgress<PerformanceMarker>? markers, CancellationToken ct)`.

- [ ] **Step 1: Write failing coordinator tests using a fake collector**

Required cases:

```csharp
Test("one source failure preserves later sources", () =>
{
    var fake = FakeCollector.AllSuccess();
    fake.EventsException = new UnauthorizedAccessException();
    var s = Collect(fake, QuickOptions());
    Require(s.Events.State == "Unavailable", "Event failure hidden.");
    Require(s.Health.State == "Complete" && s.Processes.State == "Complete"
        && s.Endpoints.State == "Complete" && s.Storage.State == "Complete",
        "Independent sources were erased.");
    Require(s.Outcome == "Partial", "Partial bundle outcome wrong.");
});

Test("category opt-out makes no collector call", () =>
{
    var fake = FakeCollector.AllSuccess();
    var o = QuickOptions(exclude: DiagnosticBundleCategory.Endpoints);
    var s = Collect(fake, o);
    Require(fake.EndpointCalls == 0 && !s.Endpoints.Requested && s.Endpoints.State == "NotRequested",
        "Opt-out still collected endpoint data.");
});
```

Also cover: pre-cancel makes zero collector calls; mid-source cancel keeps prior sources and does not start later sources; cancellation just before Extended timed phase leaves point-in-time payloads as `Partial`; cancellation during timed phase retains performance samples completed by `PerformanceSessionService`; progress reports source/phase without implying a provider hard timeout.

- [ ] **Step 2: Run and verify RED**

Expected: new coordinator cases fail because service/adapters are absent.

- [ ] **Step 3: Implement coordinator sequentially first**

Use a fixed deterministic source order for 0.15.0:

1. Health + assessment
2. Processes
3. Endpoints
4. Events (last 60 minutes, `MaxPerLog = 1000`)
5. Storage
6. Performance only for Extended and only after the above phases

Do **not** introduce parallelism in 0.15.0 unless a later benchmark/test proves a clear need. Sequential collection is easier to interpret and avoids shared WMI/Event Log concurrency surprises.

Each category helper must:
- set `StartedAt` before invoking its collector;
- catch source exceptions but not erase earlier results;
- preserve source-native `Complete/Partial/Unavailable/Cancelled` semantics through a mapping function;
- set `FinishedAt` on every exit;
- leave `NotRequested` untouched when excluded;
- check cancellation before starting the next category.

Health payload must store both `DiagnosticData` and the corresponding `ScanResult` from the same data object; never rerun the main health collection solely for assessment.

- [ ] **Step 4: Implement production Windows adapter with existing providers only**

Construct existing Windows sources (`WindowsProcessReviewSource`, `WindowsIncidentEventSource`, `EndpointWindowsSource`, `WindowsDiskDetailsSource`, `WindowsPerformanceSessionSource`) and execution context. Do not call any remediation worker, `ResourceProbeService`, Temp preview/cleanup, FileUse, or process-observation source from the bundle.

- [ ] **Step 5: Run Quick verification**

Expected: coordinator cases green; all existing individual-tool suites remain green, proving their independent semantics were not changed.

- [ ] **Step 6: Commit**

```text
feat: compose existing diagnostics into bundle coordinator
```

---

### Task 3: Summary/highlight logic and package writer

**Files:**
- Create: `src/G.PcHealthCheck/DiagnosticBundleReport.cs`
- Extend: `src/G.PcHealthCheck/DiagnosticBundleCore.cs`
- Extend: `src/G.PcHealthCheck/DiagnosticBundleSelfTest.cs`

**Interfaces:**
- Produces:
  - `DiagnosticBundleReport.Summary(DiagnosticBundleSnapshot snapshot)`
  - `DiagnosticBundleReport.Html(DiagnosticBundleSnapshot snapshot)`
  - `DiagnosticBundleReport.ManifestJson(DiagnosticBundleSnapshot snapshot)`
  - `DiagnosticBundleReport.Save(DiagnosticBundleSnapshot snapshot, string parent, bool createZip)` returning a `DiagnosticBundleSaveResult` containing unique folder and optional ZIP path.
  - pure highlight helpers for recent event groups, process working-set ranking, endpoint counts and storage warnings.

- [ ] **Step 1: Write failing report/packaging tests**

Include exact semantics:

```csharp
Test("missing source is warning not healthy", () =>
{
    var s = BundleFixture();
    s.Events = UnavailableEvents();
    var html = DiagnosticBundleReport.Html(s);
    Require(html.Contains("События") && html.Contains("Недоступно"), "Missing source hidden.");
    Require(!html.Contains("События: проблем нет"), "Unavailable source called healthy.");
});

Test("HTML encodes workstation strings", () =>
{
    var s = BundleFixture(processName: "<script>x</script>&");
    var html = DiagnosticBundleReport.Html(s);
    Require(!html.Contains("<script>x</script>") && html.Contains("&lt;script&gt;x&lt;/script&gt;&amp;"),
        "HTML injection possible.");
});

Test("saving never overwrites and keeps folder before zip", () =>
{
    var a = DiagnosticBundleReport.Save(BundleFixture(), root, createZip: true);
    var b = DiagnosticBundleReport.Save(BundleFixture(), root, createZip: true);
    Require(a.Folder != b.Folder && Directory.Exists(a.Folder) && File.Exists(a.Zip!)
        && Directory.Exists(b.Folder) && File.Exists(b.Zip!), "Package overwrite or zip-only assembly.");
});
```

Also cover: manifest distinguishes NotRequested/Unavailable/Cancelled; UTF-8 JSON parses; summary contains exact application version, mode, execution context and source matrix; event highlights group only already collected Critical/Error/Warning entries without claiming causality; process highlights rank by available `WorkingSetBytes` only and say “largest working sets” rather than “bad”; endpoint summary gives counts/listeners/established plus caveat; Extended report preserves performance markers; Quick report contains no invented performance section.

- [ ] **Step 2: Run and verify RED**

Expected: report/package cases fail because report class is missing.

- [ ] **Step 3: Implement pure highlight logic**

Event grouping key: `(Provider, EventId, Level)` over collected Application/System events with level 1..3; sort by count desc then newest timestamp; limit visible summary groups (e.g. 10) but keep full source JSON.

Process highlight: available working-set only, descending, top 10; show name/PID/working set and explicitly label as size ranking, not diagnosis.

Endpoint highlight: counts from retained rows (`LISTEN`, `ESTABLISHED`, UDP bindings), no reachability/exposure verdict.

Storage highlight: surface source warnings, non-healthy Windows health/operational evidence and reliability warnings already present; missing reliability remains unknown.

- [ ] **Step 4: Implement stable package layout**

Minimum output:

```text
DiagnosticBundle_<timestamp>_<guid>/
  summary.html
  manifest.json
  health.json
  processes.json
  endpoints.json
  events.json
  storage.json
  performance.json        # Extended only when requested/available
  performance.html        # Extended only when requested/available
```

Write each file with `FileMode.CreateNew` inside the unique folder. Build ZIP **after** all folder files close successfully. If ZIP fails, return/save the folder and expose a warning; never delete the evidence folder because ZIP creation failed.

Use existing `PerformanceSessionReport` semantics for the timed phase rather than inventing a second interpretation of the same samples.

- [ ] **Step 5: Run Quick verification**

Expected: report/package suite green, all earlier tests green.

- [ ] **Step 6: Commit**

```text
feat: add diagnostic bundle summary and evidence package
```

---

### Task 4: Idle-on-open Service Desk bundle UI and privacy/category controls

**Files:**
- Create: `src/G.PcHealthCheck/DiagnosticBundleForm.cs`
- Create: `src/G.PcHealthCheck/DiagnosticBundleIntegrationSelfTest.cs`
- Modify: `src/G.PcHealthCheck/Program.cs`

**Interfaces:**
- Consumes `DiagnosticBundleCore.DefaultOptions`, `DiagnosticBundleService.CollectAsync`, `DiagnosticBundleReport.Save`.
- Produces `DiagnosticBundleMenu.Attach(Form main)` and a WinForms form named `DiagnosticBundleForm`.

- [ ] **Step 1: Write failing UI/integration tests**

Required assertions:

```csharp
Test("bundle window opens idle", () =>
{
    using var form = new DiagnosticBundleForm();
    form.PerformLayout();
    Require(form.Controls.Find("BundleStart", true).Single().Enabled, "Start disabled.");
    Require(!form.Controls.Find("BundleSave", true).Single().Enabled, "Save enabled without snapshot.");
    Require(form.Controls.Find("BundleSourceGrid", true).Single() is DataGridView grid && grid.Rows.Count >= 6,
        "Source/privacy matrix missing.");
});

Test("Quick mode disables performance category", () =>
{
    using var form = new DiagnosticBundleForm();
    var mode = (ComboBox)form.Controls.Find("BundleMode", true).Single();
    mode.SelectedItem = "Быстрый";
    Require(!BundleUiProbe.IsCategoryChecked(form, DiagnosticBundleCategory.Performance),
        "Quick still includes timed performance.");
});
```

Also cover: Extended defaults 60/2; category privacy examples rendered; selected categories freeze for a running collection; stop requests cancellation; partial snapshot enables save; output folder dialog appears only on explicit Save; save can choose folder-only or folder+ZIP; form close requests cancellation; no active probe/remediation buttons or calls; menu attaches once.

- [ ] **Step 2: Run and verify RED**

Expected: UI tests fail because form/menu are absent.

- [ ] **Step 3: Implement layout and state machine**

The source grid must show category, included checkbox, description/privacy examples, and latest state. For Health/Processes/Endpoints/Events/Storage, Quick defaults checked. Performance is unchecked/disabled in Quick and checked in Extended. Allow opt-out of all non-Performance categories, but prevent a start with zero categories.

Execution context is captured/displayed when collection starts, not assumed from an old main-window snapshot.

Start behavior:
- copy current selections into immutable `DiagnosticBundleOptions`;
- disable mode/category controls while running;
- clear prior in-progress UI but keep previous completed snapshot until the new attempt returns useful evidence;
- show phase/source/elapsed progress;
- accept Extended symptom notes only during performance phase and cap at 100 nonempty notes of 160 chars.

Stop behavior: request cancellation once and label that provider return can be delayed; do not spin another collection.

- [ ] **Step 4: Add save workflow**

On explicit Save, show a privacy confirmation text stating that package may contain account/SID, paths/commands, IP/ports, event messages, device IDs and notes. Then choose parent folder and folder-only versus folder+ZIP. Never automatically upload or open a network location.

- [ ] **Step 5: Run Quick verification**

Expected: UI tests green and prior UI suites remain green.

- [ ] **Step 6: Commit**

```text
feat: add guided Service Desk diagnostic bundle window
```

---

### Task 5: Safe Windows integration acceptance and explicit no-side-effect gates

**Files:**
- Extend: `src/G.PcHealthCheck/DiagnosticBundleIntegrationSelfTest.cs`
- Extend: `src/G.PcHealthCheck/DiagnosticBundleSelfTest.cs`

**Interfaces:**
- Uses production `WindowsDiagnosticBundleCollector` but only safe read-only source paths.

- [ ] **Step 1: Add a production Quick-mode integration test**

Run Quick collection with a reduced/synthetic-safe envelope where possible; assert requested categories return a state and that Performance is `NotRequested`.

Do not assert that every source is `Complete` on hosted Windows; source access varies. Assert instead that each requested source ends in one of its allowed terminal states and that source warnings/states are retained.

- [ ] **Step 2: Add explicit forbidden-call tests with a spy collector**

Create a spy that would throw if the coordinator attempts any of:
- resource DNS/TCP probe;
- remediation worker/action;
- Temp preview/cleanup;
- file-use collection;
- process observation.

The bundle coordinator interface should not even expose these operations. The test should fail compilation/design review if they are added later without an intentional API change.

- [ ] **Step 3: Add Extended timed-phase test using existing fake clock/source**

Use deterministic fake performance source/clock so a 30-second / 5-second Extended session produces the expected completed sample count without sleeping wall-clock time. Add a symptom marker during the phase and verify it survives snapshot -> report JSON/HTML.

- [ ] **Step 4: Run Quick then Full verification**

```powershell
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full
```

Expected: all new bundle tests and all prior source/published/portable suites pass. If only hosted CI is available, record exact Windows run/job IDs instead of claiming local verification.

- [ ] **Step 5: Commit**

```text
test: complete diagnostic bundle Windows acceptance coverage
```

---

### Task 6: Version/docs, exact final review, protected merge and release verification

**Files:**
- Modify: `src/G.PcHealthCheck/G.PcHealthCheck.csproj`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Create: `docs/releases/0.15.0.md`
- Create: `docs/releases/0.15.0-validation.md`
- Update PR checkpoint/body.

**Interfaces:** release metadata only; no new runtime behavior.

- [ ] **Step 1: Bump version only after Task 1–5 behavior is green**

Set:

```xml
<Version>0.15.0</Version>
<FileVersion>0.15.0.0</FileVersion>
<AssemblyVersion>0.15.0.0</AssemblyVersion>
```

- [ ] **Step 2: Document exact user behavior and caveats**

README/release notes must say:
- Quick categories and that it avoids timed performance/active probes;
- Extended timed phase defaults and controls;
- per-source completeness vs health distinction;
- folder/ZIP output contents;
- sensitive-data review requirement;
- no automatic upload/redaction/remediation/elevation;
- cancellation/provider-delay caveat;
- hosted Windows vs corporate pilot boundary.

- [ ] **Step 3: Run final Full verification on the exact head**

Require:
- warnings-as-errors build green;
- all source self-tests green;
- published single EXE self-test green;
- portable worker/security matrix green;
- version/checksum/pilot bundle gates green;
- Evidence Analyzer and Supply Chain Smoke green.

Record exact head SHA, tested merge SHA, Windows run/job IDs and test counts in `0.15.0-validation.md` and PR body.

- [ ] **Step 4: Review exact diff against the spec**

Confirm specifically:
- no edits to remediation worker/allow-list/UAC/Temp deletion policy unless an unrelated pre-existing test-only formatting change exists (then remove it);
- no `ResourceProbeService` use from bundle coordinator;
- no new dependencies/workflow permission changes;
- all workstation strings in new HTML pass through HTML encoding;
- every requested source appears in manifest even on failure;
- Quick mode cannot invoke Performance;
- source opt-outs become NotRequested, not Unavailable.

- [ ] **Step 5: Merge only with expected final head after required checks pass**

Use protected PR merge with the exact verified head SHA; no force push/protection bypass.

- [ ] **Step 6: Verify fresh main build and release separately**

After merge:
- wait for exact-main Windows build and publication;
- verify v0.15.0 targets the merged main commit;
- inspect published EXE FileVersion `0.15.0.0`;
- compare release EXE/checksum/pilot asset sizes and SHA-256 against the exact tested main artifacts;
- verify attestation workflow success without claiming local cryptographic verification unless actually performed;
- only then provide the user a verified EXE link.

- [ ] **Step 7: Commit docs/checkpoint changes if they precede final head**

Avoid a docs-only commit *after* the final checked head. Prepare docs before the final Full run so the final SHA is the one actually validated.

---

## Self-Review Against Spec

Coverage check:
- Quick/Extended defaults and category opt-out: Tasks 1, 4.
- Existing collectors composed rather than duplicated: Task 2.
- Explicit source states and deterministic overall completeness: Tasks 1–2.
- Cancellation preserving useful evidence: Tasks 1–2, 4.
- Extended performance notes/timeline: Tasks 2, 4–5.
- Actionable summary without causal invention: Task 3.
- Privacy controls/no auto-upload/no generic redaction: Tasks 3–4.
- Unique folder first, optional ZIP second: Task 3.
- No hidden elevation/remediation/active probe: Tasks 2, 5–6.
- UI idle/start-stop/status/save behavior: Task 4.
- Packaged/portable regression and exact release verification: Tasks 5–6.
- Corporate Windows 11 pilot remains separate: Task 6 docs.

No placeholders are intentionally left; exact classes, paths, source ordering, mode defaults, limits and release gates are specified above.