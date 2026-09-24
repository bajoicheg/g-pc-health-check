---
schema: development-work-status/v4
repository: bajoicheg/g-pc-health-check
branch: design/0.17.0-security-posture
policy_revision: 2026-09-24-cdc-2.5.0-routing-events
policy_digest: null
observed_at_utc: '2026-09-24T19:05:31Z'
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
  last_progress_ref: 'main-cdc-2.5:9cc97f577921969f7e09a5bb2234327319e8b32c'
control:
  execution_lease_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/lease.json
  execution_lease_revision: bf7dd5173fb3a84961e253c67d262d45c7a889c1
  executor_id: null
  lease_generation: 0
  budget_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/budget.json
  recovery_snapshot_ref: null
  external_wait_ref: null
active_change: 0.17.0-security-posture
current_task: Managed Windows 11 pilot and pre-integration acceptance
phase: recovery
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
blocker: cdc_2_5_active_branch_policy_binding_reconciliation; managed_windows_11_pilot_pending; explicit_owner_integration_approval_required
next_action: Validate the exact CDC 2.5 active-branch package/adapter/checkpoint without spending the product Actions budget, bind the semantic policy digest, migrate the released generation-0 coordination lease v1→v2, initialize backend/continuation/resume records, then restore the managed Windows 11 pilot blocker and keep PR #92 unmerged.
---

# CDC 2.5 active-branch migration checkpoint

The 0.17.0 product/pilot SHA, retained Windows evidence and explicit no-merge-without-owner-approval rule remain unchanged. This process-only migration temporarily uses recovery phase until the branch-specific semantic policy digest and released coordination migration are durably bound.

# Current work status

CDC 2.5.0 is process-only state. Scheduler lifecycle and chat-dependency protections are active; BLOCKED or budget/runtime limits end only the current wake and do not authorize disabling the recurring watchdog. The existing product candidate remains pinned at `pilot/0.17.0-rc-f037bece` -> `f037bece0272814f9b0f069aaf1de17be369b626`.

The retained pilot EXE has FileVersion `0.17.0.0` and SHA-256 `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`. Hosted/manual CI is not the managed Windows 11 acceptance gate.

The new CDC budget ledger starts at adoption and deliberately does not rewrite historical provider usage to zero. Previous run IDs and artifacts remain evidence; unknown provider quota remains unknown. Actions starts after adoption use `conserve` policy. Codex Compute is policy-configured as preferred COMPUTE_ONLY validation but remains disabled/unconfigured until a real environment/user/repository binding is independently verified.
