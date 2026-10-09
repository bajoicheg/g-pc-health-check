# Independent SPEC review 47 — SPEC_GREEN

- Reviewer: /root/gpc_ui_spec45 acting on new paid attempt gpc-ui-spec47-20261009 (separate accounting from45).
- Exact candidate: 68da62237ecfa5377fcc31c5641b3baaebea39a9; tree: 0e501ab5a5ff728f23dd1203e8ae20aaa1b29e1e; exact parent: 53d4a46af7b8dd6304a6c6928e2b19cdbd3bce4a; original UI baseline: 53636f45cc14075717e4e68b0a40ba020b96d9ca.
- Start: 2026-10-09T20:13:12Z; finish: 2026-10-09T20:14:44Z.
- Coordination binding read at37c44aeba1c5ef6914e803ef87ac9425bcaa5775: exe-gpc-recovery/reviews52/spec47-plan.json and spec47-state.json, required read_only spec task running, reservation34cedbb2-eaeb-432a-a1f3-5e0336b26ca4. It grants no product/shared-branch/release/merge authority.
- Only git show/diff/grep and immutable-object static assertions were used for product assessment. No product/core/ref/HEAD/budget edits, child agents, or external writes. Authorized local operational report writes only.
- Applicable exact-candidate AGENTS.md, DEVELOPMENT.md, vendored CDC2.12.1, approved design/plan and effective adapter were loaded during45; immutable delta verifies their bytes unchanged in47. CDC VERSION2.12.1 and exact package tree9c45d98c3254e9658d452c505d8c97698e3fc9a7 freshly verified; mutable personal2.12.2 not used.

## Prior finding resolved by exact source evidence

**SPEC45-01 — P2 / resolved at candidate 68da62237ecfa5377fcc31c5641b3baaebea39a9.**

- MainForm.cs ApplyAsync(), line397 now selects `_tabs.SelectedTab = _tabs.TabPages["CompareTab"];` after PopulateVerification.
- ServiceDeskFullBatchUi.cs DoEverythingAsync(), line136 uses the same named identity after PopulateVerification.
- MainForm.cs Tabs(), line160 creates the comparison page and sets Name="CompareTab" before adding it. CompareTab() continues to own both before/after and remediation result grids.
- Immutable parent→candidate diff contains exactly these two destination corrections plus the independent PowerShell manifest filename correction; no execution/confirmation/worker/security policy logic changes.
- Fresh Python static assertions on both whole candidate files pass: both completion paths use CompareTab, neither contains numeric _tabs SelectedIndex assignments or numeric TabPages lookup. This proves closure of the concrete wrong-page mapping. It is a static regression check, not native execution of a remediation or WinForms scenario.

Resolution reference: git:68da62237ecfa5377fcc31c5641b3baaebea39a9:src/G.PcHealthCheck/MainForm.cs:ApplyAsync and git:68da62237ecfa5377fcc31c5641b3baaebea39a9:src/G.PcHealthCheck/ServiceDeskFullBatchUi.cs:DoEverythingAsync.

## Full retained UI requirements

| Requirement | Whole candidate evidence | Static disposition |
|---|---|---|
| Overview header/context/seven metrics/triage | MainForm.cs OverviewTab(), lines89–106; SecurityPosture InitializeSecurityPostureUi() adds7th security card | Compliant |
| Dedicated full findings and action tables; batch bar on Actions | MainForm.cs RecommendationsTab()/ActionsTab(); ServiceDeskActions EnsureServiceDeskActionsUi() targets named ActionsTab | Compliant |
| Details initially collapsed and opened by button/doubleclick | MainForm.ReadableLayout.cs DetailPane() uses Panel2Collapsed=true and both handlers | Compliant |
| Complete copyable long content including hidden availability/reason/scope | ReadOnly multiline TextBox with Both scrollbars, RefreshSelectedDetails enumerates all columns including invisible ones and Availability tooltip; ExecutionContext RefreshActionAvailability explicitly refreshes after tooltip updates | Compliant |
| Fixed footer with all5 wrapping buttons plus separate collection status | MainShell DockFill with percent tab row and AutoSize footer row; MainFooterButtons FlowLayoutPanel wraps5 buttons; MainStatusRow separately owns progress and CollectionStatus; only Overview viewport scrolls | Compliant source architecture; actual800x600 bounds pending Windows |
|800x600 minimum and work-area clamp | MainForm constructor MinimumSize800x600 plus ReadableWindowLayout.Attach/Fit | Compliant source architecture; native scaling pending |
| RU/EN stable tab localization/navigation | LocalizeTabsAndColumns maps all8 stable identities and details toggles; RU/EN keys parsed nonempty; both action completion destinations now named CompareTab; security navigation uses actual page reference | Compliant source; in-place live switch still requires native observation |
| Actual main menu retains footer reachability | CommonProblemsMenu adds DockTop MenuStrip; MainShell DockFill reserves client remainder; synthetic fixture attaches actual menu before Show and checks footer/status on all pages/after Overview scroll | Compliant source architecture; actual docking/bounds pending Windows |
| Existing action/UAC/security execution behavior | Full baseline→candidate diff retains original execution boundaries, confirmation and security code; only corrected result-page destinations alter action flow presentation | No scope drift found |

## Manifest correction

Test-ReadableLayout.ps1 line10 writes `powershell-sha256.json`; ReadableLayoutSelfTest CompleteRenderEvidence() still writes native `sha256.json`. Therefore PowerShell no longer overwrites the structured C#30-current-invocation manifest. Wholecandidate immutable-source assertions confirmed both distinct names. No claim of30 actual new renders is made.

## Checks and limitations

Fresh review checks exited0: candidate commit/parent/tree binding; wholecandidate static assertions for named destination, collapsed/full details, tooltip refresh, shell/footer/status/minimum, all8 named localization pages, RU/EN new resource XML values; distinct manifest names; immutable CDC VERSION/tree; `git diff --check 53636f45cc14075717e4e68b0a40ba020b96d9ca 68da62237ecfa5377fcc31c5641b3baaebea39a9`.

**Windows runtime/selftests/native renders/800x600 footer geometry/live language switch/DPI/RDP/managed Windows11 pilot remain NOT_RUN and separate mandatory acceptance gates.** Static SPEC_GREEN does not mean runtimeGREEN, compilationGREEN, artifact acceptance, integration or release approval. The parent-supplied earlier cross-published EXE was bound to prior53d4, so it is not exact corrected68da artifact evidence. Original Windows baseline1447b23 retained8 footer failures and cannot validate this candidate.

No open SPEC findings remain. Ordered independent QUALITY may proceed on exact candidate68da62237ecfa5377fcc31c5641b3baaebea39a9; required Windows/platform/artifact gates remain pending. This report contains no implementation or permission to publish guarded source/pilot.
