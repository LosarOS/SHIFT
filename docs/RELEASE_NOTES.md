# SHIFT 1.0.0-rc.1

Native, local-first Windows workspace manager. This candidate extends the initial MVP with a compact launcher, opt-in global hotkey, background persistence, complete usage statistics, paged history and safe backup restoration.

## Main capabilities

Workspace CRUD, clone, tags, favorites, search, sorting and per-item launch order/delays. Apps, shortcuts, folders, files, HTTP(S) and explicit PowerShell/cmd commands. Visible queue, cancellation, failure isolation, command consent on every run and durable history. No account, backend, telemetry or paid APIs.

WinUI interface with RU/EN and four themes, tray, notifications, installed startup, second-instance activation. Ctrl+K compact search, ↑/↓ selection, Enter dispatch; opt-in Ctrl+Alt+Space. JSON import/export and verified SQLite restoration with a safety copy.

## Quality work

52 local automated checks cover core behavior, SQLite, cancellation, command consent, backup restore, URI escaping, history statistics/paging, null/duplicate JSON and IPC. Dependencies are locked and audited; CI actions are pinned. Windows automation scripts cover real approved dispatch, refusal, theme/language, compact view, tray/activation and installed/uninstall flow, and preserve diagnostics.

## Current status

Release candidate. C# reference compilation and Linux core checks are complete. Windows build/UI/installer, clean-machine validation and accessibility/performance/signing gates must pass before stable 1.0. See `docs/VALIDATION.md`; prepared tests are not reported as executed.

## Limits

Success means Windows accepted dispatch, not app readiness or a command exit code. Cancelling leaves open programs running. Capture of arbitrary sessions, window layouts and schedules remain roadmap features. Data/exports/backups are unencrypted. Trust executable content explicitly; SHIFT is not a sandbox. Binaries are unsigned until a signing method is configured.
