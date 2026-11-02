# SHIFT RC7 — большой релиз Productivity & Reliability

Дата: 2026-11-02.
Статус релиза: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**.
Версия: **1.0.0-rc.7**, Windows x64, unsigned.
Source/tag: `7bb1760b7095bcc254b25807b9eb2934cd807919` / `v1.0.0-rc.7`.

## 1. Executive Summary

RC7 добавляет два законченных сценария работы с пространством: массовое добавление с проверкой и выбором строк, а также read-only план запуска. Улучшены русские и английские интерфейсы этих сценариев, точность диагностики и доступность элементов управления. Исправлена причина конкурирующих сохранений Studio и два обнаруженных при review пути потери более новых настроек.

**461 уникальный core-тест**, 0 failed, 0 skipped, на Windows и Linux. Это на **77** больше RC6: 51 случай Composer/Plan/localization/persistence и 26 layout/metadata/acknowledgement. Повторные CI и две платформы не суммируются как новые тесты.

Получены настоящие portable ZIP и установщик EXE. Все шесть публичных assets отдельно скачаны после публикации, сверены по размеру и SHA256; проверен source/tag и содержимое native evidence. Stable 1.0 не объявлен. Физическая Windows 10/11 и полная аппаратная/accessibility матрица остаются непроверенными.

## 2. Архитектура

Сохранены C#/.NET 10, WinUI 3, Windows App SDK, SQLite и направление Domain → Application → Infrastructure / Presentation. Новых сторонних пакетов, облака, телеметрии или платного API нет.

`WorkspaceComposer` и `WorkspaceLaunchPlanner` — отдельные Application-сервисы. Первый строит ограниченный предпросмотр и новый снимок пространства, второй проецирует реальный порядок координатора. UI в `Views/WorkspaceProductivity.cs` управляет диалогами; доступ к SQLite проходит существующий store.

Studio использует отдельный контракт compare/write для расположения и отдельную атомарную запись информационного LastApplied. Механизм восстановления обоев и существующий command consent сохранены.

## 3. Workspace Composer — VERIFIED

Кнопка «Вставить список» доступна из деталей пространства:

- Явный общий тип App / Folder / File / URL; до 100 непустых строк и 65536 символов.
- Проверка формата, повторы, ошибки по строкам, выбор строк и общая задержка 0–300000 мс.
- Новые элементы **выключены по умолчанию**. Сохранение ничего не запускает.
- «Назад» сохраняет введённый текст; отмена не пишет данные.
- Снимок сверяется перед записью. Изменившееся пространство не перезаписывается старым preview.
- После успешного Save ошибка обновления оболочки не приводит к повторному добавлению.
- Unicode и CR/LF/CRLF; имена не обрезаются внутри surrogate pair.
- Нет угадывания shell/аргументов, скрытых проверок сети или чтения буфера без действия пользователя. URL с явными credentials не принимаются массовым вводом.

Это проверка **формата**, а не доказательство существования/безопасности файла или готовности приложения. Для локальной диагностики остаётся Workspace Doctor. [Руководство](../docs/WORKSPACE-PRODUCTIVITY-RU.md).

## 4. План запуска — VERIFIED

Кнопка «План запуска» показывает точный порядок Order → ID, включённые и пропущенные шаги, число команд, оболочку PowerShell/cmd, цель, параметры, рабочую папку и накопленную сумму настроенных задержек. Выключенные задержки не входят в сумму. Одинаковые включённые цели/аргументы/рабочие папки отмечаются как повтор; shell участвует в идентичности команды.

План ничего не запускает, не пишет историю и не измеряет готовность приложений. Время команды, открытия вкладок и ожидание подтверждения в бюджет не входят. Действие «Проверить цели» открывает существующую диагностику.

## 5. Надёжность Studio

**VERIFIED — core concurrency/persistence; нормальные native lifecycle/mode/gather.**
**IMPLEMENTED / NOT RUNTIME VERIFIED — принудительно воспроизведённые живые конкурентные конфликты во всей аппаратной матрице.**

Исходный main CI40972038240 имел настоящий ERROR Widget update failed / Studio data changed / SaveProfileAsync revision conflict. Сохранённый собственный artifact прочитан read-only в audit41510313816. SQLite защитила данные, но фоновые записи могли конфликтовать. Точный порядок старых конкурирующих операций по одному логу не восстановлен; конкретные кодовые interleavings закрыты регрессионными тестами.

Теперь:
- LastApplied записывается атомарно, монотонно и не меняет configuration revision.
- Фоновая геометрия сохраняется по отдельным экземплярам с expected identity/definition/visibility/layout/mode.
- Новые правки редактора, скрытие/удаление/замена экземпляра защищены; частичные конфликты не прячутся blind retry.
- Edit/Desktop не подтверждает неприменённую чужую геометрию.
- «Собрать» строит новое расположение из актуальных сохранённых свойств, сохраняя размер/topmost/режим.
- Временный clamp под экран не затирает preferred size; Notes/Timer сохраняются отдельно.

Нельзя считать все конфликтные сценарии живого UI пройденными только на основании SQL/unit-тестов.

## 6. Desktop Profiles и Workspace Integration

Существующие независимые профили, opt-in связь с пространством, явное применение и раздельные операции визуального оформления/dispatch сохранены. Auto-apply по умолчанию выключен и ограничен профилями без обоев. Команды пространства сохраняют обычное подтверждение.

В RC7 не добавлялась атомарная «транзакция Windows+приложения»: такой гарантии нет. Ошибка оформления не должна повторять уже отправленные приложения.

## 7. Widget Runtime и Widget Lab

Сохранены Clock, System Monitor, Notes и Focus Timer; собственные управляемые окна, общий scheduler, native preview, draft/cancel/save, шесть встроенных стилей и reuse appearance. RC7 не добавляет WebView2/JS widgets и не внедряется в Explorer.

Реальный gate native Notes проверяет строку Windows-контрола после многострочного initial text. Это сохраняет исправление RC6. Перенос профилей, Notes export, keyboard/menu и действительные file picker/clipboard также прошли tagged gates.

## 8. Windows API и визуальные ограничения

Обои: поддерживаемый IDesktopWallpaper, явное подтверждение статичного оформления, managed originals и recovery journal. Slideshow/Spotlight не обещаются и не обходятся недокументированными механизмами. Always on Top остаётся opt-in; гарантированного слоя между значками и всеми обычными окнами нет.

Проверенный alpha HWND — прозрачность целого окна, **не Acrylic/Mica**. Для неподдерживаемых эффектов используется обозначенный fallback. Ни администратор, ни Explorer patch, ни WorkerW, ни глобальный hook не нужны.

## 9. Recovery и безопасность

Прежний staged apply сохраняет исходное состояние и intent до изменения. Откат сравнивает текущие значения с изменениями SHIFT; более поздние изменения пользователя не перезаписываются молча. После сбоя нет автоматического повторного apply.

На выделенном disposable Windows runner подтверждено применение и точное восстановление обоев; cleanup: WALLPAPER_RESTORED. Данные/обои владельца в этой сессии не использовались.

Composer/Plan не исполняют команд и не создают историю. Native refusal/approval и отсутствие dispatch side effects проверены. Это не сертификация безопасности произвольных приложений/ярлыков. Экспорт, SQLite и логи остаются незашифрованными; не храните секреты в аргументах.

## 10. SQLite и обновление

**Schema 2 сохранена; новой миграции RC7 нет.** Реальное RC2 v1 → v2 обновление повторено на disposable Windows с фактически скачанной RC2. Пройдены portable/installed restart и installer repair/uninstall/reinstall с сохранением данных.

Перед обновлением: Quit и копия **всего** каталога данных, включая Studio/Images, Studio/RecoveryNotes и migration-backups. SQLite backup и .shiftstudio.json не заменяют эту копию. RC6 tag/assets сохранены; downgrade поверх новой базы без проверки не рекомендуется.

## 11. UI/UX и русская локализация

Новые действия/ошибки/подсказки локализованы RU/EN. Native acceptance открывает обе языковые версии Composer/Plan; стабильные automation IDs не заменяют доступных имён и настоящих контролов. ShiftDialog задаёт локальные IDs шаблонным кнопкам без замены темы WinUI.

Существующие Dark, Light, Midnight, Forest, компактный лаунчер, трей и навигация сохранены. Новые русские инструкции встроены в README, quickstart и отдельное руководство.

[Настоящие снимки RC7](../docs/screenshots/rc7/README.md) получены работающим приложением на disposable Windows Server с синтетическими данными; PNG перенесены без изменения байтов, source/исходные имена/SHA256 в manifest. Визуально просмотрены все шесть опубликованных кадров: RU/EN Composer, русский план, Studio, desktop с четырьмя виджетами и Compact Launcher. Открытых системных ошибок нет; видимые watermark Windows Server и тестовые имена сохранены. Длинный Composer прокручивается; кадр не показывает весь список одновременно.

## 12. Performance

Фактическая среда: disposable Windows Server 2025 Datacenter, один виртуальный монитор 1024×768; около 2 секунд на sample. [Сырые значения](evidence/rc7/studio-performance-raw.json).

| Виджетов | Working set, bytes | Private bytes | CPU всей машины, % |
| --- | ---: | ---: | ---: |
| 0 | 168914944 | 71233536 | 0.000000 |
| 1 | 312205312 | 189394944 | 0.000000 |
| 5 | 316387328 | 193167360 | 0.195139 |
| 10 | 320651264 | 196132864 | 0.195291 |

Это короткие process samples в CI, а не долговременный idle/battery benchmark. Нулевой образец CPU не означает нулевой расход. Widget Lab latency, полный профиль apply latency и реальный sleep/resume: **NOT MEASURED**. Физическая multi-monitor производительность не измерялась.

## 13. Tests и Windows Runtime Validation

| Gate | Фактический результат |
| --- | --- |
| Core Windows / Linux | 461 passed, 0 failed, 0 skipped |
| Locked restore / native self-contained publish / resources.pri | PASSED |
| Portable startup / persistence / command consent | PASSED |
| RU Composer/Plan preview и cancel | PASSED |
| EN Plan read-only / Composer selected save-disabled / cleanup | PASSED, portable и installed |
| Dispatch/history side effects нового UI | Отсутствуют в проверенном сценарии; marker/history checks passed |
| Studio 20 native gates / 4 widgets / Notes / timer / restart | PASSED |
| RC2 migration / wallpaper apply + exact restore | PASSED |
| Sharing / file picker / clipboard / live HWND/data unchanged | PASSED |
| Installer startup / repair / uninstall / reinstall / data preservation | PASSED |
| Все 6 публичных assets: повторное скачивание, размер, SHA256 | PASSED |
| Физическая Win10/11 / mixed DPI / Narrator / sleep/RDP | NOT VERIFIED |

Ранние неуспешные CI не выданы за успех: исправлены UIA synchronization, неверное имя SelectionPattern API, framework automation ID precedence и неинициализированная collection доказательств под StrictMode. Проверки не отключались. После последнего изменения выполнены полные PR, main и canonical tag gates.

Локальный managed executor не был доступен: **локальные builds/tests не запускались**. Реальные проверки выполнены GitHub Actions. [Исходные JSON/txt](evidence/rc7/) и [публичная сверка](SHIFT-RC7-PUBLIC-VERIFICATION.md).

## 14. GitHub, review и source

- Feature PR: [#22](https://github.com/LosarOS/SHIFT/pull/22), 34 файла, +1484/−84; merged.
- Финальный feature head: `247342ee898d6397afa767896253f307eba62027`.
- Merge/tag binary source: `7bb1760b7095bcc254b25807b9eb2934cd807919`.
- [PR CI41520052346](https://github.com/LosarOS/SHIFT/actions/runs/41520052346): все 4 jobs success.
- [Main CI41521398106](https://github.com/LosarOS/SHIFT/actions/runs/41521398106): все 4 jobs success.
- [Canonical tagged Windows release41522532771](https://github.com/LosarOS/SHIFT/actions/runs/41522532771).
- [Независимая сверка публичных bytes41522718987](https://github.com/LosarOS/SHIFT/actions/runs/41522718987), success; 2026-11-02T20:56:28.586745+00:00.
- Изолированный concurrency diff f74788b интегрирован настоящим two-parent commit af1e153; независимый review обнаружил и закрыл Mode/Gather overwrite до финальных gates.
- Документальный follow-up не меняет source опубликованных бинарников и не перезаписывает tag/assets.

## 15. Release artifacts и Downloads

| Файл | Размер, bytes | SHA256 |
| --- | ---: | --- |
| [SHA256SUMS.txt](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/SHA256SUMS.txt) | 207 | `85e07f228e85bc9af1500ba536698f031402829478367abbea8aae7893c22d2b` |
| [SHIFT-1.0.0-rc.7-win-x64-portable.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/SHIFT-1.0.0-rc.7-win-x64-portable.zip) | 106636000 | `4ee847d358c89eab5b3add2228db9c4d5f51762595ddd60776086595d2d8d98d` |
| [SHIFT-1.0.0-rc.7-win-x64-setup.exe](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/SHIFT-1.0.0-rc.7-win-x64-setup.exe) | 72570378 | `27713424de60bb4cf4f39a6e41a0bba5340ea6b07f6a53545a3d8c8c2bcdcce2` |
| [SHIFT-Native-Smoke-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/SHIFT-Native-Smoke-Evidence.zip) | 5569 | `41c1761f31f01c2c835abe38d193b2dbb802b1d02f13bc8e89158731900d99c4` |
| [SHIFT-Windows-UI-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/SHIFT-Windows-UI-Evidence.zip) | 3735830 | `84194043364c1ee30ea2d249e9c1319900d8eeea561f54cdac8cc8c0cccf4bc6` |
| [WINDOWS-VALIDATION.json](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.7/WINDOWS-VALIDATION.json) | 1668 | `da12a99433703db10a43c616f64f0f19365cddfd5416a061d8fd145bb4951143` |

SHA256SUMS.txt содержит хеши portable и installer. Для остальных файлов независимо проверены GitHub API digest и реальные скачанные байты. Source commit в WINDOWS-VALIDATION.json совпадает с tag.

## 16. Visual Evidence

- [План запуска RU](../docs/screenshots/rc7/launch-plan-ru.png).
- [Composer RU](../docs/screenshots/rc7/workspace-composer-ru.png).
- [Composer EN](../docs/screenshots/rc7/workspace-composer-en.png).
- [Studio Overview](../docs/screenshots/rc7/studio-overview.png).
- [Desktop widgets](../docs/screenshots/rc7/desktop-widgets.png).
- [Compact Launcher](../docs/screenshots/rc7/compact-launcher.png).

Не концепт-рендеры и не физическая Windows 11. Ограничения кадра/runner не скрывались ретушью.

## 17. Known Limitations

Unsigned / возможен SmartScreen. Физические Windows 10/11, DPI100/125/150/200%, несколько мониторов/hotplug, RDP, sleep/resume, fullscreen/z-order, Narrator/high contrast/reduced motion, крупный текст и полный отдельный installed Studio/fault/concurrency matrix: **NOT VERIFIED**.

Composer анализирует формат, а не содержимое/наличие/безопасность целей. Launch Plan — настроенные задержки, не время выполнения. Arbitrary shell safety не оценивается.

Полные Mica/Acrylic, HTML/JS widgets, системная панель задач, browser session capture и автоматическое восстановление произвольных окон: **NOT IMPLEMENTED** в этом обновлении. Profile packs — конфигурация, не полный backup; Notes export не включает неоткрытые recovery drafts.

## 18. Next Steps

1. [Ручная приёмка RC7](../docs/studio/RC7-MANUAL-ACCEPTANCE-RU.md) и существующая физическая Studio матрица.
2. Forced live concurrent edit/mode/gather, длительный autosave/quit/restart soak на реальной Windows.
3. Подпись бинарников и проверка clean-machine/SmartScreen distribution.
4. Дальнейшие функции — только после сохранения этих gates: window-layout capture, дополнительные быстрые действия и более удобная per-monitor настройка.

[Релиз](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.7) · [Русский быстрый старт](../docs/QUICKSTART-RU.md) · [Журнал](SHIFT-RC7-ENGINEERING-SESSION.md).
