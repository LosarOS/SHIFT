# Фактическая валидация 0.1.0

Дата: 2026-10-20. Среда: Linux x64; .NET SDK **10.0.402**. Итоговый target: **.NET 10 LTS** / WinUI 3 / Windows App SDK 1.8.260921001. Исходный проект первоначально проверялся на .NET 8, затем переведён на .NET 10 из-за окончания поддержки .NET 8 10 ноября 2026 года.

## Выполнено

| Проверка | Результат |
|---|---|
| NuGet restore всех 5 проектов | Успешно, lock-файлы созданы и проверены locked restore |
| Release core / integration tests на .NET 10 | **32 passed, 0 failed, 0 skipped**, последний прогон 591 мс |
| WinUI C# Compile target против Windows reference assemblies | Успешно: **0 warnings, 0 errors** |
| Dependency audit всего solution, включая transitive | По текущим NuGet advisory sources известных уязвимостей не найдено |
| PowerShell syntax, Build-Release.ps1 и Smoke-Windows.ps1 | Успешно, разобраны PowerShell Parser 7.4.6; Windows-действия не выполнялись |
| GitHub workflow YAML / MSBuild XML / example JSON | Синтаксически корректны |
| Ссылки локальной Markdown-документации | Проверены |

В ходе работы исправлена обнаруженная High advisory `GHSA-2m69-gcr7-jv3q`: native SQLitePCLRaw 2.1.6 заменена на 2.1.14. Итоговый Microsoft.Data.Sqlite — 10.0.13; lock-файлы закрепляют зависимости и content hashes. Отсутствие известных advisory не является доказательством отсутствия любых уязвимостей.

## Воспроизводимые команды

```sh
dotnet restore SHIFT.sln --locked-mode
dotnet test tests/Shift.Tests -c Release --no-restore --nologo --logger trx --results-directory TestResults
dotnet list SHIFT.sln package --vulnerable --include-transitive
```

Дополнительная **проверка компилятора**, использованная в Linux:

```sh
dotnet restore src/Shift.App/Shift.App.csproj --locked-mode -p:WindowsAppSDKSelfContained=false -p:SelfContained=false
dotnet build src/Shift.App/Shift.App.csproj --no-restore -t:Compile -p:EnableWindowsTargeting=true -p:WindowsAppSDKSelfContained=false -p:SelfContained=false
```

Эта команда проверяет C# и ссылки. Она **не создаёт проверенный runnable Windows package**. Она намеренно не выполняет Windows resource/publish pipeline.

## Проверенное поведение ядра

SQLite roundtrip, update, clone, cascade delete with retained history; settings and online backup; newer-schema refusal; interrupted-run recovery. Queue ordering, disabled items, partial failure, consent decline/approval, disabled command behavior, cancellation before dispatch and inside delay, concurrent-run rejection, fatal persistence failure stopping dispatch. Import roundtrip/fresh IDs, malformed/null/oversized/unsupported JSON, bounded UTF-8 stream import with BOM, invalid encoding. HTTP/HTTPS-only URLs, script/interpreter type guards, expanded environment target checks, ambiguous Win32/ADS path refusal and accent validation.

## Ещё не выполнено

- Запуск WinUI на Windows 10/11, визуальная проверка и UI Automation smoke.
- Полная Windows self-contained сборка с native resource tooling; portable ZIP и Inno Setup EXE.
- Чистая Windows VM без SDK/runtime, install/update/uninstall, startup/tray/notifications/pickers.
- Narrator, high contrast, DPI, keyboard-only UX, performance targets.
- Code signing, SmartScreen/reputation policy, настоящие screenshots.
- Публикация GitHub-репозитория или release; здесь создан локальный исходный репозиторий.

В Linux при попытке полного WinUI build выявлено ограничение Windows tooling: self-contained MSIX manifest extraction и запуск `MakePri.exe` требуют Windows. Ошибки C# проверены отдельно и устранены. Никакие полученные здесь промежуточные DLL не выдаются за готовый Windows-дистрибутив.

## Следующий release gate

На Windows x64 с .NET 10 SDK, Windows Build Tools и Inno Setup 6:

```powershell
pwsh ./packaging/Build-Release.ps1 -Version 0.1.0
```

Сценарий делает locked restore, tests, self-contained publish, UI Automation smoke на disposable portable-профиле, portable ZIP, installer и SHA-256. В smoke: onboarding → create workspace → save/detail → Settings/save/rebuild. Затем вручную пройти `docs/QA.md` и обновить этот отчёт. Только после этого публиковать public beta.
