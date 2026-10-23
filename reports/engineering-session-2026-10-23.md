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
