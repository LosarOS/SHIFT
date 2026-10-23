# Changelog

## 1.0.0-rc.3 — Studio experimental preview (pending native gates)

- Added opt-in desktop profiles, separate workspace associations and staged static-wallpaper recovery with managed originals.
- Added four owned native widget types, centralized updates, normalized layout, notes recovery drafts and deadline-based focus timers.
- Added Widget Lab with reversible drafts, external preview, six built-in styles and bounded declaration import/export.
- Added transactional SQLite v2 migration, pre-migration backup, revision guards and v1/v2 backup validation.
- Added disposable native Studio acceptance using actual published RC2 data; physical Windows/DPI/accessibility acceptance remains outstanding.
- Kept Studio disabled by default, command consent unchanged, automatic wallpaper changes disabled and Explorer untouched.

## 1.0.0-rc.2 — 2026-10-23

- Added cancellable Workspace Doctor with Ready / Warning / Error / Unknown observations, RU/EN guidance and direct item editing.
- Local probes reject damaged PE/shortcut headers and report missing paths, access failures, unavailable associations and unverified targets without execution or network probes.
- Fixed duplicate WinUI icon PRI entries and conflicting WindowsDesktop XAML targets; added generated application metadata.
- Replaced the unsupported unpackaged accessibility event subscription with desktop HWND notifications.
- Added safe HRESULT/method-frame logging and bounded disposable CI native crash diagnostics.
- Published the generated application resource index required by unpackaged WinUI startup.
- Added 47 Doctor/logging regression cases; 110 core tests pass on Windows and Linux. Real portable/installed startup and installer smoke passed; final evidence and manual acceptance limits remain tracked in the session report.

## 1.0.0-rc.1 — 2026-10-20

### Added

- Compact launcher, keyboard selection and opt-in global hotkey.
- Existing-instance activation through current-user IPC.
- Serialized background storage, full usage summaries, sorting and tag filters.
- Paged Unicode history search, failure filter, retry and clearing.
- Validated backup restore with a safety backup and WAL handling.
- Typed localized validation, high contrast handling, responsive actions and display-aware sizing.
- Expanded Windows UI and installer smoke, screenshots/log capture, source hygiene and synthetic diagnostics.
- English/Russian README, original brand cover, audit report and pinned CI actions.

### Fixed

- Self-contained builds no longer bootstrap a separately installed Windows App SDK.
- URL shell requests escape quotes/spaces and do not expand environment placeholders.
- Save dialogs and exit wait for active writes; restore blocks new launch actions.
- Null/duplicate JSON fields, reserved path names, incomplete backup schemas and stale progress handling.
- Removed generated machine registry files from the unpublished bootstrap commit.
- Restore now snapshots committed source WAL pages, validates workspace/history values across all pages, and cleans temporary journals after refusal.
- Restore refuses to race with in-flight UI actions; repeated Launch opens the active queue, and asynchronous buttons ignore repeated clicks while busy.
- Adding an item normalizes imported sort orders, avoiding integer overflow; export refuses files larger than its own import limit before opening a picker.

### Validation

63 core/integration tests passed locally (updated 2026-10-21); Windows release gates are pending. See docs/VALIDATION.md.


## 0.1.0 — 2026-10-20 (engineering preview)

### Added

- Workspace domain, validated item types, cloning and portable versioned JSON.
- Durable SQLite workspaces, tags, history, settings, online backup and crash recovery.
- Sequential launch coordinator, cancellation, command consent and per-item failure isolation.
- Native Windows WinUI shell and editor, all core navigation, onboarding, RU/EN, four palettes.
- Tray, notifications, per-user startup integration, bounded local logs.
- Core/integration tests, release build script, portable ZIP / Inno Setup installer definitions, CI and documentation.

### Release status

Windows runtime/packaging validation pending. See `docs/VALIDATION.md` and `docs/QA.md`.
