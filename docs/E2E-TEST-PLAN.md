# G PC Health Check 0.15.1 — managed Windows 11 acceptance / pilot

## Purpose and acceptance boundary

This is the manual workstation acceptance plan for the current 0.15.1 product line. It complements CI; it does not replace CI and CI does not replace this plan.

Hosted Windows CI proves source/build regressions, the published single-file EXE self-test, portable worker/security checks, package/version/checksum validation, pilot-bundle construction, evidence-analyzer behavior and supply-chain attestations. It does **not** prove interactive UAC, real Windows 11 desktop behavior, RDP/DPI behavior, corporate VPN/EDR coexistence, OEM/USB/RAID provider behavior or Service Desk usability on managed endpoints.

Run this plan only on approved test workstations. Do not disable AV/EDR, Windows security controls, corporate VPN, execution policy, services or branch protections to make a case pass. A blocked or unavailable provider is evidence and must be recorded as such.

Record every scenario as **PASS / FAIL / NOT RUN / NOT APPLICABLE**, with the tested EXE SHA-256, Windows build, device model, execution context and short evidence note. A pilot is not complete while a required scenario is merely assumed from CI.

## 1. Pilot inventory and evidence header

For each machine/session record at minimum:

| Field | Required evidence |
|---|---|
| Application | FileVersion, source commit/release, SHA-256 before launch |
| Windows | Edition, version/build, x64 |
| Device | Manufacturer/model, physical/VM, CPU/RAM |
| Storage | System-volume letter; SATA/NVMe/USB/RAID/VHD where present |
| Session | Console or RDP; standard user or elevated process |
| Display | DPI scaling used (100 / 150 / 200%) |
| Security stack | AV/EDR product/state; do not disable it |
| Network | LAN/Wi-Fi/VPN state; proxy if relevant |
| Identity | Local/domain/Entra-style sign-in as applicable; do not publish secrets |
| Result | PASS / FAIL / NOT RUN / N/A plus evidence path |

Evidence may contain usernames, SID/domain names, device identifiers, paths, IP addresses, ports, event text and symptom notes. Keep pilot evidence in an approved location and review it before external sharing.

## 2. Verify the exact EXE and launch contract

Use the EXE/checksum from one tested source. Verify SHA-256 before opening it. If copied or renamed, hash the resulting file and confirm the same bytes.

```powershell
Get-FileHash -LiteralPath '.\G-PC-Health-Check.exe' -Algorithm SHA256
Get-Content -LiteralPath '.\G-PC-Health-Check.exe.sha256'
```

Exercise at least these local layouts when policy permits:

- ordinary Downloads path;
- path with spaces and Unicode;
- renamed EXE such as `Проверка ПК (1).exe`;
- another local volume if available;
- optional managed-software directory.

Expected: normal diagnosis starts without a location/name refusal. The product remains one portable x64 EXE; Program Files installation is not an acceptance requirement.

## 3. Baseline main-window diagnosis

Launch by normal double-click as a standard user.

Verify:

- no UAC merely to diagnose;
- GUI remains responsive during collection;
- computer/user/execution context is plausible;
- CPU/RAM/system-volume/uptime and coverage are shown without treating unavailable telemetry as zero;
- recommendations distinguish observation from remediation;
- process/event/system views render and can be sorted without exceptions;
- clipboard/report actions work;
- repeated diagnosis replaces the current result cleanly;
- final status is not overwritten by late progress from an earlier phase;
- non-C Windows volume is identified correctly on at least one applicable machine or synthetic lab image.

If a provider is unavailable under standard-user permissions, record the limitation; do not rerun the whole application elevated merely to make the metric appear.

## 4. User Temp safety scenario

Prepare dedicated E2E sentinels:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Prepare-CleanTempScenario.ps1 -IncludeJunctionTest
```

From a non-elevated GUI select only user Temp cleanup and confirm it.

Expected:

- no UAC for user Temp cleanup;
- only the verified current-user Temp scope is eligible;
- fresh sentinel is preserved;
- junction target is preserved;
- Windows Temp/Prefetch are not silently folded into user Temp;
- before/after reporting completes;
- an already elevated GUI refuses privileged user-Temp traversal and directs the operator to a safe context rather than deleting through elevation.

Verify and clean up:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Verify-CleanTempScenario.ps1
powershell.exe -NoLogo -NoProfile -File .\e2e\Cleanup-E2EScenario.ps1
```

The CI E2E smoke simulates the sentinel effect; it does not replace this real application scenario.

## 5. Portable/renamed UAC cancellation

Close the GUI between cases. From a standard-user session run the same verified bytes from the layouts in section 2, select only SFC and confirm the product action dialog.

Expected:

- UAC is reached without directory/name refusal;
- cancelling UAC performs no SFC remediation;
- cancellation is shown as cancellation, not success;
- GUI remains usable;
- no orphan GUI/worker remains;
- the elevated-worker protocol does not accept unapproved actions.

If the GUI process is already elevated, this is not a valid UAC-cancellation case.

## 6. Approved positive UAC and alternate-admin identity

On an approved disposable/test workstation, repeat one SFC case and approve UAC. Include one case using the Service Desk administrative identity rather than the standard user's credentials.

Verify:

- the worker is the same verified/renamed EXE at the same actual location;
- the parent receives the worker result;
- exit code/evidence is preserved in before/after reporting;
- nonce/session/action allow-list protections remain effective;
- `CleanTemp` is not smuggled into the elevated worker action set;
- cancelling a combined selection does not partially perform privileged actions.

DISM need not be executed merely to prove portability. Use it only when appropriate for the dedicated test machine.

A mapped/UNC path is a separate environmental case: the administrative identity must independently have access. The application must not bypass Windows/share policy.

## 7. DPI, keyboard and display acceptance

Run the primary windows at **100%, 150% and 200% DPI**. At least one 150/200% case should use a normal corporate laptop resolution rather than only a large desktop monitor.

Cover:

- main window;
- Common Problems;
- Temp Preview / Startup Review;
- Resource Probe;
- Incident Review;
- Performance Session;
- Storage Review / folder analysis;
- Endpoint Review;
- File Use;
- Process Observation;
- Service Desk Diagnostic Bundle.

Expected: no clipped primary buttons/fields, unusable grids, invisible status text or modal dialogs outside the visible work area. Keyboard navigation and cancel/close paths remain usable. Long device/path/process names must not crash layout or details views.

## 8. RDP session acceptance

Run one standard-user pilot through RDP on a managed Windows 11 endpoint.

Verify:

- execution/session context reflects the RDP session;
- diagnosis and read-only tools remain usable;
- UAC behavior is understandable in the remote session and cancellation returns control cleanly;
- clipboard/report behavior follows environment policy;
- DPI/resizing after RDP connect/reconnect does not corrupt controls;
- closing a long-running read-only window does not leave accumulating provider work.

Do not infer console-session behavior from RDP or vice versa; record which was tested.

## 9. Corporate VPN and EDR coexistence

On a representative managed endpoint, repeat baseline diagnosis and selected read-only tools with corporate VPN and EDR/AV active. Do **not** relax security controls for the test.

Minimum cases:

- baseline diagnosis while connected to VPN;
- Resource Probe to an explicitly approved test target;
- Endpoint Review;
- Incident Review;
- Service Desk Diagnostic Bundle;
- evidence ZIP creation and local analysis.

Record any EDR alert, blocked provider, network restriction, delay or false positive. The correct product behavior is to preserve/describe missing or blocked evidence, not to evade EDR/VPN controls.

## 10. OEM hardware, storage and removable-device matrix

Use real hardware where available. The goal is provider diversity, not exhaustive hardware certification.

Required representative cases across the pilot pool:

- at least one mainstream OEM laptop/desktop;
- NVMe system disk;
- SATA disk if available;
- USB/removable storage if policy permits;
- RAID / Intel VMD / vendor storage abstraction if available;
- virtual disk/VM as an explicitly labelled separate case.

For Storage Review confirm:

- model/DeviceId/bus/size are plausible;
- unknown health/reliability counters remain unknown rather than healthy/zero;
- lack of raw SMART is not presented as full SMART coverage;
- USB/RAID/VMD devices do not crash the UI when counters are absent or differently exposed;
- folder analysis avoids following links and reports incomplete/access-limited evidence explicitly.

Do not run surface tests, destructive checks or vendor firmware operations as part of this acceptance plan.

## 11. Incident, process and performance workflows

### Incident Review

Use a bounded recent window. Verify Application/System source completeness, filters, cancellation and export. Cancellation must retain a cancellation status rather than being rewritten as success.

### Performance Session

Run a short observation while reproducing a harmless test symptom. Add at least one symptom marker. Verify gaps remain gaps, stop works, saved timing/options match the collected session, and the UI does not claim causality from correlation.

### Process Observation

Select a known process instance and run a short observation. Verify PID/creation identity handling, CPU/memory/I/O rates, system timeline, process exit/PID-reuse handling, symptom markers and export. A replacement process with the same PID must not silently become the original target.

## 12. Endpoint and selected-file workflows

### Endpoint Review

Verify TCP/UDP snapshots and process association. Missing process metadata or privilege-limited ownership information must remain explicit. This is read-only inspection, not a firewall/network repair action.

### File Use

Choose a dedicated test file and reproduce both an unused and an in-use case. Verify Restart Manager evidence, cancellation/repeat behavior and selected-file identity. This is selected-file investigation only; it is not broad system-handle enumeration.

## 13. Service Desk Diagnostic Bundle

Run both Quick and Extended modes on a representative standard-user machine.

Verify:

- category selection shown in the UI matches the saved bundle;
- Health / Processes / Endpoints / Events / Storage sources are represented correctly;
- optional performance timing uses the options of the run being saved, not later UI changes;
- symptom markers attach to the intended performance timeline;
- late progress from an earlier/completed source cannot overwrite saved source state;
- execution context matches the collected bundle;
- HTML/JSON and optional ZIP are created in a new location without overwriting an older bundle;
- privacy warning is visible before saving;
- an all-unavailable attempt does not silently replace a previously useful in-memory bundle.

Inspect the resulting evidence before external transfer.

## 14. Resource Probe consent and boundaries

Use only an explicitly approved DNS/TCP target.

Verify:

- opening the window sends no request;
- consent is required and resets after target changes/run completion;
- DNS and TCP evidence are distinguished;
- cancellation is cooperative;
- proxy/TLS/HTTP/application health is not inferred from a direct TCP result;
- changing host/port/timeout after a run clearly labels the displayed result as belonging to the previous target/options.

## 15. Evidence collection and analyzer

Pass the actual executable path, including a renamed filename when that is the tested case:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Collect-E2EEvidence.ps1 -SourceExe '.\Проверка ПК (1).exe'
powershell.exe -NoLogo -NoProfile -File .\e2e\Analyze-E2EEvidence.ps1 -EvidenceZip '<path-to-zip>'
powershell.exe -NoLogo -NoProfile -File .\e2e\Cleanup-E2EScenario.ps1
```

The tooling may optionally compare against an installed copy. An absent/older installed copy is not a requirement failure for the portable product; the authoritative comparison is the tested source EXE against its expected checksum.

Never post unreviewed workstation evidence publicly.

## 16. Acceptance matrix

Use one row per actual machine/session. Do not mark a row PASS from hosted CI alone.

| Scenario | PASS criteria | Result | Evidence / issue |
|---|---|---|---|
| Standard-user baseline | no UAC for diagnosis; telemetry gaps explicit; report works | NOT RUN | |
| Non-C system volume | correct Windows system-volume identity | NOT RUN | |
| Temp cleanup | old sentinel removed; fresh/junction preserved; safe context enforced | NOT RUN | |
| Renamed portable UAC cancel | UAC reached; cancel causes no repair/orphan | NOT RUN | |
| Positive alternate-admin UAC | result returns to parent with evidence | NOT RUN | |
| DPI 100% | primary tools usable | NOT RUN | |
| DPI 150% | primary tools usable | NOT RUN | |
| DPI 200% | primary tools usable | NOT RUN | |
| RDP | context/UI/cancel/reconnect usable | NOT RUN | |
| VPN + EDR | no bypass; blocks/gaps represented honestly | NOT RUN | |
| OEM/NVMe | storage/health evidence plausible | NOT RUN | |
| USB/removable | missing/different counters handled safely | NOT RUN | |
| RAID/VMD | provider differences handled without false health | NOT RUN | |
| Incident Review | source completeness/cancel/export correct | NOT RUN | |
| Performance Session | timing/markers/gaps/stop/export correct | NOT RUN | |
| Process Observation | process identity/rates/exit handling correct | NOT RUN | |
| Endpoint Review | read-only endpoint/process evidence correct | NOT RUN | |
| File Use | selected-file Restart Manager evidence correct | NOT RUN | |
| Diagnostic Bundle Quick | visible/saved categories and privacy/evidence correct | NOT RUN | |
| Diagnostic Bundle Extended | performance/markers/options/ZIP correct | NOT RUN | |
| Resource Probe | explicit consent and DNS/TCP boundaries correct | NOT RUN | |
| Evidence analyzer | collected ZIP analyzed; sensitive evidence handled correctly | NOT RUN | |

## Exit criteria for issue #26 acceptance

Managed Windows 11 acceptance can be considered complete only when:

1. required matrix rows have explicit PASS/FAIL/N/A outcomes from actual managed Windows 11 sessions;
2. at least one standard-user corporate endpoint and one approved alternate-admin UAC case were exercised;
3. DPI 100/150/200 and RDP were covered;
4. VPN/EDR coexistence was covered without disabling controls;
5. representative OEM/storage diversity was covered, including USB/RAID/VMD where available or explicitly marked N/A;
6. Service Desk Diagnostic Bundle and the major read-only analysis workflows were exercised;
7. every FAIL has a linked reproducible defect or an accepted/environmental limitation;
8. the tested EXE hash/version and evidence location are recorded;
9. no open acceptance blocker is hidden behind a hosted-CI PASS.

Major future ideas such as raw/full SMART, broad system-handle enumeration, general treemap, stress/stability workloads and new remediation/rollback operations are not acceptance prerequisites for 0.15.1 unless separately approved into scope.
