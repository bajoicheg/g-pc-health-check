# Development instructions — G PC Health Check

Read this file and `docs/DEVELOPMENT.md` before changing the project. These instructions organize legitimate development; they do not override tool restrictions, safety checks, user approvals or repository protections.

## CDC 2.3.9 Execution Continuity

This project adopts CDC 2.3.9 execution continuity rules.

A wake/invocation must not complete after only diagnostic work when a runnable next action exists.

Forbidden completion pattern:

- status read
- health check
- lease check
- polling without transition
- report only

Required completion outcome:

- meaningful repository progress; or
- durable external operation binding; or
- resumable blocker with exact next action.

Diagnostic actions are not progress. Heartbeats, unchanged polling, lease renewal and status reports do not satisfy completion continuity.

Before final response or handoff:

- update durable checkpoint;
- preserve exact next action;
- reconcile external operations;
- release owned coordination state.

The completion gate is diagnostic policy enforcement only. It does not grant permission to bypass repository protection, ownership, CI requirements or review.

## CDC 2.3.9 Runtime Contract

Checkpoint state should include:

```yaml
execution_continuity:
  runnable_next_action:
  meaningful_progress:
  primitive_steps_since_progress:
  completion_gate:
    allowed:
    reason:
```

A second consecutive primitive-only wake while runnable work exists is a continuity failure and requires recovery diagnosis.

## Existing development rules

Preserve all existing Windows, build, release, validation and security rules below.
