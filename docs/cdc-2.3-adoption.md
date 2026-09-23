# CDC 2.3.3 adoption - G PC Health Check

Adopted 2026-09-23 as a process-only change on the active 0.17.0 development PR.

## Effective controls

- Vendored CDC version: **2.3.3**; adapter schema `continuous-development-cycle/v3`.
- Coordination: `refs/heads/cdc/coordination`; lease TTL/freshness 20/10 minutes.
- Watchdog: enabled, hourly, 60-minute idle guard.
- Codex-first with fail-closed unconfigured backend until binding is verified.
- Codex Compute starts: **4 per wake**.
- Status polls: **no arbitrary count ceiling**; time/backoff/deadline governed.
- Tool calls: 80/wake; checkpoint reserve 8 calls + 4000 tokens.
- Agents: 4/task, max 2 parallel where runtime permits.
- GitHub Actions: `conserve`, one full run per new task/candidate; no inferred budget restoration.

## Migration safety

This adoption does not move the tested 0.17.0 pilot candidate. `pilot/0.17.0-rc-f037bece` remains bound to `f037bece0272814f9b0f069aaf1de17be369b626`; the retained pilot artifact from run `35152451989` remains the endpoint-test binary.

No merge, auto-merge, release or managed Windows 11 acceptance is implied. Historical CI/compute usage before adoption is retained as evidence and is not rewritten to zero. Provider quota stays unknown until observed.
