# CDC 2.11.1 adoption

Source: `bajoicheg/g-pc-health-check@193f92b33ab095837d76d8f309f3ec8fecba8511` on `design/0.17.0-security-posture`.
Canonical release: `a262f78b82cd9e8eba9bc3b6108e0b52a17c33b0`; exact package tree: `6ffacd32cce74c3537150778d9b37cfeb361a621`.
Policy migration uses strict YAML and section replacement; replay is a no-op.
Adapter and checkpoint v4 validators pass under released CDC 2.11.1.

PREPARED ONLY: this branch is an adoption proposal. The active product ref and its owner/guard have not been changed. Re-read the live lease before integration.

This update changes the CDC dependency and project policy/checkpoint only. It does not close a product implementation task or provide new Windows/Android/runtime evidence. Existing product release and acceptance gates remain unchanged. No scheduler mutation or product Compute/CI launch is part of this adoption.
