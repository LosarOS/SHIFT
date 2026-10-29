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

## Контрольная точка 4 — второй Windows CI и UI polish

- CI https://github.com/LosarOS/SHIFT/actions/runs/40956114031, head `ddbd395`: оба core jobs прошли, **367/367**. Настоящие Windows publish, portable CRUD/Doctor/consent/compact selection/keyboard/review/refusal, native Studio до sharing, RU/EN, preset и Quick Setup прошли соответствующие этапы.
- Выпуск остановлен на настоящем SaveFilePicker: после кнопки «Сохранить файл» системный диалог не появился, приложение показало inline error. Снимок `studio-sharing-failure.png` получен из запущенного EXE, не макет. Точный этап не объявляется PASS.
- Исправлена конфигурация file-type filter: одиночное `.json`, суффикс `.shiftstudio` перенесён в SuggestedFileName. Добавлены журналирование исключения и ограниченный UTF-8 UIA failure dump. Подтверждение исправления ожидает новый Windows прогон.
- Widget Lab: копирование полного оформления библиотечного виджета в draft, рамка/тень и полный reset; содержание по типам с сохранением скрытых значений. Нет новой схемы, самостоятельной базы или автоматического применения.
- Добавлены native-проверки copy/cancel/save (включая custom font/border/shadow), Ctrl+S и Shift+F10. Новая декларация теста создаётся явно; фиксированные ожидаемые counts тестового набора обновлены на неё, проверки отмены не ослаблены.
- Локальные сборки acceptance tool и Windows-reference App: 0 errors/0 warnings, PowerShell parser и source hygiene прошли.

## Контрольная точка 5 — проверка общего style contract

- CI https://github.com/LosarOS/SHIFT/actions/runs/40957432770, head `07f8cc7`: оба core jobs и реальный portable compact/consent UIA прошли. Studio seed остановился до новых GUI-проверок: `Unsupported widget font`.
- Подтверждён продуктовый дефект: Domain/редактор разрешают `Cascadia Mono`, а отдельный persistence allowlist отклонял его. Также Domain допускает TemplateId до100символов, persistence ограничивал64. Persistence теперь использует `StudioValidator.SafeFonts` и согласованную границу100; SQL schema не меняется.
- Семь новых persistence/transfer регрессий проверили все предлагаемые шрифты и пользовательские имена65/100символов. Full Linux suite: **374/374**, без пропусков.
- Некорректный формат даты теперь имеет код Studio.DateFormat и русскую подсказку, а не непереведённую General-ошибку. Добавлена отдельная регрессия. Ошибочное содержимое нового виджета остаётся доступным при смене типа, а проверка во время ввода не прокручивает редактор от пользователя.
- Подготовлен ADR `docs/studio/PROFILE-TRANSFER.md` с границами доверия, atomic import и различием между конфигурацией, live snapshot и backup.
- Предыдущий SaveFilePicker blocker ещё не объявлен исправленным: последний прогон не дошёл до него. Публикации нет.

## Контрольная точка 6 — native keyboard readiness

- CI https://github.com/LosarOS/SHIFT/actions/runs/40958457986, head `fdd67b5`: Linux/Windows **375/375**, Windows publish и compact/consent UIA прошли. Studio seed с Cascadia Mono теперь прошёл; copy appearance cancel/save проверены на реальном EXE.
- Первый Ctrl+S сохранил ожидаемую заметку, Shift+F10 показал настоящее меню в Desktop Mode. Прогон остановился при немедленном повторном SetFocus после Escape (`Target element cannot receive focus`, Smoke-Studio.ps1:199). Само сочетание не заменялось программным вызовом функции.
- Probe теперь заново получает editor peer, дожидается keyboard-focusable/enabled, foreground HWND и PID, затем посылает каждое сочетание один раз. Повторяются только проверки готовности, а не команды. Ожидается следующий полный прогон.

## Контрольная точка 7 — подтверждённый native menu crash

- CI https://github.com/LosarOS/SHIFT/actions/runs/40959272665, head `82da63c`: core **375/375**, publish/portable и style-copy прошли; повторное получение фокуса не помогло. Анализ сохранённого desktop screenshot и `studio-app-logs` показал **продуктовый дефект**, а не только готовность UIA.
- `ContextMenuStrip.Closed` синхронно вызывал `Dispose`, пока WinForms ещё выполнял `ProcessDialogKey/SetVisibleCore` после Escape. Получен `ObjectDisposedException`; Studio безопасно отключился, основная оболочка осталась доступна. Более ранняя гипотеза о чистой readiness-гонке была неполной.
- Меню теперь принадлежит окну виджета и переиспользуется; items освобождаются перед следующим открытием, само меню — при dispose владельца. Темизация и font lifetime обновляются вместе с окном. Синхронного dispose внутри Closed больше нет.
- Regression открывает/закрывает меню с клавиатуры дважды, возвращает фокус, сохраняет восстановленный текст и продолжает остальные native gates. Проверки не исключены; требуется новый полный Windows прогон.

## Контрольная точка 8 — меню исправлено, Save As действительно открыт

- CI https://github.com/LosarOS/SHIFT/actions/runs/40960534386, head `dc99a8f`: core **375/375**, native copy/cancel/save, повторные Shift+F10/Escape, возврат фокуса и Ctrl+S прошли; Studio продолжил сценарии до sharing. Продуктовый menu crash устранён в этом прогоне.
- На настоящем screenshot `studio-sharing-failure.png` виден системный **Save As** с фильтром JSON. Исправление filter подтверждено; текущий timeout относится к probe: owned modal HWND вложен в UIA tree владельца, а поиск смотрел только desktop children. Поиск теперь ограничен Window/#32770 среди descendants; added bounded diagnostic metadata при отказе.
- `Clipboard.Flush()` делает явное копирование независимым от времени жизни SHIFT. Добавлен обязательный native gate: скопировать заметки без промежуточного clipboard consumer, завершить процесс, сравнить текст после выхода.
- До успешных real save/open/installer gates RC5 остаётся неопубликованным. Старая версия RC4 и её assets сохранены.

## Контрольная точка 9 — broker file dialog input

- CI https://github.com/LosarOS/SHIFT/actions/runs/40961694815, head `bac5076`: все core jobs **375/375**, меню/клавиатура и предшествующие native этапы прошли. Save As найден и заполнен, но подтверждение через UIA Invoke показало системное `An outgoing call cannot be made since the application is dispatching an input-synchronous call`.
- Screenshot и metadata подтвердили отдельный picker process внутри UIA subtree SHIFT. Это ограничение синхронного automation/BM_CLICK call-out в broker, не доказательство отказа обычного пользовательского клика.
- Probe теперь ищет только file dialogs внутри конкретного SHIFT owner tree, устанавливает путь в owned data и ждёт foreground HWND + filename focus + picker PID; затем один раз отправляет обычный Enter. Команда/путь не интерпретируются оболочкой. Сохранение, открытие и сравнение байтов остаются обязательными.

## Контрольная точка 10 — ввод имени файла

- CI https://github.com/LosarOS/SHIFT/actions/runs/40962766920, head `f69320a`: core **375/375**, предшествующие native этапы прошли. Broker dialog принял Enter и закрылся вместе с export dialog, но ожидаемый файл по заданному тестовому пути не появился. Результат не объявлялся успешным.
- Для проверки shell filename model probe теперь вводит путь обычными клавишами после Ctrl+A, экранируя метасимволы SendKeys, проверяет фактическое значение поля и только затем нажимает Enter. Убрано прямое ValuePattern/WM_SETTEXT изменение filename, которое может обходить edit notifications.
- При `SHIFT_STARTUP_TRACE=1` локальный журнал фиксирует возвращённый picker destination, чтобы следующая диагностика различала неправильный путь, расширение и отказ записи. В обычном запуске этот trace выключен. Нужен новый реальный Windows прогон.

## Контрольная точка 11 — реальное сохранение и многострочный текст

- CI https://github.com/LosarOS/SHIFT/actions/runs/40963759963, head `e782075`: core **375/375** и предыдущие native этапы прошли. Обычный ввод filename исправил probe: файл профиля записан приложением в ожидаемый owned path, прочитан обратно и совпал с предпросмотром. Это уже не только показ FileSavePicker.
- Следующий gate обнаружил продуктовый UI-дефект: Notes preview содержал лишь заголовок. WinUI получал Text до AcceptsReturn=true и обрезал всё после первого перевода строки. Helper/SQLite сохраняли полный текст; проблема была в инициализации контрола. Та же последовательность найдена в редакторах workspace description, item command/note, profile description и initial Notes.
- Все многострочные WinUI TextBox теперь включают multiline до присвоения текста. Export/copy заметок используют исходный authoritative snapshot, а не read-back из presentation control.
- Добавлены настоящие GUI-проверки повторного открытия/сохранения многострочного workspace description, команды и item note; Notes template и profile description также проверяются на нескольких строках. Исторический RC2 не выдаётся за реализацию этих RC5-specific gates.
- Native сохранённый Notes-файл дополнительно сравнивается **точно** с `StudioNotesExport` по исходным состояниям; сравнение UI допускает только нормализацию CR/LF. Ни символы, ни строки не игнорируются.
- Публикация всё ещё ожидает полный green Windows/installer прогон. Проверки отказов не удалены.
