# План развития

## Итерации первого прохода

1. **Фундамент:** solution, domain records, validation, PRD, architecture and stable contracts.
2. **Данные и запуск:** SQLite schema, transactions, JSON, history, launch sequence, cancellation, crash recovery and tests.
3. **Desktop slice:** native WinUI shell, all 5 sections, editors, queue, onboarding, localization, themes and Windows adapters.
4. **Release engineering:** CI, publish script, portable, installer, checksums and docs.
5. **Windows acceptance:** build on real Windows, install and launch smoke, QA matrix, fix runtime issues, measure performance.

Первые 4 представлены кодом в 0.1.0. Пятый — обязательный gate перед public beta; его нельзя заменить тестами на Linux или проверкой C# reference assemblies.

## MVP → engineering preview 0.1

CRUD, 5 item types, tags/favorites/search, sequential launch and cancel, command consent, durable history, import/export/backup, RU/EN, themes, tray, per-user startup. All UI calls real services; no fake workspace storage.

## Beta 0.2–0.4

- Windows 10/11 real-device QA, install/update/uninstall verification, unsigned signing policy.
- Fix all release-blocking issues from checklist; test crash, disk-full, readonly and multi-monitor scenarios.
- Measure cold start and large dataset behavior; serialized storage executor if needed.
- Incremental UI updates, responsive layout polish, high contrast / Narrator, text scaling.
- Portable move tests, imported missing-path repair, preview of environment expansion.
- App shortcuts and custom icon UI; explicit multi-file import/export options.
- Bring running instance forward on second invocation.
- Typed error codes and complete localized validation messages.

## v1.0

- Global hotkeys and compact launcher using the same launch coordinator and command consent.
- Strong release reproducibility, dependency maintenance, signed downloads when signing is arranged.
- App readiness probes for known adapters, command exit tracking as a separate opt-in mode.
- History pagination, retention controls, local summary metrics; no telemetry.
- Guided backup restore with pre-restore backup and restart.

## Future

- Explicit capture of windows/apps/folders, browser extension for opt-in URLs.
- Layout presets with DPI/monitor topology and restoration retries.
- Previous session recovery as a proposal for user approval, never blindly rerun commands.
- Scheduled routines with visible missed-run policy and security model.
- Bounded parallel launch groups and dependency steps.
- Local recommendations, quick-launch widgets and usage analytics.

Не планируются как обязательные зависимости: облако, API-ключи, online аккаунты, платные AI-сервисы, Windows service с повышенными привилегиями.

## Реализовано в 1.0 candidate

- Compact launcher с поиском, выбором ↑/↓, Enter/Esc, Ctrl+K и opt-in Ctrl+Alt+Space.
- Activation существующего экземпляра, восстановление из трея.
- Background SQLite access, full-data usage, сортировка и tag filter.
- History pagination/search/failure filter/retry/clear.
- Backup restore с validation и safety copy.
- Typed validation RU/EN, system high contrast/reduced motion handling, DPI-aware initial window size.
- Более полный Windows UI/installer smoke с diagnostics/screenshots и source hygiene gate.

Код этих возможностей включён в RC. «Реализовано» не означает «подтверждено на Windows»; текущая валидация фиксируется отдельно. Stable 1.0 требует закрытия release gates, а session capture/layouts/schedules остаются следующими версиями.
