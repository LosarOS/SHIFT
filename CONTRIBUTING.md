# Contributing

Используйте .NET 8 и Windows 10 build 19041+ / Windows 11 для WinUI. Core tests доступны на Linux. Build/run инструкции — в README.

- Сохраняйте направление Domain ← Application ← Infrastructure; Windows UI не должен проникать в ядро.
- Перед PR запускайте core tests и Windows build/publish. Для UI приложите screenshot реального Windows-окна и описанную ручную проверку.
- Меняйте schema только миграцией с backup, version guard и integration tests. Не переставляйте persisted enum values.
- Никогда не обходите ICommandConsent для команд и не запускайте workspace при импорте.
- Не отправляйте private DB/команды/полные логи в issues. Воспроизводите на отдельном безличном workspace.
- PR description: проблема, изменение поведения, validation, известные ограничения.

Публичные релизы проходят `docs/QA.md`. Непроверенные Windows-изменения не обозначайте verified.
