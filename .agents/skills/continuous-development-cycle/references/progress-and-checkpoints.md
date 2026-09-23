# Progress and checkpoints

## Interactive progress contract

Long-running work must remain visibly alive to the user without turning the chat into a terminal log.

Send a concise progress update when any of these occurs:

- orchestration starts and the immediate plan is known;
- phase changes materially (recovery → implementation → validation → release);
- a durable commit/SHA becomes the new candidate;
- important compute/CI evidence arrives;
- an unexpected defect or blocker is discovered;
- a fallback/backend/executor switch happens;
- several minutes of otherwise silent work would reasonably look stalled;
- work reaches a terminal state.

A good update states: **what changed, current evidence/state, and next action**.

Do not spam every shell command, tool call, polling request, or trivial file edit.

## Durable checkpoint

The checkpoint is for machine/repository recovery, not chat presentation. A v3 checkpoint should record at minimum:

- repository identity and branch;
- reconciled policy revision/digest and durable operation intent/key when external work exists;
- observation timestamp;
- orchestration origin;
- active specification/change and task;
- phase;
- implementation/candidate SHA;
- last trustworthy GREEN SHA and evidence reference;
- active/last CI or compute reference, including environment ID, request/comment ID and actual task ID/URL for Codex;
- release candidate/version when relevant;
- blocker, if any;
- one concrete next action.

Remote repository facts override a stale checkpoint.

Update the checkpoint after meaningful RED/GREEN transitions, failed-CI diagnosis, task closure, release transitions, executor/backend switches, migration reconciliation, and before forced runtime exit.

Never leave only "waiting for CI". Record exact SHA and run/job reference; while the session remains alive, poll the actual provider to terminal state.

Bound polling by `references/bounded-recovery.md` and the task/wake ledger. Save phase start, last successful observation and retry timing; do not reset a deadline on each wake. Preserve enough budget for checkpoint/handoff before starting another poll or agent.

For an in-flight exact-SHA task, keep the source branch stable. If committing the status file would move its HEAD, put the same checkpoint fields and explicit lease/handoff state in a durable PR coordination comment and reconcile the file after terminal state. An environment save, request submission or setup spinner must never be reported as test execution or success.

Validate state with `scripts/validate_checkpoint.py` against the current validated adapter. A template/null digest permits recovery only. Unknown submissions retain their operation reference even when no actual task ID was received; follow `references/external-operations.md`.

Skill 2.3 optionally adds `control` pointers to the independently retrievable ownership record/revision, executor UUID/generation, budget ledger, recovery snapshot and external wait state. Null is unresolved. Structural validation proves neither liveness nor permission; retrieve actual records and match every binding using `references/orchestration-controls.md`. Keep actual current instructions in each recovery read set, even when a fresh matching probe allows unchanged long specifications or logs to be skipped.
