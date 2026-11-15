# SHIFT RC8 — итог кандидата, 15 ноября 2026

> Обновление публикации: владелец подтвердил выпуск; [публичные файлы, итоговый source commit, свежие Windows gates и повторная сверка SHA256](RELEASE-PUBLICATION-2026-11-15.md). Доказательства кандидата ниже сохраняют исходные SHA/хеши и не подменяют результаты опубликованной пересборки.

## Результат

Реализован кандидат **1.0.0-rc.8**. Автоматические gates: **PASS**. Публичный release status: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**. Stable1.0 не назначена.

База: main `205a5ff421f6084c7714f794e505e0d5e59b5afb`, RC7. Проверенный Windows source: **`a5effcdcc4861ff099636be7782dd5155a029671`**. PR24 объединён обычным merge в незaщищённый main; код совпадает с ранее проверенным feature-кандидатом28b. [Main CI43665373830](https://github.com/LosarOS/SHIFT/actions/runs/43665373830) также SUCCESS. Последующие изменения отчётов/руководств не меняют входные файлы приложения. [PR24](https://github.com/LosarOS/SHIFT/pull/24) · [Windows43665382675](https://github.com/LosarOS/SHIFT/actions/runs/43665382675).

## Пользовательские изменения

| Возможность | Статус и граница |
|---|---|
| Настройки preflight и реакции на ошибку | VERIFIED: core policies; native stop-before-dispatch, сохранение RU и повторное чтение EN |
| Quick Actions | VERIFIED: декларативная валидация/JSON и native Lab→Save→restart→отказ/разрешение команды Workspace→реальный marker/history |
| Центр восстановления: каталог, проверка, preview/cancel | VERIFIED: core и реальное native UI без изменения live SQLite |
| Кнопка restore в новом центре | IMPLEMENTED / NOT RUNTIME VERIFIED для полного GUI-click сценария; использует прежний guarded restore, покрытый core/infrastructure tests |
| Schema2→3 | VERIFIED: транзакция, FK, safety backup и настоящая база старого RC7 |
| Старый RC7 открывает копию schema3 | VERIFIED: видимый точный RU-отказ, graceful exit, неизменность копии и исходной upgraded DB |
| RU/EN и оформление GitHub | VERIFIED для проверенных текстов/экранов/ссылок; физическая accessibility/DPI матрица остаётся отдельно |

Каждое quick action использует обычный launch flow. Импорт и preview не исполняют действия. Команды сохраняют отдельное согласие. Привязанное оформление не применяется при preflight-abort до dispatch. Старые четыре WidgetKind сохранили ordinal; пустое Actions не добавляется к их JSON. Profile packs удаляют локальные назначения с предупреждением; отдельные widget definitions могут содержать пользовательские пути.

## Исправленные дефекты

- Каталог backup давал ложное предупреждение о доступе для отсутствующих необязательных папок. Сначала воспроизведено3 failed/9 passed, затем12 passed; настоящий denied-access Linux probe продолжает показывать предупреждение. [Red/green evidence](evidence/rc8-catalog-regression/README.md).
- Fixture enum/таймера и UIA peers исправлены без удаления assertions. Старый RC7 binary неизменен; его исходный harness сохранён по SHA, а два UIA helper адаптированы отдельной совместимой копией с diff/хешами. Original harness не объявляется прошедшим.
- Реальные диагностические RU-строки сохраняются UTF8. Новый снимок Lab поднимает owned main через обычный Windows API; PNG не ретушируется.

## Тесты и Windows evidence

- **514 passed /0 failed /0 skipped** — полный core на Linux/.NET10.0.404 и на Windows. Одинаковые cases между средами не суммируются. Новых cases относительно исходного RC7:53.
- Locked restore, self-contained win-x64 publish, `resources.pri`, фактический EXE/portable startup, command refusal/approval/history, restart/localization/theme и существующие Composer/Doctor/Quick Setup gates прошли.
- Отмена запуска покрыта core regression cases; отдельный native launch-cancellation сценарий не заявляется.
- Native Studio: собственные окна, alpha217, Notes multiline, timer/lifecycle/layout, sharing/file pickers, static wallpaper apply/restore на одноразовом Windows Server2025. Исходные обои восстановлены.
- Installer: per-user install, installed UI, same-version repair, uninstall, reinstall и сохранение SQLite/settings. Реальное RC7 portable data upgrade проверено отдельно. Это не универсальная проверка всех исторических installers.
- [Оригинальные validation/proofs и независимая проверка скачанных байтов](evidence/rc8-main-candidate/README.md). Снимки и provenance: [gallery](../docs/screenshots/rc8-main-candidate/README.md).

## Данные и безопасность

SQLite3-миграция создаёт проверенный backup до изменения. Restore revalidates приватную копию и создаёт safety backup перед заменой. Inspection не изменяет live DB. Backup содержит SQLite; внешние обои и recovery drafts отдельно копируются после Quit согласно руководству. Команды не получают новых разрешений, elevation/Explorer injection/скрытого сетевого сканирования нет.

Audit NuGet Infrastructure/direct+transitive не обнаружил известных advisories в проверенной области. Это не аудит всех native/OS dependencies. Бинарники **unsigned**, сертификат не предоставлялся; SmartScreen/антивирусная приёмка не заявляются.

## Файлы для review

[Скачать Actions artifact](https://github.com/LosarOS/SHIFT/actions/runs/43665382675/artifacts/14142410522),190105590 bytes. Срок хранения ограничен, требуется доступ GitHub. Публичные Releases downloads остаются RC7 до одобрения.

| Файл | Байты | SHA256 |
|---|---:|---|
| SHIFT-1.0.0-rc.8-win-x64-portable.zip |106674332|e225dbc849278958e8a064a4f7a33d98ff7e312a948193e6b7cab3dba5bea123|
| SHIFT-1.0.0-rc.8-win-x64-setup.exe |72613165|d6fe9b72173798908295f07106afda47df7ab590a4a50ba208afeecc83a84a5f|

SHA256SUMS, original Windows validation, native proofs и core TRX включены. Source ZIP не используется вместо EXE. Проверены actual downloaded bytes, ZIP CRC/paths/duplicates, AMD64 PE, PRI/portable flag и отсутствие именованных пользовательских баз в дистрибутиве. [RU notes](../docs/RELEASE_NOTES.md) · [EN notes](../docs/RELEASE_NOTES.en.md) · [RU guide](../docs/RC8-GUIDE-RU.md).

## Производительность и оставшаяся приёмка

Для source maina5effcd сохранены [сырые измерения](evidence/rc8-main-candidate/studio-performance-raw.json) на Windows Server2025/одном дисплее:

| Виджетов | Working set, MiB | Private, MiB | Process CPU, % всей машины |
|---:|---:|---:|---:|
|0|161.91|66.15|0|
|1|301.30|183.18|0|
|5|303.94|184.95|0|
|10|307.94|187.20|0|

Это короткие окна около2s по TotalProcessorTime/числу CPU. Нулевые дельты отражают точность выборки, а не гарантию отсутствия нагрузки. Контролируемый benchmark, Lab/profile timings и sleep **NOT MEASURED**. Сравнительное ускорение не заявляется.

Физические Windows10/11, DPI100/125/150/200%, mixed monitors, Narrator/high contrast, RDP, fullscreen, sleep/resume и длительный soak **NOT VERIFIED**. Перед stable требуется ручная матрица и полный новый Recovery Center restore-click на тестовом профиле. Следующий функциональный scope: process-aware behavior и ограниченный parallel launch после отдельного дизайна; полный resource backup и window capture — отдельные задачи.

## GitHub

Код объединён PR24 в maina5effcd без force-push и переписывания истории. Настройки защиты не менялись; обе продуктовые основные ветки фактически protected=false, обычный merge входил в одобренный scope. Существующие tags/releases сохранены; новых публичных релизов пока нет. Профильный README LosarOS опубликован через отдельный PR1. Description/topics/homepage/social-preview settings не применены: API403, локальных browser tools нет. Лицензия не изменена. После отдельного одобрения публикации использовать проверенный release source, выполнить предусмотренный workflow и повторно скачать опубликованные assets для SHA256. Финальные отчёты поступают отдельным docs-only PR, без изменения приложения.
