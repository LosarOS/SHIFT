# RC8 Recovery Center — implementation checkpoint

2026-11-14. Shared feature branch, original main205a5ff. No commit/push by subagent.

Implemented IBackupCatalog contracts, bounded inventory (100 results; root plus known automatic/migration-backups directories only), no directory recursion/symlink traversal; private SQLite online snapshot inspection with WAL pages and existing full restore validation. Summary: schema, workspaces, launch runs, Studio profiles and widget definitions. Inspection never calls Initialize, restores or modifies active data.

Usable localized Recovery Center dialog: inventory, Browse external DB, Create consistent backup, Validate/preview, explicit Restore enabled only after validation. Restore delegates to existing guarded root flow with confirmation, revalidation and safety copy. Closing during background work requests cancellation and closes automatically after snapshot completion, retaining lifecycle ownership. Snapshot's synchronous SQLite copy is cancellation-aware before/after, not forcibly interruptible mid-copy.

SQLite-only boundary is explicit: images and Notes recovery files are NOT included. Complete-copy guidance requires Quit and copying entire data folder incl Studio/Images, Studio/RecoveryNotes and migration-backups; no unsafe partial archive restore implementation. Existing data folder/files are not deleted.

New files: src/Shift.Application/BackupCatalog.cs; src/Shift.Infrastructure/BackupCatalog.cs; SqliteStore.BackupInspection.cs; src/Shift.App/Views/RecoveryCenter.cs; Services/Strings.Recovery.cs; tests/Shift.Tests/BackupCatalogTests.cs.

Actual targeted Linux .NET10 tests: **10 passed,0failed,0skipped**. Cases: valid preview source/live preservation, corrupt/no changes, missing not created, future schema rejected, executable trigger rejected, actual committed WAL workspace captured, cancellation, bounded/discrete inventory, absent/known directories, RU/EN scope. Command: /workspace/.dotnet/dotnet test tests/Shift.Tests -c Release --filter FullyQualifiedName~BackupCatalogTests --nologo --no-restore.

Current Studio schema3 integration: inspection summaries accept schema>=2; schema validation reuses Quick Actions agent's existing ValidateBackupAsync accepting1/2/3. Restore is always revalidated at time of replacement, even if file changed after preview.

Windows GUI compile/native Recovery Center acceptance remains NOT VERIFIED. Root owns Settings hook and extraction RestoreBackupFromPathAsync preserving existing guard/maintenance/safety behavior. No claim of full-resource bundle backup; that capability remains deferred rather than advertised.

## Independent schema3 / import review

Reviewed Quick Actions domain validation, WidgetTransfer strict JSON, profile pack scrub/reject behavior, dispatch entry point and schema3 persistence/restore. The FK parent-table rebuild disables foreign_keys outside its own transaction, copies definitions, drops/replaces only the parent table, checks all FKs, commits user_version3, and restores foreign_keys in finally. Existing enum values retained. Existing restore upgrades only an owned snapshot before live replacement; validation accepts1/2/3 and Studio validation includes>=2.

Added four independent QuickActionsMigrationSafetyTests: actualv2 backup restored via private migration to livev3 while sourcebackupbytes/version stayunchanged and safetycopy retainscurrentworkspace; staging-table DDL collision leavesv2data/refs intact and hasvalidatedv2backup; cancelled restore preservesv2source/livev3; cancelled initialization preservesv2Notes/schema. Combined targeted suite **14passed,0failed,0skipped**, Linux .NET10. These are cancellation-before-operation checks, not a claim that every mid-transaction cancellation interleaving was injected.

Actual old RC7 Windows executable downgrade rejection still requires Windows native evidence. Baseline RC7 Initialize rejects user_version>2 and RC7 restore rejects backups outside1/2 by source review; new migration's pre-backup is version2. No runtime downgrade PASS inferred from source.

Independent lifecycle finding sent to Quick Actions owner: old runtime action can resolve store.ListAsync then dispatch after Studio disabled/replaced/quit because precheck precedesawait. Owner asked to recheck sender identity/enabled/quit/busy afterawaitbefore StartLaunchAsync.

Recovery.Create automation ID added. Automation owner is adding native inventory/create/inspect/cancel gate, including hash/history/command-marker no-side-effect assertions; result pending, not claimed passed here.

## Actual RC7 upgrade acceptance — new, not yet executed

Added acceptance-only packaging/Smoke-UpgradeRC7.ps1 and Rc7UpgradeAcceptance tool stages. Requires disposable GitHub Windows with explicit owned temp root and clean extracted candidate. It downloads immutable RC7 portable, checks fixed SHA256 + published sums + source manifest, downloads its exact old smoke script pinned by blob SHA256, and executes actual RC7 UI to create v2 workspace/history.

The new candidate gets only a private SQLite online snapshot and owned marker fixture. Current UI resume smoke performs actual RC8 startup/restart; tool checks full saved workspace/history/settings records, schema3, FK integrity and matching validated v2 pre-migration backup. Original RC7 v2 data is retained until owned final cleanup. Old RC7 then receives a separate v3 database copy; the gate requires the visible original StartupError plus newer-version message, graceful exit and unchanged v3-copy SHA256. The original upgraded database SHA256 must also remain unchanged.

Output rc7-upgrade-smoke.json only on success. Linux tool compilation0warnings/0errors. PowerShell native acceptance **NOT EXECUTED HERE**; root owns Windows workflow wiring and results. No application-source modification, no user profile/wallpaper use, no old-version failure counted as a successful upgrade.


## Actual RC7 old-harness failure and bounded adaptation

CI43653711472 failed inside the immutable original RC7 smoke at line107: Set-Field GetCurrentPattern(ValuePattern) returned Unsupported Pattern. The actual RC7 process had already created a workspace and genuine Declined/Completed history; this is not claimed as a completed baseline. The caller field ID was absent in the old log, so its exact identity was not inferred from timestamps alone.

Smoke-UpgradeRC7 now retains/verifies the immutable source script, then modifies exactly Set-Field in a separate compatible copy. The helper waits for an enabled writable ValuePattern from a fresh peer, invokes SetValue once and acknowledges its value via another fresh peer. Linebreak equivalence is normalized; spaces/content are not trimmed. No launch/consent action retries, no feature gates removed, no binary changes or fabricated v2 data. Timeout remains a failure.

Diagnostics retain both complete scripts, SHA256 hashes, a unified helper diff and adaptation JSON. The final proof explicitly records original_harness_passed=false rather than pretending the unmodified old harness passed. Compatible harness parsing is an early mandatory gate.

Actual local verification: trusted pinned PowerShell7.6.0 installed in a workspace-owned tool directory; both main script and generated compatible old script parsed successfully. Exactly one helper replacement and preserved UTF8 BOM verified. This is syntax/patch evidence, not Windows UI runtime acceptance. Windows result pending root CI.

## Second harness readiness finding — 2026-11-15

CI43654962743 failed the current Quick-Key helper at SetFocus with ElementNotAvailable after language/rebuild. The immutable RC7 contains the identical unsafe helper. Its compatible harness now replaces exactly TWO helpers: Set-Field (writable ValuePattern + readback) and Quick-Key (fresh enabled peer, stable owned foreground/focus before one SendWait). Each replacement must match exactly once; all other source and feature assertions remain unchanged. Original hash guard remains intact. Diagnostic diff contains two correctly offset hunks and both script hashes; provenance names both failures and original_harness_passed remains false.

Local PowerShell7.6 parser passed main and generated compatible script. Verified generated text differs only by these two exact replacements, with exactly one SetValue and one SendWait in the respective helpers. This does not establish Windows runtime success; root runs the next batch CI. No production source changed.

## Single-write ambiguous outcome handling — 2026-11-15

CI43657366311 passed current portable and Studio gates but old RC7 compatibility Set-Field failed inside SetValue with ElementNotAvailable (line117). Readiness polling cannot remove the time-of-check/time-of-use peer-replacement race. The compatibility helper still invokes SetValue exactly ONCE. Only ElementNotAvailableException from that invocation is treated as an ambiguous outcome; it then requires the existing exact normalized fresh-peer readback before continuing. Mismatch/timeout remains a failure; other exceptions propagate. Neither field writes nor commands/launches/key actions are retried. Logs identify the field and distinguish ambiguous invocation from confirmed postcondition without logging values. Both helpers and all feature assertions remain unchanged outside this narrow handling.

PowerShell parser passed the wrapper and generated compatible script; exactly-one helper match guards remain. Actual Windows result after this correction is pending root CI, not reported as passed here.

## Downgrade refusal localization mismatch — 2026-11-15

CI43658629380 passed the actual immutable RC7 baseline and RC8 upgrade; ACTUAL_RC7_UPGRADE_PRESERVED was printed before the final refusal probe failed. The probe incorrectly required an English body while permitting a Russian title. Exact immutable source7bb1760 Strings.Presentation.cs line77 translates the known exception into “Эта база создана более новой версией SHIFT. Обновите приложение перед открытием.” MainWindow initializes the saved language only AFTER store.InitializeAsync, so a schema refusal retains the initial Russian UI. The failed probe had not retained a screenshot/UIA text for this final view, so no actual Russian runtime body is claimed from that failed artifact alone.

Acceptance-only correction matches the exact known RU or EN failure title AND the exact known RU or EN newer-schema reason from pinned source. Generic errors do not pass. It additionally retains actual UIA names/title/reason, original startup logs and an unmodified screenshot on both success and timeout; successful final proof records the observed strings. Graceful exit, downgrade-copy SHA256, source upgraded SHA256 and repeated preservation checks remain mandatory.

PowerShell7 parser and git diff --check passed. Actual corrected Windows refusal/unchanged-hash result awaits the next CI. Production source/binaries not modified.
