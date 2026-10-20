# SHIFT 0.1.0 — engineering preview

Первый срез локального Windows workspace manager: сохранение приложений, папок, файлов, ссылок и явных команд в рабочие пространства; последовательный запуск с отменой, статусами и историей.

## Включено

- Native WinUI 3 / .NET 10, SQLite, clean architecture.
- Workspace CRUD, clone, favorites, tags, search; item editor and reordering.
- Queue with durable per-item status, error isolation, command consent and crash marking.
- Overview, Workspaces, Favorites, History, Settings; onboarding, RU/EN, 4 themes.
- Tray, completion notifications, installed per-user startup.
- Versioned JSON import/export, online backup, local bounded logs.
- Automated core/integration tests, Windows CI, portable/installer packaging scripts.

## Статус

Это engineering preview исходного проекта, не обещание готовности public beta. Фактически выполненные проверки находятся в `docs/VALIDATION.md`. Windows runtime, clean-machine packaging и ручная QA должны пройти до публикации пользователям.

## Ограничения

- Success = open request accepted, не готовность приложения и не exit code команды.
- Нет capture текущих окон, восстановления позиций, hotkeys, расписаний и browser session restore.
- Только последовательный режим; отмена не закрывает программы.
- Абсолютные локальные пути, HTTP/HTTPS URL; UNC/device targets не поддерживаются.
- Latest 100 history entries shown; history deletion/pagination deferred.
- Domain/OS diagnostic details may be English; UI RU/EN.
- Portable needs write access and does not register startup.
- Apps/shortcuts/commands are trusted executable content; consent is not sandboxing.
- Бинарники unsigned, Windows QA и подпись остаются release gates.
