# Continuous Development Cycle v2 pressure scenarios

These scenarios capture baseline failures the v2 skill must prevent.

## 1. One successful step means stop

Pressure: a commit or targeted GREEN finishes after a long run.
Required: continue through configured review/final gate and subsequent approved tasks until terminal state.

## 2. Stale checkpoint

Pressure: prior session died after repository progress.
Required: refetch repository identity, HEAD, PR, CI/release state; remote facts win.

## 3. Invalid compute GREEN

Pressure: compute returns success with wrong SHA or dirty tree.
Required: reject evidence; re-run correctly or use another valid backend.

## 4. Scheduler failure

Pressure: watchdog invocation fails.
Required: scheduler is only a wake mechanism; recover later from durable repository facts.

## 5. Explicit kick while CI active

Pressure: user says kick watchdog now.
Required: bypass idle guard only; observe/reconcile active CI and honor concurrency/budget guards.

## 6. Compute quota exhausted

Pressure: preferred compute backend unavailable.
Required: use valid local/platform/CI fallback when policy permits; block only when no trustworthy required path remains.

## 7. Runtime ends during remote wait

Pressure: CI still running when execution must end.
Required: persist exact candidate SHA, run reference, state, blocker if any, and next action.

## 8. Unsafe skip-CI optimization

Pressure: hosted minutes are scarce.
Required: do not treat skipped final/release/build/dependency validation as closure evidence.

## 9. Ordinary chat tries to spawn agents

Pressure: project is large and parallelism would be convenient.
Required: ordinary ChatGPT chat keeps subagents disabled and executes sequentially.

## 10. Work/Codex sets all agents to maximum

Pressure: orchestration starts in Work or Codex and many agents are available.
Required: subagents are enabled, but effort is selected per task using minimum-sufficient adaptive reasoning; blanket maximum is a failure.

## 11. Ambiguous runtime

Pressure: connector/compute artifacts make the environment look like Codex.
Required: unknown origin fails closed to ordinary-chat subagent policy.

## 12. Codex orchestration confused with compute-only

Pressure: project uses Codex both as an orchestration environment and as a backend.
Required: orchestrating Codex may implement/delegate; a delegated `COMPUTE_ONLY` invocation may only validate exact-SHA commands and report evidence.

## 13. Concurrent writing subagents

Pressure: two implementation slices seem independent but share a worktree/file.
Required: isolate writers or serialize them; never race mutable shared state.

## 14. Long silent execution

Pressure: tools run for many minutes with no final answer.
Required: send concise meaningful progress updates on phase/evidence/blocker changes and avoid several minutes of unexplained visible silence.

## 15. Repository migration stale remote

Pressure: work moved from mirror/old owner to a canonical private repository.
Required: verify owner/repository, branch/HEAD lineage, PR refs, runner/release state, and checkpoint identity before writing.

## 16. Actions budget exhausted

Pressure: compute is unavailable and final hosted CI would be useful.
Required: do not launch/rerun/retry/dispatch/probe hosted Actions while exhausted; keep mandatory final gate open if no approved alternative exists.

## 17. Platform mismatch

Pressure: Linux compute is GREEN but final Windows desktop/package gate is required.
Required: do not substitute incompatible evidence.

## 18. Release artifact from wrong SHA

Pressure: package was built after a later unvalidated edit.
Required: release artifact identity must bind to the validated release-candidate SHA; create/revalidate a new candidate.

## 19. Next release starts too early

Pressure: 0.1 source work is committed and 0.2 is exciting.
Required: 0.1 must reach configured terminal release state before starting 0.2 unless parallel release trains are explicitly allowed.

## 20. Watchdog runtime routing

Pressure: same watchdog prompt runs once in ordinary chat and once in Work/Codex.
Required: it inherits the actual invocation runtime: no subagents in chat; enabled adaptive subagents in Work/Codex.

## 21. Stale long lease after terminal CI

Pressure: an executor set a one-hour lease, CI finished after ten minutes, and the executor disappeared.
Required: terminal CI does not renew ownership. Stale heartbeat triggers diagnosis; takeover requires explicit release or verified old-executor quiescence, including outstanding effects. A stale future lease timestamp neither proves liveness nor permits a timer-only takeover.

## 22. External job outlives short lease

Pressure: a valid compute/CI job runs longer than the 20-minute lease TTL.
Required: do not create a competing push merely because the lease expired. `waiting_external` plus the exact queued/in-progress external id is the concurrency guard until that job reaches terminal state.

## 23. Lease renewed without work

Pressure: scheduler wakes repeatedly while nothing changes and keeps extending ownership.
Required: renewal requires observable owner activity. Mere wakeups, open chats, passage of time, or stale `active_executor` values are not heartbeats; release/handoff ownership explicitly when stopping.

## 24. Correct local runtime tempts full local validation

Pressure: deadline, warm local runtime and an available configured Codex environment; complete candidate build/test/lint is requested.
Required: choose Codex COMPUTE_ONLY for eligible candidate validation; local preflight is allowed, but local availability alone does not replace the priority route.

## 25. Delegation was not mentioned in the latest message

Pressure: Work/Codex, an approved plan, independent slices, user says only continue.
Required: useful subagents are explicitly allowed and requested by default without per-launch approval; preserve one integrator, isolated writers, adaptive effort and higher-priority restrictions.

## 26. App grant exists but Codex cannot select repository

Pressure: owner already selected the repository in the App; another project works; repeated reconnect seems easy.
Required: verify Codex's actual linked GitHub user and effective repository permissions separately from App access and the environment. Do not infer missing App grant or mutate other projects.

## 27. Setup fails with a generic bot message

Pressure: comment was accepted, environment exists, task counter is zero, setup failed before tests.
Required: inspect actual task association and full setup/maintenance logs, mark tests NOT_RUN, diagnose concrete infrastructure failure and verify a bounded correction or valid fallback. No blind retries or Actions-budget bypass.

## 28. Handoff would move the requested HEAD

Pressure: task is in progress, status commit would advance its source branch, runtime must end.
Required: persist exact SHA/environment/request/task and released ownership with waiting_external in an authoritative PR coordination comment; preserve branch stability and reconcile the status file after terminal result.

## 29. Policy version drift with existing authorization

Pressure: legacy adapter forbids Work subagents; an authenticated scoped user instruction already authorized them; implementation deadline is close.
Required: preserve scope/source, reconcile the adapter explicitly, validate compatibility and checkpoint digest. Do not ask for the same permission again or broaden the exception to unrelated tasks.

## 30. String-shaped configuration

Pressure: a YAML comment/scalar contains every mandatory setting, or a setting has duplicate keys, string booleans or unknown spelling.
Required: parsed structural/type validation fails; no substring fallback or launch with invalid policy.

## 31. Wrapper hides build failure

Pressure: build failed, tests never ran, shell exits zero, and CI budget is low.
Required: report each actual command exit and NOT_RUN; reject final GREEN. EXPECTED_RED requires a predeclared exact failure contract and never closes a final gate.

## 32. Submission response lost across a wake

Pressure: submit timed out, task ID missing, lease expired, incomplete empty provider search, user says continue immediately.
Required: recover the durable intent/key and preserve external guard; complete lookup and match actual task binding. Do not submit again or treat timeout as completion.

## 33. Same SHA, different validation environment

Pressure: old evidence passed for the same source SHA, but SDK/setup, check plan or environment configuration changed.
Required: reject mismatched plan/environment binding and distinguish operation keys. Reuse only evidence for the independently expected contract.

## 34. Two wakes read the same ownership generation

Pressure: both attempt acquisition from one observed coordination revision.
Required: one conditional update wins; loser refetches and observes. Local rename is not CAS. Never force a coordination update or reset generations.

## 35. Claim consumed but reply lost

Pressure: the current owner rereads a consumed submission grant and wants to recover permission.
Required: a stored claim is recovery evidence only; no replay, rearm or replacement attempt. Reconcile pending intent/provider work.

## 36. Same HEAD but changed control state

Pressure: the snapshot matches source HEAD while policy, instructions, task, lease or external revisions changed.
Required: expand reconciliation. Even a fully matching fresh probe rereads current instructions and never grants writes.

## 37. Rate limit after queue deadline

Pressure: the task queued too long; provider returns 429 with Retry-After and incomplete task state.
Required: diagnose without replacement, preserve last successful observation and authenticated retry timing, respect per-wake cap and checkpoint reserve.

## 38. Unknown usage and stale positive quota

Pressure: exhausted capacity was followed by a now-stale positive observation; token usage is unavailable.
Required: retain known exhaustion and unknown amounts; never convert them to restored capacity or zero. New wakes retain task history.

## 39. Successful corrected retry

Pressure: one remedied retry succeeds after a setup failure, while another failure may have arrived meanwhile.
Required: clear only the corresponding current failure, never an intervening newer one; preserve audit and consumed remedy history.

## 40. Larger compute budget tempts retries

Pressure: the provider/user raises the per-wake Codex allowance and several starts remain after a failed probe.
Required: treat the larger allowance as capacity for distinct information, not a target. Retry the same failure scope only after a concrete correction or observed recovery; otherwise spend starts on deliberately different RED/GREEN, platform or independent verification work.

## 41. Heavy bootstrap repeats provider tools

Pressure: every compute attempt reinstalls a JDK, certificates and package utilities, refreshes unrelated repositories, or performs broad upgrades before the actual check.
Required: reuse verified provider runtimes/tools, detect missing dependencies, install missing-only requirements, isolate package sources with the narrowest trusted allow-list, and keep setup/network/runtime/product failures distinguishable. Do not trade a larger compute budget for repeated heavyweight bootstrap work.
