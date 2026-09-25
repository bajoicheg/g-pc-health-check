# Development instructions — G PC Health Check

Read this file, `docs/DEVELOPMENT.md`, and `.agents/skills/continuous-development-cycle/SKILL.md` before changing the project. These instructions organize legitimate development; they do not override tool restrictions, safety checks, user approvals or repository protections.

## Resume from evidence, not a conversation recap

1. Read the current main ref and the relevant open PR once. Record base SHA, head SHA, branch, task scope and the last tested SHA/run ID. Check actual changes before accepting a previous completion claim.
2. Read only the files needed for that task, at an explicit SHA. Reuse cached file bodies and returned blob SHAs; do not repeatedly retrieve the entire repository, PR body or successful full logs.
3. Work on the existing task branch when resuming. One coherent feature or maintenance task per PR. Do not create another release branch or another draft archive merely because a chat turn ended.
4. Keep one current checkpoint section in the PR body, using `.github/pull_request_template.md`; use `docs/development/handoff-template.json` for a local handoff. Update at a meaningful checkpoint, not on every poll. Remote state may differ from the checkpoint: re-read refs on resume.

## Short iteration loop

- On a prepared Windows checkout: `pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick`.
- Quick includes restore, transitive vulnerability audit, warnings-as-errors compilation and ALL source self-tests. It omits EXE packaging and the repeated portable EXE matrix; it does not replace final CI.
- Before proposing the final head: `pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full`. It adds fresh EXE packaging, the EXE self-test, existing portable matrix, FileVersion and SHA-256 validation.
- In a connector-only environment, inspect available commands once. If Windows/.NET are absent, use normal Windows CI for runtime evidence; static checks are not C# compilation. Do not repeatedly attempt unavailable local SDK installation.
- Keep test-first changes small, demonstrate the expected failing behavior, then implement and verify. A test-only RED commit is not a product release. Prepare documentation alongside code to avoid an extra full CI cycle just for forgotten version notes.
- Do not bump the application version or publish new assets for development-process-only changes. Existing immutable-by-policy release assets stay associated with their tested commit.

## GitHub interaction budget

- Group logically related file changes in a normal commit from the known base. Preserve transparent source content; batching is not a way to evade a blocked request.
- After an uncertain write result, read the ref/commit/PR before retrying. Never assume failure means nothing was written, and never force-push to resolve uncertainty.
- List runs for the exact SHA/event once. Save run/job IDs. Poll the relevant run at sensible intervals (e.g. 30–60 seconds while actively attending); do not request completed-job logs while a job is still running.
- Read failed-step logs once after completion. For a passing run, step summaries and the compact result artifact normally suffice; inspect full logs when needed to support exact claims.
- A tool safety denial is NOT a GitHub permission error or a compiler failure. Stop the blocked operation. Do not split, encode, rename, change endpoints/accounts or use an alternate channel to circumvent it. Preserve the checkpoint and error privately; request platform support. Do not loop identical rejected writes. A later legitimate user-authorized continuation still uses ordinary checks.
- For actual authorization failures, verify account/repository access; do not ask for a token in chat. For a rate limit, respect Retry-After. For transient read failures, bounded retries are acceptable. For writes/timeouts, reconcile state first.

## Observable progress and handoff

Tell the owner what is being done before starting. During a long active operation give factual milestone updates, normally every 2–4 minutes; do not fabricate progress or promise work after the response ends. End with committed changes, exact checks, PR/release state and the one next unresolved step. State clearly whether a binary exists and whether it is a release or only a locally built artifact.

Record separately: prepared locally / committed / tested / reviewed / merged / published / downloaded bytes verified. Include stale/partial/unknown states. Keep build logs and handoffs under ignored `artifacts/`; inspect them before sharing. No credentials, env dumps, private workstation evidence or signed download URLs in public issues/PRs. Prefer one current source snapshot and a patch against a named base, not a growing set of ambiguous archives.

## Existing product boundaries

- One portable Windows x64 EXE; arbitrary valid EXE location/name remains supported.
- Preserve UAC, worker allow-list and nonce/session binding; do not reintroduce the retired Program Files-only rule.
- Elevated read-only Temp preview is allowed. Temp deletion remains tied to a verified same-user non-elevated context.
- Missing telemetry is not zero or proof of health; query-only tools must not silently become remediation.
- Preserve read-only PR permissions, pinned Actions, independent required checks and the exact-main-build publication/attestation chain. No bypass merges or disabling failing checks.
- Hosted Windows Server tests are not interactive Windows 11/UAC/RDP/DPI acceptance. A source review by the implementing agent is not an independent reviewer.

## Continuous Development Cycle 2.7.0

This repository uses the vendored `.agents/skills/continuous-development-cycle` **2.7.0** as the orchestration core. On every development/watchdog resume, read `docs/development-cycle.yaml` and `docs/work-status/current.md` after this file and before shared writes or external starts.

Project-specific product/security rules above remain authoritative constraints. CDC adds recovery, ownership, budget, external-operation and continuity controls; it does not weaken UAC/security boundaries, required Windows gates, repository protections, or owner approvals.

- Canonical coordination backend: `refs/heads/cdc/coordination`.
- New ownership uses invocation-bound `execution-lease/v2`; expiry alone is never takeover permission.
- Finalization is transactional: `active → draining → checkpointed → reconciled → ready → release`; the `ready` transition requires an allowed hard execution-continuity decision bound to the exact invocation.
- Primitive status/health/lease/poll/report/heartbeat work is not meaningful progress and cannot terminate runnable work.
- Resume-capsule fast path requires exact repository/ref/HEAD, skill version, policy revision/digest, checkpoint digest and lease-revision agreement.
- Before selecting compute/CI/backend, form an explicit `capability-request/v1` and route it through fresh `backend-capability-registry/v1`; backend names do not prove platform/runtime capability and a route never grants launch authority.
- Known operational failure classes use deterministic allow-listed recovery recipes before open-ended RCA; recipe steps retain all normal authorization gates.
- Durable `continuation-queue/v1` events are deduplicated and invocation-claimed for delivery only. Immediate event wake is preferred when supported; the hourly watchdog remains the mandatory scheduler fallback.
- Persist/read back operation intent before external submit and reconcile unknown outcomes before resubmission.
- Ordinary ChatGPT runs without subagents. Work/Codex orchestration may delegate within writer-isolation and budget rules.
- Prefer configured compatible Codex COMPUTE_ONLY for eligible exact-SHA validation; it never edits/commits/pushes/merges and never substitutes for managed Windows 11 acceptance.
- Current wake cap: 4 Codex Compute starts; status polling has no count ceiling and uses bounded backoff/deadlines.
- Actions policy is `conserve`; do not spend a full product run on policy/status-only changes.
- Hourly watchdog obeys the same concurrency/budget/product gates and never disables itself because a wake is blocked.
- Treat the task-linked conversation as an operational dependency and never invent or silently replace an unknown/missing binding.
- The 0.17.0 pilot candidate remains pinned on `pilot/0.17.0-rc-f037bece`; process-only CDC commits are not a newly tested product binary.


## CDC 2.5 validation

When CDC package/control-plane policy changes, validate:
- `python -B .agents/skills/continuous-development-cycle/scripts/validate_package.py`
- `python -B .agents/skills/continuous-development-cycle/scripts/validate_adapter.py docs/development-cycle.yaml`
- `python -B .agents/skills/continuous-development-cycle/scripts/validate_checkpoint_24.py docs/work-status/current.md --adapter docs/development-cycle.yaml`
- `python -B -m unittest discover -s .agents/skills/continuous-development-cycle/tests -v`

These CDC checks do not replace Windows/.NET Quick/Full, product CI, managed Windows 11 pilot, review, release or provenance gates.


## CDC 2.6 fleet controls

Validated package fingerprint: `git-tree:e2cf6199eb60ca998012184b460c9a05c9f33b80` from exact validation commit `1e20edd807e3bae60b82aafdb2b9daa503e37715`.

Fleet Supervisor is read/control-plane only and never gains product-write, takeover, external-start, merge, release or scheduler authority. Project snapshots, convergence, progress SLO and audit recommendations still pass all project ownership/security/approval gates.

Convergence requires version `2.6.0` + exact package fingerprint + checkpoint v4. Meaningful-progress SLO ignores heartbeat/status/poll/report activity; default thresholds are 20 min DEGRADED and 60 min STALLED, while real blocker/waiting_external pauses the clock. Important control-plane transitions are recorded in the hash-chained audit log.


## CDC 2.7 canonical release binding

This branch vendors the immutable canonical CDC 2.7.0 release from
`bajoicheg/g-cdc`, ref `refs/heads/release/v2.7.0`, release commit
`5b84c89596e04d8411bf6cc24d8aa882a24c483a`, exact package tree
`a667549d48c2e93cba36359335c1b1ff4534ac86`.

The binding is recorded in `docs/cdc-consumer-lock.json`. Version equality alone
is not sufficient: the vendored subtree must match the locked package tree exactly.
Product-specific policy/checkpoint/security gates remain project-local and unchanged.
