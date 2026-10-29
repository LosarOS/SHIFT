# SHIFT RC5 — инженерная сессия

Дата: 2026-10-29. База: `00bc877adaed78cd1a9ab457fa87bd76123d09f3` (main), опубликованный RC4: `ebcae08bbe4c66bf1aece9bbc811cc706fbe3c99`.

## Цель и границы

Продолжить развитие существующего продукта: переносимые декларативные профили Studio, экспорт заметок перед удалением, доступный компактный лаунчер и подтверждённые исправления runtime. Сохранить SQLite schema2, текущий consent и архитектурные границы. RC5 — prerelease; физическую Windows-приёмку нельзя подменять CI.

## Контрольная точка 1 — аудит

- `main` чистый, актуален на origin; последний публичный релиз RC4. Открыты только dependency PR, они не объединяются без отдельной проверки.
- Отдельная интеграционная ветка `feature/shift-studio-rc5`; независимые worktrees для profile transfer, notes export, native runtime и accessibility.
- Базовые Linux/.NET10 тесты: **296 passed, 0 failed, 0 skipped**, TRX вне репозитория: `/workspace/scratch/rc5-baseline-tests/`.
- Offline source hygiene: 162 текстовых файла, совпадений credential patterns нет. Это ограниченная проверка, не полный security scan.
- Предыдущие результаты RC4 не считаются доказательством новых изменений.

## План

1. Profile pack: строгий версионированный JSON, новые идентификаторы, атомарное добавление, предварительный просмотр и отказ без записей. Без внешних ресурсов, wallpaper paths, workspace bindings и автоприменения.
2. Заметки: отдельный читаемый экспорт текущих данных всех Notes выбранного профиля, включая скрытые экземпляры; явная пользовательская запись/копирование.
3. Native runtime и compact launcher: исправления по коду/регрессиям, без массовой перестройки.
4. Интеграция, regression, Windows native build/UIA/installer и снимки. PR, merge и публикация только при фактическом прохождении gates.

## Ещё не подтверждено

На этой контрольной точке новые функции и Windows-проверки RC5 не завершены. Release status: **BLOCKED (development in progress)**.

## Контрольная точка 2 — интеграция

- Включены строгие profile packs: schema1/2МиБ/64 виджета, проверка повторяющихся и неизвестных JSON-полей, свежие идентификаторы и существующий insert-only transaction. UTF-8 BOM поддерживается; UTF-16 не угадывается.
- Добавлен локальный экспорт заметок по экземплярам, в том числе скрытым. Открытые окна предоставляют read-only live snapshot, который перекрывает данные SQLite; неоткрытые recovery drafts не выдаются за экспортированные.
- UI: экспорт/импорт с review/back/cancel, поиск профилей и отдельное сохранение заметок. Большой начальный текст не вызывает необработанное исключение при включении checkbox: недоступный вариант отключён с объяснением.
- Compact launcher использует настоящий ListView/SelectionPattern, отдельные слова/описание/теги, состояние отсутствия результатов и Review без запуска.
- Native runtime: блокировка ввода на final hand-off, завершение ожидающих вызовов при выходе STA, подтверждение Stop до ExitThread, конфликт таймера/заметки и доступ к меню с клавиатуры.
- Исправлена устаревшая локальная версия App.csproj: общий источник версии теперь Directory.Build.props. Release packaging передаёт Version явно.
- Объединённые Linux/.NET10 тесты: **367 passed, 0 failed, 0 skipped**. Windows-reference C# compile: **0 errors / 0 warnings**, это не Windows runtime.
- Дополнительные SQL integration tests проверили rollback после поздней ошибки/коллизии, независимые импорты и сохранность workspace/settings/history/current Studio state.
- Подготовлены новые compact UIA gates; исторический RC2 smoke явно использует legacy-режим, чтобы не требовать от RC2 интерфейс RC5.
- Следующий шаг: полный native Windows CI на итоговой ветке. Новые функции ещё не объявлены runtime verified.

## Контрольная точка 3 — первый Windows CI

- PR: https://github.com/LosarOS/SHIFT/pull/18. CI: https://github.com/LosarOS/SHIFT/actions/runs/40955664773, head `35b509b86a0e8569c8524e677f61ad74e93e3a26`.
- Linux core и diagnostics прошли. Windows core: **366 passed / 1 failed / 0 skipped**. Причина воспроизведена по stack trace: финальная очистка нового теста удаляла каталог без очистки SQLite pool; Windows удерживает файл `shift.db`. Исправлено закрытие idle pools, без изменения или исключения проверяемых assertions.
- Packaging закономерно остановился на той же обязательной core-проверке. Нативные UI-сценарии этого запуска ещё не выполнены; новый релиз не публиковался.
- Full solution locked restore прошёл локально. NuGet advisory audit шести проектов (включая App и transitive packages) не сообщил уязвимых пакетов на дату проверки; это не доказательство отсутствия неизвестных уязвимостей.
