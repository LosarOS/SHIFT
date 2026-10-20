# Архитектура SHIFT

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
| Application | IWorkspaceStore, IRunStore, ISettingsStore, IItemLauncher, ICommandConsent, IAppLog; LaunchCoordinator; WorkspaceTransfer |
| Infrastructure | SqliteStore, versioned initialization, transactions, online backup, WindowsItemLauncher, FileLog |
| Presentation | ShellViewModel: снимки списка/истории, запрос поиска, selection, settings, текущая очередь, cancel token; MainWindow: native controls и dialogs; DesktopIntegration: tray/registry |

UI строится программно из WinUI controls. `MainWindow.cs` — shell и общие helpers; `Views/Pages.cs` — разделы, `Views/Dialogs.cs` — редакторы и взаимодействие с pickers. Обновления состояния происходят в UI synchronization context. Изменения базы завершаются до перезагрузки view model; временные поля редакторов не меняют сохранённый workspace до Save. Команды UI вызывают view model / application, launcher напрямую не доступен пользователю.

Текущий MVVM облегчённый: `INotifyPropertyChanged` есть в ShellViewModel, представление явно обновляет visual tree по завершении операции и progress. В нём нет XAML bindings / generator commands. Это облегчает первый WinUI срез, но до масштабирования истории следует перейти к observable row view models и incremental updates; существующие слои ядра останутся прежними.

## Модель данных / schema v1

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

SQLite: foreign keys enabled per connection, WAL, transactions for workspace + items + tags, transactions for run snapshot, read transactions for consistent history/workspace reads. `PRAGMA user_version=1`; higher versions fail safely. Future migration must backup first, run transactionally, be idempotent and add tests. Backups use SQLite BackupDatabase, not a raw copy of an active WAL database.

## Запуск и состояния

- `SemaphoreSlim` rejects a second concurrent launch. A named process mutex prevents concurrent instances for the same data directory.
- Validate workspace → create durable Pending snapshot → confirm all enabled commands as one review → items by order / stable ID tie break.
- Disabled → Skipped. Otherwise delay → persist Running → dispatch via OS → persist Success or Failed.
- Failure of one item does not stop subsequent items. Queue finishes Completed / PartialFailure.
- Cancellation → Cancelled; pending/running steps become Skipped. Adapter checks token immediately before Process.Start and does not throw cancellation after dispatch.
- Declined consent means no element launches, including earlier non-command elements; entire run Declined.
- Fatal orchestration / persistence failure → Interrupted where storage is writable; propagate to UI. If persistence is unavailable, stop further dispatch and recover the previously persisted running state on next startup.
- On startup unfinished runs become Interrupted, unfinished items Skipped with uncertainty note. **Never automatically rerun** an interrupted command.

Есть неизбежное окно: Windows уже приняла запуск, но процесс SHIFT завершился до записи Success. Восстановление отмечает неопределённость, а не делает повторный запуск. Exactly-once delivery для чужих desktop-приложений невозможна без их участия.

## OS adapter / доверие

App supports .exe/.lnk with arguments; file and folder use registered Windows handlers; URL only HTTP/HTTPS. Interpreter EXEs should use explicit Command type; validation blocks common interpreters in App mode and script extensions in File mode. This is a type guard, not an allowlist or malware detector.

PowerShell uses explicit System32 Windows PowerShell with `-NoProfile -Command`; cmd uses explicit System32 cmd with `/d /s /c`. No execution-policy bypass, elevation, hidden window, downloaded script or profile load. Commands can themselves start other processes or perform arbitrary actions; consent covers their full text, not a promise of safety.

Imported workspaces receive new IDs; validation runs before import, storage and launch. Relative / UNC / device targets are rejected for file-based types. Expanding Windows env vars occurs at runtime; a missing path still becomes a per-item history error. Paths are unquoted in model; separate arguments exist only for App.

## Настройки / файлы

Installed data `%LOCALAPPDATA%\SHIFT`; portable mode requires `portable.flag` next to EXE and writes `data\`. Repeated launches of the same data path return early; bringing the first instance to foreground on a second launch is deferred. Tray restore does bring it forward.

Automatic backup occurs once per UTC day on startup, retained last 10 under `<backup directory>/automatic`. Manual backups are not pruned. Backup failure is visible and does not block opening the app. Import/export limit 2 MB; export writes a temp file on the destination filesystem then replaces target.

Logging is local, day-based, max 5 MB/day, 14 files; no automatic target/argument logging. OS exception messages can still contain local paths. Unexpected unhandled UI exceptions are logged and allowed to terminate the process; marking arbitrary exceptions handled would risk corrupt state.

## Extension seams

| Будущая возможность | Новый boundary / хранение |
|---|---|
| Session capture | IEnvironmentCapture, CapturedWindow records, explicit preview before save |
| Window layouts | IWindowLayoutService, display topology + DPI metadata, retry after window discovery |
| Hotkeys / compact | IHotkeyService invokes existing coordinator, same consent required |
| Scheduling | IScheduleService, persisted Schedule, missed-run policy; no silent command approvals |
| App readiness | ILaunchReadinessProbe by adapter; separate dispatched vs ready states |
| Browser integration | Optional extension with explicit permission, no extraction of browser profile secrets |
| Recommendations | Local query over history, no cloud calls or background capture |
| Parallel launch | Explicit execution groups + bounded concurrency; maintain cancel and persistence semantics |

## Known architecture debt

SQLite async APIs execute SQLite work synchronously internally. Current bounded MVP datasets are acceptable for functionality, but UI start/scroll performance is unmeasured. Before beta, measure 100+ workspaces; move repository orchestration to a serialized background executor if needed. History currently displays latest 100 runs, without retention deletion or pagination. Per-item custom Icon field is reserved; UI uses built-in glyphs rather than remote or executable icon extraction.

## Выбор LTS runtime

Проект использует .NET 10 LTS в рамках требования .NET 8+. Поддержка .NET 8 заканчивается 10 ноября 2026 года, поэтому для нового приложения осенью 2026 выбран более длительный горизонт поддержки .NET 10. Слои и WinUI 3 остаются прежними. Обновление зависимостей подтверждается core tests и отдельной Windows QA.
