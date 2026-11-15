# RC8 — independent documentation / source consistency review

2026-11-14. Read-only production review, no runtime claims. Checked candidate release notes, Recovery Center contracts/report, Quick Actions validation/dispatch, widget/profile transfer and existing published-version README.

## Findings

- **Correct:** Recovery Center is described as SQLite-only. `BackupInspection.ExternalStudioResourcesIncluded=false`; full resource bundle backup is not implemented. Keep Images/RecoveryNotes/Quit guidance visible. Do not describe this as full-system recovery.
- **Correct:** Quick Actions are declarative Workspace/Folder/File/URL only. A saved workspace is resolved from current store and goes through existing launch/consent; preview controls do not execute. `WorkspaceValidator` rejects executable/script/shortcut extensions as File targets. No executable-file bypass confirmed in this review.
- **Correct:** Profile packs strip Actions on export and reject nonempty Actions on import. Single-widget declarations retain valid local actions; import assigns a fresh identity and does not create windows or execute. These are different transfer contracts and must not be described interchangeably.
- **P2 disclosure gap:** Profile export currently clears local Actions silently while its standard warnings name wallpaper/runtime/display exclusions. Add a localized exclusion warning and update `docs/STUDIO-SHARING-RU.md` so users know they must configure actions again after importing a profile. This is configuration-loss clarity, not an observed command execution defect.
- **P2 historical docs:** General architecture contains a historical schema-v1 section. Candidate release notes correctly state schema3. Keep migration report explicit, including schema2→3 backup, downgrade refusal and full-folder backup instructions; historical reports must not be rewritten as current runtime results.
- **Correct:** README download links continue to published RC7 rather than nonexistent RC8 files. Historical screenshots remain labeled and are not passed off as RC8 proof.

## Evidence boundary

No Windows UI test performed by this reviewer. Root-owned core/native suites and source freeze determine release eligibility. No claim that full-resource backup, forced live races or hardware/accessibility matrix passed.
