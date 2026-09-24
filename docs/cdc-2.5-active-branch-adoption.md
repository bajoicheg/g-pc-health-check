# CDC 2.5 active-branch adoption

This process-only change synchronizes the complete CDC 2.5 package already merged and validated on `main` into the active `design/0.17.0-security-posture` branch.

Safety boundary observed immediately before the commit:
- product HEAD: `1718f4896743d7fa68367c6329bf514631bc3938`;
- coordination: `execution-lease/v1`, generation 0, released;
- external guard: null;
- coordination source ref: `refs/heads/design/0.17.0-security-posture`.

The 0.17.0 product candidate remains `f037bece0272814f9b0f069aaf1de17be369b626`; no application source, pilot artifact, managed Windows 11 acceptance evidence or explicit integration approval is changed by this CDC migration. PR #92 remains draft and must not be merged or auto-merged without the owner's separate integration approval.
