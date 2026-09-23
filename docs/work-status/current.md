---
schema: development-work-status/v3
repository: bajoicheg/g-pc-health-check
branch: design/0.17.0-security-posture
policy_revision: 2026-09-23-cdc-2.3.3-adoption-1
policy_digest: 31f60c3dd4936d0fd7dbdbd7ce54d00b9fe24b0f7f88ae98859dfcf1e99ac576
observed_at_utc: '2026-09-23T06:57:54Z'
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
control:
  execution_lease_ref: https://github.com/bajoicheg/g-pc-health-check/blob/cdc/coordination/lease.json
  execution_lease_revision: a020adeb890cf8bd49eb5a2b378a58d45b1eb2c8
  executor_id: null
  lease_generation: 0
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

# Current work status

CDC 2.3.3 is process-only state. The existing product candidate remains pinned at `pilot/0.17.0-rc-f037bece` -> `f037bece0272814f9b0f069aaf1de17be369b626`.

The retained pilot EXE has FileVersion `0.17.0.0` and SHA-256 `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`. Hosted/manual CI is not the managed Windows 11 acceptance gate.

The new CDC budget ledger starts at adoption and deliberately does not rewrite historical provider usage to zero. Previous run IDs and artifacts remain evidence; unknown provider quota remains unknown. Actions starts after adoption use `conserve` policy. Codex Compute is policy-configured as preferred COMPUTE_ONLY validation but remains disabled/unconfigured until a real environment/user/repository binding is independently verified.
