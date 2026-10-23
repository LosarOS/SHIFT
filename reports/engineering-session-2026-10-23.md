# Operation Double SHIFT — SHIFT

## Baseline and plan

- Base/main SHA: `b3a4d11cb0b5321742fa02c908060727e092c4a4`; clean tree confirmed.
- Branch: `engineering/double-shift-rc2`; isolated Git worktree and build/data directories.
- No published releases. Eight open Dependabot PRs; major dependency upgrades are reviewed separately.
- Target: `1.0.0-rc.2`, unsigned prerelease only after real Windows gates pass.
- Baseline [CI](https://github.com/LosarOS/SHIFT/actions/runs/39947835446): core tests pass on Windows and Linux (63); diagnostics pass; Windows publish fails PRI175/PRI277 duplicate `Files/Assets/shift.ico`. Startup/installer not reached.

## Confirmed findings

| Priority | Evidence | Action |
|---|---|---|
| P1 release blocker | Native Windows MakePRI reports duplicate icon; SDK default image Content and explicit Include overlap | Change icon Content Include to Update; verify with original Build-Release script in Windows CI |
| P1 release gate | No successful native runtime / installer evidence | Exercise disposable Windows UI smoke, restart, preservation, install/upgrade/uninstall; preserve real diagnostics |
| P2 safety/QA gap | Existing smoke terminates process and removes only its newly created data; no graceful restart/preservation acceptance | Extend within disposable profile, never use owner's existing data |

## Execution order

1. Fix Windows packaging/runtime before new feature work.
2. Workspace Doctor service, read-only Windows adapters, cancellable UI, RU/EN and regression tests.
3. Focused UX and quick setup if release quality allows.
4. Codex Team integration in an isolated copy; record live SDK availability honestly.
5. Full final checks, PR/merge according to actual gates, native artifacts and published digest verification.

## Evidence ledger

Initial checkpoint: inspected source, history, PRs, releases, CI, README, security, roadmap and packaging. This report does not certify pending checks. Windows runner automation does not establish manual Windows 10/11/WebView2/DPI acceptance.

Next: push icon fix and run PR CI; inspect next native failure or genuine runtime screenshots.

## Checkpoint — 12:35 UTC, source 30a559c

Implemented Workspace Doctor in Application/Infrastructure and a WinUI RU/EN dialog accessible from workspace details. Per-item Ready/Warning/Error/Unknown, repeat inspection, cancellation and direct editor action. Local metadata/read access only; no process/network executor. PE headers bounded, shortcut targets not silently resolved, mapped network drives/reparse ancestors/offline placeholders excluded. Dispatch still validates independently.

Tests: **110 passed / 0 failed / 0 skipped**, Linux .NET SDK10.0.402. Includes 41 Doctor cases and six logging cases added this session. The Windows counterparts for ACL/long paths and native dialog still need CI/runtime acceptance.

Native progression/evidence:

- Original native build blocked on duplicate icon PRI entries; fixed Content metadata, native publish now succeeds.
- Generated App.xaml metadata added; replaced Forms SDK opt-in with explicit runtime reference to stop WinFX overriding WinUI XAML compilation.
- [39951617423](https://github.com/LosarOS/SHIFT/actions/runs/39951617423) identified HRESULT0x80070490 at `AccessibilitySettings.HighContrastChanged`; replaced with desktop HWND settings notifications.
- Subsequent runs construct and activate MainWindow, then native XAML fails before Loaded. Disposable CI native dump diagnosis identifies **0x880008B0 (default style resource URI not found)**. Runtime agent is investigating the actual missing resource.
- No runtime PASS, installer PASS or public binary release is claimed. PR9 remains a draft; native gate BLOCKED.

Quick Workspace Setup is isolated in a separate feature worktree and does not block core fixes. User explicitly authorized subagents; ownership is split among native runtime, Doctor tests, Codex quality/release and optional setup, with changes reviewed before integration.

## Checkpoint — native baseline recovered

At source `b900ab60fa1891008df02c129b52e4d56872eb79`, [39955142632](https://github.com/LosarOS/SHIFT/actions/runs/39955142632) completed successfully. The missing generated main resource index was the confirmed native XAML blocker; explicitly publishing `resources.pri` resolved it.

- Native Windows and Linux core suites: **110 passed, 0 failed, 0 skipped** each.
- Canonical Build-Release succeeded, including self-contained native publish, actual portable startup and UI Automation.
- Installed UI smoke and Inno uninstall succeeded in the disposable runner; real screenshots captured.
- Main workspace/item creation, command refusal/approval and benign visible dispatch, RU/EN, theme selection, compact search, tray and second-instance activation passed at that source.
- NuGet transitive audit: six projects, no known advisories reported by configured nuget.org source.

Additional Doctor UI, graceful restart, data preservation and repair/reinstall gates are being strengthened and require a new successful native run. No stable release or manual Windows10/11/DPI acceptance is claimed.

Optional Quick Workspace Setup commit `3c0bcff` passed independent source review and **129 core tests** (19 new) in a separate worktree. It will remain a separate P2 PR until native wizard validation, rather than expanding this release's unverified surface.

Release workflow now stages exact source/test evidence, refuses stable versions and existing releases, verifies nonempty downloaded GitHub binary assets against SHA-256 while draft, then publishes only a prerelease. Release notes disclose all manual acceptance and signing limits.

## Cross-project authentication regression

A second bounded real Engine experiment used SHIFT `e054cd00e3d73cf815bae2b1280b98d8d45f01ea` and Codex Team `ced1aecac1569150cb0d9f1dfabf5979eaaac246`. Task: uppercase EXE/LNK Doctor regression cases, Implement/standard/isolated, no auto-commit, 4-call/1200-second/80000-token ceiling.

Run `run_3f8712c9e45e` created its isolated worktree. Actual first official SDK turn hit the same stale refresh-token error and now correctly stopped as **AUTH_REQUIRED**, rather than generic FAILED. One attempted coordinator call, recorded elapsed1.684s/wall1.74s; no developer/QA/reviewer stage, changed files or token metrics available. No paid API fallback. Live AI development remains **NOT VERIFIED**; only the real auth-stop classification and static Project Doctor integration are confirmed.

## Checkpoint — expanded native acceptance

Native [40004559552](https://github.com/LosarOS/SHIFT/actions/runs/40004559552) at `e054cd00e3d73cf815bae2b1280b98d8d45f01ea` and [40004952246](https://github.com/LosarOS/SHIFT/actions/runs/40004952246) at `16a6319` both passed. The latest screenshot-composition refinement [40005570348](https://github.com/LosarOS/SHIFT/actions/runs/40005570348), source `5d4b61b`, also passed.

- 110/110 core tests on Windows and Linux, zero failures/skips.
- Real Doctor inspection/repeat left the command marker absent; command refusal still did not dispatch, explicit approval created the benign marker.
- Graceful exit/restart preserved workspace, English and Midnight; compact search, tray and second-instance activation passed.
- Per-user install/Start Menu shortcut, same-version in-place reinstall, uninstall, reinstall/reopen and SQLite SHA256 preservation passed. This is not a claim that every older-binary upgrade was tested.
- Retrieved the genuine native artifact through the GitHub connector, checked both binary hashes, reviewed six actual running-window captures. Clean readable layout and persisted theme/language confirmed. Corrected capture timing waits for composition; screenshots remain raw Windows runner captures.

Final canonical packaging now tests the actual extracted portable ZIP, rather than only the directory before archiving. Its JSON records `portable_smoke_archive=true`; release publication requires that evidence. This last distribution gate must pass at the final source before publication.

P2/P3 findings deferred: Russian singular item-count grammar and the enabled-item toggle's state-like action label can be clarified; physical-PC DPI/Narrator/high-contrast acceptance remains open. Optional Quick Setup [PR10](https://github.com/LosarOS/SHIFT/pull/10) is draft:129 core tests and native compilation/standard application smoke passed, but the new wizard-specific Windows acceptance is **NOT VERIFIED**.
