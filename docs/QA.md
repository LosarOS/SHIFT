# QA / release gate

## Автоматические проверки

`dotnet test tests/Shift.Tests -c Release`: domain constraints, unsafe URL/path types, malformed/oversized/null JSON, fresh IDs on import/clone; SQLite CRUD/update/cascade/history, settings/online backup, future schema rejection, interrupted-run recovery; queue ordering/partial failure/disabled/consent refusal/cancellation/concurrency.

Windows CI должен восстановить NuGet по lock-файлам, скомпилировать и publish self-contained приложение, выполнить `Smoke-Windows.ps1` (onboarding → создание → сохранение → перестроение оболочки после Settings), собрать Inno installer и portable ZIP. Успешный cross-compile не заменяет interactive smoke.

## Ручная матрица (до public beta)

Для каждой проверки записать OS build, package version/hash, DPI, результат и ссылку на issue.

- [ ] Windows 11 clean x64 VM: installer без .NET / App SDK preinstall, startup, onboarding, full exit.
- [ ] Windows 10 22H2 clean x64 VM: тот же сценарий, контраст и native handlers.
- [ ] Portable: запуск из папки с кириллицей/пробелами, перенос, отсутствие Run key, путь без права записи.
- [ ] Первый запуск: demo / empty; второй запуск не повторяет onboarding.
- [ ] CRUD: новое, metadata, clone, tag search, favorite, delete с сохранением истории, restart persistence.
- [ ] Item editor: EXE, .lnk, folder, PDF/file, URL, PowerShell, cmd; args с кавычками/пробелами; рабочая папка.
- [ ] File/folder picker from dialog, cancel picker, focus restoration; JSON save-picker and replacement.
- [ ] Launch: порядок и задержки 0 / 300 / 300000; disabled skipped; missing path error; остальные продолжаются.
- [ ] Command consent: полный текст/оболочка, отказ не запускает ничего; повторный запуск снова спрашивает.
- [ ] Cancel во время задержки, между элементами; ранние успехи остаются; приложения не закрываются.
- [ ] Второй Launch и второй process: нет конкурирующей очереди / записи.
- [ ] Убить SHIFT после Running; restart отмечает Interrupted и не запускает автоматически.
- [ ] Закрытие в трей; restore; уведомление; явный Quit во время очереди; Windows shutdown/logoff.
- [ ] Autostart enable/disable: HKCU Run, Windows sign-in, task manager toggle; uninstall removes Run key.
- [ ] Settings: 4 темы × RU/EN, persistence, tray false, notifications false, backup folder failure.
- [ ] Import: valid with review, invalid JSON/schema/fields, >2 MB, malicious URL, command review.
- [ ] Export → import сохраняет значения, новые IDs; отказ экспорта не портит старый файл.
- [ ] Backup while app active, restore по инструкции, automatic retention, invalid/newer DB fail safely.
- [ ] Disk full, read-only DB/data folder, denied registry: читаемая ошибка, никакого скрытого сброса.
- [ ] UI: 960/1240/1920 ширина, DPI 100/150/200%, multi-monitor, high contrast, text scaling.
- [ ] Keyboard only: navigation, search, create/edit, reorder, command consent, cancel; Narrator names.
- [ ] Performance: cold startup, 100 workspaces × 20 items, latest 100 history runs; record timings/memory.
- [ ] Installer upgrade/uninstall: no locked files, no data loss, shortcut paths correct, no registry leftovers.
- [ ] ZIP/EXE checksums, absence of sample private paths/secrets, license and release notes.
- [ ] Signing decision documented; dependency vulnerability scan; screenshots captured on real Windows.

## Публичный релиз

1. Зеленый Windows CI и core matrix.
2. Устранены critical/high severity issues по матрице.
3. Создать tag `vX.Y.Z`, получить draft artifacts; проверить install/portable ещё раз по SHA-256.
4. Заполнить фактический VALIDATION, добавить настоящие screenshots, обновить changelog и release notes.
5. Подписать и пересчитать checksums, если используется signing. Ключи не хранить в репозитории.
6. Опубликовать draft вручную. Собирать обратную связь через GitHub issues без телеметрии.
