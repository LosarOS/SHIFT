# SHIFT Studio architecture

RC8 implementation description, 2026-11-15. SQLite schema3; widget-definition JSON schema1 remains distinct from the database version. This document does not certify pending Windows workflows or physical acceptance. See [validation](../VALIDATION.md).

Studio is an opt-in modular subsystem. Existing workspace dispatch, command consent, history and SQLite remain authoritative. Domain → Application → Infrastructure → WinUI presentation; UI accesses IStudioStore, never SQL.

## Data and orchestration

A DesktopProfile owns per-monitor WallpaperAssignments and WidgetInstances. WidgetDefinitions contain bounded declarative content/style; instances contain layout/interaction; WidgetRuntimeState holds per-instance notes/timer state. Six built-in style templates are immutable; users save customized definitions as independent library entries. Layout uses monitor device identity, normalized work-area coordinates and DIP dimensions, with primary/nearest fallback and explicit Gather.

DesktopProfileService implements apply and recovery under one operation gate. Validate → prepare managed images → capture static-image values → preserve managed originals → persist journal → persist action intent → mutate Windows → apply widget windows → persist result. Windows and SQLite are not a shared transaction. An attempted but unacknowledged OS call is ambiguous and is compared, never automatically repeated.

Restore checks current values against intended/before/recorded recovery copies, with explicit confirmation for later-user conflicts. Journal snapshots survive profile deletion. System provider configuration is outside this snapshot: Spotlight is unsupported, slideshow/empty/remote states are refused. Current static-image confirmation is required each manual wallpaper apply. Automatic workspace application supports widget-only profiles and follows actual workspace dispatch attempts; a stopped preflight with no started item does not apply a profile. Workspace commands retain their existing independent consent.

## Persistence and schema3

The historical Studio RC3–RC7 schema was2. Its definition table used `CHECK(Kind BETWEEN 0 AND 3)`; merely appending a CLR enum would fail on real upgraded data. Schema3 widens that constraint to4. Existing enum values remain Clock0/SystemMonitor1/FocusTimer2/Notes3; QuickActions is4.

`SqliteStore.InitializeAsync` captures and validates a committed online backup before v1/v2 migration. V2→v3 temporarily disables FK enforcement outside a transaction, rebuilds only StudioDefinitions, copies rows, checks foreign keys and advances user_version atomically. Enforcement is restored even on failure. Instance IDs, definition revisions, profiles, runtime state and journals remain unchanged. V1 adds the current Studio tables; a fresh database starts at3. A newer unsupported version or nonempty unversioned database is refused. Old RC7 must use a saved schema2 copy for downgrade.

Definitions/instances/state are separate tables; StudioRecovery deliberately has no profile FK, because recovery must survive profile deletion. Profile configuration uses revision CAS. Informational LastApplied is atomic and monotonic without configuration-revision churn; layout writes compare acknowledged instance identity/visibility/layout/mode and update compatible instances atomically. Conflicts do not overwrite unseen editor changes or repeat dirty writes automatically. Notes/Timer revisions remain independent.

## Quick Actions

QuickActions is a fifth native widget, not a script host. Its bounded declarative list contains Workspace, Folder, File or HTTP/HTTPS URL actions. Workspace entries reference saved IDs; direct targets reuse WorkspaceValidator and the normal item construction/dispatch boundary. No arbitrary executable or shell-command field is supported.

A click is marshalled to the main shell. Workspace actions resolve the current workspace and use the existing coordinator, configured preflight and ordinary command consent. File/folder/URL actions use the same trusted launch path; callbacks cannot grant permissions or silently retry. Preview and click-through do not activate actions. The shell rechecks runtime identity/enabled state, maintenance, open dialogs and the coordinator single-launch guard after asynchronous data lookup; stale activations are refused rather than retried. Failure is shown rather than hidden.

Widget Lab edits a draft and validates the selected targets before save. Portable Studio profile packs intentionally strip local action lists during export and reject action payloads during import: IDs/private paths are not portable capabilities. Imported QuickActions definitions require explicit local configuration before use. This does not change ordinary workspace JSON consent rules.

## Native windows

The main shell remains WinUI3. Simple widgets use managed WinForms/Win32 tool windows on one dedicated STA/pump, reusing the existing desktop framework. This provides supported whole-window alpha without introducing a browser/process per widget. It is not Explorer's icon plane: ordinary z-order, no WorkerW/injection, topmost opt-in only. Solid/translucent are different from Acrylic/Mica; requested backdrops currently show an explicit fallback.

One1s scheduler handles due visible clocks/metrics and timer deadlines. Metrics are sampled off the UI thread; notes are change-driven with bounded local recovery drafts and Revision CAS. Timer uses monotonic elapsed time and saved UTC deadlines for restart/sleep; wall-clock changes remain a documented approximation. Quick Actions update from configured declarations rather than periodic model/network calls. Stop flushes or preserves a durable draft before window/thread teardown. Runtime failures disable Studio; repeated unclean startup triggers safe disabled mode. This is managed fault isolation within one process, not protection against every native process crash.

## Lab and safety controls

Draft controls and in-editor mockup do not write saved configurations. Separate desktop preview is an ephemeral owned window with no SQL state/layout writes. Save persists declarations; apply is explicit. Disable, Restore and Gather are available above the Lab/gallery pages; the tray always restores the main window. JSON v1 has a256KiB/depth16 bound and strict fields; it cannot carry arbitrary scripts/executables.

## Backup, restore and resource scope

Recovery Center belongs to the main shell and uses IBackupCatalog/BackupCatalog, not direct SQL from a view. It lists bounded metadata, inspects a private SQLite snapshot and displays schema/workspace/history/profile/definition counts. It does not apply wallpaper, restore widget windows, dispatch workspaces or infer that external resources are present.

Explicit restore revalidates the selected source, upgrades old snapshots privately, creates a live-data safety backup, checkpoints the active database and replaces it through the existing serialized maintenance path. SQLite/model/foreign-key validation and trigger/view rejection precede replacement. Inspection approval is not cached authorization for a later modified file.

SQLite contains saved widget state and configuration, but does not include managed image files or Notes recovery drafts. Full resource preservation requires Quit plus a complete data-directory copy. Profile packs and Notes text exports are different, narrower formats; neither is a full recovery image. Historical statements referring only to schema2 backups must be read with this schema3 compatibility boundary.

## Deferred research decisions

| Area | Decision |
|---|---|
| HTML/JS widgets | Deferred. A future separate WebView2 context is not a complete hostile-code sandbox; no arbitrary-code import now. |
| Taskbar/Explorer transparency | No supported general control used; no registry patching, shell extensions or third-party utility installation. |
| Accent/system theme | Readable UI/system preferences are distinct from a supported system-wide setter. Not changed. |
| Windows Widgets Board | Separate packaged provider/identity investigation required; current Inno/portable app remains unpackaged. |
| Advanced backdrops | Supported platform-dependent Mica/Acrylic need independent composition/accessibility acceptance; explicit solid/alpha fallback now. |
