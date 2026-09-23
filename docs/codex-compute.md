# Codex Compute - G PC Health Check

## Current state

CDC 2.3.3 prefers Codex Compute for eligible exact-SHA validation, but this repository currently has no independently verified Codex environment/user binding. Therefore `compute.codex_backend.enabled=false` and `configuration_status=unconfigured` are intentional fail-closed values.

The 2026-09-23 CDC adoption authorizes project configuration and future use subject to the adapter. It does not prove that a provider environment exists and does not authorize fabricated environment IDs or provider quota.

## Enablement gate

Before changing the adapter to `enabled: true`, independently verify/read back linked GitHub user, repository grant for `bajoicheg/g-pc-health-check`, saved environment ID/URL, compatible toolchain/setup, active source ref and exact candidate SHA. Persist evidence and revise adapter/checkpoint together.

## COMPUTE_ONLY contract

Every Codex validation request must bind to the literal exact source SHA, verify HEAD and clean worktree before/after, run only reviewed commands, forbid edits/fixes/dependency changes/commit/push/PR/merge/release/Actions dispatch, stop on SHA mismatch/dirt, and report command exit codes plus final HEAD/status.

Eligible categories: build, unit/regression, lint, schema, deterministic contracts when compatible. Linux/compile-only evidence never replaces Windows EXE/UAC/firmware/registry/managed-device evidence.

## Limits

- Maximum starts per wake: 4.
- More capacity is not a target; another start requires information gain or a concrete correction/recovery.
- No arbitrary status-poll count limit; use adapter backoff and queue/setup/run/unknown deadlines.
- Unknown provider remaining quota stays unknown.
- Unknown submission outcome blocks replay until reconciled.

No Codex start is required merely for CDC adoption. The current product candidate is already pinned at `f037bece0272814f9b0f069aaf1de17be369b626`; the remaining managed Windows 11 pilot cannot be replaced by Codex.
