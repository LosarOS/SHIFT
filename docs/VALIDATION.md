# Фактическая валидация 1.0.0-rc.1

2026-10-21 · Linux x64 / Debian 13 · .NET SDK 10.0.402 / runtime 10.0.13.

## Выполнено

| Проверка | Результат |
|---|---|
| Unit / integration tests | **63 passed, 0 failed, 0 skipped** |
| C# WinUI Compile target против Windows references | **0 warnings, 0 errors** |
| Locked NuGet restore | Успешно; lock-файлы для всех проектов |
| Dependency audit с transitive dependencies | Известных advisory по текущим NuGet sources нет; NU1901–NU1904 — ошибки restore |
| Source hygiene | Не найдены совпадения credential patterns и tracked build/data files; не исчерпывающий scanner |
| PowerShell syntax | Build-Release, Smoke-Windows, Smoke-Installer разобраны parser без ошибок; Windows actions не выполнялись |
| YAML / XML / JSON / Markdown links | Проверены локально |
| Self-contained initialization | Effective BootstrapInitialize пусто, UndockedRegFreeWinRTInitialize=true; ошибочное принудительное bootstrap удалено |
| Synthetic data / queue diagnostics | Успешно; изолированные 100 spaces / 2000 items / 1000 runs / 20000 run items; ничего реально не запускается |

## Измерения ядра

[Машиночитаемый результат](validation/core-diagnostics-linux.json). Округление до 0.1 мс:

| Операция | Измерено |
|---|---:|
| Сохранение 100 пространств × 20 элементов | 178.6 мс |
| Чтение этих 100 пространств | 27.3 мс |
| Сохранение 1000 запусков × 20 результатов | 911.9 мс |
| Чтение страницы истории из 30 записей | 41.8 мс |
| Статистика всей истории | 3.4 мс |
| Online backup | 8.6 мс |
| Синтетический dispatch 20 шагов с durable progress | 29.5 мс |

Это конкретный Linux-host run на временных данных, не benchmark WinUI startup/rendering или Windows process launch. Память 69.8 MiB в JSON относится к console harness, не к desktop-приложению. Числа не являются performance SLA.

## Покрытие

Workspace CRUD/update/clone/tags/cascade; preserved history; settings and invalid-settings refusal; online backup; unknown-schema refusal; interrupted-run recovery. Ordering, delay/cancel, disabled items, partial failures, concurrent-launch guard, consent approved/declined and fatal persistence failure. JSON fresh IDs, malformed/null/duplicate/oversized fields, bounded UTF-8 streams/BOM/invalid encoding. URL/path restrictions, environment expansion guards, reserved names, canonical shell URL escaping, separate app arguments, explicit visible PowerShell/cmd requests. History Unicode queries, pages, filtering, full counts; restore success/safety backup, invalid schema/invalid file/cancellation preserving original data; 40 simultaneous serialized saves; CurrentUserOnly instance signal.

Дополнительные регрессии 2026-10-21: restore читает committed WAL страницы выбранной базы; неверные GUID, даты, типы, статусы и имена отклоняются до замены текущих данных. Проверяется история за первой страницей (201 запись), отсутствие временных файлов после отказа и запрет экспорта JSON сверх лимита собственного импорта. Исходные реализации воспроизвели потерю WAL-изменений и приём повреждённых значений; после исправлений тесты проходят. UI guards, нормализация порядка новых элементов и блокировка повторных нажатий проверены компиляцией; их интерактивная Windows-проверка ещё нужна.

## Воспроизведение

```sh
dotnet restore SHIFT.sln --locked-mode
dotnet test tests/Shift.Tests -c Release --nologo --logger trx --results-directory TestResults
dotnet list SHIFT.sln package --vulnerable --include-transitive
python tools/audit_source.py
dotnet run --project tools/Shift.Diagnostics -c Release -- --output artifacts/core-diagnostics.json
```

Linux-only reference compilation (не runnable Windows package):

```sh
dotnet restore src/Shift.App --locked-mode -p:WindowsAppSDKSelfContained=false -p:SelfContained=false
dotnet build src/Shift.App --no-restore -t:Compile -p:WindowsAppSDKSelfContained=false -p:SelfContained=false
```

## Не выполнено — обязательные release gates

- Полный Windows self-contained publish с MakePri/native manifest tooling.
- Подготовленные UI Automation smoke на portable и installed build; настоящие screenshots.
- Inno installer / update / uninstall на чистой Windows VM, startup и prerequisites.
- Windows 10/11, Narrator, high contrast, DPI, text scaling, shutdown/logoff, GUI performance.
- Signing / SmartScreen policy и фактические проверенные дистрибутивы.

Поэтому статус — RC, не подтверждённая stable 1.0. Скрипты в CI не считаются выполненными только потому, что написаны.

## GitHub

Первоначальное создание через API получало **403 Resource not accessible by integration**. 2026-10-23 владелец создал пустой [LosarOS/SHIFT](https://github.com/LosarOS/SHIFT); доступ к нему и права ADMIN подтверждены. Credentials сохранены вне дерева проекта и не входят в исходники.

Актуальные удалённые проверки публикуются в [GitHub Actions](https://github.com/LosarOS/SHIFT/actions). Этот документ фиксирует локальные результаты; наличие workflow не означает успешного Windows run. Дальнейшие release gates: Windows CI → диагностика/исправление фактических ошибок → package hashes → manual QA → stable tag/release. На Windows команда `pwsh ./packaging/Build-Release.ps1 -Version 1.0.0-rc.1` выполняет tests/publish/portable UI smoke/package; отдельный `Smoke-Installer.ps1` предназначен только для disposable GitHub runner.
