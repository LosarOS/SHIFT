# RC8 Quick Actions — implementation checkpoint

2026-11-14. Shared branch feature/rc8-product-evolution. No independent push or merge by this agent.

## Implemented

- Fifth actual native widget type appended as Kind4 (original enum values0–3 unchanged).
- Up to12 declarative Workspace/Folder/File/HTTP(S) actions, title/target validation reuses WorkspaceValidator. No arbitrary shell/arguments fields; executable file targets rejected. Saved workspace IDs resolve current store data when clicked.
- Widget Lab adds/removes selected destinations in a draft; preview cannot execute. Native FlowLayoutPanel scrolls actual buttons; keyboard Invoke works through standard button controls. Action windows use existing lifecycle/placement/style runtime.
- Main shell marshals callbacks off widget STA, guards runtime identity, Studio enabled, quit/maintenance/dialog/active launch both before and after asynchronous read, shows main window, and dispatches through existing StartLaunchAsync. No direct Process.Start or new command bypass.
- Profile-pack export strips all destinations and workspace IDs; preview and RU guide explicitly explain reconfiguration after import. Individual definition export retains them with explicit privacy documentation. Outgoing serializers omit empty Actions, preserving strict RC7 compatibility for original widget kinds; new QuickActions kind requiresRC8.
- WidgetQuickActionList has content equality, avoiding legacy empty JSON round-trip record equality regressions.

## Schema3

Required because schema2 StudioDefinitions has CHECK Kind0–3. Initialize validates and creates online snapshot before v1/v2 migration. For v2, rebuild declaration parent table with Kind0–4 in a transaction; preserve all child rows, notes, geometry and revisions; foreign_key_check before commit; restore FK enforcement in finally. v0/v1 initialize/migrate directly to3. Private backup restore upgrades v1/v2 snapshot before replacing live data. RC7 safely refuses schema3; pre-migration v2 copy is the downgrade route. No downgrade-in-place claim.

Regression uses actual v2 CHECK0–3 fixture, preserved definition/profile/Notes, v2 backup, and successful newkind persistence. Recovery agent separately reviewed transaction and added failure/backup safety tests.

## Executed evidence

- Full shared Linux core suite:502pass/0failed/0skipped at initial actions3.trx; later509pass/0failed/0skipped at actions-final.trx including serializer followup and concurrent agents' tests. These are repeated snapshots, not additive tests.
- Actual TRX: /workspace/scratch/actions-tests/actions-final.trx.
- Acceptance tool build:0warnings/0errors, Linux .NET10. This is tool compilation, NOT Windows runtime.
- git diff --check passed at checkpoint.
- Attempted Linux WindowsApp --no-restore build failed NETSDK1004 missing Windows app assets; no native app/runtime evidence claimed.

## New disposable Windows gate — NOT YET EXECUTED

Smoke-Studio end phase prepares owned fixture, actual Lab action add/save without dispatch, one native window, screenshot, quit/restart, command refusal without marker, explicit approval with actual marker and history, cleanquit. Check quick_actions_lab_restart_consent_dispatch and quick-actions-native.json only written after assertions pass. New tool actions-* stages are protected by existing GITHUB_ACTIONS/Windows/dataowner entry guards. Runs after existing ten/four-widget assumptions, preserving their prior checks.

Windows App compilation, native UI/PowerShell gate, screenshots, highcontrast/DPI/multi-monitor and physical Win11 remain NOT VERIFIED until root Actions evidence. Root owns final integration/commit/CI/release confirmation.
