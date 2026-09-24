# CDC 2.5 adoption

Source package: exact canonical commit `116d0a6ae6c3f35a22fc6170a25eece3e011eff5`.

This adoption builds on the verified CDC 2.4 control plane and adds all CDC 2.5 scope:

- capability-based backend routing from fresh evidence;
- deterministic allow-listed recovery recipes;
- durable deduplicated continuation events with invocation-bound delivery claims;
- immediate continuation wakes where supported, with the recurring watchdog retained as scheduler fallback.

Routing, recipe selection and event delivery never grant takeover, product-write, external-start, scheduler-mutation or budget authority. Existing Windows/.NET validation and release rules are unchanged.
