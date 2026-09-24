<!-- continuous-development-cycle-v2:start -->
## Continuous Development Cycle v2.3

For substantial implementation, resume, release, repository migration, or watchdog work, load the installed/repo-local `continuous-development-cycle` skill.

Before coding, reconcile:
1. repository identity and remote HEAD;
2. `docs/development-cycle.yaml`;
3. `docs/work-status/current.md`;
4. active specification/task fingerprints and relevant bodies (skip unchanged long material only after a complete fresh recovery probe);
5. PR/CI/compute/release state.

Follow the instruction hierarchy and reconcile existing user authorization with repository policy. Remote facts override stale checkpoint/chat claims about state.

Subagents depend on orchestration origin: ordinary ChatGPT chat = disabled; Work or Codex orchestration = explicitly enabled by default with adaptive minimum-sufficient effort. This instruction requests useful independent delegation in Work/Codex without additional per-launch approval, subject to higher-priority instructions and explicit task restrictions. Keep one integrator and isolated writers; continue sequentially if tools/quota are unavailable. Do not confuse Codex orchestration with a read-only `COMPUTE_ONLY` backend.

Prefer configured, authorized Codex Compute for eligible exact-SHA build/test/lint checks, even when a local runtime exists. Follow the skill's `references/codex-compute.md` to establish access, environment, request/task binding and terminal evidence; retain the project runbook in `docs/codex-compute.md`. Use cheap local preflight/short debugging loops or a justified fallback; preserve required platform gates and Actions budgets.

Do not stop merely because a commit, compute result, CI launch, checkpoint, review, or one task completed. Continue until configured work/release gates are complete, a real blocker exists, or runtime/tool limits force a durable handoff.

Validate adapter/checkpoint v3 using the skill tools, reconcile version or policy-digest drift with `references/policy-compatibility.md`, and preserve the source/scope of existing user authorization. Before external submit, persist/read back an operation intent; after a lost reply reconcile the existing task. Unknown submission outcome retains the external guard despite lease expiry. Require per-command `command-evidence/v1` bound to the expected plan, SHA and environment; a successful wrapper, NOT_RUN or EXPECTED_RED is not final GREEN.

Apply the skill's `references/orchestration-controls.md` before shared mutations or external starts. Use conditional ownership with a unique executor UUID/generation, or a verified designated single writer; expiry alone never permits takeover. Git submissions require a fresh one-use claim; restored submitting/unknown records only reconcile. Bound waits and reserve task/wake spending before effects, preserving checkpoint resources and unknown charges. Always read actual current instructions on resume.
Before chat cleanup or watchdog recovery, reconcile the canonical task-to-conversation binding. Protect its verified chat dependencies; follow the archive prevention/recovery procedure in `references/watchdog-recovery-and-migration.md`. A successful unarchive or enabled flag alone is not recovery: require a fresh completed run, visible result, and preserved enabled schedule.
<!-- continuous-development-cycle-v2:end -->
