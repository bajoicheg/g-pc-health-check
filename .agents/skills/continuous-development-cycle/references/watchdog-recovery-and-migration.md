# Watchdog, recovery, and repository migration

## Watchdog model

A watchdog is a best-effort wake-up mechanism. It executes this same development cycle from durable state.

Every wake:

1. reads current repository instructions, adapter and compact checkpoint; verifies a fresh recovery probe before skipping unchanged long spec/task bodies;
2. discovers actual repository identity, branch, HEAD, PR, CI, and release state;
3. validates policy compatibility and checkpoint binding, then reconciles stale data;
4. applies concurrency and budget guards;
5. resumes the next safe action;
6. continues until COMPLETE, BLOCKED, or a runtime limit.

An explicit kick bypasses only an idle threshold. It never bypasses concurrency, destructive-action, validation, release, or budget safeguards.

Watchdog subagent policy is inherited from the runtime that executes the wake: ordinary chat means no subagents; Work/Codex orchestration explicitly enables useful delegation by default with adaptive effort and no per-launch approval, subject to higher-priority restrictions. Prefer configured Codex Compute for eligible candidate checks and follow `codex-compute.md` for setup, task binding and fallback.

## Execution lease and heartbeat

Use the conditional ownership or designated single-writer protocol in `references/execution-ownership.md`. Checkpoint timestamps alone do not prevent concurrent writers or prove background activity. Apply the complete sequence in `references/orchestration-controls.md`.

Default reusable policy:

- `default_ttl_minutes: 20`;
- `heartbeat_fresh_minutes: 10`;
- renew only after a new observable owner activity reference such as a commit/checkpoint, compute request, or newly observed provider event; repeated polling of the same state is not activity;
- never renew merely because time passed, a chat is open, or an executor was previously active;
- before remote compute/CI waits, persist `waiting_external`, exact candidate SHA, and the external task/comment/run identifier;
- while that exact external work is actually queued/in-progress, it remains a concurrency guard even if the lease TTL expires;
- a terminal external result does not renew owner activity; stale heartbeat or expired TTL requests diagnosis, never takeover by itself. Require explicit release or verified previous-executor quiescence, including pending writes and provider calls;
- on handoff, blocker, runtime/tool stop, or normal session exit, explicitly release ownership and persist one exact next action;
- explicit kicks bypass idle guard only, never a fresh heartbeat or genuinely in-flight external operation;
- legacy long leases should be normalized at the next safe checkpoint rather than honored as unconditional locks.

A repository may override the numeric windows, but the semantic distinction between **ownership heartbeat** and **external work state** must remain.

Use `references/bounded-recovery.md` for separate phase deadlines and retry-aware polling. Queue/setup/run timeouts never clear a guard or authorize a replacement. Reserve each attempted poll against the per-wake budget and hand off with exact state when resources run low. A fresh matching recovery snapshot may reduce long-document reads; current instructions, ownership, external state and budget checks are always required.

If a checkpoint commit would move an in-flight request's source HEAD, preserve the exact external binding and lease/handoff fields in a durable PR coordination comment, identifying it as authoritative. Reconcile the checkpoint file after terminal state; retain the SHA actually tested. Read both the checkpoint and current PR coordination on recovery.

## Recovery after interruption

On a fresh session, never assume the previous session, Work run, Codex run, subagent, or scheduler is still alive.

Fetch remote facts. If the repository is ahead of the checkpoint, determine whether movement belongs to the same development lineage. Reconcile known same-cycle commits; stop only when unexplained external movement makes a write unsafe.

## Repository identity and migration

Repository migrations are first-class state changes. Before and after migration verify:

- canonical owner/repository identity;
- visibility/access required by the workflow;
- default branch and working branch;
- actual HEAD lineage;
- active PR destination/source refs;
- workflow/runner availability;
- release/tag state;
- checkpoint adapter remote identity;
- watchdog/scheduler prompts that may contain repository identifiers.

Never continue pushing to an old mirror simply because the local checkout or stale checkpoint still points there.

If source and destination repositories diverged during migration, establish the canonical history and record the reconciliation before further implementation.

## Interrupted submission

A durable `submitting` or `unknown` operation intent is an external guard even when the task ID is missing and lease has expired. Follow `references/external-operations.md`: complete provider lookup, match operation/attempt and actual task binding, and reuse the existing task/outcome. Empty or incomplete lookup never authorizes resubmission. A client timeout is not a terminal provider outcome.
