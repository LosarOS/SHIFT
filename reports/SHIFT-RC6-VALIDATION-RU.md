# SHIFT 1.0.0-rc.6 — технический отчёт

Фактическая контрольная точка: **2026-10-29 23:14 UTC**. Binary/tag source: `de8e744d6993b3293b55ed56675cb6472ef68fab`. Статус выпуска: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**. Это неподписанный prerelease; stable 1.0 не объявлен.

[Скачать RC6](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.6) · [PR20](https://github.com/LosarOS/SHIFT/pull/20) · [Tagged Windows build](https://github.com/LosarOS/SHIFT/actions/runs/40970869902) · [Инженерный журнал](SHIFT-RC6-ENGINEERING-SESSION.md) · [Независимая загрузка публичных байтов](evidence/rc6/independent-public-verification.json).

**RC6 исправляет подтверждённый после RC5 P1.** [Проверка неизменённых RC5 bytes](https://github.com/LosarOS/SHIFT/actions/runs/40969409884) получила native line0 вместо 1 при CR-only тексте после WinUI. Теперь CR/LF/CRLF преобразуются в CRLF для native control, LF для runtime snapshot; Unicode/пробелы/пустые строки не теряются,20k model bound сохраняется. [Новое native evidence](evidence/rc6/native-notes-newline-proof.json) подтверждает реальный HWND line1 на финальном source. Старые RC5 assets не заменены.

## 1. Executive Summary

Выпущено расширение работающего SHIFT: перенос профилей Studio, читаемый экспорт заметок, повторное использование оформления в Widget Lab, поиск профилей и доступный компактный лаунчер. Все новые действия имеют RU/EN. Исправлены подтверждённые native-дефекты меню, многострочных редакторов и рассогласованной валидации стилей.

**384 уникальных теста, 0 failed, 0 skipped.** Полный suite выполнен на Windows и Linux. На disposable Windows runner действительно запущены извлечённый portable EXE и установленная программа; выполнены Studio, файловые диалоги, clipboard, restart и installer preservation. Шесть опубликованных файлов повторно скачаны: размеры, API digests, SHA256SUMS, tag/source и содержимое portable проверены отдельно. Неизменённые предыдущие релизы сохранены.

Физическая Windows 10/11, специальные возможности, mixed DPI/multi-monitor и длительные аппаратные сценарии требуют отдельной приёмки. Эти ограничения не скрываются за зелёным CI.

## 2. Архитектура SHIFT Studio

**VERIFIED для перечисленных автоматических сценариев.** Сохранены C#/.NET 10, WinUI 3 / Windows App SDK, SQLite и направленные слои Domain → Application → Infrastructure / Presentation. Лёгкие виджеты работают в собственных управляемых native-окнах одного STA в процессе SHIFT. Electron, облачный backend и обязательный WebView2 не добавлены.

`StudioProfileTransfer` отвечает за декларативный формат; `StudioNotesExport` — за читаемый снимок текста. `IStudioPresetStore` сохраняет импортируемый graph одной insert-only транзакцией. UI показывает review и вызывает application contracts, без прямого SQL. `CaptureLiveStatesAsync` получает текущий текст окон отдельно от persistence. [ADR](../docs/studio/PROFILE-TRANSFER.md).

## 3. Desktop Profiles

**VERIFIED:** поиск по имени/описанию/Workspace; default-private экспорт; explicit InitialNotes opt-in; реальное сохранение JSON; импорт через системный Open; preview/cancel без записи; atomic save с новыми IDs без активации и изменения десяти существующих HWND.

Формат `SHIFT.StudioProfile`, schema1, ограничен 2 МиБ UTF-8, глубиной 16 и 64 экземплярами/используемыми определениями. Неизвестные/повторяющиеся поля, неподдерживаемые версии, enum и неверные ссылки отклоняются. UTF-8 BOM принимается, UTF-16 не угадывается. Обои/файлы/пути/monitor IDs, Workspace binding, автоприменение, current Notes/Timer state и recovery в пакет не входят. Названия и добровольно включённый начальный текст могут содержать личные сведения.

Импорт по умолчанию готовит видимые окна к размещению на основном экране и Edit Mode, выключает topmost/click-through. Не открывает окна и не меняет Windows. Обои и autoapply не разрешаются импортом.

## 4. Widget Runtime

**VERIFIED в native CI:** Clock, System Monitor, Notes и Focus Timer; окна 0/1/5/10; отсутствие дубликатов после reapply; alpha attribute; режимы, геометрия, gather; локализация без пересоздания; restart/disable/enable/Quit. Центральный scheduler сохранён.

Финальный hand-off состояния блокирует ввод на ограниченное время; ожидающие STA-вызовы завершаются при выходе, Stop подтверждается до остановки message pump. Меню принадлежит окну и больше не Dispose-ится внутри Closed: повторные Shift+F10/Escape, возврат фокуса и Ctrl+S прошли на EXE.

**VERIFIED:** Notes export объединяет live snapshot открытых окон выбранного профиля и сохранённый текст остальных экземпляров, различает пустой текст и отсутствие состояния, помечает hidden/InitialNotes. Файл точно сравнен с authoritative buffer; clipboard проверен после Quit. Лимит 8 МиБ. Неоткрытые recovery drafts исключены; текст не зашифрован. Это снимок, а не live feed или полный backup.

**IMPLEMENTED / NOT RUNTIME VERIFIED:** все варианты принудительного timer-conflict выбора и дисковых/аппаратных отказов. Их реализация не означает полную fault-матрицу.

## 5. Widget Lab

**VERIFIED:** полный стиль библиотечного виджета переносится в draft; Cancel не пишет данные; Save сохраняет палитру/opacity/font/border/shadow. Проверены Cascadia Mono, font32, border8, Shadow=false и предельные значения. Содержание не копируется вместе со стилем; поля по типам сохраняют скрытые значения черновика.

Исправлена обрезка многострочного текста при WinUI TextBox.Text до AcceptsReturn. Проверены Notes template и profile description при повторном открытии/сохранении. Общий `StudioValidator.SafeFonts` используется и persistence; лимит custom TemplateId согласован 100. Ошибка формата даты получила RU-подсказку. Проверка во время ввода не прокручивает редактор от пользователя.

**NOT RUNTIME VERIFIED:** широкая мастерская на всех физических DPI, Narrator и переключение всех системных accessibility-настроек.

## 6. Workspace Integration

**VERIFIED в указанных regression gates:** CRUD, Doctor без dispatch, command consent/refusal, безвредная подтверждённая команда, история, tray, second instance, restart. Новая multiline-регрессия проверяет полное описание Workspace, command target и item note после повторного открытия/сохранения.

Compact Launcher использует настоящий ListView/SelectionPattern, поиск слов по имени/описанию/тегам, стрелки/Enter, пустое состояние и Review без dispatch/history/marker изменений. Выполнены семь дополнительных compact/multiline gates. Включение визуального профиля отдельно от приложений сохранено; ошибка оформления не повторяет запуск приложений. Успех dispatch означает принятие запроса Windows, а не готовность сторонней программы.

## 7. Windows API и ограничения

**VERIFIED в disposable Windows Server 2025 x64, один виртуальный монитор.** Поддерживаемый `IDesktopWallpaper` применяет подтверждённые статичные локальные обои; native smoke проверяет точное восстановление. Документированный Win32 interop управляет собственными окнами. Нет WorkerW/Explorer injection, shell extensions или elevation.

Alpha217 при opacity0.85 подтверждена HWND attribute. Это не pixel-perfect compositor/accessibility benchmark. Mica/Acrylic имеют явно обозначенный fallback; whole-window alpha влияет и на текст. Topmost выключен по умолчанию. Гарантированное размещение между значками и всеми обычными окнами не обещается. Spotlight/slideshow provider restoration не поддерживается.

## 8. Recovery / безопасность

**VERIFIED:** core staged apply/cancel/rollback/conflicts и native static wallpaper apply/restore, disable/gather/restart. Перед изменением сохраняются доступное исходное состояние и durable journal. Неопределённая операция не повторяется автоматически; более поздние изменения пользователя требуют решения.

Команды Workspace по-прежнему требуют отдельного consent. Импорт не содержит executable paths/JS/native host calls, не исполняет команды, не скачивает ресурсы и не повышает права. Телеметрия/платные API не добавлены. Буфер и файлы экспорта обычные, без шифрования; перед передачей нужно проверить текст.

**PARTIAL:** полная fault-isolation/crash/power/disk-failure матрица и физические shutdown/sleep сценарии. Общая оболочка оставалась доступной даже при обнаруженном до исправления menu crash. Все desktop/destructive smoke выполнялись только в ownership-guarded одноразовых каталогах CI.

## 9. SQLite migrations и совместимость

**VERIFIED в автоматических сценариях:** schema2 сохранена, новой миграции RC4→RC5 нет. Профили/история/настройки совместимы; atomic import не перезаписывает существующие данные, транзакция откатывается при поздней ошибке. Existing settings/history/runtime в sharing smoke остаются неизменными. Проверены upgrade данных, созданных настоящим опубликованным RC2, и current-version installer data preservation.

SQLite backup не включает Studio/Images и Studio/RecoveryNotes. Полная копия — весь каталог после Quit, не отдельный активный shift.db без WAL. Старый RC4 может отвергать новые допустимые style values Cascadia Mono/TemplateId65–100; downgrade требует отдельной копии данных. Удаление профиля/экземпляра удаляет его runtime-данные — сначала экспорт/backup.

**NOT RUNTIME VERIFIED:** все прежние версии установщика и полный installed Studio upgrade на физических машинах.

## 10. UI/UX и RU/EN

**VERIFIED в CI-сценариях:** русские Studio/Quick Setup/пресеты/импорт/экспорт, real language change без пересоздания 10 окон, многострочные поля, понятные inline validation errors, copy-style draft/cancel и клавиатурный список. Сохранены Dark, Light, Midnight и Forest. Ctrl+S сохраняет, Ctrl+Shift+S экспортирует отдельную Notes, Shift+F10 открывает меню.

[Русский быстрый старт](../docs/QUICKSTART-RU.md), [перенос и заметки](../docs/STUDIO-SHARING-RU.md), [реальные снимки](../docs/screenshots/rc6/README.md). Снимки получены из Windows-приложения с синтетическими данными; генеративных подмен нет. Видимость элементов в этих кадрах не доказывает весь DPI/Narrator protocol.

## 11. Performance

**VERIFIED только как короткие измерения процесса в CI.** Working set/private bytes — MiB; CPU нормирован на всю машину, интервал около 2 секунд. Это не steady-state idle, battery benchmark или сравнение с физическим ПК. Прогон последовательный; между 0/1 окнами также происходит инициализация Lab/host/JIT, поэтому разность не является чистой стоимостью одного виджета. Модель CPU и число логических ядер отдельно в raw report не зафиксированы; формула collector нормирует CPU на фактический ProcessorCount.

| Окон | Working set MiB | Private MiB | CPU всей машины | Sample ms |
| --- | --- | --- | --- | --- |
| 0 | 161.1 | 68.7 | 0.00% | 2001 |
| 1 | 302.2 | 185.4 | 0.00% | 2001 |
| 5 | 304.7 | 186.3 | 0.00% | 2000 |
| 10 | 311.1 | 191.5 | 0.00% | 2001 |

[Сырые значения](evidence/rc6/studio-performance-raw.json) · [Окружение](evidence/rc6/studio-native-environment.json).

**NOT MEASURED:** длительный idle, потребление батареи, Lab-open/profile-switch latency и физический sleep/resume. Значения не заменяются нулями.

## 12. Tests

**384 passed / 0 failed / 0 skipped.** Это 384 уникальных теста, не сумма одинаковой suite в Linux/Windows/повторных native builds. Прирост к 296 RC4: **88** — transfer/encoding/atomic integration48, Notes18, host presentation5, style compatibility7, localized date error1, native newline9.

Финальные [PR CI](https://github.com/LosarOS/SHIFT/actions/runs/40970021017), [main CI](https://github.com/LosarOS/SHIFT/actions/runs/40970866586) и [tag build](https://github.com/LosarOS/SHIFT/actions/runs/40970869902) связываются с конкретным source. Полный suite, locked restore, publish и обязательные gates записи validation прошли для тега; [WINDOWS-VALIDATION](evidence/rc6/WINDOWS-VALIDATION.json).

Дополнительно выполнены локальная Windows-reference compilation0warnings/errors (только compile), PowerShell parser, source hygiene и NuGet advisory audit шести проектов/transitive packages без reported vulnerabilities на дату проверки. Это не абсолютная гарантия отсутствия уязвимостей.

Неудачные прогоны сохранены в журнале: pool cleanup, native picker extension/probe, общий font contract, преждевременное Dispose меню и многострочные TextBox. Assertions не отключались ради зелёного CI.

## 13. Windows Runtime Validation

| Gate | Статус |
| --- | --- |
| Locked restore /384tests / self-contained publish / resources.pri | VERIFIED |
| Запуск EXE из распространяемого portable ZIP | VERIFIED |
| Native core, compact, consent, multiline редакторы | VERIFIED |
|20 Studio gates: widgets/Lab/transfer/Notes/keyboard/recovery | VERIFIED |
| Настоящие SaveAs/Open и clipboard после выхода | VERIFIED |
| Статичные обои: применение и точное восстановление | VERIFIED |
| Real RC2 DB upgrade без потери Workspace/history | VERIFIED |
| Install/shortcut/installed UI/repair/uninstall/reinstall/SQLite preservation | VERIFIED |
| Полная отдельная installed Studio матрица | NOT RUNTIME VERIFIED |
| Физическая Windows 10/11, mixedDPI/monitor/accessibility/sleep/RDP/fullscreen | NOT RUNTIME VERIFIED |

Проверены реальные программы, а не только Windows references на Linux. Но disposable Server UIA не является интерактивной физической Windows 11 приёмкой. [Незаполненный ручной протокол](../docs/studio/RC5-MANUAL-ACCEPTANCE-RU.md) содержит ожидаемые результаты и оставшиеся строки.

## 14. GitHub PR / commits

- [PR20](https://github.com/LosarOS/SHIFT/pull/20) объединён; head `0e06a7e3fc2db6650e7f782659609432142ac502`, проверенный PR CI.
- Merge/tag source `de8e744d6993b3293b55ed56675cb6472ef68fab`; tree совпал с проверенным head перед отправкой тега.
- Annotated tag `v1.0.0-rc.6`; [tag workflow](https://github.com/LosarOS/SHIFT/actions/runs/40970869902). Отчёт/снимки добавляются отдельным docs-only PR без изменения собранного source.
- Force-push, удаление релизов/истории и работа напрямую в main не выполнялись. Субагенты работали в отдельных зонах; их отчёты не заменяли фактические логи.

## 15. Release artifacts

Все шесть файлов реально прикреплены к публичному prerelease и повторно скачаны независимо. Сверены ненулевые размеры, API SHA256 digests, обе записи SHA256SUMS, tag/source и validation. Portable проверен на ZIP integrity, наличие MZ/SHIFT.exe/resources.pri/portable.flag и отсутствие пользовательской DB/data.

| Файл | Байт | SHA256 |
| --- | --- | --- |
| [SHA256SUMS.txt](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHA256SUMS.txt) | 207 | `55ffcd47d768aefc43381e46d3b2eccc22bd6c575f5bc7ec1955dd59e408e5d7` |
| [SHIFT-1.0.0-rc.6-win-x64-portable.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-1.0.0-rc.6-win-x64-portable.zip) | 106,613,701 | `e627c30f3e4a3a24765337ca8e55d84cbb47794479274f3c92136caf389195cf` |
| [SHIFT-1.0.0-rc.6-win-x64-setup.exe](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-1.0.0-rc.6-win-x64-setup.exe) | 72,542,204 | `4f893f8a05210f5ad9930f028d1288e2a92fdbe33cedefd8113f65894037eaae` |
| [SHIFT-Native-Smoke-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-Native-Smoke-Evidence.zip) | 5,426 | `ab5b5ccaba950578e28ec798cdc49dab3a2582effd72227d5abc8b4de50d91a2` |
| [SHIFT-Windows-UI-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-Windows-UI-Evidence.zip) | 3,132,059 | `00ba14e0032f7bc5b6d5479d120a4c549733d999a1c219a4ccf87ce31fcdf6ee` |
| [WINDOWS-VALIDATION.json](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/WINDOWS-VALIDATION.json) | 1,556 | `ce8aa4c77f5eb1842ad9f3b750bbc5083f737cbd2d8839d123aecfe27da90fb3` |

Бинарники **unsigned**. SmartScreen может предупреждать; отсутствие предупреждений не заявлено. Source ZIP GitHub не выдаётся за Windows-дистрибутив.

## 16. Download links

[Установщик](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-1.0.0-rc.6-win-x64-setup.exe) · [Portable](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHIFT-1.0.0-rc.6-win-x64-portable.zip) · [Контрольные суммы](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.6/SHA256SUMS.txt) · [Весь релиз](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.6).

Установщик — для текущего пользователя. Portable распаковать полностью в доступную для записи папку; portable.flag оставить рядом с SHIFT.exe. Перед обновлением: Quit и копия всей папки данных. [Инструкция](../docs/INSTALLATION.md).

## 17. Known Limitations

- Не stable1.0: физическая Windows 10/11/DPI/multi-monitor/Narrator/highcontrast/reducedmotion/sleep/RDP/fullscreen матрица не выполнена.
- Native Studio проверен в portable; установленный core/installer проверен, полная отдельная installed Studio матрица — нет.
- Только static wallpaper с явным подтверждением, без Spotlight/slideshow; autoapply — widget-only. Mica/Acrylic fallback не называется настоящим стеклом.
- Profile pack — конфигурация без файлов/runtime. Notes export — снимок без неоткрытых recovery drafts. Нужна отдельная полная копия для переноса всех данных.
- Notes/clipboard/SQLite/backups не зашифрованы; могут содержать личные сведения. Удаление профиля/экземпляра удаляет его runtime.
- Downgrade старого RC4 может отклонить новые допустимые style values; сохраняйте отдельную копию данных.
- Короткие process samples не подтверждают долговременную производительность. Полная fault/conflict/power matrix не проверена.
- Нет Authenticode-подписи; предупреждения SmartScreen возможны. Готовность сторонних приложений после dispatch не гарантируется.

## 18. Roadmap следующего выпуска

**P0 перед stable:** физическая Windows 11/поддерживаемая Windows 10, DPI/accessibility/monitor/sleep/RDP, installed Studio upgrade и долгий Notes/Timer/disk-failure soak, решение о подписи. При FAIL — воспроизведение и регрессия, а не исключение строки.

**P1:** долгие raw performance measurements и time-to-Lab/profile-switch, согласованный backup managed images/recovery drafts, улучшения широкой мастерской по фактической обратной связи.

**P2:** Quick Links и дополнительные безопасные действия/пресеты. Произвольный JS, Explorer hacks, скрытые платные API и повышение прав ради внешнего вида не планируются.

Репозиторий может нормализовать CRLF/BOM текстовых evidence-файлов; исходные байты и опубликованные SHA256 относятся к неизменённым release assets. PNG в manifest сохранены побайтно.
