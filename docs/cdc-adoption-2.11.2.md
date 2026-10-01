# CDC 2.11.2 adoption

Base source: `bajoicheg/g-pc-health-check@7aba823fd6103f416b291a944cd012eea78687b0` on `design/0.17.0-security-posture`.
Canonical release: `48e637b230e7640d0dcd13da60d712f38f59a2b6` at `refs/heads/release/v2.11.2`.
Exact package tree: `7a7a7faa75b7fc9160d912d8fb507c6b9573d17f`.
Adapter policy revision: `2026-09-30-cdc-2.11.2-fleet-adoption`.
Adapter semantic digest: `77b0da8bf1930862064fcc24e676395b1e2e949e0ebfe7ed0b0b39a256c1d5ab`.
Adopting lease generation: 15; invocation: `chat-20260930-cdc2112-adoption-g-pc-health-check`.

This is a process-only adoption at a fresh released/no-guard safe boundary. It replaces only the vendored CDC dependency and its repository-local policy/checkpoint/provenance bindings. Product source, retained 0.17.0 Windows/pilot evidence, product release gates, budget history and scheduler pause are preserved. No scheduler enable/run/rebind/recreate and no product Compute/CI launch is part of this adoption.

Final authoritative adoption evidence and lease release are written separately on `cdc/coordination` after exact subtree and policy/checkpoint readback.
