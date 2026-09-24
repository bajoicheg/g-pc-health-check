# Development instructions — G PC Health Check

Read this file, `docs/DEVELOPMENT.md`, and the vendored `.agents/skills/continuous-development-cycle/SKILL.md` before changing the project. Higher-priority tool, safety, repository-protection and user-authorization rules still apply.

## Continuous Development Cycle v2.4

This repository uses CDC 2.4.0.

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
