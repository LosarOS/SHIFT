# SHIFT 1.0.0-rc.7 — рабочее пространство под контролем

Большое обновление Productivity & Reliability: новые инструменты настройки рабочих пространств и защита изменений оформления Studio. Неподписанный Windows x64 prerelease.

## Новые возможности

- **Вставить список.** Добавляйте до 100 путей или HTTP/HTTPS ссылок одним списком. Выберите общий тип, просмотрите ошибки/повторы, отметьте строки, задайте задержку и сохраните. По умолчанию новые элементы выключены. Команды и параметры не угадываются; пути не открываются, сайты не проверяются по сети.
- **План запуска.** Снимок фактического порядка, включённых/пропущенных шагов, команд и накопленных настроенных задержек. Повторы одинаковых целей/параметров помечаются. Предпросмотр ничего не запускает и не пишет историю; из него доступна существующая «Проверка пространства».
- **RU/EN и доступность.** Оба сценария имеют локализованные пояснения, клавиатурные контролы, явные состояния и стабильные automation IDs. Отмена предпросмотра не сохраняет изменения.

## Надёжность Studio

Подтверждённый конфликт записей из последнего RC6 main CI исследован по сохранённому настоящему логу. LastApplied теперь записывается атомарно как метаданные без изменения configuration revision; время не откатывается назад при сохранении старого редакторского снимка.

Положение виджетов сохраняется атомарно по отдельным экземплярам с проверкой ранее подтверждённого состояния. Изменения редактора, скрытие, удаление или замена виджета не перезаписываются фоновой геометрией. Конфликт явно показывается; повторные тики не затирают новые значения. Режим и «Собрать виджеты» сохраняют новые размеры/topmost/режим, отделяя временный экранный clamp от preferred size.

Без blind retry, подавления произвольных ошибок, новой схемы базы или дополнительных платных сервисов.

## Проверки

Фактические результаты привязаны к source/tag в приложенном WINDOWS-VALIDATION.json и native evidence. Публикация допускается только после полного core suite, настоящих portable/installed Windows-сценариев, RU/EN Composer/Plan, отсутствия dispatch/history side effects, Studio, installer repair/uninstall/reinstall и SQLite preservation. До этого готовый RC7 не объявляется.

## Установка и ограничения

Windows 10 build19041+ / Windows 11, x64. Установщик — для текущего пользователя; portable распаковать полностью. Проверить SHA256SUMS.txt. Перед обновлением: Quit и копия всего каталога данных, включая Studio/Images и Studio/RecoveryNotes. SQLite schema 2 сохранена.

Бинарники unsigned; возможен SmartScreen. Автоматизация выполняется на disposable Windows Server, а не физическом ПК Windows 11. Mixed DPI/multi-monitor, Narrator/highcontrast, sleep/RDP/fullscreen, длительный soak и полная отдельная installed Studio матрица остаются отдельными gates. Только явные статичные обои; Spotlight/slideshow не поддерживаются; Mica/Acrylic используют обозначенный fallback. Stable 1.0 не объявлен.

Composer проверяет формат, а не существование/содержимое цели. Повторы URL сравниваются с учётом регистра пути/query. Ссылки с явными credentials не добавляются массово. Аргументы приложений и shell-команды редактируются отдельно с обычным consent flow.

## English

RC7 adds reviewed multi-item workspace composition and a read-only launch plan with ordering, enabled/skipped steps, command counts, configured delay budget and repeated-target warnings. New items are disabled by default; no shell inference, hidden network checks or launch/history side effects.

Studio now separates informational LastApplied from configuration revisions and saves layout edits atomically per instance against acknowledged baselines. Editor geometry/deletions/replacements are protected; mode/gather preserve newer saved properties and preferred sizes. No schema/package change, blanket retry or error suppression.

Unsigned Windows x64 prerelease. Consult attached actual validation/evidence and SHA256SUMS. Disposable Server UIA does not replace physical Windows 10/11, DPI/accessibility/multi-monitor/sleep acceptance.
