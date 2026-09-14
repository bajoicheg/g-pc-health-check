# 0.15.1 maintenance summary

- Packages the maintenance line accumulated after 0.15.0 into a versioned pilot build without expanding product/remediation scope.
- Fixes stale row/detail presentation and stale-target/next-run-option handling across read-only diagnostic windows so visible and saved evidence remain tied to the collected snapshot.
- Prevents queued progress callbacks from older, cancelled or completed operations from overwriting newer or terminal state across File Use, Endpoint Review, read-only review, Common Problems, main scan/remediation and Diagnostic Bundle flows.
- Preserves cancellation and explicit terminal export/save failures instead of leaving stale success/running text in Incident Review, Diagnostic Bundle, Endpoint Review, File Use, Process Observation, Storage Review, Performance Session and Resource Probe.
- Tightens Diagnostic Bundle save-thread affinity, snapshot/context/source alignment, symptom-marker timing and progress ownership.
- Fixes equal-boundary Performance Session catch-up reads and clarifies unavailable/missed values as unknown evidence rather than zero.
- Refreshes managed Windows 11 acceptance for UAC/RDP/DPI/VPN/EDR/OEM/storage and rejects ambiguous multi-root E2E evidence bundles.
- Preserves arbitrary valid EXE location/name, UAC/worker allow-list/nonce/session binding, elevated read-only Temp preview, same-user non-elevated Temp deletion and the existing remediation set.
- Version `0.15.1`, FileVersion `0.15.1.0`.
