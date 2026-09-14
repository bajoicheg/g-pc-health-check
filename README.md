# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable findings, before/after reporting and controlled remediation.

Current project version: **0.16.0**. A self-contained single-file `G-PC-Health-Check.exe`; no installation is required. Portable copies may use any folder and filename.

> **Privacy:** review exports before sharing. Account names/SIDs, profile and file paths, commands, events, device identifiers, resource addresses and notes can be sensitive. Search is not redaction. See [`SECURITY.md`](SECURITY.md).

## 0.16.0 — bilingual Service Desk actions and one-click repair

0.16.0 adds RU/EN operator UI, MB presentation for human-readable file/folder sizes, safe one-shot auto-collection for fully defined read-only Analysis views, About/branding cleanup and a centralized Service Desk action model. Stable IDs, JSON keys and raw provider evidence remain language-independent.

The main action area now has Select All plus two explicit batch paths. **Сделать хорошо / Make it better** runs only currently recommended, automated and requestable actions. **Сделать всё / Do everything** uses one fixed 14-action code-owned allow-list and an authenticated phased worker with one UAC for administrative work. Network-disruptive actions are deliberately late; the worker remains session/nonce bound and does not expose a generic command/service/adapter interface.

The red batch must first be tested only on an approved disposable/test workstation. It can interrupt VPN/RDP/network connectivity, reset DHCP/Winsock/TCP-IP, restart adapters/services, delete pending print jobs, refresh Group Policy, run DISM/SFC and leave Windows requiring a reboot. Security software is never disabled or weakened. [0.16.0 scope and safety notes](docs/releases/0.16.0.md). [Release-candidate validation](docs/releases/0.16.0-validation.md).

## 0.15.1 — pilot hardening maintenance release

0.15.1 is a pilot-focused maintenance release over 0.15.0. It keeps the same product scope and security boundaries while incorporating the accumulated reliability, evidence-state and UX fixes validated after 0.15.0: stale row/detail state, next-run options versus saved evidence, cancellation/final-status ownership, Diagnostic Bundle save/progress/timeline consistency, explicit export-failure terminal states, performance scheduling/report wording, and stricter E2E evidence-root analysis.

No new remediation operation, raw/full SMART, broad system-handle enumeration, treemap or stress/stability workload is added. The managed Windows 11 acceptance matrix remains the release gate for real pilot behavior beyond hosted CI. [Maintenance scope and pilot notes](docs/releases/0.15.1.md).

## New in 0.15.0 — Service Desk diagnostic bundle

**Анализ → Собрать пакет для Service Desk…** combines the existing read-only diagnostic collectors into one explicit escalation workflow. The window opens idle and shows the selected categories plus examples of sensitive evidence before collection.

**Быстрый** mode collects the existing Health Check/assessment, current processes, local TCP/UDP owner-PID tables, Application/System events for the last 60 minutes, physical-disk/reliability evidence and execution context. It deliberately does not run timed performance or an active DNS/TCP probe. **Расширенный** adds an explicit whole-machine performance phase, default **60 seconds / 2 seconds**, with 30/60-second duration, 1/2/5-second intervals and symptom notes.

Each category retains `NotRequested`, complete, partial, unavailable or cancelled state independently. Missing telemetry is not interpreted as healthy and Health Score/Coverage semantics are unchanged. Collection uses the current GUI token and never silently elevates. No remediation, Temp cleanup, process termination, service restart, active network probe or automatic upload is part of the bundle.

Saving is explicit and creates a unique evidence folder first, with optional ZIP second. `summary.html`, `manifest.json` and separate source JSON files preserve the source matrix and collected evidence; Extended packages include performance output when available. Review the package before sharing because it may contain accounts/SIDs, paths and command lines, IP/ports, event messages, device identifiers and notes. [Scope, output semantics and pilot limits](docs/releases/0.15.0.md).

## New in 0.14.0 — observe a selected process

In **Анализ → Подробности процессов…**, select a current process with known PID/creation time and choose **Наблюдать за процессом…**. The new window opens idle and starts observation explicitly, default 120 seconds / 2 seconds. It presents process CPU, working set, private committed memory and read/write I/O rates alongside the existing whole-machine CPU/RAM/disk context, with aligned timelines, gap-preserving graphs, symptom markers and complete HTML/JSON.

One limited-query/synchronize handle binds the selected instance. Exit or identity mismatch never transfers observation to a reused PID; machine collection continues. CPU is normalized to total active logical processors, not process affinity/quota. I/O means process-accounted transfer, not physical disk throughput. The first point has no derived rates; unavailable data is not zero. All nine metric graphs, raw counters and collecting context are exported regardless of chart selection. No artificial workload, process modification, elevation or new repair. [Scope, measurement definitions and pilot checks](docs/releases/0.14.0.md).

## New in 0.13.0 — applications using a file

**Анализ → Кто использует файл…** investigates one explicitly chosen ordinary local file through Windows Restart Manager. It separates native application/service names from executable metadata verified using PID and exact process creation time. A reused or unavailable PID stays explicit; a successful empty list is not proof that the file is unlocked or deletable.

The window opens idle and offers choose/paste path, repeat/stop, literal search, numeric sorting, row details, elapsed progress, clipboard and whole-attempt HTML/JSON exports with current/previous targets and account context. Collection has four bounded list attempts and a 1024-record cap, preserves native cancellation codes and attempts RM session cleanup on every exit after successful start. Reserved DOS device names are excluded from the selected data-file path; the utility's portable executable-name policy is unchanged.

No user-file content read/change, forced handle closure, process termination, service restart, elevation or new repair action. RM owns temporary registration/session state; this is not full system-handle enumeration. [Scope, interpretation, validation and pilot checks](docs/releases/0.13.0.md).

## New in 0.12.0 — network endpoints and processes

**Анализ → Сетевые соединения и порты (TCP/UDP)…** reads local TCP4/TCP6/UDP4/UDP6 owner-PID tables on demand. It shows local/remote IPs and ports where meaningful, native states and PIDs, and process names only when PID/creation-time evidence agrees before and after the table reads. Missing or reused process identity remains explicit; PID 0 is not guessed as the idle process.

The window offers literal process/PID/address/port search, TCP/UDP/LISTEN/ESTABLISHED/change filters, typed sorting, details, repeat/stop, elapsed progress and full HTML/JSON exports of current/previous snapshots. Comparisons require complete corresponding tables and matching host/actor/session/rights. They describe observations, not exact socket lifetimes; failed collection cannot imply disappearance. Limits: 5000 retained rows per table, 16 MiB buffers/four retries, 8192 process objects per observation, 2000 displayed matching rows.

No reverse DNS, packet capture, active network probe, connection closing, process termination, elevation or firewall changes. UDP shows local bindings, not remote conversations; LISTEN does not prove reachability through the firewall. This is separate from the existing active resource check. [Scope, sources and pilot checks](docs/releases/0.12.0.md).

## Execution context (0.11.0) — who runs the tool, whose data it reads

The main context strip distinguishes a standard user, an administrator without elevation, an elevated administrator, a full token without a linked UAC pair, and incomplete evidence. It separately displays the **process account** and **user of the process's Windows session**. Open **Права и доступные действия…** for token/session/profile facts, explanations and an availability matrix.

The action table shows availability before confirmation. Unavailable actions are not selectable; DISM/SFC explicitly show when UAC is needed. The process rechecks context before applying and records the actual account, rights and target scope for each action. Mixed normal/admin batches retain both contexts in the before/after view and HTML/JSON. A saved UI snapshot is not an authorization credential.

**Elevated read-only Temp preview is allowed.** It identifies the current session user and exact profile-based Temp path rather than falling back to the console user or technician's profile. Unknown identity stays unknown. Preview does not delete anything. Actual CleanTemp still requires a normal, non-elevated process belonging to that same verified session user; this difference is visible before applying.

Startup review still reads HKCU/personal Startup of the **process account**. Raising only the repair worker does not elevate the original GUI's subsequent diagnostics. There is no automatic privileged diagnostic broker or account impersonation. Machine repairs no longer require finding a user profile. See [`docs/releases/0.11.0.md`](docs/releases/0.11.0.md) for the matrix, implemented scope and required pilot cases.

## Analysis tools

| Menu under Анализ | Purpose | Scope / version notes |
|---|---|---|
| Собрать пакет для Service Desk… | Guided Quick/Extended collection of existing read-only evidence into one reviewable folder/optional ZIP | Per-source completeness remains explicit; no hidden elevation, active probe, remediation or upload. [0.15.0](docs/releases/0.15.0.md) |
| Предпросмотр очистки Temp… | Exact age cutoff, candidate count, logical-size estimate and largest 200 files before the existing cleanup | Metadata only; current-session profile and elevated preview since 0.11.0. [Inspection scope](docs/releases/0.6.0.md) |
| Разбор автозагрузки… | Searchable Run/RunOnce/Startup records, raw commands, account and source status | No command execution, disabling or shortcut resolution; not full Autoruns. [0.6.0](docs/releases/0.6.0.md) |
| Проверить доступность ресурса (DNS/TCP)… | Resolve one hostname/IP and connect to one chosen port after explicit outbound consent | Separate DNS/address outcomes; direct OS/VPN TCP, not HTTP proxy or TLS/application validation. [0.7.0](docs/releases/0.7.0.md) |
| События за время сбоя… | Local Application/System events for a selected incident interval, search and filters | Up to seven days and 1000 newest records per log; missing/truncated data stays visible. [0.8.0](docs/releases/0.8.0.md) |
| Подробности процессов… | Process identity, parent, path/command, memory/session and checked owner lookup | Exact PID/creation-time matching; no process changes or historical PID guesswork. [0.8.0](docs/releases/0.8.0.md) |
| Наблюдать за процессом… (button in process details) | One selected instance's CPU/memory/I/O time series with aligned whole-machine context | Query-only handle; no PID rebinding, artificial workload or privilege changes. [0.14.0](docs/releases/0.14.0.md) |
| Сеанс производительности… | Timed CPU/RAM/disk observation with live graphs and symptom markers | Default 120 seconds / 2 seconds; sample statistics, not time fractions or proof of a bottleneck. [0.9.0](docs/releases/0.9.0.md) |
| Место по папкам… | Own/subtree logical sizes, counts, immediate folders and largest 200 files | Default 200000 entries / 20000 folders / 120 seconds; no deletion or file-content reads. [0.10.0](docs/releases/0.10.0.md) |
| Подробности накопителей… | Physical-disk properties and explicitly associated Windows reliability counters | Missing is not zero; consumed wear, not remaining health; not full raw SMART or a surface test. [0.10.0](docs/releases/0.10.0.md) |
| Сетевые соединения и порты (TCP/UDP)… | Local owner-PID tables, checked process-name attribution and qualified snapshot differences | No probe/reverse DNS or connection/process changes; full retained-snapshot export. [0.12.0](docs/releases/0.12.0.md) |
| Кто использует файл… | Restart Manager application/service evidence for one selected local file, with checked executable metadata | No forced unlocking or termination; failed/empty results stay distinct. [0.13.0](docs/releases/0.13.0.md) |

Read-only tools with fully defined default scope may collect once when opened in 0.16.0; target/consent-required tools such as Resource Probe, File Use and Process Observation remain idle. Collection, progress, cancellation, details and local reports preserve complete evidence rather than only a search filter. Permissions, source limits and unavailable values remain meaningful; no provider is guaranteed to return promptly. Folder sizes are logical, nested totals overlap and hard links count per name. Network/cloud paths and native name resolution may generate OS traffic.

## Main diagnostics and common problems

The dashboard collects CPU/RAM, logical/physical disks, Windows/build, processes, events, startup, security-product, network and update signals. Short-series medians reduce transient load findings. Disk pressure requires elevated busy and queue together; event findings consider repeated Provider/Event ID groups. Score, diagnostic coverage, findings and next steps are separate: unavailable telemetry is neither health nor a fabricated hardware fault. System-volume selection does not assume C:.

**Типовые проблемы: сеть, печать, устройства** adds local IP/DNS configuration, printing/Spooler and PnP evidence with guided steps, repeat/cancel and export. Fixed Windows Settings shortcuts aid investigation; optional DNS flush needs separate confirmation. Configuration is not reachability, printer status is not successful printing and a command exit code is not symptom resolution.

The original health thresholds and previous tools are preserved. See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md).

## Portable remediation and execution boundaries

0.16.0 centralizes remediation metadata and keeps a fixed, code-owned action set. The green **Сделать хорошо / Make it better** batch includes only currently recommended, automated and requestable actions. The red **Сделать всё / Do everything** batch is the exact fixed 14-action allow-list documented in [0.16.0 release notes](docs/releases/0.16.0.md); it cannot accept arbitrary commands, services or adapters.

Administrative actions use one authenticated phased worker lifetime with session/nonce-bound IPC and one UAC prompt when elevation is required. Parent-side original-user actions and worker-side machine actions remain separated; network disruption is scheduled late. Alternate-admin elevation does not impersonate the interactive user. `CleanTemp` remains limited to the verified same-user/session/profile scope.

**DISM/SFC work from any EXE folder/name**, including Downloads and renamed copies. Windows access and enterprise launch policies still apply. No security software disable/stop/exclusion, Event Log clearing, credential/profile deletion, forced reboot or forced logoff is provided.

The red batch is intentionally disruptive and must first be exercised only on an approved disposable/test workstation. It may interrupt VPN/RDP/network, reset DHCP/Winsock/TCP-IP, restart adapters/services, delete pending print jobs, refresh Group Policy, resynchronize time and leave Windows requiring reboot. Hosted CI validates the fixed handlers/protocol with fakes and negative tests; it does not execute those real disruptive repairs.

## Build, CI and provenance

`Windows EXE` uses read-only repository permissions and pinned Actions. Its gates include PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source and single-EXE self-tests, portable worker tests, exact FileVersion, SHA-256 and pilot metadata/UTF-8. Successful main builds trigger release publication and supply-chain attestations for the exact tested SHA/run. Existing releases are not overwritten. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not releases. Hosted Windows Server tests are not a substitute for real corporate Windows 11 standard/admin/other-account/RDP contexts, redirected profiles, OEM disks, VPN/proxy/DNS, GUI/DPI, interactive UAC and actual remediation. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and version-specific notes.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md); use synthetic/redacted public evidence and [`SECURITY.md`](SECURITY.md) for private vulnerabilities. Source is Apache License 2.0 ([`LICENSE`](LICENSE)); G branding is reserved ([`NOTICE`](NOTICE)). The EXE is not Authenticode-signed. Verify checksums/provenance and use an approved distribution channel; build attestations do not replace a Windows publisher signature.