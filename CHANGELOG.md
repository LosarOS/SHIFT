# Changelog

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

### Validation

52 core/integration tests passed locally; Windows release gates are pending. See docs/VALIDATION.md.


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
