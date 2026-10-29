# Установка и данные

## Требования

Windows 10 2004 / build 19041+ x64 или Windows 11 x64. Основные релизные проверки: Windows 10 22H2 и Windows 11. Без администратора. Сборка включает .NET runtime и Windows App SDK; отдельно ставить их конечному пользователю не нужно. Нужны стандартные Windows runtime prerequisites, проверяемые на чистой VM перед beta.

SHIFT распространяется как неподписанный prerelease. Готовые файлы публикуются в [GitHub Releases](https://github.com/LosarOS/SHIFT/releases) только после Windows build/runtime/installer gates. Исходники собираются на Windows через `packaging/Build-Release.ps1`; подробный статус проверки находится в `WINDOWS-VALIDATION.json` релиза и `docs/VALIDATION.md`. Ручная приёмка Windows 10/11 и DPI остаётся отдельным этапом.

## Installer

1. Сверьте SHA-256 скачанного файла с `SHA256SUMS.txt`, полученным из того же доверенного release.
2. Запустите `SHIFT-<version>-win-x64-setup.exe`.
3. Установка идёт в `%LOCALAPPDATA%\Programs\SHIFT`, данные — `%LOCALAPPDATA%\SHIFT`.
4. Создайте demo или первое пространство. Ничего автоматически не запускается.

Installer per-user, обновляет файлы приложения и сохраняет данные. Перед обновлением полностью выйдите через трей и создайте backup. Uninstall удаляет файлы/ярлыки и Run key SHIFT; пользовательская база и логи сохраняются. Для полной очистки после выхода и uninstall удалите `%LOCALAPPDATA%\SHIFT` вручную.

## Portable

1. Распакуйте **весь** ZIP в папку с правом записи. Не запускайте из ZIP или Program Files.
2. Сохраняйте `portable.flag` рядом с `SHIFT.exe`.
3. Запустите `SHIFT.exe`. Данные появятся в `data\`, backup в `data\backups`.
4. Для переноса выйдите из SHIFT через трей и переместите всю папку. Абсолютные пути приложений и файлов нужно проверить на новом компьютере.

Portable не регистрирует автозапуск. Installed и portable с разными каталогами данных могут работать раздельно. Не используйте одну portable-папку одновременно на нескольких машинах или сетевом диске.

## Повседневное использование

Создайте workspace → добавьте элементы → задайте порядок и задержки → Launch. Поля путей содержат абсолютные локальные пути **без внешних кавычек**. Параметры приложения — отдельно; при Command полная командная строка вводится в Target. Только HTTP/HTTPS URL. `.lnk` допустим как App, а не File.

Команды выполняются в видимой оболочке с пользовательскими правами. Перед каждым запуском необходимо подтверждение. Статус Success говорит о передаче запроса Windows; команда может ещё работать или завершиться внутренней ошибкой позже. Наблюдение за кодом выхода команды пока не реализовано.

Закрытие окна по умолчанию сворачивает SHIFT в трей. Двойной клик по иконке восстанавливает окно; контекстное меню → Quit завершает процесс. При работающей очереди выход предлагает остановить её. Отмена не завершает уже открытые программы.

## Импорт / экспорт

В списке используйте Import JSON. Пример без локальных путей и команд: `examples/developer.workspace.json` в исходниках. SHIFT проверяет размер (2 MB), версию, поля и показывает preview; импорт получает новые IDs и не перезаписывает существующее пространство. До запуска отредактируйте чужие пути и команды. Export находится в меню `···` на странице пространства. JSON содержит личные пути и аргументы, без шифрования.

## Резервное копирование

В Settings можно выбрать папку и сохранить настройки. Кнопка Create backup использует **сохранённый** путь. Каждая ручная копия — самостоятельный SQLite `.db`; автоматические создаются при открытии раз в UTC-день в `automatic\`, сохраняются последние 10. Backup API согласует активную WAL-базу. Не копируйте только `shift.db` во время работы.

### Восстановление через интерфейс

Если SHIFT открывает базу: остановите очередь, завершите редактирование/импорт → Settings → Restore backup → выберите `.db` → проверьте предупреждение. SHIFT получает согласованный снимок выбранной копии, включая committed WAL-изменения, проверяет SQLite, схему, настройки, пространства и всю историю. Затем создаёт отдельную копию текущих данных, выполняет WAL checkpoint и заменяет базу. Повреждённая копия отклоняется до замены. Команды из backup не запускаются. Существующие настройки автозапуска Windows сверяются с реальным Run key; restore не включает startup автоматически.

### Ручное восстановление при недоступной базе

1. Выйдите из SHIFT через трей. Убедитесь, что `SHIFT.exe` завершён.
2. Сохраните текущую папку данных целиком в отдельное место (включая `shift.db-wal` и `shift.db-shm`, если они есть).
3. Переместите текущие `shift.db`, `shift.db-wal`, `shift.db-shm` из рабочего каталога в сохранённую папку; не смешивайте старый WAL с backup.
4. Скопируйте выбранный backup `.db` в каталог данных под именем `shift.db`.
5. Откройте SHIFT и проверьте пространства. Никогда не открывайте более новую schema старой версией приложения.

Для переноса между portable и installed удобнее JSON отдельных пространств; полная backup переносит также историю и настройки, которые нужно проверить (особенно startup и backup paths).

## Диагностика

Settings → Open logs / Data folder. Logs: `logs\shift-YYYYMMDD.log`, до 5 MB за день и до 14 файлов. При недоступной/повреждённой базе SHIFT показывает экран ошибки, не сбрасывает данные. Проверьте свободное место, права записи, antivirus events и лог.

Неподписанные сборки могут вызывать SmartScreen. Для публичного релиза подпись и проверка на чистых VM остаются отдельным release gate. Не обходите предупреждения для файлов из неизвестного источника.

## Быстрый запуск и история

Ctrl+K открывает компактный launcher; ↑/↓ выбирают строку, Enter запускает, Esc возвращает полный режим. Глобальный Ctrl+Alt+Space включается в Settings явно; при конфликте сочетания SHIFT сообщает об ошибке. Второй запуск EXE активирует существующий экземпляр. History поддерживает поиск по имени, фильтр ошибок, страницы, повтор и очистку завершённых записей. Повтор использует текущее содержимое workspace и снова требует consent для команд.

## SHIFT Studio preview: migration and recovery

Studio is disabled by default. Enable it explicitly in Studio; saving or opening a desktop profile does not change Windows. Wallpaper apply requires confirmation that Windows uses static images. Spotlight/provider and slideshow restoration are unsupported. Automatic workspace rules currently apply widget-only profiles.

Before upgrading RC2, quit SHIFT and copy the entire data folder. SQLite initialization creates a validated v1 snapshot in `migration-backups/` before transactional schema2 migration. RC2 refuses a schema2 database. To downgrade, keep the Studio data safely and use the preserved v1 snapshot in a separate data directory.

SQLite `.db` backups contain profile/image references, not the image files or local note drafts. For a complete Studio backup or portable move, copy the entire data folder including `Studio/Images`, `Studio/RecoveryNotes` and `migration-backups`. Keep managed images while Windows still uses them as wallpaper. Uninstall retains user data; it does not silently undo the desktop. Use Studio → Restore Windows appearance first if desired.

Recovery checks for later user changes and asks before overwriting conflicts. Disable Studio closes owned widgets without deleting their configuration or implicitly restoring wallpaper. Gather widgets moves lost windows onto an available monitor. The tray always provides access to the main window and these controls.
