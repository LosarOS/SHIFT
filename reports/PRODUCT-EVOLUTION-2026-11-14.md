# SHIFT — Product Evolution, сессия 14–15 ноября 2026

## Авторизация и границы

Объём согласован владельцем. Базовый main: `205a5ff421f6084c7714f794e505e0d5e59b5afb`, публичный RC7. Кандидат: **1.0.0-rc.8**, PR [#24](https://github.com/LosarOS/SHIFT/pull/24), ветка `feature/rc8-product-evolution`.

Отдельное подтверждение финальной публикации и merge ещё не получено. Main, существующие релизы и пользовательские профили не перезаписаны. Windows GUI и изменение обоев проверяются только на одноразовых GitHub runners. Linux executor используется для core-проверок; его результат не считается Windows runtime.

## Реализованный объём

1. Политики запуска: Doctor перед dispatch, продолжение / остановка / пропуск ошибок проверки; продолжение / остановка после ошибки dispatch. По умолчанию прежнее поведение. Не запускает параллельные процессы, не повторяет команды, не заменяет consent. При остановке до первого dispatch не применяется связанный Studio-профиль.
2. Quick Actions: пятый встроенный тип, до12 декларативных действий Workspace / File / Folder / HTTP(S), реальный Lab и native widget. Preview не запускает действия. Workspace разрешается из текущего хранилища, переход в обычный launch/consent. Сериализация исходных четырёх типов не добавляет пустое поле Actions. Пакеты профилей удаляют локальные назначения с предупреждением; индивидуальные определения могут содержать пути, что описано в руководстве.
3. Центр восстановления: ограниченный каталог, WAL-consistent приватная копия, проверка схемы/данных и сводка перед подтверждением restore. Cancel ждёт завершения принадлежащего ему snapshot. **Это SQLite-backup, не полный архив внешних изображений и заметок.**
4. Schema3: транзакционная миграция v2→v3, проверка FK, валидированный backup перед миграцией. RC7 должен отказать в открытии новой схемы, не изменив базу.
5. RU основной README, полноценный EN, отдельные руководства, оригинальная графика и шаблоны участия. Публичная download-ссылка остаётся на RC7 до одобрения кандидата.

## Подтверждённые проверки

- .NET10.0.404: **512 уникальных тестов, 0 failed, 0 skipped**, Linux; тот же core suite успешно выполнен на Windows в CI [43658629380](https://github.com/LosarOS/SHIFT/actions/runs/43658629380). +51 случай относительно RC7. Повторные прогоны и поднаборы не суммируются.
- Locked restore и source audit выполнены. Проверка NuGet Infrastructure, включая transitive, не обнаружила известных уязвимостей на момент проверки; это не аудит всех Windows/native компонентов.
- Введены обязательные native gates: preflight stop/no dispatch, restart policy, recovery inspect/no restore, Quick Actions save/restart/refuse/approve, реальное обновление RC7 и отказ старого EXE открыть schema3.
- Полный Windows-кандидат [43658826225](https://github.com/LosarOS/SHIFT/actions/runs/43658826225), source `48211c7cb4c073ad36a65b08219295c82083b317`: **IN PROGRESS на этом checkpoint**, не объявлен прошедшим.

## Диагностика и исправления native probes

Все ошибки сохранены в Actions. Не исключались проверки ради зелёного статуса.

- Добавление enum выявило коллизию ID тестового fixture; исходные четыре типа и пятый сценарий теперь независимы.
- Хеширование живого WAL SQLite давало sharing violation; проверка неизменности теперь сравнивает детерминированный read-only logical fingerprint внутри snapshot-транзакции, а не делает небезопасную копию живого файла.
- Fixture таймера опирался на позицию enum и затёр стартовую Notes; выбор выполняется по Kind. Реальная проверка переноса строк сохранена.
- Новый PS-файл с RU требовал UTF8 BOM для Windows PowerShell5.1; добавлены ранняя проверка синтаксиса и encoding guard.
- UIA WinUI заменяет Edit peers при перерисовке: ожидание writable pattern, точный readback, свежий keyboard focus. После неоднозначного SetValue проверяется постусловие без повторной записи. Нажатия и dispatch не повторяются.
- Старый RC7 EXE остаётся неизменным. Его исходный harness сохранён по SHA; отдельно используется строго ограниченная compatibility-копия только двух UIA helper-функций. Diff/хеши пишутся в evidence; исходный harness не объявляется прошедшим.

## Ограничения / дальнейшие шаги

Дождаться полного Windows packaging + installed/reinstall/uninstall + upgrade smoke, скачать реальные файлы, независимо проверить SHA256/PE/PRI и просмотреть скриншоты. До этого статус **BLOCKED для публикации**. Физическая Windows10/11, DPI100/125/150/200, несколько дисплеев, Narrator, RDP, sleep/resume и длительный soak не подтверждены. Подписи нет, SmartScreen/антивирус не обещаются.

GitHub metadata description/topics/homepage вернули403 Resource not accessible by integration. Файлы README/брендинга подготовлены; social preview и pinning не применены. Профильный PR LosarOS/LosarOS#1 безопасно объединён в незащищённый main; это не merge продукта.

Следующий checkpoint должен содержать окончательные source SHA, Actions status, размеры/хеши бинарников, ссылки на retained evidence и точные оставшиеся gates.

## Windows checkpoint — source932999a, 15 ноября

[Canonical43659620650](https://github.com/LosarOS/SHIFT/actions/runs/43659620650) **SUCCESS**; [PR CI43659598698](https://github.com/LosarOS/SHIFT/actions/runs/43659598698) **SUCCESS**. Полные Windows publish, portable/native Studio, RC7 upgrade/refusal, installed/reinstall/uninstall прошли. Root скачал оригинальный190173278-byte Actions artifact через GitHub connector, source/version/proofs прочитаны; independent byte checks и genuine PNG сохраняются отдельно. Публикационный job на ветке не выполнялся.

Обнаружено при финальном UI review: каталог Recovery Center считает отсутствующие необязательные папки ошибкой доступа. Воспроизведение **3 failed/9 passed** на прежнем коде; узкий catch(FileNotFoundException) рядом с DirectoryNotFoundException: **12 passed**, без изменения обработки Unauthorized/прочего IO. Два новых regression cases + усилен существующий. Исправление56f6268 требует нового final Windows source, прежний успешный932 не доказывает новый бинарник.

Первый Quick Actions Lab PNG перекрыт10 stress-window fixture; это сохраняется как техническое evidence, не как чистый marketing shot. Probe теперь поднимает только owned main через документированный BringWindowToTop и проверяет foreground перед DwmFlush/Capture. Окна не скрываются, изображение не редактируется. Качество нового кадра будет проверено отдельно.

Короткие измерения прежнего успешного932 на disposable Windows Server2025, один дисплей: working set0/1/5/10 widgets =161.31/297.65/301.46/307.51MiB; private65.91/178.91/181.66/187.19MiB. Каждая CPU-выборка около2s,0/0/0.1953/0% всей машины. Нули отражают квантование короткой выборки, не гарантию отсутствия нагрузки. Нет сравнительного benchmark, длительного soak, лабораторной точности или утверждения об ускорении. Lab/profile timing и sleep NOT MEASURED. Сырые значения studio-performance-raw.json находятся в исходном Actions artifact.
