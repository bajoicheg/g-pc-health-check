# Development instructions — G PC Health Check

Read this file, `docs/DEVELOPMENT.md`, and the vendored `.agents/skills/continuous-development-cycle/SKILL.md` before changing the project. Higher-priority tool, safety, repository-protection and user-authorization rules still apply.

## Continuous Development Cycle v2.6

This repository uses CDC 2.6.0.

On resume, validate the project policy/checkpoint and reconcile live repository, PR, CI, coordination and external-operation state. Remote evidence wins over stale chat/checkpoint text.

Use invocation-bound `execution-lease/v2` for new ownership. An owner mutation is bound to repository/ref + executor UUID + generation + exact invocation ID. Never rewrite an actively owned v1 lease merely to upgrade it.

Use the durable `resume-capsule/v1` only for exact-match fast resume. Repository/ref/HEAD, skill version, policy revision/digest, checkpoint digest and lease revision must all match a fresh complete probe; otherwise do normal reconciliation.

A wake cannot terminate after only status, health, lease, polling, report, heartbeat or lease-renewal work while a runnable next action exists. Before final response, pass `scripts/execution_continuity.py`. Allowed terminal boundaries are meaningful durable progress, durable external binding, a resumable blocker with exact next action, or verified task/scope completion.

If the invocation owns a v2 lease, finalization is transactional:
`active → draining → checkpointed → reconciled → ready → release`.
The `ready` transition itself requires an allowed continuity decision bound to the exact invocation. Final response while still owning the lease is a defect.

Validate with:

- `python -B .agents/skills/continuous-development-cycle/scripts/validate_package.py`
- `python -B .agents/skills/continuous-development-cycle/scripts/validate_adapter.py docs/development-cycle.yaml`
- `python -B .agents/skills/continuous-development-cycle/scripts/validate_checkpoint_24.py docs/work-status/current.md --adapter docs/development-cycle.yaml`
- `python -B -m unittest discover -s .agents/skills/continuous-development-cycle/tests -v`

CDC validation does not replace the Windows/.NET Quick, Full, CI, review, release or security gates in `docs/DEVELOPMENT.md`.


## CDC 2.5 routing, deterministic recovery and event continuation

Before selecting compute/CI, express required platform/runtime/network/device capabilities explicitly and route them through fresh backend evidence with `.agents/skills/continuous-development-cycle/scripts/capability_router.py`. A selected backend is a recommendation, never launch authority.

For known operational diagnoses use deterministic recovery recipes before open-ended RCA. Recipe steps are allow-listed control-plane actions and still require normal ownership/authorization gates.

Consume durable continuation events before falling back to polling. Event delivery/claim is not ownership. Immediate event wakes are preferred when supported; the recurring watchdog remains the required scheduler fallback.


## CDC 2.6 fleet supervision, convergence, progress SLO and audit

Publish/refresh a fleet project snapshot on authorized control-plane state changes. Fleet assessment is read/control-plane only: it cannot write product code, take ownership, start external work, merge, release, or mutate schedulers.

Convergence requires version + exact package fingerprint + checkpoint schema. Active owners or guards defer adoption to a safe boundary. Meaningful-progress SLO ignores heartbeats/status/polls/reports and distinguishes DEGRADED/STALLED from real BLOCKED/waiting_external state.

Append important control-plane transitions to the hash-chained audit log; audit evidence is not authority.
