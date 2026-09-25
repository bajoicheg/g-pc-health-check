# G PC Health Check 0.17.0 — managed Windows 11 acceptance / pilot

## Purpose and acceptance boundary

This is the manual workstation acceptance plan for the current 0.17.0 product line. It complements CI; it does not replace CI and CI does not replace this plan.

Hosted Windows CI proves source/build regressions, the published single-file EXE self-test, portable worker/security checks, package/version/checksum validation, pilot-bundle construction, evidence-analyzer behavior and supply-chain gates. It does **not** prove interactive UAC, real Windows 11 desktop behavior, RDP/DPI behavior, corporate VPN/EDR coexistence, Windows Security Center/Defender/Kaspersky behavior, enterprise WUA/WSUS behavior, OEM firmware providers, ADMX delivery, real disruptive remediation or Service Desk usability on managed endpoints.

Run this plan only on approved test workstations. Do not disable AV/EDR, Windows security controls, corporate VPN, execution policy, services or branch protections to make a case pass. Do not remove legitimate production administrative access or weaken encryption/firmware controls merely to manufacture a Security FAIL. A blocked, unsupported or unavailable provider is evidence and must be recorded as such.

Record every scenario as **PASS / FAIL / NOT RUN / NOT APPLICABLE**, with the tested EXE SHA-256, Windows build, device/session context and short evidence note. A pilot is not complete while a required scenario is merely assumed from CI.

## 1. Pilot inventory and evidence header

For each machine/session record at minimum:

| Field | Required evidence |
|---|---|
| Application | FileVersion, source commit/release, SHA-256 before launch |
| Windows | Edition, version/build, x64 |
| Device | Manufacturer/model, physical/VM, CPU/RAM |
| Storage | System-volume letter; BitLocker state; SATA/NVMe/USB/RAID/VHD where present |
| Firmware | UEFI/Legacy, Secure Boot, supported OEM provider if present |
| Session | Console or RDP; standard user, admin member or elevated process |
| Display | DPI scaling used (100 / 150 / 200%) |
| Security stack | Effective AV/EDR and firewall provider/state; do not disable them |
| Update source | Configured Windows Update/WSUS source and pending state where observable |
| Network | LAN/Wi-Fi/VPN state; proxy if relevant |
| Identity | Local/domain/Entra-style sign-in as applicable; do not publish secrets |
| Local-admin policy | ADMX/GPO/Registry delivery state for `AllowedLocalAdministrators` |
| Result | PASS / FAIL / NOT RUN / N/A plus evidence path |

Evidence may contain usernames, SIDs/domain names, device identifiers, paths, IP addresses, ports, event text and symptom notes. Keep pilot evidence in an approved location and review it before external sharing. Never collect or publish BitLocker recovery keys, BIOS/UEFI passwords, AV/EDR passwords/tokens or credentials.

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

Expected: normal diagnosis starts without a location/name refusal. The product remains one portable x64 EXE; Program Files installation is not an acceptance requirement. No adjacent JSON/INI/XML/ADMX file is required for runtime operation.

## 3. Baseline main-window diagnosis

Launch by normal double-click as a standard user.

Verify:

- no UAC merely to diagnose;
- GUI remains responsive during collection;
- computer/user/execution context is plausible;
- technical Health Score and technical coverage remain independent from Security Score/coverage;
- CPU/RAM/system-volume/uptime and technical coverage are shown without treating unavailable telemetry as zero;
- the **ИБ / SECURITY** card is present, shows score/band/security coverage separately and opens the Security tab;
- Security `Unknown` does not become Pass/zero and low coverage does not receive a green High/Good presentation;
- recommendations distinguish observation from remediation;
- process/event/system views render and can be sorted without exceptions;
- clipboard/HTML/JSON report actions work and Security evidence is present without raw secrets;
- repeated diagnosis replaces the current result cleanly;
- final status is not overwritten by late progress from an earlier phase;
- non-C Windows volume is identified correctly on at least one applicable machine or synthetic lab image;
- switch between RU and EN without restarting; operator text changes while stable IDs/raw provider evidence remain semantically unchanged;
- About shows the running 0.17.0 version and attribution.

If a provider is unavailable under standard-user permissions, record the limitation; do not rerun the whole application elevated merely to make the metric appear.

## 3A. Endpoint Security Posture provider matrix

Run on representative Windows 11 endpoints without weakening their normal protection.

### Defender-primary endpoint

Verify:

- Defender is identified as the effective primary AV when it actually owns the role;
- AV active/RTP/definitions/tamper/platform evidence is consistent with local state;
- Security coverage reflects unsupported/unavailable provider fields as `Unknown` rather than fabricated health;
- the blue planner may offer Defender definitions update or Defender RTP only when fresh local evidence proves the action is needed and locally actionable.

### Kaspersky-primary + passive Defender endpoint

Verify:

- active Kaspersky is selected as primary and passive Defender does not create a false AV failure;
- fixed read-only KESCLI/OPSWAT state is shown only when the installed interface is actually available;
- if Windows Security Center COM product enumeration fails, strong read-only KESCLI evidence may recover AV active/RTP/definitions without inventing platform/tamper state; passive Defender must not override an active Kaspersky fallback;
- Kaspersky definitions update is the only Kaspersky automatic blue candidate and only when the fixed supported local interface/policy permits it;
- Kaspersky RTP remains read-only/manual and no `SecurityEnableKasperskyRtp` action exists;
- KSC/tamper/policy refusal is reported as blocked/unavailable/failure rather than bypassed.

### Ambiguous/other AV provider

Where available, record one endpoint/provider that is not Defender/Kaspersky or intentionally ambiguous in a lab fixture. Primary-dependent controls must remain `Unknown`/unsupported; the blue batch must not guess a provider or execute a generic security command.

## 3B. Firewall, Windows Update and platform Security matrix

### Firewall ownership

Cover where available:

- Windows Defender Firewall effective provider with Domain/Private/Public enabled;
- a test case with an applicable Windows profile disabled/local-control state;
- active third-party firewall ownership;
- centrally enforced Windows Firewall state.

Expected: third-party ownership does not false-fail solely because Defender Firewall profiles are off; blue Windows Firewall enablement is unavailable for third-party/ambiguous ownership and blocked when central policy owns the disabled state.

### Windows Update / WSUS

On an endpoint using the organization's normal configured update source, record:

- last successful qualifying update evidence;
- applicable pending qualifying update if present;
- pending reboot evidence;
- configured source/service context;
- elapsed time from the progress message `ИБ: проверяю обновления Windows…` until the next Security stage begins.

Pilot regression acceptance after the 2026-09-25 managed-endpoint finding:

- the Windows Update Security stage must complete in **8 seconds or less** on the test endpoint, including a slow/unresponsive WUA search;
- the network-backed pending-update probe has a hard 5-second child-process budget and must not block the GUI scan indefinitely;
- locally installed Windows update history remains usable when the pending WUA search times out/fails;
- fresh installed-update evidence plus unresolved pending-update state is conservatively `Warn`, never `Pass`; severely stale installed-update evidence may still `Fail`;
- pending-update classification uses stable Windows Update classification IDs rather than relying only on localized title text;
- the product must not change WSUS/Windows Update source, trigger update installation or weaken update policy.

Do not change WSUS/update source for the test. A newly pending qualifying update should not be presented as the same severity as a severely stale endpoint. 0.17.0 must not install Windows quality/security updates automatically.

### Secure Boot / TPM / VBS-HVCI / UAC

Record real runtime evidence on supported Windows 11 hardware. If TPM WMI is denied/unavailable under the standard-user context, the read-only Windows TBS device-info fallback may prove TPM presence/version; presence of TPM 2.0 with readiness unresolved is `Warn`, never `Pass`. If neither trusted source is available, TPM remains `Unknown`. The blue batch must not change these controls.

## 3C. BitLocker and fixed-data-volume Security matrix

Use approved test endpoints; do not collect recovery passwords/keys.

Cover where available:

- protected OS volume;
- applicable protected fixed internal data volume;
- a disposable/lab unprotected or transitional volume case;
- removable/EFI/recovery/optical exclusions.

Verify per-volume evidence identifies the applicable volume and protection/conversion state but never recovery material. If BitLocker WMI is denied/unavailable, a bounded read-only `manage-bde -status <drive> -protectionaserrorlevel` fallback may prove protection on/off only; because conversion/encryption details remain unresolved, that limited evidence is `Warn`, never `Pass`. OS/data controls are assessment/manual-remediation only in 0.17.0; the blue batch must not enable/provision BitLocker.

## 3D. Firmware/OEM Security matrix

Use representative hardware where available:

- Lenovo with the already-present supported BIOS WMI provider;
- Dell with an already-present supported provider/Command Monitor surface;
- HP with an already-present supported Instrumented BIOS/provider;
- at least one unsupported/absent OEM provider case.

Verify BIOS admin-password and boot-restriction status only when trusted provider telemetry is sufficient. The unsupported/absent provider must return `Unknown`, not install OEM tooling and not guess from BCD alone. Never enter/read/store/export a BIOS password. No firmware setting is automatically changed.

## 3E. Local Administrators ADMX/GPO matrix

Deploy the supplied Administrative Templates in an approved test GPO/lab policy store:

- `policy/GPCHealthCheck.admx`;
- `policy/en-US/GPCHealthCheck.adml`;
- `policy/ru-RU/GPCHealthCheck.adml`.

Policy path:

`Computer Configuration -> Administrative Templates -> G PC Health Check -> Security Posture -> Allowed local administrators`

The endpoint must read only `HKLM\SOFTWARE\Policies\GPCHealthCheck\AllowedLocalAdministrators` as `REG_MULTI_SZ`.

Test on disposable/approved endpoints:

- exact principal rule;
- `*` wildcard rule;
- `?` wildcard rule;
- SID rule such as the renamed built-in Administrator RID-500 pattern;
- permitted direct group;
- explicitly empty effective allow-list;
- missing policy;
- wrong-type/unreadable policy where it can be reproduced safely;
- an unauthorized direct administrator only on a disposable/lab endpoint.

Expected: whole-string case-insensitive matching; `*` = zero or more, `?` = exactly one, no regex/environment expansion. Missing/unreadable policy is `Unknown`; a confirmed unauthorized direct member is `Fail`. The product never removes membership automatically.

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
- `CleanTemp` is not smuggled into an inappropriate elevated-user context;
- cancelling a combined selection does not partially perform privileged actions;
- alternate-admin elevation does not make the technician account the target user profile.

DISM need not be executed merely to prove portability. Use it only when appropriate for the dedicated test machine.

A mapped/UNC path is a separate environmental case: the administrative identity must independently have access. The application must not bypass Windows/share policy.

## 6A. Red “Do everything / Сделать всё” batch — disposable workstation only

**Do not run this scenario first on a production workstation.** Use only an approved disposable/test workstation with a local recovery path and no business-critical active work. Save evidence before starting.

The red batch remains the exact 0.16.0 fixed set and is intentionally disruptive. It may:

- interrupt or sever VPN, RDP and ordinary network connectivity;
- release/renew DHCP;
- reset Winsock and TCP/IP state;
- restart network adapters;
- restart Spooler and update-related services;
- delete pending print jobs;
- refresh machine/user Group Policy;
- resynchronize time;
- run DISM RestoreHealth and SFC;
- leave Windows indicating that a reboot is required.

Before confirmation verify the UI shows the exact fixed 14-action set and the disruption/reboot warnings. The set must be exactly: `CleanTemp`, `FlushDns`, `RegisterDns`, `DhcpReleaseRenew`, `WinsockReset`, `TcpIpReset`, `RestartNetworkAdapters`, `RestartSpooler`, `ClearPrintQueue`, `RestartUpdateServices`, `GpUpdate`, `TimeResync`, `Dism`, `Sfc`.

During and after the run verify:

- at most one UAC approval is requested for the administrative batch;
- cancelling UAC performs no privileged worker actions;
- original-user work remains bound to the verified interactive user/session/profile;
- the worker never exposes or accepts an arbitrary command/service/adapter target;
- network-disruptive work happens after earlier non-network phases;
- if VPN/RDP drops, the endpoint remains recoverable locally and the result is not falsely reported as a clean remote success;
- print-queue deletion is explicitly visible in the confirmation/results and is tested only with disposable jobs;
- before/after/result evidence records each action as run/skipped/superseded/failed as applicable;
- a reboot requirement is reported rather than forcing reboot/logoff;
- Defender/AV/EDR/firewall and other protective software remain enabled and are not weakened;
- no blue Security hardening action is silently included in the red batch.

Hosted CI uses fakes/negative tests for these mutations and is **not** evidence that this real disruptive scenario passed.

## 6B. Blue “Harden security / Усилить защиту” batch — approved test workstation

Run only where the fresh Security preflight offers at least one supported action. Do not force a control off on a production endpoint to create a candidate.

Before confirmation verify:

- blue is left of green, green left of red; red is not the default Enter action;
- the dialog lists runnable and skipped Security actions separately with reason/risk/UAC/policy context;
- the plan contains only the exact approved three action IDs;
- Kaspersky RTP, BitLocker, firmware, UAC, TPM, VBS/HVCI, OS update installation and local-admin membership are not runnable blue actions.

During execution verify:

- fresh Security/context collection occurs before authorization;
- at most one UAC elevation is requested for the fixed security worker;
- security-worker namespace rejects ordinary Service Desk action IDs and vice versa;
- policy/tamper/management refusal is not bypassed;
- after execution the app recollects Security posture before claiming an improvement;
- failed/blocked/skipped actions are not displayed as Pass merely because a process returned;
- green/red batches remain unchanged and do not inherit blue actions.

Minimum real cases where safely available: Defender definitions update, Defender RTP enable on a disposable/lab endpoint where policy permits, Windows Firewall enable on a locally controllable approved test endpoint, and Kaspersky definitions update only where KSC/local policy permits normal local execution.

## 7. DPI, keyboard and display acceptance

Run the primary windows at **100%, 150% and 200% DPI**. At least one 150/200% case should use a normal corporate laptop resolution rather than only a large desktop monitor.

Cover:

- main window including seven metric cards and three superbuttons;
- Security tab/grid and hardening confirmation/results;
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

Expected: no clipped primary buttons/fields, unusable grids, invisible status text or modal dialogs outside the visible work area. Keyboard navigation and cancel/close paths remain usable. Long device/path/process/provider/principal names must not crash layout or details views.

## 8. RDP session acceptance

Run one standard-user pilot through RDP on a managed Windows 11 endpoint.

Verify:

- execution/session context reflects the RDP session;
- diagnosis, Security posture and read-only tools remain usable;
- UAC behavior is understandable in the remote session and cancellation returns control cleanly;
- clipboard/report behavior follows environment policy;
- DPI/resizing after RDP connect/reconnect does not corrupt controls;
- closing a long-running read-only window does not leave accumulating provider work.

Do not infer console-session behavior from RDP or vice versa; record which was tested.

## 9. Corporate VPN and EDR coexistence

On a representative managed endpoint, repeat baseline diagnosis and selected read-only tools with corporate VPN and EDR/AV active. Do **not** relax security controls for the test.

Minimum cases:

- baseline diagnosis and Security posture while connected to VPN;
- Resource Probe to an explicitly approved test target;
- Endpoint Review;
- Incident Review;
- Service Desk Diagnostic Bundle;
- evidence ZIP creation and local analysis;
- blue hardening only if explicitly approved and naturally applicable on the test endpoint.

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

For Security posture also record whether the OEM firmware provider is supported/present and verify unsupported telemetry remains `Unknown`. Do not run surface tests, destructive checks or vendor firmware changes as part of this acceptance plan.

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

Review Security exports specifically for accidental recovery/password/token/raw-command-output leakage. Never post unreviewed workstation evidence publicly.

## 16. Acceptance matrix

Use one row per actual machine/session. Do not mark a row PASS from hosted CI alone.

| Scenario | PASS criteria | Result | Evidence / issue |
|---|---|---|---|
| Standard-user baseline | no UAC for diagnosis; technical/Security telemetry gaps explicit; reports work | NOT RUN | |
| Security card/tab | separate score/band/coverage; Unknown truthful; navigation works | NOT RUN | |
| Defender primary | effective provider and local protection/definition evidence correct | NOT RUN | |
| Kaspersky primary + passive Defender | Kaspersky selected; passive Defender no false fail; RTP remains manual | NOT RUN | |
| Third-party/ambiguous AV | no generic provider mutation; dependent controls Unknown/unavailable | NOT RUN | |
| Windows Firewall ownership | Windows/third-party/central-policy cases handled truthfully | NOT RUN | |
| Enterprise WUA/WSUS | configured source respected; pending/stale evidence correct; no auto OS install | NOT RUN | |
| BitLocker OS/data | applicable volumes classified correctly; no recovery material collected | NOT RUN | |
| Secure Boot/TPM/VBS/UAC | real runtime evidence plausible; unavailable stays Unknown | NOT RUN | |
| Lenovo firmware provider | BIOS/boot evidence read-only and plausible | NOT RUN | |
| Dell firmware provider | BIOS/boot evidence read-only and plausible | NOT RUN | |
| HP firmware provider | BIOS/boot evidence read-only and plausible | NOT RUN | |
| Unsupported OEM/provider | Security firmware controls Unknown; no provider install attempt | NOT RUN | |
| ADMX exact rule | exact allowed local admin evaluates correctly | NOT RUN | |
| ADMX `*` rule | whole-string case-insensitive wildcard correct | NOT RUN | |
| ADMX `?` rule | exactly-one-character wildcard correct | NOT RUN | |
| ADMX SID rule | SID/RID-500 style rule works independently of localized name | NOT RUN | |
| ADMX empty/missing policy | explicit empty vs missing/unreadable remain distinct | NOT RUN | |
| Unauthorized local admin | disposable endpoint only; confirmed unauthorized direct member -> Fail | NOT RUN | |
| Blue hardening | fresh preflight, exact plan, max one UAC, post-rescan, no policy bypass | NOT RUN | |
| Blue/green/red separation | no cross-batch action leakage; red remains exact 14 | NOT RUN | |
| RU/EN switch | language changes without restart; stable IDs/raw evidence preserved | NOT RUN | |
| Non-C system volume | correct Windows system-volume identity | NOT RUN | |
| Temp cleanup | old sentinel removed; fresh/junction preserved; safe context enforced | NOT RUN | |
| Renamed portable UAC cancel | UAC reached; cancel causes no repair/orphan | NOT RUN | |
| Positive alternate-admin UAC | result returns to parent with evidence | NOT RUN | |
| Red 14-action batch | disposable workstation only; one-UAC phased result and recovery verified | NOT RUN | |
| DPI 100% | primary tools/Security UI usable | NOT RUN | |
| DPI 150% | primary tools/Security UI usable | NOT RUN | |
| DPI 200% | primary tools/Security UI usable | NOT RUN | |
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

## Exit criteria for 0.17.0 managed acceptance

Managed Windows 11 acceptance can be considered complete only when:

1. required matrix rows have explicit PASS/FAIL/N/A outcomes from actual managed Windows 11 sessions;
2. Defender-primary and Kaspersky-primary+passive-Defender cases were exercised where the organization uses those providers;
3. Windows/third-party firewall ownership and enterprise Windows Update source behavior were covered;
4. BitLocker OS/data plus Secure Boot/TPM/VBS/UAC evidence were covered without collecting secrets;
5. representative Lenovo/Dell/HP firmware providers were covered where hardware is available and an unsupported provider case proved `Unknown`;
6. ADMX delivery plus exact/`*`/`?`/SID/empty/missing policy behavior were exercised; unauthorized-admin FAIL was manufactured only on an approved disposable/test endpoint;
7. the blue hardening batch was exercised on an approved test endpoint with fresh before/after recollection, policy-boundary behavior and no cross-batch leakage;
8. at least one standard-user corporate endpoint and one approved alternate-admin UAC case were exercised;
9. DPI 100/150/200 and RDP were covered;
10. VPN/EDR coexistence was covered without disabling controls;
11. representative OEM/storage diversity was covered, including USB/RAID/VMD where available or explicitly marked N/A;
12. Service Desk Diagnostic Bundle and the major read-only analysis workflows were exercised;
13. the red exact 14-action batch was exercised on an approved disposable/test workstation with recovery/result evidence or explicitly remains a release/pilot blocker;
14. every FAIL has a linked reproducible defect or an accepted/environmental limitation;
15. the tested EXE hash/version and evidence location are recorded;
16. no open acceptance blocker is hidden behind a hosted-CI PASS.

Major future ideas such as full CIS/Microsoft baseline compliance, raw/full SMART, broad system-handle enumeration, general treemap, stress/stability workloads, automatic BitLocker/firmware/VBS/local-admin remediation or KSC/Intune cloud integration are not acceptance prerequisites for 0.17.0 unless separately approved into scope.
