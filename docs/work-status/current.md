---
schema: development-work-status/v4
repository: bajoicheg/g-pc-health-check
branch: design/0.17.0-security-posture
policy_revision: 2026-09-25-cdc-2.6.0-fleet-control-plane
policy_digest: 362b598cafef3ca535f0f2a097ba6c50877bcb03a388fead08c59fc0e2a4fa97
observed_at_utc: '2026-09-25T05:36:00Z'
orchestration_origin: chat
active_executor: none
lease_state: released
executor_heartbeat_at_utc: null
execution_lease_until_utc: null
waiting_external_kind: managed-windows-11-pilot
waiting_external_id: pilot/0.17.0-rc-f037bece
waiting_external_sha: f037bece0272814f9b0f069aaf1de17be369b626
operation_intent_ref: null
operation_key: null
resume_capsule_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/resume.json
execution_continuity:
  invocation_id: null
  runnable_next_action: false
  meaningful_progress: true
  primitive_steps_since_progress: 0
  completion_gate: resumable_blocker
  last_progress_ref: 'cdc26-integration:a5e6263a9fa2ec68e5825f85cc0dd9086bf3693b'
control:
  execution_lease_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/lease.json
  execution_lease_revision: 9f11462c4ac4af7bcf40fe5980794d4d234ac1b7
  executor_id: null
  lease_generation: 1
  budget_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/budget.json
  recovery_snapshot_ref: null
  external_wait_ref: null
active_change: 0.17.0-security-posture
current_task: Managed Windows 11 pilot and pre-integration acceptance
phase: blocked
implementation_sha: 2a7b1b78180773563fea7f4d10be86021f90d17f
candidate_sha: f037bece0272814f9b0f069aaf1de17be369b626
last_green_sha: f037bece0272814f9b0f069aaf1de17be369b626
last_green_evidence: PR #92 runs 35142227790, 35142227782, 35142227765; retained pilot run 35152451989
active_compute: none
active_ci_run_id: ''
last_ci_run_id: '35152451989'
last_ci_status: success
release_version: 0.17.0
release_candidate_sha: f037bece0272814f9b0f069aaf1de17be369b626
release_state: blocked
blocker: managed_windows_11_pilot_pending; explicit_owner_integration_approval_required
next_action: Use the retained pilot bundle from run 35152451989 on approved managed/disposable Windows 11 endpoints. Do not merge PR #92 or enable auto-merge. If source changes, create a new exact-SHA candidate and re-run the required gates under CDC budgets.
---

# CDC 2.5 active-branch binding complete

The exact vendored 2.5 package subtree matches validated `main` byte-for-byte (`0a5d673f6f0a9456655a59b607cba6ee774e5219`). Branch-specific adapter semantic digest is `91ab8cf8408a4cf2cb8f758b3397a33dc90a1eba40b3de45e3bb7db5cf2c1bc5`. The 0.17.0 product/pilot SHA, retained Windows evidence and explicit no-merge-without-owner-approval rule remain unchanged.

# Current work status

CDC 2.5.0 is process-only state. Scheduler lifecycle and chat-dependency protections are active; BLOCKED or budget/runtime limits end only the current wake and do not authorize disabling the recurring watchdog. The existing product candidate remains pinned at `pilot/0.17.0-rc-f037bece` -> `f037bece0272814f9b0f069aaf1de17be369b626`.

The retained pilot EXE has FileVersion `0.17.0.0` and SHA-256 `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`. Hosted/manual CI is not the managed Windows 11 acceptance gate.

The new CDC budget ledger starts at adoption and deliberately does not rewrite historical provider usage to zero. Previous run IDs and artifacts remain evidence; unknown provider quota remains unknown. Actions starts after adoption use `conserve` policy. Codex Compute is policy-configured as preferred COMPUTE_ONLY validation but remains disabled/unconfigured until a real environment/user/repository binding is independently verified.


## CDC 2.5 main-line reconciliation — 2026-09-24

Current `main` CDC 2.5 lineage was merged into this branch at `4d42c155a6e2fecbd0808db341908b748301562f`. Product source and the pinned pilot candidate were not changed. Missing CDC 2.3.9/2.4/2.5 adoption records, execution-continuity tooling and the CDC section in `docs/DEVELOPMENT.md` were imported; stricter branch-specific adapter/checkpoint/product boundaries were retained.
