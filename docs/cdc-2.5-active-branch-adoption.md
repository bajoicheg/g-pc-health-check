# CDC 2.5 active-branch adoption

CDC 2.5.0 is fully bound to the active `design/0.17.0-security-posture` branch as a process/control-plane layer.

## Verified package and lineage

- Vendored CDC subtree is byte-identical to validated CDC 2.5 on `main` (90/90 blobs by path, SHA and mode).
- Validated CDC 2.5 main commit: `9cc97f577921969f7e09a5bb2234327319e8b32c`.
- Dedicated package/project validation on the validated CDC 2.5 line: run `36043606449` SUCCESS.
- Active branch reconciled current `main` lineage with merge commit `4d42c155a6e2fecbd0808db341908b748301562f`, preserving stricter product-specific policy.
- Branch-specific adapter digest remains `91ab8cf8408a4cf2cb8f758b3397a33dc90a1eba40b3de45e3bb7db5cf2c1bc5`.

## Durable control plane

- `cdc/coordination/lease.json` is `execution-lease/v2`; new ownership is invocation-bound.
- `resume.json` provides exact-match resume state.
- `backend-registry.json` backs evidence-based capability routing.
- `continuation-queue.json` provides durable deduplicated continuation events; event delivery is never authority.
- Deterministic recovery recipes are recommendation-only and retain authorization/ownership/budget gates.
- Hard execution continuity and transactional finalization prevent primitive one-step wakes from masquerading as completed work.

## Product boundary

The tested 0.17.0 product candidate is unchanged:
- pilot ref: `pilot/0.17.0-rc-f037bece`;
- product/pilot SHA: `f037bece0272814f9b0f069aaf1de17be369b626`;
- retained pilot run: `35152451989`;
- EXE SHA-256: `0cb8de4247ddcd42348b39dd673d4791956795a5d365eb5abc9c178442b74759`.

CDC/main-line reconciliation does not reclassify process commits as a newly tested product binary. Managed Windows 11 pilot and explicit owner integration approval remain required. PR #92 must not be merged or auto-merged without that separate approval.
