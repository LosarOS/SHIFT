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
