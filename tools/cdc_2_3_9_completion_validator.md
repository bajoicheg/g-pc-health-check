# CDC 2.3.9 Completion Gate

The validator rejects a wake completion when:

- runnable_next_action is true;
- blocker is absent;
- external wait is absent;
- all recorded actions are diagnostic only.

Diagnostic-only actions:

- status_read
- health_check
- lease_check
- poll_without_transition
- report_only

Allowed completion reasons:

- meaningful_repository_progress
- durable_external_binding
- resumable_blocker

This validator does not authorize takeover, writes, merges or external execution.
