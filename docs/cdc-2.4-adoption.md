# CDC 2.4 adoption

Source package: exact commit `225a8e3bc89927d9bc921a88380d7332da049f7a` from the canonical CDC 2.4 control-plane branch.

This adoption installs the complete vendored skill, checkpoint v4 binding, invocation-bound lease-v2 protocol, resume capsule, hard execution-continuity gate and transactional finalization. It does not change application version, product code, release artifacts, Windows acceptance requirements or repository protection.

Coordination initialization is intentionally separate from package installation: an existing/live owner must never be replaced merely for version convergence. The project starts from a released recovery checkpoint and initializes `refs/heads/cdc/coordination` only after package/adoption validation is GREEN.
