# RC8 launch scenarios — implementation checkpoint

2026-11-14. Branch feature/rc8-product-evolution; baseline 205a5ff. No commit/push by this agent.

## Implementation

- Domain `LaunchScenarioOptions`, explicit Preflight Off/Continue/StopOnErrors/SkipErrors and launch-error Continue/Stop. Defaults retain legacy behavior.
- `AppSettings.WorkspaceScenarios`: local per-workspace GUID map; no SQLite schema migration. Load/save and backup validation reject unknown enum values, null/oversized maps and empty IDs. Import/clone use fresh IDs and do not inherit policy automatically. Scenario editor prunes deleted-workspace settings.
- LaunchCoordinator snapshots immutable input, holds existing single-run gate during read-only Doctor, persists inspection observations before dispatch, refuses incomplete/mismatched reports, enforces stop/skip and keeps ordinary command consent. No shell inference, command execution by Doctor, retry or parallel dispatch.
- Missing targets stopped by preflight are Failed; following steps skipped. Skipped invalid targets produce PartialFailure, never false Completed. Warnings/Unknown are observations and do not imply readiness.
- Launch rejection after a successful inspection remains an actual item failure and follows configured error policy. Cancellation and storage failures retain existing durable-history semantics.
- `ShellViewModel` passes saved scenario and normal WorkspaceDoctor/LocalItemInspection.
- `Views/WorkspaceScenarioDialog.cs`: genuine RU/EN native controls, draft/cancel/save, explicit scope/trust explanations; Scenario.Preflight and Scenario.OnError UIA IDs. Root owns Workspace.Scenario button and localized persisted reasons.

## Actual Linux tests

`PATH=/workspace/.dotnet:$PATH dotnet test tests/Shift.Tests -c Release --filter FullyQualifiedName~LaunchScenarioTests --nologo`: **20 passed, 0 failed, 0 skipped**.

Combined CoreTests/StabilityTests/LaunchScenarioTests: **78 passed, 0 failed, 0 skipped** (includes the20 above; do not add totals).

Includes all preflight policies, dispatch-stop/continue, consent/refusal, cancellation, failed persistence, malformed report, unknown/warning behavior, no retries, single-flight during async Doctor, factual saved observations, post-inspection target failure, settings bounds, old JSON defaults and real SQLite backup/restore roundtrip. Imported/cloned workspace defaults and absence of history side effects verified.

## Native acceptance added, pending execution

`packaging/Smoke-Windows.ps1`: current nonlegacy fresh portable/installed candidate opens RU scenario dialog, saves/reopens policies, creates one explicitly owned missing File target, verifies PartialFailure without consent/command marker, removes only that new item, and preserves existing command. Policy survives the existing process restart; EN readback observes actual selected values, then resets to defaults and reopens to prove cleanup. No repeat dispatch clicks; selection assertions only observe and never repair missing persistence. RC2 legacy and resumed existing-profile gates retain their actual scope.

New gates: `workspace_scenario_preflight_stop_no_dispatch`, `workspace_scenario_settings_restart_and_cleanup`.
Screenshots: `*-launch-scenario-ru.png`, `*-launch-scenario-stopped.png`, `*-launch-scenario-restored-en.png`.

**NOT RUNTIME VERIFIED** until real Windows CI completes. No PowerShell executable was present locally, so no local parser pass claimed. Full Windows/DPI/physical hardware and malicious-shell safety are not implied by these tests.

## Recovery native probe coordination

RecoveryCenter is implemented by the independent recovery agent; root owns navigation. Native probe now creates an owned backup through Recovery.Create, selects a genuine catalog entry, validates and cancels. Live SQLite database/WAL hashes, history count and command marker/hash/mtime must remain unchanged across inspect/cancel. It does not click Restore. Gate `recovery_center_inspection_no_restore`; screenshot `*-recovery-center-preview.png`. Native execution remains pending; no green result inferred from script existence.

Root notified that automatic linked Studio apply must not run for preflight rejection (PartialFailure with no StartedAt item); ordinary post-dispatch behavior is separate. UI and core changes are ready for root review/integration. No concurrent edits to root workflow/version files made.

## Confirmed Windows proof failure and correction

CI43651401999 reached the new recovery gate but Get-FileHash could not open the active SQLite database because Windows SQLite uses file sharing incompatible with that generic file reader. This was a probe failure; its failed native run is not a passed gate.

The recovery gate now compares logical committed state using a dedicated owned acceptance-tool stage: SQLite read-only connection, query_only, one deferred read transaction, schema/user_version and every user-table row. Typed row values are hashed deterministically with duplicate-row counts preserved. It reads WAL through SQLite rather than racing physical database/WAL bytes or taking a checkpoint. Ownership requires the disposable GHA Windows profile's .shift-ci-owner marker. Fresh disposable portable smoke allocates its own marker; preserve/resume still require explicit caller ownership. Actual restore is never invoked.

History count, marker hash/mtime and the selected idle backup's physical SHA256 checks remain enforced. A diagnostics *-recovery-logical-proof.json records source/before/after/backup SHA256. No generic error suppression, retries or removed readonly assertions.

New DatabaseFingerprintTests: **3passed,0failed,0skipped**, Linux .NET10. Regressions prove checkpoint/row-order stability, sensitivity to real data/schema/duplicate changes, and missing-file refusal without creation. Acceptance-tool build succeeded0warnings/0errors. Corrected Windows native execution remains pending.
