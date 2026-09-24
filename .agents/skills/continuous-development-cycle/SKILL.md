---
name: continuous-development-cycle
description: Use when substantial software development must continue across long sessions, interruptions, CI runs, repository migrations, watchdog resumes, development chat cleanup, Work/Codex orchestration, Codex Compute setup or failures, or limited compute budgets.
---

# Continuous Development Cycle v2.3.5

Durable repository state is the project state. Sessions, agents and schedulers are disposable. Apply the instruction hierarchy, preserve the source/scope of existing user authorization, and reconcile repository policy. Live remote facts override stale checkpoint/chat claims. A spinner, lease or submitted request is not progress evidence.

Before scheduler status, recovery or changes, read `references/watchdog-recovery-and-migration.md`. Keep user-authorized scheduler state separate from current-wake execution eligibility. Blockers, budget/runtime limits and quiet notifications do not authorize disabling a recurring watchdog. Honor a later verified user pause; audit unexplained drift rather than invent its cause. Protect verified task-linked chat IDs from cleanup; diagnose archived/missing chat dependencies before retrying. Read the chat recovery procedure in that reference.

## Route the executor

- **Ordinary ChatGPT chat:** no subagents; execute sequentially.
- **ChatGPT Work:** useful independent subagents explicitly allowed and requested within authorized work.
- **Codex used as the orchestration environment:** same delegation permission as Work.
- **Unknown origin:** no subagents. Watchdogs inherit their actual runtime.

Use adaptive **minimum sufficient effort**, one integrator and isolated/non-overlapping writers; honor configured agent budgets. Read `references/runtime-routing-and-subagents.md` when delegating. A delegated Codex `COMPUTE_ONLY` worker only verifies supplied exact-SHA commands; it cannot edit, commit, push, merge or close tasks. Unavailable delegation means sequential work.

## Recover with a compact probe

1. Read this core, current `AGENTS.md`/applicable instructions, adapter and compact checkpoint. Validate policy/checkpoint with `scripts/validate_adapter.py` and `scripts/validate_checkpoint.py`. Reconcile permission/version/digest drift using `references/policy-compatibility.md`; never silently discard a restriction or repeat an already-granted permission question.
2. Refresh canonical repository identity, branches/HEAD, coordination revision/owner/generation, external operations, and PR/CI/release revisions. An incomplete/stale lookup is uncertainty.
3. Use `scripts/recovery.py` and `references/bounded-recovery.md` to compare a durable snapshot with this fresh probe. On exact fresh agreement, load only phase-relevant references and changed material. Otherwise expand reconciliation. Always load actual current instructions; a matching HEAD alone cannot justify reuse of old task, lease or external state.
4. Before writes, apply the ownership, external-operation and budget gates in `references/orchestration-controls.md`.

Adapter/checkpoint v3 stay compatible with v2.2 records. The optional `orchestration` block requires skill 2.3+. Missing control configuration is not permission to elect a writer or fabricate available budget; establish an authorized backend/assignment and durable state first. Observe existing work while doing so.

## Own work before acting

Read `references/execution-ownership.md`. Use a unique executor UUID, monotonically increasing lease generation and conditional updates to a separate coordination ref. Refetch/check ownership and expected revision immediately before each shared write/external start. Renew only after a new observed owner action.

A timestamp or local atomic file replacement is not a distributed lock. Git coordination uses ordinary conditional fast-forward pushes, never force-push. Where conditional storage is unavailable, only the explicitly designated executor may write; others observe. Expiry alone never authorizes takeover: require release or verified previous-executor quiescence, including pending effects. CAS ownership does not fence arbitrary downstream writes.

Persist/read back the exact operation intent and external guard before submission; consume the submission claim once through the ownership protocol. Lost replies, unknown outcomes and active tasks retain the guard across lease expiry/handoff. Reconcile using `references/external-operations.md`; never reconstruct a launch grant from a saved record. Keep an in-flight candidate's source ref stable.

## Execute and verify

Follow the configured lifecycle, ordinarily:

`recover → test/spec RED → durable exact SHA → bounded implementation → targeted GREEN → independent review → required final gate → close task → next task`

Use Codex Compute first for eligible authorized, configured and platform-compatible candidate validation. A local runtime alone does not displace it. Read `references/codex-compute.md` for actual access/environment/task binding; retain `references/validation-compute-and-ci.md` platform and Actions-budget gates. Small local preflight/RED/debugging loops are useful. On concrete unavailability, ineligibility or budget denial, use an authorized valid fallback; do not stall when one exists or bypass an exhausted Actions budget.

Use the versioned plan, `scripts/run_checks.py` and `scripts/validate_evidence.py` from `references/command-evidence.md`. Bind evidence to the independently expected plan, exact SHA and environment configuration. Each command reports its own exit and `PASS / EXPECTED_RED / FAIL / NOT_RUN`; a wrapper exit, absent/zero-test report or expected RED is not final GREEN.

## Bound waits and spending

Use `scripts/recovery.py` for separate queue/setup/run/unknown deadlines, last successful observation and capped poll backoff. A missed deadline requests diagnosis; it never proves a task stopped or permits replacement. Respect provider retry timing. Do not stop supervising an authorized existing task merely because four (or any preset number of) polls have occurred. Default status-poll count caps are null; bound polling by backoff and phase deadlines, and diagnose at the deadline. Observing the same task requires no renewed launch permission. Keep blocking waits at most 60 seconds, preserving handoff state when runtime/budget ends.

Use `scripts/budget.py` and `references/budget-ledger.md`: reserve starts before effects, preserve uncertain charges, track per-task/per-wake use and leave checkpoint resources. Default Codex Compute admission is **4 starts per wake/cycle** (`wake_limits.compute_starts: 4`); keep GitHub Actions policy and CI-start limits unchanged. Unknown tokens/quotas stay unknown. Repeated failure requires concrete correction/service recovery; a new SHA alone does not repair infrastructure. Budget approval is one gate, not launch permission.

**Higher compute capacity** is capacity, **not a retry instruction**. Spend the additional starts where each attempt has expected **information gain**: a deliberately distinct RED or GREEN check, independent candidate/platform validation, or a retry only after concrete correction or observed service recovery. A larger allowance is not a target; do not spend it on identical probes, unchanged setup failures, or duplicated verification that cannot change the decision.

Give concise updates on phase, evidence, blockers and backend changes; follow the runtime's communication interval. Persist control references and one concrete next action on handoff, release ownership and retain unresolved external guards. See `references/progress-and-checkpoints.md` and `references/watchdog-recovery-and-migration.md`.

Continue until approved work and closure/release gates are complete, a real blocker remains, or a runtime/tool limit forces a durable handoff. A commit, result, review or single completed task is a continuation point.

Release from an exact **release-candidate SHA** with configured version, platform, artifact and smoke/security evidence; see `references/release-management.md`. Do not start a parallel release line unless policy allows it. Apply the concurrency guard and verify canonical identity before every push; stop on unreconciled external movement.

## Companion skills

Use available `superpowers:brainstorming` for new/scope-changing work, `superpowers:writing-plans` for multi-step implementation, `superpowers:test-driven-development` for features/fixes, `superpowers:systematic-debugging` for failures, `superpowers:requesting-code-review` for independent review, `superpowers:verification-before-completion` before success claims, and `superpowers:finishing-a-development-branch` for integration. Existing user authorization and higher-priority instructions govern their workflow gates.
