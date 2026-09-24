# CDC 2.3.9 enforcement completion

## Implemented

- Execution continuity policy in AGENTS.md.
- Runtime completion contract.
- Executable completion gate validator: `tools/dev/Test-CdcExecutionContinuity.ps1`.

## Completion rule

A wake cannot complete when:

- `runnable_next_action=true`
- no blocker exists
- no external wait exists
- only diagnostic actions were performed.

Diagnostic-only actions:

- status_read
- health_check
- lease_check
- poll_without_transition
- report_only

Allowed completion outcomes:

- meaningful progress;
- durable external binding;
- resumable blocker.

The validator is policy enforcement only. It does not grant ownership, bypass CI,
change repository protection or authorize external operations.
