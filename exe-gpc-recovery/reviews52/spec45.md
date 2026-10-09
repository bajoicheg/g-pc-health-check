# Independent SPEC review 45 — SPEC_RED

- Reviewer: /root/gpc_ui_spec45; attempt: attemptgpc-ui-spec45-20261009.
- Candidate: 53d4a46af7b8dd6304a6c6928e2b19cdbd3bce4a; baseline: 53636f45cc14075717e4e68b0a40ba020b96d9ca.
- Recorded review start: 2026-10-09T20:05:28Z (first explicit clock observation after instruction loading).
- Finish: 2026-10-09T20:07:57Z.
- Read-only product inspection through git show/diff/grep at immutable candidate/baseline objects. No mutable worktree product contents used; no product writes, refs, HEAD, external writes, or child agents.
- Governing instructions read: exact-candidate AGENTS.md, docs/DEVELOPMENT.md, vendored CDC SKILL.md and specification-review-and-finishing.md; effective-adapter-cap52.yaml. CDC VERSION=2.12.1 and package subtree=9c45d98c3254e9658d452c505d8c97698e3fc9a7 confirmed. Personal 2.12.2 source not used.
- Scope: original height-density/footer complaint, authorized Overview/Recommendations/Actions design in docs/superpowers/plans/2026-10-09-readable-tabs.md and docs/releases/0.17.1.md. This is SPEC disposition only, no QUALITY or release approval.

## Open finding

**SPEC45-01 — P2 / medium / open: both completion paths navigate to Events instead of the verification results.**

Exact candidate evidence:

- src/G.PcHealthCheck/MainForm.cs, Tabs(), lines 154–160: insertion order is Overview=0, Recommendations=1, Actions=2, Processes=3, Events=4, System=5, Compare=6. Security is appended afterward by InitializeSecurityPostureUi().
- src/G.PcHealthCheck/MainForm.cs, ApplyAsync(), lines 395–397: Populate(after); PopulateVerification(verification); **_tabs.SelectedIndex = 4;**.
- src/G.PcHealthCheck/ServiceDeskFullBatchUi.cs, DoEverythingAsync(), lines 134–136: identical post-verification **_tabs.SelectedIndex = 4;**.
- src/G.PcHealthCheck/MainForm.cs, CompareTab(), lines 264–270: Compare owns _compare and _remediation result grids.
- Baseline Tabs() had Compare at index4. The lines selecting4 are inherited but their target changes because this candidate inserts two new preceding tabs.

Trigger: successful completion of Apply selected (also Make better, which calls ApplyAsync) or Do everything after confirmation/execution/re-diagnosis. Actual resulting selected page is Events; users must manually find the verification results. This violates the authorized stable named tab navigation and preservation of existing post-action behavior. The defect is proven by source ordering and both call sites; executing real remediation is unnecessary to establish this mapping.

Required correction: select the stable CompareTab identity in both completion paths and verify their intended destination with a synthetic non-remediating regression. This report implements nothing. Re-review a new exact candidate after correction before independent QUALITY.

## Other requirement assessments — static only

| Requirement | Exact source evidence | Assessment |
|---|---|---|
| Overview owns complete header/context/7 metrics/triage | MainForm.cs OverviewTab() lines89–106; InitializeSecurityPostureUi() lines14–38 adds seventh card | Implemented structurally |
| Findings and actions have separate full table pages | MainForm.cs RecommendationsTab() lines217–231 and ActionsTab() lines183–214; ServiceDeskActions EnsureServiceDeskActionsUi() lines28–49 places batch bar on ActionsTab | Implemented structurally |
| Details collapsed initially; button/double-click opens full copyable text | MainForm.ReadableLayout.cs DetailPane() lines78–103 uses Panel2Collapsed=true, ReadOnly/Multiline/Both-scroll TextBox and click handlers; RefreshSelectedDetails() lines69–75 includes every column without truncation | Implemented structurally |
| Hidden availability reason/scope retained | MainForm.cs lines200–202 hide Availability; MainForm.ReadableLayout.cs lines73–75 includes hidden column and its tooltip; MainForm.ExecutionContext.cs RefreshActionAvailability() lines44–51 explicitly refreshes after reason/scope tooltip updates | Implemented structurally |
| Footer outside scrolling, five wrapping buttons and own status row | MainForm.cs BuildUi() lines75–82 gives MainShell tabs Percent100 and footer AutoSize; Footer() lines273–295 uses wrapping MainFooterButtons plus separate MainStatusRow/CollectionStatus. OverviewScrollViewport owns only Overview | Structurally addresses complaint; physical visibility at800x600 still requires Windows |
|800x600 minimum/work-area clamp | MainForm.cs lines61,68; ReadableWindowLayout Attach/Fit lines6–9,23–30 | Implemented structurally; native behavior not executed |
| RU/EN stable tab localization | MainForm.Localization.cs LocalizeTabsAndColumns() lines124–135 maps all8 pages by stable name; CultureChanged handler refreshes; new RU/EN keys parsed and nonempty | Implemented structurally for localization; navigation finding above remains |
| Main menu should reserve space without footer clipping | CommonProblemTools.cs CommonProblemsMenu.Attach() lines31–55 adds DockTop MenuStrip to form; MainShell DockFill. ReadableLayoutSelfTest.cs lines26–27 attaches actual CommonProblems/ReadOnly menu before Show, and lines64–86 checks footer/status on every tab and after Overview scroll | Plausible standard docking structure; Windows menu/footer bounds still need real runtime observation |
| Action/UAC/security semantics preserved | Diff limited to layout/localization/details/placement, fixtures/resources/docs; action execution, workers, policy/confirmation/security source unchanged against baseline apart from accidental UI destination change above | No execution-policy drift found in reviewed diff |

## Evidence boundaries and remaining gates

- `git diff --check 53636f45cc14075717e4e68b0a40ba020b96d9ca 53d4a46af7b8dd6304a6c6928e2b19cdbd3bce4a` exited0.
- Immutable-source Python checks exited0: both wrong positional destinations confirmed; new RU/EN keys parsed; candidate resolves exactly; vendored CDC VERSION/tree matched.
- Parent supplied artifact evidence: SDK8.0.425 cross-publish exit0, zero warnings/errors; delivered PE64/version0.17.1 EXE 78,763,727 bytes, SHA256 0bdcb5df5fcac8c4044bb9ef352a2b7d9b1cae9e8487aa622a626c7db39e5f82. This reviewer did not rerun or independently authenticate that artifact and does not convert it into Windows evidence.
- Current candidate Windows runtime/selftests/native renders/DPI/RDP/managed Windows11 pilot: **NOT_RUN**. This is a separate acceptance gate, not compilationFAIL and not inferred runtimeGREEN. Original Windows baseline1447b23 retained8 failing footer fixtures; it does not validate this candidate.
- ReadableLayoutSelfTest includes800x600 RU/EN, full hidden facts, long selectable text, actual menu, footer/status across tabs/Overview scrolling and30 requested current-invocation renders; definitions are evidence of intended coverage, not passing runtime results. Its RU/EN loop constructs separate forms rather than explicitly asserting an in-place live language transition; live switch still needs a native check.
- Guarded source/design/security baseline, original unknown CI6 response, production/integration authority, and preserved operation guard are outside this read-only review and remain unchanged.

Disposition: **SPEC_RED**, exactly1 open material finding. Missing Windows is independently pending. Root should correct SPEC45-01, obtain exact-candidate SPEC_GREEN, then continue independent QUALITY and required native acceptance gates.
