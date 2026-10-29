# Validation — SHIFT RC5

**VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS.** [Russian report, downloads, SHA256, screenshots and raw evidence](../reports/SHIFT-RC5-VALIDATION-RU.md). Tagged binary source `176c27040929c37058354e35aa147f5634a204ec`; **375 passed,0failed,0skipped**. [Canonical tag workflow](https://github.com/LosarOS/SHIFT/actions/runs/40968079932) passed native portable/Studio/sharing/file-picker/clipboard/installed/installer preservation gates. All six public assets were independently downloaded and matched their API digests; binary hashes matched SHA256SUMS. No stable1.0 or physical Windows10/11/DPI/accessibility acceptance is claimed.

[Real screenshots](screenshots/rc5/README.md) · [Uncompleted manual acceptance protocol](studio/RC5-MANUAL-ACCEPTANCE-RU.md) · [Engineering history](../reports/SHIFT-RC5-ENGINEERING-SESSION.md).

## Historical RC4 validation

# Validation — SHIFT RC4

**VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS.** [Russian report, downloads, exact SHA256 and repeated original-byte proof](../reports/SHIFT-RC4-VALIDATION-RU.md). Tagged binary source `ebcae08bbe4c66bf1aece9bbc811cc706fbe3c99`;296tests and native tagged gates passed. Two extra full native passes used the unchanged downloaded public binaries with corrected UIA locale selection. Physical Windows/accessibility/multi-monitor/sleep acceptance remains outstanding.

## Historical RC3 validation

# Validation — SHIFT Studio RC3

**VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS.** [Final Russian report and public-byte proof](../reports/SHIFT-STUDIO-VALIDATION-RU.md). Tagged source `dd9d047d17f395902173c7231d2b2a59fdb843d4`; release and main workflows passed. Physical Windows/accessibility gates remain outstanding.

Current implementation and exact native gate outcomes are tracked in [Studio engineering journal](../reports/SHIFT-STUDIO-ENGINEERING-SESSION.md) and [Studio QA matrix](studio/QA-MATRIX.md). The older report below is retained as historical RC1 evidence; its pending Windows gates are not the current RC2 release status.

- Published RC2 has real portable/installed Windows smoke evidence and remains available.
- Studio preview has190 automated tests,0failed/0skipped locally on Linux; final Windows evidence must reference the matching source revision.
- Windows publish, portable startup, installer smoke, Studio widget/wallpaper recovery and physical Windows acceptance are separate gates.
- Studio native acceptance is disposable-runner-only; it refuses existing data directories and restores wallpaper before deleting owned test assets.
- All binaries are unsigned. Manual Win10/11, DPI, Narrator, sleep/RDP/fullscreen checks remain outstanding.

## Historical RC1 local report

# Published RC2 — final evidence

**VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS** · [Download](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.2).

- Binary/tag source:`7557c6e84732df26e2d1578d2c7662dc058bd870`; release build [40007797394](https://github.com/LosarOS/SHIFT/actions/runs/40007797394) passed all canonical gates and verified downloaded GitHub hashes.
- Core:**110 passed,0failed,0skipped**, Windows and Linux. Real extracted portable ZIP, installed UI, Doctor without dispatch, command consent, restart/persistence, same-version repair/uninstall/reinstall and SQLite preservation passed.
- Parallel main CI exposed a smoke synchronization defect: an existing navigation button did not acknowledge asynchronous settings save. [PR11](https://github.com/LosarOS/SHIFT/pull/11) waits for rebuilt saved controls; no application code changed.
- The unchanged original ZIP/EXE were downloaded and revalidated using corrected scripts, [40008979283](https://github.com/LosarOS/SHIFT/actions/runs/40008979283). Additive proof `WINDOWS-REVALIDATION-40008979283.json` records both binary SHA256 values, original source and verification-script source. [Fixed-script CI40008965322](https://github.com/LosarOS/SHIFT/actions/runs/40008965322) also passed.
- Public setup/portable/checksum links returned **HTTP200**, nonzero expected sizes. Release is public, prerelease=true, unsigned.
- [Six raw native captures](screenshots/rc2/README.md) were reviewed. Environment: disposable Windows Server2025 x64 runner. No physical Windows10/11,100–200% DPI,Narrator/high contrast or signing acceptance is claimed. Stable1.0 remains blocked on these gates.

The following sections preserve earlier session checkpoints; their pending statuses are historical, not the final public RC2 status.

# Current RC2 session — 2026-10-23

[Engineering report](../reports/engineering-session-2026-10-23.md) records the current source and remaining gates. Native Windows [run39955142632](https://github.com/LosarOS/SHIFT/actions/runs/39955142632) at `b900ab60fa1891008df02c129b52e4d56872eb79` **passed**:

- Locked restore and **110 passed / 0 failed / 0 skipped** core tests, both Windows and Linux.
- Self-contained WinUI publish with the actual application `resources.pri`.
- Real portable and installed UI Automation: onboarding, workspace/item creation, command refusal/approval, benign dispatch, RU/EN, theme, compact search, tray and second-instance activation.
- Inno per-user installation, installed startup, uninstall and registry cleanup.

This proves the native startup blockers are resolved at that source. Later restart, Doctor UI and SQLite-preservation extensions require their own successful run; see the session report and release `WINDOWS-VALIDATION.json` for final evidence. No manual physical-PC Windows10/11, DPI, Narrator/high-contrast or signing acceptance is claimed. Native CI screenshots are captured from the actual running application, not generated renders. Stable1.0 remains blocked on manual acceptance.

The following RC1 section is historical evidence; its outstanding list describes the RC1 checkpoint, not current RC2 results.

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
