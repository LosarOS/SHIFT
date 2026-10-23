# SHIFT 1.0.0-rc.2 — проверка пространств и первый Windows-кандидат

SHIFT — локальный менеджер рабочих пространств: приложения, папки, файлы, ссылки и явно подтверждаемые команды одним запуском. Windows 10 build 19041+ / Windows 11 x64. Учётная запись, облако и платные API не нужны.

## Новое

- **Проверка пространства / Workspace Doctor:** действие «Проверить» на странице пространства. Асинхронная отменяемая проверка, состояния Ready / Warning / Error / Unknown, объяснения RU/EN, повторная проверка и переход к редактору элемента.
- Анализ локальных путей, доступа, рабочих каталогов, заголовков EXE/ярлыков и HTTP(S)-адресов без запуска элементов и сетевых запросов. Подтверждение команд по-прежнему требуется при каждом запуске.
- Исправлены блокировавшие Windows-запуск проблемы: дублирование ресурсов иконки, конфликт XAML-компиляторов, неподдерживаемая подписка на системное событие и отсутствие application `resources.pri` в publish.
- Диагностика ошибок сохраняет HRESULT и имена методов без аргументов или исходных приватных путей.

Существующие CRUD, теги, поиск, избранное, клонирование, очередь/отмена, история, JSON, SQLite backup/restore, RU/EN, Dark / Light / Midnight / Forest, трей, компактный launcher и горячая клавиша сохранены.

## Скачать и установить

- `SHIFT-1.0.0-rc.2-win-x64-setup.exe` — установка для текущего пользователя, без elevation.
- `SHIFT-1.0.0-rc.2-win-x64-portable.zip` — распакуйте **всю** папку и запустите `SHIFT.exe`; `portable.flag` должен оставаться рядом с EXE. Данные хранятся в `data` внутри portable-папки.
- `SHA256SUMS.txt` — сверяйте SHA-256 с файлами из этого же релиза (`Get-FileHash <file> -Algorithm SHA256`).

Установленная версия хранит данные в `%LOCALAPPDATA%\SHIFT`. Перед обновлением выйдите через трей и создайте backup. Uninstall сохраняет пользовательскую базу. Инструкции: [installation](https://github.com/LosarOS/SHIFT/blob/v1.0.0-rc.2/docs/INSTALLATION.md).

**Бинарники не подписаны.** SmartScreen может показать предупреждение; отсутствие такого предупреждения не проверено.

## Проверки и ограничения

Релиз допускается только после locked restore, core suite, настоящего self-contained Windows publish, автоматического запуска portable и установленного приложения, проверок установщика и проверки скачанных GitHub-файлов по SHA-256. Точный source SHA и выполненные проверки записываются в `WINDOWS-VALIDATION.json`; подробности — в [engineering report](https://github.com/LosarOS/SHIFT/blob/v1.0.0-rc.2/reports/engineering-session-2026-10-23.md) и [Actions](https://github.com/LosarOS/SHIFT/actions).

Это **prerelease**, не stable 1.0.0. UI Automation на одноразовом Windows runner не заменяет ручную приёмку на физических Windows 10/11, Narrator, high contrast, 100–200% DPI и увеличенного текста. Не подтверждается запуск каждого стороннего приложения или каждой конфигурации компьютера.

Doctor проверяет доступные локальные признаки. Ready не обещает успешную работу приложения; цели ярлыков и произвольный текст команд не оцениваются как безопасные. Между проверкой и запуском файл может измениться. Quick Workspace Setup развивается отдельным PR и не входит в этот релиз.

Success в истории означает принятие запроса Windows, а не готовность приложения или код завершения команды. Отмена не закрывает уже открытые программы. Захват сессий, расположение окон и расписания остаются в roadmap. Данные, exports и backups не зашифрованы; SHIFT не является sandbox.

## English

This unsigned Windows x64 prerelease adds cancellable Workspace Doctor with local-only diagnostics, RU/EN explanations and item editing. It fixes confirmed native WinUI startup/publish blockers and preserves existing local data contracts. Commands still require explicit consent on every run; Doctor never executes them. Install per user or extract the entire portable ZIP; verify the matching SHA256SUMS. Native automated evidence and exact source commit accompany the release. Interactive Windows 10/11, accessibility and physical DPI acceptance remain unverified; this is not stable 1.0.0.
