# Архитектура SHIFT

Описание реализации RC8 на 2026-11-15. Текущая схема SQLite — **3**. Это документ архитектуры, а не заявление о прохождении финальной Windows-приёмки; результаты конкретных исходных ревизий фиксируются в [VALIDATION](VALIDATION.md) и инженерных отчётах.

## Решение

Clean architecture / modular monolith. .NET 10 LTS, WinUI 3, Windows App SDK 1.8, SQLite через Microsoft.Data.Sqlite. Нет контейнеров, сервисов, backend или браузерного UI. Явный composition root в `MainWindow` выбран вместо DI-контейнера для небольшого набора сервисов; зависимости создаются в одном месте.

Направление compile-time зависимостей:

```text
Shift.App ──────────> Shift.Infrastructure ──> Shift.Application ──> Shift.Domain
    └──────────────────────────────────────> Application / Domain
```

Infrastructure реализует интерфейсы Application, Application не знает о SQLite/WinUI. Окружение Windows проверяется внутри launch adapter; тестируемая логика работает на Linux.

## Слои и contracts

| Слой | Ответственность |
|---|---|
| Domain | Records, enums, проверка допустимого workspace, безопасное дублирование |
| Application | IWorkspaceStore, IRunStore, ISettingsStore, IItemLauncher, ICommandConsent, IAppLog; LaunchCoordinator; WorkspaceTransfer; WorkspaceComposer; WorkspaceLaunchPlanner; IWorkspaceDoctor; IBackupCatalog; Studio services |
| Infrastructure | SqliteStore + LocalDataStore (serialized background access), versioned initialization, transactions, backup/restore, LaunchRequestFactory, WindowsItemLauncher, SingleInstanceSignal, FileLog; BackupCatalog; Windows adapters Studio |
| Presentation | ShellViewModel: снимки списка/истории, запрос поиска, selection, settings, текущая очередь, cancel token; MainWindow: native controls и dialogs; DesktopIntegration: tray/registry |

UI строится программно из WinUI controls. `MainWindow.cs` — shell и общие helpers; `Views/Pages.cs` — разделы, `Views/Dialogs.cs` — редакторы и взаимодействие с pickers. Обновления состояния происходят в UI synchronization context. Изменения базы завершаются до перезагрузки view model; временные поля редакторов не меняют сохранённый workspace до Save. Команды UI вызывают view model / application, launcher напрямую не доступен пользователю.

Текущий MVVM облегчённый: `INotifyPropertyChanged` есть в ShellViewModel, представление явно обновляет visual tree по завершении операции и progress. В нём нет XAML bindings / generator commands. Это облегчает первый WinUI срез, но до масштабирования истории следует перейти к observable row view models и incremental updates; существующие слои ядра останутся прежними.

## Основные таблицы — историческое ядро schema v1

Следующие таблицы появились в v1 и сохранены в текущей схеме 3. Это не перечень всех таблиц Studio и не текущее значение `user_version`; расширения и миграция описаны ниже.

| Таблица | Поля и связи |
|---|---|
| Workspaces | Id GUID PK; Name, Description, Color, IsFavorite, CreatedAt, UpdatedAt |
| WorkspaceItems | Id GUID PK; WorkspaceId FK CASCADE; Name, Kind, Target, Arguments, WorkingDirectory, Shell, SortOrder, DelayMs, Enabled, Note, Icon |
| Tags | Name PK COLLATE NOCASE |
| WorkspaceTagLinks | WorkspaceId FK CASCADE + TagName FK; composite PK |
| LaunchRuns | Id GUID PK; WorkspaceId snapshot (не FK), WorkspaceName snapshot; StartedAt, FinishedAt, Status |
| LaunchRunItems | RunId FK CASCADE + ItemId snapshot PK; Name, Kind, SortOrder, Status, Error, StartedAt, FinishedAt |
| AppSettings | singleton Id=1, version-bound JSON settings |

History deliberately retains names/types but not full target/command text. Deleting a workspace does not delete its launch history. Tags are deduplicated and orphan tags cleaned. GUIDs serialized as text, timestamps UTC ISO 8601, UI shows local time. Enum numeric values in SQLite are part of schema compatibility; never reorder them without migration.

SQLite: foreign keys enabled per connection, WAL, transactions for workspace + items + tags, transactions for run snapshot, read transactions for consistent history/workspace reads. Current `PRAGMA user_version=3`; versions above3 fail safely. The historical RC1/RC2 core used schema1, Studio RC3–RC7 used schema2. Backups use SQLite BackupDatabase, not a raw copy of an active WAL database.

## Текущая schema3 и совместимость Studio

Studio добавляет StudioProfiles, StudioWallpapers, StudioDefinitions, StudioInstances, StudioWidgetState, StudioSettings и StudioRecovery; связи и ответственность — в [Studio architecture](studio/ARCHITECTURE.md). `WidgetKind` сохраняет числовые значения Clock=0, SystemMonitor=1, FocusTimer=2, Notes=3; QuickActions добавлен в конец как4.

Schema2 ограничивала `StudioDefinitions.Kind` значениями0–3. RC8 не меняет существующие GUID/revision/layout/state: миграция v2→v3 пересоздаёт только таблицу определений с `CHECK(Kind BETWEEN 0 AND 4)`, копирует строки и проверяет foreign keys до commit. Для перестройки FK enforcement временно выключается вне транзакции и гарантированно возвращается в `finally`; внутри транзакции выполняется явный `foreign_key_check`. `user_version=3` записывается в той же транзакции. При ошибке таблица и версия откатываются.

Перед изменением v1/v2 создаётся online SQLite snapshot в `<data>/migration-backups`, он проходит validation. Ошибка backup/validation блокирует миграцию до изменения схемы. V1 получает Studio-таблицы сразу с текущими ограничениями; пустая новая база создаётся какv3. Непустая база с `user_version=0` отклоняется. Старый RC7 отказывается открывать schema3: downgrade требует отдельной сохранённой schema2-копии, а не открытия новой базы старым EXE.

Политики сценария находятся в version-bound JSON `AppSettings.WorkspaceScenarios`, а не в новых колонках Workspaces. Отсутствующее поле означает legacy Off/Continue. Карта ограничена, проверяет IDs/enums; импорт/clone создают новые IDs и не наследуют локальную политику. Сами политики не дают согласие на команды.

## Запуск и состояния

- `SemaphoreSlim` rejects a second concurrent launch. A named process mutex prevents concurrent instances for the same data directory.
- Validate workspace/policy → immutable snapshot → single-run gate → persist Running run with Pending items.
- Optional Workspace Doctor runs before consent/dispatch, against that snapshot. Observations are persisted before continuing. Off retains legacy behavior; Continue records observations; StopOnErrors rejects dispatch if an enabled item is Error; SkipErrors skips those items. Warning/Unknown do not imply readiness and are not treated as Error.
- Confirm the enabled commands remaining after skip filtering as one review → items by order / stable ID tie break. Malformed/mismatched Doctor results interrupt the run without dispatch.
- Disabled → Skipped. Otherwise delay → persist Running → dispatch via OS → persist Success or Failed.
- LaunchErrorPolicy.Continue retains the historical continue-after-failure behavior; Stop marks remaining steps Skipped after the first dispatch failure. There are no automatic retries. Preflight rejection/skipping is PartialFailure; normal completion is Completed / PartialFailure.
- Cancellation → Cancelled; pending/running steps become Skipped. Adapter checks token immediately before Process.Start and does not throw cancellation after dispatch.
- Declined consent means no element launches, including earlier non-command elements; entire run Declined.
- Fatal orchestration / persistence failure → Interrupted where storage is writable; propagate to UI. If persistence is unavailable, stop further dispatch and recover the previously persisted running state on next startup.
- On startup unfinished runs become Interrupted, unfinished items Skipped with uncertainty note. **Never automatically rerun** an interrupted command.

Есть неизбежное окно: Windows уже приняла запуск, но процесс SHIFT завершился до записи Success. Восстановление отмечает неопределённость, а не делает повторный запуск. Exactly-once delivery для чужих desktop-приложений невозможна без их участия.

The launch plan is a read-only projection of ordering and configured enabled delays, not execution/readiness measurement. Composer reviews bounded format-only batches and stale snapshots before appending disabled-by-default items. Both are separate from dispatch. Linked Studio auto-apply requires an actual started item; a preflight rejection with no dispatch attempt must not change the desktop.

## OS adapter / доверие

App supports .exe/.lnk with arguments; file and folder use registered Windows handlers; URL only HTTP/HTTPS. Interpreter EXEs should use explicit Command type; validation blocks common interpreters in App mode and script extensions in File mode. This is a type guard, not an allowlist or malware detector.

PowerShell uses explicit System32 Windows PowerShell with `-NoProfile -Command`; cmd uses explicit System32 cmd with `/d /s /c`. No execution-policy bypass, elevation, hidden window, downloaded script or profile load. Commands can themselves start other processes or perform arbitrary actions; consent covers their full text, not a promise of safety.

Imported workspaces receive new IDs; validation runs before import, storage and launch. Relative / UNC / device targets are rejected for file-based types. Expanding Windows env vars occurs at runtime; a missing path still becomes a per-item history error. Paths are unquoted in model; separate arguments exist only for App.

## Настройки / файлы

Installed data `%LOCALAPPDATA%\SHIFT`; portable mode requires `portable.flag` next to EXE and writes `data\`. Repeated launches signal ACTIVATE through a bounded CurrentUserOnly named pipe, then exit. The first instance restores and activates its existing window. No workspace command is accepted over IPC.

Automatic backup occurs once per UTC day on startup, retained last 10 under `<backup directory>/automatic`. Manual backups are not pruned. Restore uses SQLite online backup to create a private, self-contained snapshot including committed source WAL pages. It validates integrity, schema columns, settings, foreign keys, workspace models and every page of history before changing live data; creates a safety backup, checkpoints the active WAL and atomically replaces the database. Before acceptance, backup validation rejects triggers/views, unsupported versions, missing schema columns, invalid settings/models and broken foreign keys; Studio model/state/journal relationships are checked for supported Studio versions. Restoring v1/v2 upgrades and validates the private snapshot before safety backup and live replacement. Private validation connections are read-only and unpooled; snapshot sidecars are cleaned on success and refusal. LocalDataStore serializes storage operations. The UI refuses restoration while earlier actions are in flight, then blocks new actions during maintenance. Backup failure is visible and does not block opening the app. Import/export limit 2 MB; export validates UTF-8 size before the picker, writes a temp file on the destination filesystem, then replaces the target.

Recovery Center uses `IBackupCatalog` for bounded discovery (up to100entries) and inspection through a private SQLite snapshot, then delegates explicitly confirmed replacement to the existing restore path. Inspection/cancel does not replace the live database or execute targets. Preview is not a reusable restore approval: the file is revalidated before replacement. Unavailable locations and truncated lists are shown; files can also be selected manually.

A SQLite backup includes workspaces/history/settings/Studio configuration and saved widget state. It does **not** bundle `Studio/Images`, external wallpapers or `Studio/RecoveryNotes`; profile-pack JSON is also not a full backup. A full resource backup means Quit and copy the complete data directory separately. Migration copies under `<data>/migration-backups` can be selected manually; catalog discovery is bounded to the chosen backup root and its documented child directories, not arbitrary recursive filesystem search.

Logging is local, day-based, max 5 MB/day, 14 files; no automatic target/argument logging. OS exception messages can still contain local paths. Unexpected unhandled UI exceptions are logged and allowed to terminate the process; marking arbitrary exceptions handled would risk corrupt state.

## Extension seams

| Будущая возможность | Новый boundary / хранение |
|---|---|
| Session capture | IEnvironmentCapture, CapturedWindow records, explicit preview before save |
| Window layouts | IWindowLayoutService, display topology + DPI metadata, retry after window discovery |
| Existing hotkeys / compact | WindowCommands and compact UI already invoke the coordinator; future triggers must preserve the same consent |
| Scheduling | IScheduleService, persisted Schedule, missed-run policy; no silent command approvals |
| App readiness | ILaunchReadinessProbe by adapter; separate dispatched vs ready states |
| Browser integration | Optional extension with explicit permission, no extraction of browser profile secrets |
| Recommendations | Local query over history, no cloud calls or background capture |
| Parallel launch | Explicit execution groups + bounded concurrency; maintain cancel and persistence semantics |

## Background storage and bounded presentation

SQLite APIs are synchronous internally. `LocalDataStore` puts the complete operation on a worker and serializes it through a semaphore, preserving ordering without blocking WinUI. Connections and transactions are owned by each operation. UI progress is reported inline on the coordinator caller's synchronization context, avoiding stale queued callbacks after completion.

History is paged (30 rows by default) with Unicode casefold search and failure filtering. Overview uses independent recent runs and full-database aggregate counts. Explicit history clearing retains Running records. The compact launcher and hotkey use the same coordinator and consent flow.

`WindowCommands` owns the current window's native hook, minimum dimensions and opt-in Ctrl+Alt+Space registration. High contrast uses system foreground/background, animations respect system preferences, and initial window sizing is clamped to the current display/DPI. These runtime behaviors still require the Windows matrix.

Known limits: view updates still rebuild native control trees; performance of large workspace-card lists and assistive technology remains a manual gate. Per-item Icon metadata is reserved; the UI uses built-in glyphs. Commands record dispatch, not completion/exit codes. Automatic scheduling, browser session capture and layout restoration remain outside 1.0.

## Выбор LTS runtime

Проект использует .NET 10 LTS в рамках требования .NET 8+. Поддержка .NET 8 заканчивается 10 ноября 2026 года, поэтому для нового приложения осенью 2026 выбран более длительный горизонт поддержки .NET 10. Слои и WinUI 3 остаются прежними. Обновление зависимостей подтверждается core tests и отдельной Windows QA.

## Workspace Doctor

`IWorkspaceDoctor` / `WorkspaceDoctor` owns a cancellable read-only pass over the saved item snapshot and reuses `WorkspaceValidator`. `IItemInspection` is the infrastructure boundary; `LocalItemInspection` performs filesystem/access/PE-header/shortcut-header and Windows association observations on a worker thread. It never depends on `IItemLauncher`, a process executor or HTTP client.

Mapped network drives, reparse points (including ancestors), offline/cloud objects and unresolved shortcut targets are reported as unverified instead of being followed. The scan has bounded item counts and executable-header offsets. Command text is never parsed as trusted code; consent remains mandatory in the existing LaunchCoordinator. Arguments are checked only against the supported data model.

The WinUI dialog owns scan cancellation and waits for its current task on closure. Results are temporary observations, not persisted safety approvals. LaunchRequestFactory independently revalidates the path at dispatch time; a successful observation cannot authorize a removed/replaced file. Closing the dialog or selecting an editor never launches an item.
