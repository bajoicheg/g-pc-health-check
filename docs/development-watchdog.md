# Development watchdog - CDC 2.5.0

The project watchdog follows `.agents/skills/continuous-development-cycle` and `docs/development-cycle.yaml`.

## Schedule and wake behavior

- Cadence: hourly condition watch.
- Idle guard: 60 minutes. An explicit owner kick bypasses only the idle guard.
- Concurrency/lease, budget, operation-intent and external-work guards always apply.
- Ordinary ChatGPT runs sequentially without subagents. Work/Codex orchestration may use isolated subagents within adapter caps.
- Do not notify the owner for an unchanged blocker or normal external work already in progress.

## Recovery order

On every wake, read current `AGENTS.md`, adapter, checkpoint, CDC VERSION/SKILL, then independently refetch repository/default branch, active PR #92, exact branch HEAD, pinned pilot ref, CI/compute/release state, and `refs/heads/cdc/coordination`.

Never treat a checkpoint or expired TTL as permission to take ownership. Use explicit release or verified quiescence. Never force-push.

## Current 0.17.0 boundary

- product/pilot SHA: `f037bece0272814f9b0f069aaf1de17be369b626`
- pinned ref: `pilot/0.17.0-rc-f037bece`
- retained Windows pilot run: `35152451989`
- retained pilot EXE SHA-256: `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`

The remaining product gate is the managed Windows 11 pilot plus explicit owner integration approval. Do not merge/auto-merge PR #92, release, move the pinned candidate, or claim platform acceptance without new evidence.

## Budgets and polling

- Codex Compute: at most 4 starts per wake; capacity is not a target.
- Status polls: no count ceiling; use 30s initial backoff, cap 300s, and adapter phase deadlines.
- Tool calls: 80 per wake, reserving 8 calls and 4000 tokens for handoff.
- Agents: task cap 4; max 2 parallel where runtime permits.
- GitHub Actions: `conserve`; at most one full run per new task/candidate. Do not launch Actions merely to check watchdog health.
- Historical pre-adoption usage is not zero and does not establish provider remaining quota.

Near the wake budget limit, persist a resumable checkpoint and release ownership rather than creating another wake/external start to evade caps.


## Scheduler lifecycle and chat dependency

Desired scheduler state, observed scheduler state and current-wake execution eligibility are separate. The recurring watchdog remains enabled through product BLOCKED states, foreign ownership, external guards, budget exhaustion, runtime limits and unchanged work. Those conditions stop or constrain only the current wake.

An ordinary scheduled wake must not call scheduler update/delete/create operations or change its DTSTART/cadence. Disabling or rescheduling requires an explicit current owner instruction for this automation; a later verified owner/UI pause overrides older enabled state. An observed disabled state with unknown actor is drift to diagnose, not permission to assume an owner decision.

Foreground status/recovery must reconcile the canonical scheduler record and `docs/watchdog-chat-binding.json`: enabled state, preserved hourly schedule/timezone, last completed run, and the linked conversation when the platform exposes it. The linked chat is an operational dependency and must be protected from bulk cleanup/archiving. If conversation ID/access/archive state is not observable, keep it `unknown`; do not fabricate an ID, silently rebind the watchdog or create a duplicate. A scheduler repair is not complete until a fresh post-repair run finishes, its result is visible in the intended destination, the chat is accessible where inspectable, and the recurring task remains enabled.


## CDC 2.5 continuation and routing

At each wake, inspect the durable continuation queue before falling back to polling. Event claims coordinate delivery only; they never grant ownership, launch authority or product-write authority. The hourly watchdog remains the mandatory scheduler fallback.

Before selecting compute/CI, form a `capability-request/v1` and route it against fresh `backend-capability-registry/v1` evidence. A backend name is not capability evidence and a route is recommendation-only. Known diagnoses use deterministic allow-listed recovery recipes before open-ended RCA; normal authorization, lease, guard and budget gates remain mandatory.

New shared-write ownership uses invocation-bound `execution-lease/v2`. An owning wake must finish through hard execution continuity and transactional finalization `active → draining → checkpointed → reconciled → ready → release`; primitive status/poll/heartbeat work is not a terminal boundary when runnable work exists.
