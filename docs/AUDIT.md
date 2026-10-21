# Аудит кандидата 1.0

Дата: 2026-10-21. Это ревизия исходного кода, зависимостей и сценариев отказа; она не заменяет независимый penetration test или проверку Windows runtime.

| Область / найденный риск | Исправление | Проверка |
|---|---|---|
| Self-contained WinUI принудительно включал bootstrap установленного App SDK | Убрана принудительная настройка; используется undocked reg-free initialization | Effective MSBuild properties: SelfContained=true, Bootstrap пусто, UndockedRegFree=true; инвариант проверяет packaging script. Чистая Windows VM ещё нужна |
| SQLite async API мог выполнять операции на UI thread | LocalDataStore сериализует операции в background tasks | 40 одновременных сохранений, отмена и повторное использование; отдельный диагностический harness |
| Ошибка durable history могла трактоваться как ошибка одного элемента | Persistence failures останавливают очередь до dispatch | Регрессионный test: dispatcher не вызван, Interrupted сохранён |
| Raw URL с кавычками мог попадать в Windows handler | Canonical AbsoluteUri; env vars в URL не раскрываются | Quotes/spaces escaping, отдельные URL/environment tests |
| Неоднозначные Windows paths / script type bypass | Проверка расширенного пути, ADS, управляющих символов, reserved names; scripts требуют App/Command | Негативные тесты путей, переменных, URI и типов |
| Null JSON/domain fields и duplicate keys | Структурированные validation issues; null guards; запрет duplicate JSON properties | Null/malformed/oversized/UTF-8/BOM/duplicate tests |
| Восстановление могло принять файл с чужой SQLite-схемой | Integrity, foreign keys, table/column/settings validation до замены; safety backup; WAL checkpoint; atomic replacement | Restore roundtrip, malformed DB, fake schema, cancelled restore; исходные данные остаются |
| Копирование только main DB теряло committed страницы WAL выбранной копии | SQLite online snapshot вместо побайтового копирования; валидация именно этого снимка | Живая исходная WAL-база восстанавливается с последним committed именем; старый код воспроизводил потерю |
| Схема могла быть корректной при повреждённых значениях | Проверка domain models и всей истории страницами до замены; read-only unpooled connections | 8 вариантов плохих данных, повреждение за первой страницей, текущие данные сохраняются |
| Read-only проверка WAL-снимка оставляла временные sidecars | Приватный снимок переводится в DELETE journal mode; очистка snapshot и sidecars в finally | Проверка отсутствия временных файлов после каждого отказа |
| Экспорт мог превышать лимит собственного импорта | Проверка UTF-8 размера результата до picker и записи | Большой валидный workspace с Unicode-командами получает локализуемую ошибку |
| UI мог запускать новые операции во время restore / выхода | Maintenance guard; ожидание активных действий при выходе; dialogs нельзя закрыть посреди сохранения | C# compile; Windows interaction smoke подготовлен, ещё не выполнен |
| Действие, начатое до restore, могло записать старое состояние после него | Повторная проверка in-flight действий после confirmation; restore отклоняется, пока они не завершены | C# compile; отдельный Windows сценарий в QA |
| Повторный Launch мог перезаписать ссылку на активную задачу; импортированный Order=int.MaxValue переполнялся при добавлении | Переход к текущей очереди без новой задачи; нормализация порядка при добавлении | C# compile; интерактивная проверка в QA |
| Dashboard считал только отображаемые 100 записей | COUNT/usage по всей БД, отдельная пагинация и recent list | 123 истории, поиск кириллицы, фильтр ошибок, страницы без пересечений |
| Повторный экземпляр молча завершался | Ограниченный ACTIVATE pipe с CurrentUserOnly | IPC test; Windows tray/foreground smoke подготовлен |
| В исходники попали build tool registry files | Удалены из неопубликованного initial commit; .mono исключён; offline source hygiene gate | git/source scan; актуальный архив собирается только из исходников |
| CI actions использовали изменяемые теги | Pins на commit SHA, Dependabot для обновления | Проверены GitHub refs; YAML parsing |
| Ручные копии и команды могут содержать личные данные | Никакого облака/телеметрии; тексты не пишутся специально в diagnostic log; документировано отсутствие шифрования | Ревизия data flows; OS messages могут содержать пути |

## Выполненные автоматические проверки

- 63 unit/integration tests на .NET 10 / Linux.
- WinUI C# Compile target без ошибок/предупреждений (не полный Windows publish).
- NuGet audit с transitive dependencies: известных advisory по текущим источникам нет. NU1901–NU1904 включены в errors на restore.
- Offline source hygiene: credential patterns и недопустимые build/data файлы. Совпадения не печатаются, только location/type.
- Синтаксис PowerShell/YAML/XML/JSON проверяется отдельно.
- Synthetic storage/queue diagnostics: 100 пространств / 2000 элементов / 1000 записей запуска / 20000 строк истории. Данные изолированы, программы не открываются.

## До стабильной 1.0 остаётся подтвердить

Windows build/publish, реальное UI, portable и installed smoke, чистая VM, DPI/high contrast/Narrator, системный startup, shutdown/logoff, performance UI и signing policy. Prepared smoke включает настоящий безвредный command dispatch только после явного consent и проверяет отсутствие dispatch после отказа.

Команда или приложение остаются доверенным кодом пользователя. Проверка типов и подтверждение не делают SHIFT sandbox. Браузерные вкладки/состояния, позиционирование чужих окон и произвольные third-party handlers не гарантируются.
