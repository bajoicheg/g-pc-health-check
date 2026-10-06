# G PC Health — rebranding and project state

The product owner requested the exact display name **G PC Health**. This is a bounded presentation and packaging change on top of the active 0.17.0 development line.

## Branding contract

| Surface | Current identity |
|---|---|
| Product, dashboard, About and RU/EN window/report headings | `G PC Health` |
| Newly built portable Windows x64 executable | `G-PC-Health.exe` |
| Checksum | `G-PC-Health.exe.sha256` |
| New pilot package | `G-PC-Health-0.17.0-pilot.zip` |
| New evidence / analysis file prefix | `G-PC-Health-E2E-` |
| New SPDX file prefix and product name | `G-PC-Health-` / `G PC Health` |
| Existing GitHub repository | `bajoicheg/g-pc-health-check` |
| Existing source namespace and resource identities | `G.PcHealthCheck` |
| Existing language preferences | `%LOCALAPPDATA%\G\G PC Health Check\settings.json` |
| Existing report directory | `%LOCALAPPDATA%\G\PCHealthCheck\Reports` |
| Existing machine-policy key | `HKLM\SOFTWARE\Policies\GPCHealthCheck` |
| Existing ADMX/ADML deployment filenames and category IDs | `GPCHealthCheck`; visible category becomes `G PC Health` |
| Existing workflow artifact IDs | Retained to keep build/download/attestation consumers compatible |

The G-shield remains the established visual identity. Published historical releases, their filenames, checksums and verification records retain their original identities. The optional E2E comparison against an old Program Files installation still looks for its original filename; it is not an installation requirement. Supply the actual new executable path through `-SourceExe`.

## Historical observations — 2026-10-05

| Item | Fresh observation |
|---|---|
| Active product branch | `design/0.17.0-security-posture` @ `38b00741642cf11afb879767ebd41901daf5d9bb` |
| Active product PR | #92, draft/open; explicit owner integration approval still required |
| Main | `09a6956bb74ae1459bea2effe1605700ef861ea0` |
| Latest published release | `0.16.0`, commit `98870618dfc91a77a6868f775c8e9f0562eceee4` |
| Development version | `0.17.0` / FileVersion `0.17.0.0` |
| Last observed active-branch Windows gate | Run `36756774316`, success at `15e8d61d2982d28b314d1d7621cbf862f85c1b46` |
| Companion gates at that SHA | CDC `36756774276`, E2E analyzer `36756774301`, supply-chain smoke `36756774262`: success |
| Retained earlier pilot marker | `cbd20da8822f321df0d6404a955bf2cea9e553bb`; Windows run `36128619572` |
| Current CDC binding | `2.11.3`; package tree `39f733127ac130de4f647cf9e5ec55afcca0769c` matches the consumer lock |
| Coordination | Generation 17, owner released, no external guard |
| Scheduler | Owner pause preserved; no scheduler changes |
| In-progress GitHub Actions | None observed during reconciliation |
| Separate maintenance PR | #105: CDC workflow consolidation on main; outside this rebranding change |

0.17.0 adds independent Endpoint Security Posture and Security coverage, a Security tab, local-admin allow-list/GPO support and three fixed blue hardening actions. Existing technical assessment and 14 Service Desk actions remain separate.

The previously reported managed-Windows pilot still needs retesting: Windows Update Security collection exceeded 60 seconds and Security coverage was 15% on the managed Huawei endpoint. The implemented bounded WUA/provider fallback must be checked on that endpoint; target WUA stage duration is at most 8 seconds. Missing policy and unproven firmware/security telemetry stay `Unknown`.

The older checkpoint's CDC 2.11.2 finalization task and `blocker: none` were stale. CDC adoption is complete; Windows acceptance and explicit product integration approval remain unresolved. Old successful CI does not validate the new brand or new EXE name.

## Historical next steps — 2026-10-05

1. Publish this prepared rebranding change from the exact named base through an authorized managed executor; re-read the current product HEAD and released/no-guard coordination immediately before publication. Keep it separate from PR #105 and immutable CDC package bytes.
2. Run Windows Quick/Full and one final PR pipeline for the exact new source SHA. Verify `G-PC-Health.exe`, Product metadata, FileVersion, source/EXE self-tests, portable worker matrix, pilot manifest/checksum and the renamed release/SBOM/attestation paths. Existing product CI/release permissions and pinned Actions remain in place.
3. Obtain an independent code review, then repeat the managed Windows 11 pilot with the new binary: WUA timing/provider coverage, Huawei behavior, Defender/Kaspersky, RU/EN, resizing/DPI, alternate-admin UAC, retained language settings/report directory and existing ADMX/GPO deployment.
4. Record pilot evidence and obtain explicit owner approval for product integration. Merge only after the required checks and review; release from the exact successful main build and verify the downloaded hashes/provenance.
5. After 0.17.0 acceptance, reconcile older open issues #26 and #84 against implemented code and actual pilot evidence before closing them, then prioritize improvements from measured pilot defects.

## Historical validation observations — 2026-10-05

- Branding source/package contracts: **545/545 passed**; the pre-change probe failed on the old display/product/package names as expected.
- All 25 changed XML documents parse; RU/EN resource keys are retained and each changed value is an exact brand replacement.
- The four changed workflow structures differ only in product/file naming; action pins, permissions, triggers and artifact IDs are preserved.
- CDC package, adapter and checkpoint validators pass; immutable package source is unchanged.
- Independent read-only source review found no critical or important defect. Its historical-provenance wording correction is included.
- The full immutable CDC suite ran **950 tests: 948 passed, one failure, one error**. The missing `.agents/fault-injection/scenarios.json` is also absent from the named original base. `test_managed_pool_fault_regressions_are_retained` reproduced that error in isolation. The two-worker overlap test also failed in isolation: its fixed 0.8-second worker interval did not overlap the second launch in this environment. These are unresolved baseline/harness results, not a GREEN suite. Investigate the canonical consumer test setup and overlap synchronization separately without editing the immutable vendored core in this rebranding change.
- Windows compilation, PowerShell parsing/execution, EXE self-tests and Windows 11 acceptance are **NOT_RUN**; the required runtime is unavailable here.

The patch was checked for applicability against the exact named original base. Source preparation and these static checks do not authorize merge or publication of a product release.

## Current review candidate — 2026-10-06

Current source baseline: `813162ebb5f0fa574d7d8d0a6bf1ad1026d108bb`, `refs/heads/design/0.17.0-security-posture`. Canonical CDC 2.11.6 binds release `b3b517fb70e2deea4006e265f708f29881377885` and exact 329-file package tree `79257a06c40de6f514f9b059be05d610885a50e7`. Only the prepared branding change was ported; CDC 2.11.4 process commits were excluded. The latest observed coordination revision is `c7811cd7ed2ecf32efcce10d17b9cbb4be89b7dd`, released generation 19, owner/guard null. Recheck source, coordination, lease, guard and budget immediately before managed start; these observations grant no ownership.

ZIP/account intake is resolved. Actual Cloud tools are Git 2.52.0, Python 3.12.14, PowerShell 7.4.6 and .NET SDK 8.0.425. Authenticated reviewed Git execution is available; normal proxy/CA/TLS and per-call network review remain mandatory. Supported canonical managed_host_bridge publication is prepared and pending; no shared source publication, managed worker, lease or CI start is claimed.

Fresh branding contracts passed 545/545 on a tracked snapshot. All 36 XML documents and workflow YAML parsed; PowerShell parser checks passed. Linux cross-target compilation completed with zero warnings/errors using tracked icon/shield inputs in ignored obj/brand and DesignTimeBuild=true. This did not run the Windows brand generator, build a validated portable EXE, or verify GUI/UAC/Windows 11 acceptance. Immutable core/lock identity is exact; package/adapter/checkpoint/lock validators passed. A complete new CDC suite was not run. Canonical nine multidocument regression tests were reported GREEN upstream; separately, actual mixed-tree historical reads for generations 17/19 passed and unrelated root modes/OIDs were preserved across all 13 intervening coordination commits.

Independent source review of the 69-file product/tooling branding diff reported no Critical, Important or Minor findings; retained namespace/resource/GPO/state/protocol identities were verified. Documentation corrections preserve the historical observations above rather than representing their old failures or runtime limitations as current capability evidence.

Next steps: complete checkpoint/host-adapter review, reserve budget through the existing authoritative coordination CAS, perform package-managed conditional design publication and transactional release, observe one exact-final-SHA PR #92 synchronize Windows CI cycle, repeat the managed Windows 11 pilot, and obtain explicit owner integration approval. No dispatch/rerun/pilot push/main publication, merge, release or scheduler change is authorized here. The owner scheduler pause remains in force.

The tested implementation commit is `36a62146ed90cd4236e0aaf4a6826a1c6df4cf0a`. The final documentation/publication candidate is recorded outside this tree in the immutable host review journal after its commit exists; no circular self-SHA is asserted. Old CI/pilot evidence does not validate the new brand or executable name.
