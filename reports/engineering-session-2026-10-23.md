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
