# Studio SQLite migration: RC2 → schema 2

## Versions and safety

RC2 uses `PRAGMA user_version=1`. Studio uses version2 and refuses any newer/negative version before schema writes. Version0 is accepted only for an empty database; unversioned owner tables are never adopted or changed. RC2 already refuses databases newer than1, so downgrade cannot silently overwrite Studio data. To return to RC2, close SHIFT and retain the Studio profile, then use the pre-migration v1 snapshot as a separate database/profile. Do not point an old executable at a new active database.

For an existing v1 database, initialization first writes a SQLite online backup into `migration-backups/SHIFT-*.db` beside the database. This includes committed WAL pages. The v1 snapshot is validated before the first schema mutation. Backup failure aborts migration. New tables/indexes and `user_version=2` commit in one transaction; a table collision or SQL error rolls the schema transaction back. Migration snapshots are never pruned by this subsystem. Existing workspace/history/settings records are retained. Migration does not execute a workspace or apply wallpaper. Studio is disabled when its settings row is absent.

## Data model

- `StudioProfiles`: independent visual profiles; nullable workspace FK (`ON DELETE SET NULL`), flags, wallpaper placement, last application and revision.
- `StudioWallpapers`: per-profile/per-monitor image references.
- `StudioDefinitions`: widget type, bounded declarative appearance/content and definition revision.
- `StudioInstances`: profile/definition references and per-instance layout, visibility and interaction.
- `StudioWidgetState`: per-instance notes/timer state and revision.
- `StudioSettings`: explicit enablement/consent, current profile, runtime recovery flags.
- `StudioRecovery`: durable operation ID, source profile, timestamp, status and bounded self-contained recovery snapshot. No profile FK: deleting a profile must not destroy its recovery record.

Foreign keys, uniqueness, enums/booleans and revisions have database constraints; indexes support profile/workspace lookup, widget references and pending recovery. One partial unique index permits at most one enabled automatic profile per workspace. A second automatic choice fails explicitly; no implicit reassignment or launch occurs.

Profile/definition/runtime-state saves use optimistic revisions. New records use revision0; successful saves return revision+1. Stale updates and attempts to recreate a deleted nonzero revision fail. Profile edits upsert retained instance IDs instead of deleting/reinserting them, preserving saved notes/timers. Removing an instance deletes its runtime state; definition deletion is restricted while referenced. Workspace deletion detaches its profiles, turns off automatic application and increments their revisions. Workspace cloning does not copy Studio bindings or grant automatic application.

## Backups and restore

Existing online backup and guarded replacement remain authoritative. Restore accepts validated schema1 or2. Version2 requires all Studio tables/columns, valid bounded JSON/models and referential integrity. Unknown JSON properties, executable triggers/views, invalid relations and corrupt state are rejected before replacing the live database. A safety copy of current data is retained. Restored v1 data is migrated to2 before normal use; interrupted workspace runs are marked interrupted without redispatch.

The UI must stop and flush widget runtime before database replacement and re-load Studio state afterward. The serialized LocalDataStore gate moves database work off the UI thread; it cannot by itself cancel already queued widget callbacks.

Image files are not embedded in SQLite. Managed asset ownership/rebasing belongs to the image library; image references are stored verbatim and never executed. Moving portable requires resolving managed references against the new data root, not deleting owner images. Recovery journals are not launch packages.

## Executed evidence

Linux .NET10: baseline CoreTests32 passed. Focused StudioPersistenceTests + existing StabilityTests45 passed before the final automatic-profile uniqueness case. Final targeted persistence run:21 passed,0failed/skipped; raw log `studio-store-final.log` and TRX retained in the session scratch area. These focused counts overlap and must not be added together. Tests use unique temporary databases only. The v1 fixture retains the unchanged RC2 base tables/settings/history and removes Studio tables to model the version boundary. Native RC2 installer upgrade, real Windows runtime, display changes and wallpaper restoration require the parent Windows acceptance workflow; not claimed here.
