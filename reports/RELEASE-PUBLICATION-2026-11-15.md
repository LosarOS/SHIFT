# SHIFT 1.0.0-rc.8 — публикация и независимая проверка

**Release status: VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS.**

Владелец явно подтвердил публикацию. Выпуск опубликован как unsigned prerelease, не stable1.0.

- [Публичный релиз](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.8); `draft=false`, `prerelease=true`.
- Публикация: `2026-11-15T08:51:44Z`. Независимая повторная загрузка закончена `2026-11-15T08:52:34.911759+00:00`.
- Source commit и проверенная цель тега `v1.0.0-rc.8`: **`e8b432cab4c6007389e84f1e88f8fc7b950325d5`**.
- [Windows workflow](https://github.com/LosarOS/SHIFT/actions/runs/43695742372). Полные source tests не пропускались.
- Все приложенные assets повторно скачаны **после** публичной публикации. Для каждого сверены ненулевой размер и GitHub asset digest; для бинарников — опубликованный SHA256SUMS. ZIP проверены по CRC и безопасным именам записей; установщики имеют PE/MZ header. Это проверка целостности, не антивирусная сертификация.
- [Машинный результат](evidence/published-2026-11-15/independent-published-verification.json) · [метаданные Releases](evidence/published-2026-11-15/release-metadata.json) · [SHA256SUMS](evidence/published-2026-11-15/SHA256SUMS.txt).

## Скачивание и SHA256

| Файл | Байт | SHA256 |
|---|---:|---|
| [SHIFT-1.0.0-rc.8-win-x64-portable.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.8/SHIFT-1.0.0-rc.8-win-x64-portable.zip) | 106674298 | `09a0673fd0e3b7aac7de2eee394ce2c74dc16588048fc00ebb6c54afed304ce7` |
| [SHIFT-1.0.0-rc.8-win-x64-setup.exe](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.8/SHIFT-1.0.0-rc.8-win-x64-setup.exe) | 72617500 | `0fcc17c3b074533aa96564312af9a82aed72b7fcf8a0694d79a340dffc438501` |

Хеши относятся к свежей опубликованной пересборке; хеши предыдущих кандидатов сохранены в прежних отчётах и не выдаются за эти файлы.

## Реальные Windows gates

**514 passed / 0 failed / 0 skipped** — core на disposable Windows x64 runner. Locked restore, штатный Build-Release.ps1, self-contained publish, resources.pri, архив portable, portable/installed UIA и installer preservation прошли. [Исходный Windows report](evidence/published-2026-11-15/WINDOWS-VALIDATION.json).

Проверены сценарии запуска с preflight и без dispatch при stop, повторное чтение RU/EN настроек, Quick Actions с явным consent/отказом, реальный marker/history, инспекция Центра восстановления без замены live DB. Studio: собственные окна, Notes/timer, native alpha, файл-пикеры, static wallpaper apply/restore. Проверены реальная RC7 база schema2→3, safety backup и защитный отказ старой версии без изменения upgraded данных; прежний original RC7 harness не объявляется прошедшим, адаптация отражена в report.

Установщик проверен в одноразовой CI-среде: per-user install, installed startup, same-version repair, uninstall, reinstall и сохранение SQLite/settings. Это не универсальная приёмка всех прежних installers или физических конфигураций Windows10/11.

## Пользовательский результат

- Настраиваемый preflight и реакция на ошибки dispatch; сохранение не запускает приложения.
- Пятый реальный виджет Studio: Quick Actions для Workspace, folder/file/HTTP(S), без произвольного shell-поля.
- Центр восстановления: каталог копий, Browse, проверка приватного SQLite-снимка, preview, отдельное guarded restore.
- Исправлено ложное предупреждение о доступе для отсутствующих необязательных backup-каталогов. Настоящий отказ доступа остаётся видимым.
- Полные RU/EN руководства и сохранённые настоящие снимки кандидата с исходным происхождением.

## Ограничения

Полная физическая Windows10/11/DPI/Narrator/high-contrast/sleep/RDP матрица **NOT VERIFIED**. Новый GUI-click сценарий полного restore из Recovery Center **NOT RUNTIME VERIFIED**; native inspection/preview/cancel проверены, underlying guarded restore имеет core/infrastructure coverage. SQLite backup исключает внешние обои и recovery drafts; полная копия требует Quit и копирования всей папки данных. Dispatch success означает принятие Windows-запроса, не готовность стороннего приложения. Бинарники не подписаны; предупреждения SmartScreen не сертифицированы. Performance benchmarks не объявляются измеренными.
