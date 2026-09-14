# Changelog

## 0.16.0 — Bilingual Service Desk actions and one-click repair

- Adds persistent in-app RU/EN operator localization while keeping stable control/action IDs, JSON keys and raw provider evidence language-independent.
- Presents human-readable file/folder sizes in MB without changing raw byte evidence in JSON.
- Auto-collects safe read-only Analysis views with fully defined default scope once on open; target/consent-required Resource Probe, File Use and Process Observation remain idle.
- Adds About/running-version attribution and branding cleanup.
- Centralizes Service Desk action metadata and recommendation/risk/host semantics in one code-owned registry.
- Adds Select All and a conservative green **Сделать хорошо / Make it better** path for currently recommended automated requestable actions.
- Adds explicit red **Сделать всё / Do everything** with an exact fixed 14-action allow-list: CleanTemp, FlushDns, RegisterDns, DhcpReleaseRenew, WinsockReset, TcpIpReset, RestartNetworkAdapters, RestartSpooler, ClearPrintQueue, RestartUpdateServices, GpUpdate, TimeResync, Dism and Sfc.
- Adds fixed native handlers for the approved machine repairs; no arbitrary command, executable, service or adapter interface is introduced.
- Uses one authenticated, session/nonce-bound phased worker lifetime and one UAC prompt for administrative batches, preserving original-user actions in the parent and keeping network disruption late.
- Preserves alternate-admin safety: the elevated worker does not impersonate the interactive user, and CleanTemp remains constrained to verified same-user/session/profile rules.
- Preserves security boundaries: no AV/EDR/firewall weakening, Event Log clearing, credential/profile deletion, forced reboot/logoff, raw/full SMART, broad handle enumeration or stress workload.
- Requires the red batch to be first pilot-tested only on an approved disposable/test workstation because it may sever VPN/RDP/network, delete pending print jobs, refresh policy/services and leave Windows requiring reboot.
- Version `0.16.0`, FileVersion `0.16.0.0`. [Scope and safety notes](docs/releases/0.16.0.md).

## 0.15.0 — Service Desk diagnostic bundle

- Adds **Анализ → Собрать пакет для Service Desk…**, an idle-on-open guided workflow that composes existing read-only diagnostics into one escalation package.
- Quick mode collects Health Check/assessment, current processes, local TCP/UDP endpoints, Application/System events from the last 60 minutes, physical storage/reliability and execution context; timed performance and active DNS/TCP probing are excluded.
- Extended mode adds an explicit whole-machine performance phase, default 60 seconds / 2 seconds, with 30/60-second duration, 1/2/5-second intervals and bounded symptom notes.
- Keeps per-source `NotRequested`, complete, partial, unavailable and cancelled states separate; missing telemetry is never treated as healthy and existing Health Score/Coverage semantics are unchanged.
- Adds category opt-out and privacy examples before collection, fresh execution-context capture, cooperative stop and preservation of already useful evidence when a later source fails or collection is cancelled.
- Saves a unique evidence folder first with `summary.html`, `manifest.json` and source JSON files; optional ZIP is created only afterward and never replaces the folder as the sole evidence copy.
- Summary reuses existing assessment and performance semantics, groups already collected warning/error events, ranks working-set evidence, summarizes endpoint counts/storage warnings and avoids causal, exposure or maliciousness claims.
- Adds explicit tests that the bundle collector surface exposes only Health/Processes/Endpoints/Events/Storage/Performance collection and no active probe, remediation, Temp, file-use or process-observation operation.
- Preserves arbitrary executable location/name, existing UAC/worker allow-list, elevated read-only Temp preview, same-user non-elevated Temp deletion, dependencies and workflow permissions. No automatic upload/redaction/remediation/elevation is added.
- Version `0.15.0`, FileVersion `0.15.0.0`. [Scope and pilot checks](docs/releases/0.15.0.md). [Observed validation](docs/releases/0.15.0-validation.md).

## 0.14.0 — Selected-process observation with machine context

- Adds a process-details button and idle-on-open observation window for one selected PID/creation-time identity.
- Presents interval CPU, working set, private commit and read/write process-accounted I/O rates beside existing system CPU/RAM/disk measurements on aligned elapsed-time axes.
- Holds a query-only process handle; exit and identity changes never rebind to a reused PID. System observations continue after process exit.
- Reuses the existing sequential performance scheduler and commits only completed process/system pairs, retaining their separate source receipt timestamps.
- Preserves zero versus unknown, rate warmup, missing/rollback/long-gap boundaries and unconfirmed processor counts. CPU normalization is relative to total active logical processors, not affinity/quota.
- Provides selectable live charts, numeric sample table, symptom markers, detailed warnings, summary and unique complete HTML/JSON exports including all nine metric graphs.
- Adds 33 behavior and eight native/UI/export acceptance cases; all prior suites and portable tests remain enabled.
- No artificial workload, process modification, automatic elevation, new repairs, dependencies or workflow-permission changes. Existing UAC/context/Temp and unrestricted portable filename/location behavior remain unchanged.
- Version `0.14.0`, FileVersion `0.14.0.0`. [Scope, definitions and validation gates](docs/releases/0.14.0.md).

## 0.13.0 — Applications using a selected file

- Adds an idle-on-open Analysis window using Windows Restart Manager for one explicitly selected ordinary local file.
- Preserves RM application/service names separately from executable metadata verified by exact PID and process creation time on a query-only handle.
- Adds choose/paste path, repeat/stop, literal search, numeric sorting, details, elapsed progress, clipboard and full current/previous-attempt HTML/JSON exports.
- Distinguishes an unavailable or cancelled query from a successful empty result; neither is presented as proof that the file can be deleted or renamed.
- Bounds list retries to four and records to 1024; validates returned counts against capacity and attempts session cleanup on every exit after successful start.
- Fixes all four source-review findings: invalid-record PID-cache poisoning, reserved DOS path components, lost native cancellation code and unrealistic zero-capacity fake responses/service count validation.
- Keeps native service rows separate even when they share a process. No file-content read/change, forced handle closure, process termination, service restart or privilege adjustment is added.
- Adds 44 behavior, six native/UI/export and 20 follow-up acceptance cases. Previous suites and the 32-case portable worker matrix remain enabled.
- Preserves arbitrary executable location/name, existing UAC/context/Temp rules, health model, packages and workflow permissions.
- Version `0.13.0`, FileVersion `0.13.0.0`. [Scope and pilot checks](docs/releases/0.13.0.md). [Observed validation](docs/releases/0.13.0-validation.md).

## 0.12.0 — TCP/UDP endpoint and process review

- Adds an idle-on-open Analysis window for local TCP4/TCP6/UDP4/UDP6 owner-PID tables, addresses, ports and native states.
- Matches process names only across consistent pre/post PID and creation-time observations; unavailable and changing identity stays explicit.
- Provides qualified two-snapshot comparisons; failed/partial tables cannot imply disappearance, and ambiguous tuples are preserved.
- Includes literal search, protocol/state/change filters, numeric sorting, details, repeat/stop, elapsed progress and full HTML/JSON exports with collection context.
- Distinguishes UDP local bindings and TCP listeners from remote conversations or externally reachable services. No reverse DNS, active probe, capture, connection/process modification or elevation.
- Bounded native buffers/retries and retained rows; UI display limit does not truncate saved evidence further.
- Adds 39 behavior and eight integration cases, including disposable IPv4/IPv6 loopback sockets. Previous suites and portable worker matrix remain enabled.
- Preserves 0.11.0 execution-context/Temp boundaries, all prior tools, remediation commands, health model, dependencies and workflow permissions.
- Version `0.12.0`, FileVersion `0.12.0.0`. [Scope, sources and pilot checks](docs/releases/0.12.0.md).

## 0.11.0 — Execution context and elevated read-only Temp preview

- Distinguishes standard, filtered-admin, elevated-admin and full-token contexts; separates process account from current-session user/profile without a physical-console fallback.
- Adds a main-window context strip, details/availability matrix and preflight action availability; unavailable operations cannot remain selected.
- Allows read-only session-user Temp preview from elevated and separate technician accounts when the target profile is known.
- Keeps deletion in a verified same-user non-elevated context, with a fresh runtime check and explicit target scope.
- Removes unrelated user-profile prerequisites from machine repair commands; keeps original UAC and portable EXE behavior.
- Records actor/rights/target per remediation action and preserves mixed contexts in UI, clipboard and HTML/JSON; missing legacy context stays unknown.
- Fixes native effective-role inspection by opening the current token with the identification-duplication right required by WindowsPrincipal.
- Rejects invalid/inaccessible cleanup roots instead of confusing Directory.Exists false with successful empty cleanup.
- Adds 30 context/preview, seven native/UI/report and eight action-boundary review cases; previous tests stay enabled.
- No new commands, credential storage, impersonation, packages, drivers or workflow-permission changes. Version `0.11.0`, FileVersion `0.11.0.0`. [Scope and pilot checks](docs/releases/0.11.0.md).

## 0.10.0 — Folder space analysis and detailed storage evidence

- Adds idle-on-open Analysis tools for an explicitly chosen folder and local physical-disk details.
- Streams metadata into logical folder totals, own versus subtree sizes, file counts and the 200 largest files; supports typed sorting, literal search and full-snapshot export.
- Makes access errors, skipped reparse points, cancellation and bounded traversal explicit; does not infer physical allocation or recommend deletion from size.
- Uses the documented physical-disk/reliability-counter association with nonempty matching DeviceId and rejects ambiguous/mismatched evidence.
- Shows available firmware, health/media/bus codes, temperature/device limit, consumed wear, power-on hours and uncorrected error counters without inventing absent values.
- Adds two GUI windows, progress/stop, row details, clipboard and non-overwriting HTML/JSON. Reports retain all collected rows irrespective of search and show immediate-folder size shares.
- Adds 53 behavior and eight integration cases, complementing prior suites and the portable worker matrix.
- Preserves arbitrary EXE location/name, UAC, original Health Score/Coverage, existing repairs, dependencies and workflow permissions. No disk operations or file-content reads/deletion are added.
- Version `0.10.0`, FileVersion `0.10.0.0`. [Scope and validation limits](docs/releases/0.10.0.md).

## 0.9.0 — Timed performance observation

- Adds an explicitly started CPU/RAM/disk session with duration and interval controls, default 120 seconds / 2 seconds.
- Adds live actual-time graphs, missing-data gaps, a full sample table, collection-time/warning evidence and timestamped symptom notes.
- Adds sample-based min/median/nearest-rank P95/max and reference-threshold counts, without treating these as time fractions or diagnoses.
- Uses native CPU/physical-memory counters and read-only aggregate WMI disk idle/queue data; unknown values and unsupported whole-machine CPU remain unavailable.
- Supports cooperative stop retaining completed observations, no overlapping/catch-up reads, explicit skipped slots, and local full-session HTML/JSON/clipboard outputs.
- Corrects final-sample loss on small timer jitter with a bounded 100-ms grace and avoids median overflow from finite nonnegative values.
- Adds 45 behavior, 10 integration and five timing/review cases; prior tests remain enabled.
- Keeps portable arbitrary EXE location/name, UAC, original health model, previous tools, dependencies and remediation allow-list unchanged.
- Version `0.9.0`, FileVersion `0.9.0.0`. Scope, measurement semantics and validation limits: [version notes](docs/releases/0.9.0.md).

## 0.8.0 — Incident-window events and process details

- Adds local Application/System event review for an explicit interval, literal search and journal/Event ID/severity filters.
- Adds current process metadata, exact PID/creation-time checked owner lookup, details and full local exports.
- Makes partial data and provider failures visible; historical emitter PIDs are not automatically attributed to current processes.
- Adds 44 behavior and seven integration cases. [Version notes](docs/releases/0.8.0.md).

## 0.7.0 — Explicit DNS/TCP resource diagnostics

- Adds one-host/one-port diagnostics with separate resolver and per-address TCP outcomes, timing, source IP and error evidence.
- Requires explicit outbound-probe consent; supports IPv4/IPv6/IDN, bounded timeouts, repeat/cancel and local exports.
- Distinguishes cancellation, mixed results and missing data; does not claim TLS/application health from TCP success.
- Adds 55 behavior, six integration and four cancellation/evidence cases. [Version notes](docs/releases/0.7.0.md).

## 0.6.0 — Read-only cleanup and startup review

- Adds metadata-only preview of existing user-Temp cleanup candidates and the largest eligible files.
- Adds searchable supported Run/RunOnce/Startup records with exact commands/references and source status.
- Includes cancellation, missing-data warnings, full details and non-overwriting HTML/JSON exports.
- Adds 36 behavior and eight UI/export cases. [Version notes](docs/releases/0.6.0.md).

## 0.5.2 and earlier

The complete existing history is preserved verbatim in [Changelog through 0.5.2](docs/releases/CHANGELOG-THROUGH-0.5.2.md), including portable elevation, system-volume fixes, common-problem diagnostics and earlier security/supply-chain work.