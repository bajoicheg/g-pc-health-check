---
schema: development-work-status/v4
repository: bajoicheg/g-pc-health-check
branch: design/0.17.0-security-posture
policy_revision: 2026-09-26-cdc-2.8.2-fleet-adoption
policy_digest: cf838783316d918a00a6355fc596547187447cc847571086cd60d9c6928f8436
observed_at_utc: '2026-09-26T11:11:09Z'
orchestration_origin: chat
active_executor: none
lease_state: released
executor_heartbeat_at_utc: null
execution_lease_until_utc: null
waiting_external_kind: null
waiting_external_id: null
waiting_external_sha: null
operation_intent_ref: null
operation_key: null
resume_capsule_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/resume.json
execution_continuity:
  invocation_id: chat-2026-09-25T120258Z-cdc271-policy-convergence
  runnable_next_action: false
  meaningful_progress: true
  primitive_steps_since_progress: 0
  completion_gate: resumable_blocker
  last_progress_ref: 'git:e20b8487a3d84e557c0886f59d8ed7704be2fb09'
control:
  execution_lease_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/lease.json
  execution_lease_revision: 8a98465cc580ed175bd69a13295c22b563cb519c
  executor_id: null
  lease_generation: 8
  budget_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/budget.json
  recovery_snapshot_ref: null
  external_wait_ref: null
active_change: 0.17.0-security-posture
current_task: CDC 2.7.1 core and project policy converged; managed Windows 11 acceptance remains
phase: blocked
implementation_sha: e20b8487a3d84e557c0886f59d8ed7704be2fb09
candidate_sha: cbd20da8822f321df0d6404a955bf2cea9e553bb
last_green_sha: cbd20da8822f321df0d6404a955bf2cea9e553bb
last_green_evidence: auto pilot push run 36128619572 success; artifacts 10861076525 (EXE) and 10860941608 (pilot E2E)
active_compute: none
active_ci_run_id: ''
last_ci_run_id: '36128619572'
last_ci_status: success
release_version: 0.17.0
release_candidate_sha: cbd20da8822f321df0d6404a955bf2cea9e553bb
release_state: blocked
blocker: managed_windows_11_retest_pending; explicit_owner_integration_approval_required
next_action: Use the automatically retained artifacts from run 36128619572 for the managed Windows 11 baseline retest, measuring Windows Update Security stage latency and recovered Security coverage. No manual workflow_dispatch/Run workflow is required. Do not merge PR #92 or enable auto-merge without explicit owner integration approval.
---

# CDC 2.5 active-branch binding complete

The exact vendored 2.5 package subtree matches validated `main` byte-for-byte (`0a5d673f6f0a9456655a59b607cba6ee774e5219`). Branch-specific adapter semantic digest is `91ab8cf8408a4cf2cb8f758b3397a33dc90a1eba40b3de45e3bb7db5cf2c1bc5`. The 0.17.0 product/pilot SHA, retained Windows evidence and explicit no-merge-without-owner-approval rule remain unchanged.

# Current work status

CDC 2.5.0 is process-only state. Scheduler lifecycle and chat-dependency protections are active; BLOCKED or budget/runtime limits end only the current wake and do not authorize disabling the recurring watchdog. The existing product candidate remains pinned at `pilot/0.17.0-rc-f037bece` -> `f037bece0272814f9b0f069aaf1de17be369b626`.

The retained pilot EXE has FileVersion `0.17.0.0` and SHA-256 `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`. Hosted/manual CI is not the managed Windows 11 acceptance gate.

The new CDC budget ledger starts at adoption and deliberately does not rewrite historical provider usage to zero. Previous run IDs and artifacts remain evidence; unknown provider quota remains unknown. Actions starts after adoption use `conserve` policy. Codex Compute is policy-configured as preferred COMPUTE_ONLY validation but remains disabled/unconfigured until a real environment/user/repository binding is independently verified.


## CDC 2.5 main-line reconciliation — 2026-09-24

Current `main` CDC 2.5 lineage was merged into this branch at `4d42c155a6e2fecbd0808db341908b748301562f`. Product source and the pinned pilot candidate were not changed. Missing CDC 2.3.9/2.4/2.5 adoption records, execution-continuity tooling and the CDC section in `docs/DEVELOPMENT.md` were imported; stricter branch-specific adapter/checkpoint/product boundaries were retained.


## CDC 2.7 canonical core adoption — 2026-09-25

At a released execution-lease/v2 safe boundary, the vendored CDC core was replaced
with the immutable canonical CDC 2.7.0 package from `bajoicheg/g-cdc`.
The resulting Git subtree is exactly
`a667549d48c2e93cba36359335c1b1ff4534ac86`, bound by
`docs/cdc-consumer-lock.json` to release ref `refs/heads/release/v2.7.0` and
release commit `5b84c89596e04d8411bf6cc24d8aa882a24c483a`.

This is a process-only CDC adoption. It does not alter or revalidate the retained
0.17.0 product/pilot candidate, does not satisfy the managed Windows 11 pilot, and
does not grant merge/integration approval.


## Managed Windows pilot regression correction — 2026-09-25

Real pilot evidence on a managed Windows 11 Huawei endpoint found Security coverage 15% and repeated Windows Update Security stage delays above 60 seconds (total scans about 90s and 242s). Tests-first corrective work is now on code candidate `ad436c50a00cc40b0f291696a4ab56fa4344de09`: bounded 5-second pending-WUA child probe, conservative QFE fallback, provider-specific AV fallback after WSC COM failure, native TBS TPM fallback and read-only manage-bde BitLocker protection fallback. No security remediation surface was expanded. Local-admin missing-policy and unsupported Huawei firmware remain Unknown by design.

The current repository head that includes this checkpoint must pass the full Windows PR gate before it can become a new pilot candidate. The managed endpoint must then be retested; the Windows Update Security stage acceptance target is <=8 seconds and any increase in coverage must come only from explicit trusted evidence, not Unknown-to-Pass coercion.


## Autonomous pilot artifact path — 2026-09-25

`.github/workflows/build.yml` now treats `pilot/**` push events as first-class
Windows EXE build triggers while retaining the existing rule that pull-request runs
do not upload binary artifacts. The pilot marker
`cbd20da8822f321df0d6404a955bf2cea9e553bb` triggered run
`36128619572` automatically and completed GREEN. GitHub retained both
`g-pc-health-check-windows-x64` (artifact 10861076525) and
`g-pc-health-check-pilot-e2e` (artifact 10860941608).

This removes `workflow_dispatch` availability and a human “Run workflow” click
from the normal CDC execution path. Release and supply-chain workflows remain
protected because they require successful push builds on `main`.


## CDC 2.7.1 autonomy and lease-v2 hardening — 2026-09-25

At released/no-guard ownership boundary, the vendored CDC core was advanced to
canonical CDC 2.7.1 from `bajoicheg/g-cdc`. Exact package subtree:
`a78b8e7df4bfdd5a067f9e9da5a3a8a4b33394fc`; immutable release ref
`refs/heads/release/v2.7.1`; release commit
`8e97192ef89bf37657a4444a954530f22e8b2267`. The binding is recorded in
`docs/cdc-consumer-lock.json`.

CDC now treats missing connector/API operations (including unavailable
`workflow_dispatch`) as an execution-transport capability gap, not implicit owner
approval. Safe durable event triggers/compatible backends are attempted before asking
for a mechanical user action. The same release also makes Git lease storage schema-aware
and rejects malformed owned v2 records without valid invocation/finalization before CAS.

This process-only adoption does not satisfy the managed Windows 11 acceptance gate and
does not grant PR merge/integration approval. The autonomous pilot artifact evidence
remains run `36128619572` at exact pilot SHA
`cbd20da8822f321df0d6404a955bf2cea9e553bb`.


## CDC 2.7.1 project policy convergence — 2026-09-25

Project adapter now requires CDC >=2.7.1 and targets convergence 2.7.1. The semantic
adapter digest was independently recalculated with the CDC canonical JSON digest
algorithm: `0550cb93b1f895e50af09d268965604dd8c2dfa74cab5ddd62c286c329ed0fbf`.
The digest matches this checkpoint. Exact vendored package tree remains
`a78b8e7df4bfdd5a067f9e9da5a3a8a4b33394fc`.

The project therefore no longer has formal 2.6 target drift. Product acceptance remains
blocked only on the real managed-Windows-11 retest and explicit integration approval,
not on CDC transport or policy convergence.


## CDC 2.8.2 fleet adoption — 2026-09-26

Process-only convergence advanced this active design line to canonical CDC 2.8.2.
Exact vendored subtree: `bdf18b8dedb2f0cf62728935d92e6260b4a64ef0`.
Semantic adapter digest: `cf838783316d918a00a6355fc596547187447cc847571086cd60d9c6928f8436`.
The retained managed-Windows pilot evidence, product candidate, managed-Windows retest
blocker, and explicit owner integration approval gate are unchanged.
