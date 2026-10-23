# SHIFT Studio architecture

Studio is an opt-in modular subsystem. Existing workspace dispatch, command consent, history and SQLite remain authoritative. Domain → Application → Infrastructure → WinUI presentation; UI accesses IStudioStore, never SQL.

## Data and orchestration

A DesktopProfile owns per-monitor WallpaperAssignments and WidgetInstances. WidgetDefinitions contain bounded declarative content/style; instances contain layout/interaction; WidgetRuntimeState holds per-instance notes/timer state. Six built-in style templates are immutable; users save customized definitions as independent library entries. Layout uses monitor device identity, normalized work-area coordinates and DIP dimensions, with primary/nearest fallback and explicit Gather.

DesktopProfileService implements apply and recovery under one operation gate. Validate → prepare managed images → capture static-image values → preserve managed originals → persist journal → persist action intent → mutate Windows → apply widget windows → persist result. Windows and SQLite are not a shared transaction. An attempted but unacknowledged OS call is ambiguous and is compared, never automatically repeated.

Restore checks current values against intended/before/recorded recovery copies, with explicit confirmation for later-user conflicts. Journal snapshots survive profile deletion. System provider configuration is outside this snapshot: Spotlight is unsupported, slideshow/empty/remote states are refused. Current static-image confirmation is required each manual wallpaper apply. Automatic workspace application initially supports widget-only profiles. Workspace commands retain their existing independent consent.

## Native windows

The main shell remains WinUI3. Simple widgets use managed WinForms/Win32 tool windows on one dedicated STA/pump, reusing the existing desktop framework. This provides supported whole-window alpha without introducing a browser/process per widget. It is not Explorer's icon plane: ordinary z-order, no WorkerW/injection, topmost opt-in only. Solid/translucent are different from Acrylic/Mica; requested backdrops currently show an explicit fallback.

One1s scheduler handles due visible clocks/metrics and timer deadlines. Metrics are sampled off the UI thread; notes are change-driven with bounded local recovery drafts and Revision CAS. Timer uses monotonic elapsed time and saved UTC deadlines for restart/sleep; wall-clock changes remain a documented approximation. Stop flushes or preserves a durable draft before window/thread teardown. Runtime failures disable Studio; repeated unclean startup triggers safe disabled mode. This is managed fault isolation within one process, not protection against every native process crash.

## Lab and safety controls

Draft controls and in-editor mockup do not write saved configurations. Separate desktop preview is an ephemeral owned window with no SQL state/layout writes. Save persists declarations; apply is explicit. Disable, Restore and Gather are available above the Lab/gallery pages; the tray always restores the main window. JSON v1 has a256KiB/depth16 bound and strict fields; it cannot carry arbitrary scripts/executables.

## Deferred research decisions

| Area | Decision |
|---|---|
| HTML/JS widgets | Deferred. A future separate WebView2 context is not a complete hostile-code sandbox; no arbitrary-code import now. |
| Taskbar/Explorer transparency | No supported general control used; no registry patching, shell extensions or third-party utility installation. |
| Accent/system theme | Readable UI/system preferences are distinct from a supported system-wide setter. Not changed. |
| Windows Widgets Board | Separate packaged provider/identity investigation required; current Inno/portable app remains unpackaged. |
| Advanced backdrops | Supported platform-dependent Mica/Acrylic need independent composition/accessibility acceptance; explicit solid/alpha fallback now. |
