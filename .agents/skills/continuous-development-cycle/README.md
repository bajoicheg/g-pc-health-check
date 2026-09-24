# Continuous Development Cycle v2.3.4

Installable ChatGPT/Codex/agent skill for recoverable, long-running software development.

## Install in ChatGPT

Upload the ZIP containing this folder through the ChatGPT Skills UI. The ZIP root contains exactly one skill folder, `continuous-development-cycle/`, whose entry point is `SKILL.md`.

## Repository bootstrap

For a project that should persist the policy in Git, copy:

- `templates/development-cycle.yaml` → `docs/development-cycle.yaml`
- `templates/work-status.md` → `docs/work-status/current.md`
- merge `templates/AGENTS.snippet.md` into `AGENTS.md`
- use `templates/watchdog-prompt.md` for a scheduler/watchdog wake prompt

Edit repository-specific commands and policies before relying on the adapter.

## v2 highlights

- durable recovery from GitHub/repository facts;
- ordinary chat runs without subagents;
- Work/Codex orchestration explicitly enables useful subagents by default without per-launch approval;
- adaptive minimum-sufficient subagent effort, not blanket maximum;
- strict separation between Codex orchestration and Codex `COMPUTE_ONLY` backend;
- visible long-run progress updates;
- priority Codex Compute with reusable access/environment/request setup and explicit CI budget states;
- repository migration identity checks;
- watchdog recovery state machine;
- conditional execution ownership with explicit release or verified quiescence and `waiting_external`;
- exact-SHA release candidate and artifact discipline.

## v2.2 contracts

Adapter/checkpoint v3 add versioned policy and strict YAML validation. Versioned check plans produce per-command JSON evidence. External intent records protect interrupted submissions. See `references/policy-compatibility.md`, `references/command-evidence.md` and `references/external-operations.md` for migration and CLI usage.

## v2.3 orchestration controls

- Conditional Git coordination serializes ownership and one-use external submission claims; a designated single-writer fallback remains available.
- Separate queue/setup/run/unknown deadlines and backoff retain unresolved external guards.
- Fresh compact probes avoid rereading unchanged long material while always loading current instructions.
- A durable task/wake ledger reserves spending before effects, retains unknown charges and checkpoint resources, and requires concrete recovery before repeating a failure.

Use `references/orchestration-controls.md` to connect these controls. Existing adapter/checkpoint v3 and operation-intent/v1 records remain readable; enable the additive configuration through an explicit policy reconciliation. Run `python -B scripts/validate_package.py` and `python -B -m unittest discover -s tests` to check the package.

## v2.3.3 compute efficiency

- Higher Codex start capacity is spent for information gain, not identical retries.
- Compute failures are classified as setup, network, runtime or product before another start.
- Environment preparation reuses provider runtimes and uses missing-only dependencies, narrow package-source allow-lists and bounded provisioning instead of repeated heavyweight normalization.

## v2.3.4 watchdog lifecycle

Separate scheduler state from wake eligibility. Preserve recurring schedules through blockers and budget limits, honor verified user pauses, and audit unexplained drift without inventing its cause.
