# Readable tabs implementation plan

> **For agentic workers:** Use executing-plans for this bounded layout correction; keep guarded source refs unchanged.

**Goal:** Readable recommendations and reachable controls in an ordinary window.
**Architecture:** Overview owns header/context/metrics; Recommendations owns findings; Actions owns action selection. MainShell owns tabs and a fixed footer. Selected-row details are collapsed by default and retain complete copyable content when opened.
**Tech Stack:** C# .NET 8 Windows Forms, existing resource files and self-test harness.
**Spec:** User feedback 2026-10-09 and uploaded 1208x930 screenshot; chosen layout recorded in docs/releases/0.17.1.md.

## Global constraints

- CDC remains pinned to 2.12.1; no guarded source/pilot/intent/lease moves.
- UAC, nonce/session validation, same-user Temp rules and action execution remain unchanged.
- Cross-publish does not establish Windows, DPI, RDP or production acceptance.

## Review focus

- 800x600 with all footer buttons and collection status visible.
- RU/EN live language switching uses stable tab identities.
- Large selected-row text stays complete and copyable.
- Hidden action availability/reason/scope facts remain in details.
- Adding the actual main menu must not overlap content or move the footer off-screen.

### Task 1: Layout and regression fixtures

Files: MainForm.cs, MainForm.ReadableLayout.cs, MainForm.Localization.cs, MainForm.ExecutionContext.cs, MainForm.ServiceDeskActions.cs; MainStrings RU/EN; ReadableLayoutSelfTest.cs, SecurityPostureUiSelfTest.cs; Test-ReadableLayout.ps1; release notes.

- [x] Prepare Windows assertions for separate tabs, collapsed detail panes and fixed visible footer before changing layout.
- [ ] Execute baseline RED in Windows; unavailable locally, NOT_RUN.
- [x] Replace stacked findings/actions with dedicated named tabs and isolate Overview scrolling.
- [x] Keep footer buttons and status in separate rows outside scrolling.
- [x] Adapt localization, security navigation and service-desk placement.
- [ ] Compile with warnings as errors in configured compute; inspect exact resulting PE/hash.
- [ ] Run Windows synthetic/render and existing Quick/Full gates.
- [ ] Obtain independent required reviews and managed Windows11 pilot before integration/publication.
