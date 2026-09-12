# 0.15.0 — Diagnostic bundle for Service Desk

## Intent

Turn the existing set of strong, mostly independent read-only diagnostics into one guided Service Desk workflow that produces a coherent evidence package for escalation. This release is primarily orchestration, presentation, evidence consistency and privacy control; it does not add new remediation commands or silently broaden collection scope.

The bundle must remain useful when some sources are unavailable. Unknown/failed/partial telemetry must stay explicit and must never be converted into a healthy result.

## User flow

Add **Собрать пакет для Service Desk…** to the main Analysis area.

The window opens idle and shows:
- mode: **Быстрый** or **Расширенный**;
- categories that will be collected, with per-category inclusion switches and a short privacy note;
- exact collecting process/account/elevation/session context;
- output location selection only when the user explicitly saves;
- start/stop, elapsed time and per-source state.

The default selection is intentionally conservative and read-only. No collection starts when the window opens.

### Quick mode

Target: routine first-line escalation, normally seconds rather than minutes.

Collect, through reusable existing collectors where possible:
- core Health Check snapshot and assessment/coverage;
- current process review snapshot;
- local TCP4/TCP6/UDP4/UDP6 endpoint snapshot;
- Application/System incident events for the last 60 minutes;
- physical storage details/reliability evidence;
- current execution context and source completeness/warnings.

Do not run active DNS/TCP probes, Temp scanning/deletion, SFC/DISM, file-use investigation, startup modification or other remediation. Do not wait for a timed performance session in Quick mode.

### Extended mode

Includes all Quick categories plus a short whole-machine performance observation. Initial default: **60 seconds / 2 seconds**; allow 30 or 60 seconds and 1/2/5-second intervals if that fits the existing scheduler without changing its semantics.

The user may add symptom notes while this timed portion is running. These notes belong to the bundle and must appear on the performance timeline. Extended mode is still read-only and must not create artificial workload.

## Collection architecture

Introduce a bundle coordinator that composes existing services instead of duplicating their OS/WMI/Event Log/IP Helper logic. Each source returns its existing native snapshot/model wherever practical. If an existing UI class owns orchestration that should be reusable, extract the smallest non-UI operation/service rather than calling forms programmatically.

The coordinator owns only:
- requested category set and mode;
- start/finish timestamps;
- execution context captured for the bundle;
- source state (`Complete`, `Partial`, `Unavailable`, `Cancelled`, `NotRequested` as appropriate);
- bounded sequencing/concurrency policy;
- warnings and elapsed progress;
- final bundle manifest/report assembly.

Prefer bounded parallelism only for independent read-only collectors that are already safe to run concurrently. Do not parallelize providers merely for speed if they share WMI/Event Log resources, static gates, or non-thread-safe code. Timed performance observation runs as an explicit phase, not in parallel with arbitrary collectors, unless tests prove semantics remain clear.

Cancellation is cooperative. Completed source results remain in memory and are exportable as a partial bundle. A cancelled/failed source does not erase other completed sources.

## Bundle model and manifest

Use a versioned `DiagnosticBundleSnapshot` envelope containing:
- schema version and application version;
- computer name;
- bundle mode and requested categories;
- started/finished timestamps and overall outcome;
- execution context;
- per-source status, timestamps, warnings and whether the source was requested;
- typed payloads or typed references to the existing snapshots;
- symptom notes for the timed phase, if any.

The manifest must distinguish:
- not requested;
- requested and complete;
- requested but partial;
- requested but unavailable;
- cancelled.

Do not infer overall health solely from source availability. Keep the existing Health Score/Coverage semantics unchanged.

## Report and output package

Saving creates a new unique folder first. The user may optionally create a ZIP from that folder; ZIP creation must never be the only copy of the evidence during assembly.

Minimum files:
- `summary.html` — human-readable triage entry point;
- `manifest.json` — machine-readable bundle envelope and source states;
- separate JSON files for major source snapshots where this keeps schemas stable and avoids one oversized opaque document;
- timed performance HTML/JSON or embedded equivalent in Extended mode, reusing the existing report semantics.

`summary.html` should prioritize actionability rather than raw volume:
1. bundle identity, timestamps, mode and execution context;
2. **Главные выводы** from the existing assessment plus explicit coverage/incomplete-source warnings;
3. source-status matrix;
4. notable recent Critical/Error/Warning event groups;
5. process highlights based on already collected evidence only (for example largest working sets), without labeling benign processes as bad;
6. endpoint summary (counts/listeners/established and explicit caveats, not an exposure verdict);
7. storage evidence and warnings;
8. performance graphs/notes in Extended mode;
9. links/anchors to detailed sections and filenames.

The report must not invent causal relationships between simultaneous observations.

HTML encoding is mandatory for all workstation-provided strings. JSON remains UTF-8. Output files use create-new/unique names and do not overwrite previous packages.

## Privacy and operator control

Before collection, show a concise category table with examples of potentially sensitive values:
- account/SID and session context;
- process names, paths and command lines;
- IP addresses and ports;
- event messages;
- device identifiers;
- symptom notes.

No automatic upload, telemetry submission, cloud enrichment or reputation lookup. Search/filtering is not redaction. The bundle must tell the operator to review evidence before sharing externally.

Do not add a generic automatic redaction engine in 0.15.0; incorrectly redacted technical evidence can be misleading. Per-category exclusion before collection/export is sufficient for this release.

## Permissions and safety boundaries

Collection uses the rights of the current GUI process. It does not silently elevate diagnostics. Per-source access failures are retained as evidence.

Preserve all existing invariants:
- portable single EXE, arbitrary valid folder/name;
- UAC and repair worker allow-list unchanged;
- elevated read-only Temp preview remains allowed;
- Temp deletion remains verified same-user and non-elevated;
- no new repair/action command;
- no automatic service restart, process kill, startup change, network reset, firewall change, driver install or reboot;
- no active DNS/TCP probe as part of the bundle by default;
- health/coverage thresholds and meanings unchanged.

## Performance and limits

Quick mode should avoid deliberately long waits. Every collector keeps its existing bounded limits; the bundle must surface those limits rather than hiding truncation.

Extended mode's deliberate timed phase is the main duration driver. UI stays responsive, shows current phase/source, elapsed time and cancellation state.

Do not promise a hard timeout for native/WMI/Event Log calls that do not support one. Cancellation may wait for the provider to return.

## Error semantics

A bundle can be valid and useful while `Partial`.

Overall outcome guidance:
- `Complete`: every requested category completed according to its own source semantics;
- `Partial`: at least one requested category produced useful evidence but one or more were partial/unavailable/cancelled;
- `Unavailable`: no requested diagnostic payload was successfully collected;
- `Cancelled`: user cancellation occurred before any useful evidence; if useful evidence exists, prefer `Partial` plus a cancellation marker.

These labels describe collection completeness, not workstation health.

## Tests and validation

Test-first coverage must include:
- default Quick/Extended category sets;
- `NotRequested` versus unavailable distinction;
- one-source failure preserving all other results;
- cooperative cancellation before start, mid-source and before/during timed phase;
- deterministic bundle outcome calculation;
- category opt-out and manifest consistency;
- HTML encoding and UTF-8 JSON;
- output uniqueness and no overwrite;
- summary sections never claim a missing source is healthy;
- no remediation/active-probe calls from bundle collection;
- existing collectors keep their independent semantics;
- UI opens idle, selection/privacy controls work, save disabled before a snapshot;
- Extended mode notes/timeline survive into report;
- packaged EXE and portable renamed-location matrix remain green.

Windows integration should use only existing safe/synthetic/native fixtures. Do not invoke real DISM/SFC, DNS flush, Temp deletion, third-party process termination or external network probes in CI.

Hosted Windows Server CI is not corporate Windows 11 acceptance. Pilot acceptance still includes ordinary/elevated/alternate-account/RDP sessions, 100/150/200% DPI, VPN/EDR-heavy machines, partial WMI/Event Log access and realistic workstation data volume.

## Out of scope for 0.15.0

- automatic problem fixing from the bundle;
- startup disabling or rollback;
- CPU/RAM stress testing;
- full Process Explorer/Autoruns equivalence;
- remote workstation collection;
- automatic upload to Service Desk or SIEM;
- generic PII/secret redaction;
- active scan of arbitrary hosts/ports;
- causal diagnosis generated from temporal coincidence.

## Success criteria

0.15.0 is successful when an engineer can start one explicit read-only workflow, obtain a single coherent and reviewable Service Desk evidence package, immediately see which sources succeeded or failed, and still drill into the same detailed evidence already provided by the individual tools—without weakening any existing security, permission, portability or diagnostic semantics.