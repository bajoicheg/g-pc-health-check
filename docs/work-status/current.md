---
schema: development-work-status/v4
repository: bajoicheg/g-pc-health-check
branch: design/0.17.0-security-posture
policy_revision: 2026-09-25-cdc-2.6.0-fleet-control-plane
policy_digest: 362b598cafef3ca535f0f2a097ba6c50877bcb03a388fead08c59fc0e2a4fa97
observed_at_utc: '2026-09-25T11:20:00Z'
orchestration_origin: chat
active_executor: d665904f-e88a-4f59-80e0-d56ce2193058
lease_state: active
executor_heartbeat_at_utc: '2026-09-25T11:16:14Z'
execution_lease_until_utc: '2026-09-25T11:36:14Z'
waiting_external_kind: null
waiting_external_id: null
waiting_external_sha: null
operation_intent_ref: null
operation_key: null
resume_capsule_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/resume.json
execution_continuity:
  invocation_id: chat-2026-09-25T111614Z-pilot-artifact-autonomy
  runnable_next_action: true
  meaningful_progress: true
  primitive_steps_since_progress: 0
  completion_gate: continue_execution
  last_progress_ref: 'actions:36128619572:success'
control:
  execution_lease_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/lease.json
  execution_lease_revision: 8a98465cc580ed175bd69a13295c22b563cb519c
  executor_id: d665904f-e88a-4f59-80e0-d56ce2193058
  lease_generation: 6
  budget_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/budget.json
  recovery_snapshot_ref: null
  external_wait_ref: null
active_change: 0.17.0-security-posture
current_task: Pilot artifact automation complete; managed Windows 11 acceptance remains
phase: validating
implementation_sha: 2fcf4413a96af6758222ec06285e380c1d362eb6
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
