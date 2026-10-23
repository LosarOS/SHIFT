# SHIFT Studio — инженерный отчёт RC3

## 1. Executive Summary

Реализован экспериментальный SHIFT Studio. Основной WinUI3-продукт сохранён; Studio включается отдельно. Исходный коммит сборки: `dd9d047d17f395902173c7231d2b2a59fdb843d4`. Тег: `v1.0.0-rc.3`. Статус: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**. Все 6 публичных assets повторно скачаны; размеры, SHA256 и исходный коммит проверены. Это не stable1.0.

## 2. Архитектура

VERIFIED на уровне кода и автоматических проверок: Domain/Application/Infrastructure/App, отдельные definitions/instances/layout/runtime state, единый gate операций оформления, отдельный жизненный цикл окон. Главная оболочка остаётся WinUI3. Простые виджеты используют управляемые Win32/Forms tool windows на одном STA без нового процесса и без внедрения в Explorer.

## 3. Desktop Profiles

VERIFIED на disposable Windows: сохранение, ручное применение статичных обоев по мониторам, отдельные результаты и возврат исходных значений. CRUD/clone/привязки/ограничения покрыты автоматическими проверками. Нет изменения Windows от открытия или сохранения карточки. Профиль не предоставляет разрешение на команды пространства.

## 4. Widget Runtime

VERIFIED на Windows Server2025runner: Clock с реальным временем; CPU/RAM; Focus Timer с фактическим завершением; Notes с Unicode и сохранением. Проверены4 окна, reapply без дубликатов и устаревшего текста, Gather/Edit, отключение/включение,10 окон после restart и выход. Обычный z-order; Always on Top по умолчанию выключен. Мониторные/sleep/fullscreen сценарии на физическом ПК не подтверждены.

## 5. Widget Lab

PARTIAL относительно полного целевого дизайна: реализован компактный property editor с настоящим внешним preview, локальным макетом, draft/cancel/save,6 шаблонами и строгим JSON. Native preview/cancel/save проверены UI Automation; отдельный свободный графический canvas и полноценный менеджер пользовательских style templates отложены. Сохранённые пользовательские определения доступны, клонируются и экспортируются.

## 6. Workspace Integration

IMPLEMENTED / NOT RUNTIME VERIFIED для полной новой комбинационной GUI-матрицы: независимый запуск приложений и применение оформления, явная привязка, автоматическое применение выключено по умолчанию и ограничено профилями без обоев. Команды используют прежний consent flow. Базовые command refusal/approval/dispatch и повторный запуск проверены нативным smoke. Ошибка оформления не инициирует повторный dispatch приложений.

## 7. Windows API

IDesktopWallpaper; read-only monitor topology; GetSystemTimes/GlobalMemoryStatusEx; обычные HWND и documented layered-window alpha. VERIFIED: реальная смена/возврат статичного изображения, alpha217 при opacity.85. Acrylic/Mica/Frosted Glass: NOT IMPLEMENTED как настоящие desktop backdrops, явно обозначен fallback. Spotlight/provider state недоступен для надёжного восстановления; такие сценарии не поддерживаются.

## 8. Recovery / безопасность

VERIFIED автоматическими тестами и native static-wallpaper smoke: validate/capture/managed originals/journal/action intent/apply/results; частичный результат и отмена; ambiguous outcomes; compare-before-restore; конфликт позднего пользовательского выбора; повторный restore без повтора завершённого действия. После сбоя нет автоматического повторного применения. Импорт декларативный, не исполняет команды/JS; elevation, Explorer hacks, платные API и телеметрия отсутствуют.

## 9. SQLite migrations

VERIFIED: v1→v2 с online snapshot до транзакционной миграции, WAL, FK, revision CAS, проверка private restore snapshot до замены рабочей БД. Реальная опубликованная RC2 создала v1 workspace/history; данные сохранились при тестовом upgrade. Старая версия отказывается открывать schema2. Полный backup Studio требует всей папки данных: SQLite не содержит image files и note drafts. Удаление экземпляра/профиля удаляет его runtime state; нужные заметки предварительно экспортируйте.

## 10. UI/UX

PARTIAL: Studio Overview/Profiles/Widgets/Lab/Settings-Recovery, RU/EN, существующие4 темы, постоянные Disable/Restore/Gather/Edit. Проверены настоящие скриншоты Windows, доступные в release evidence. Компактный редактор использует прокрутку. Полная RU/EN, DPI100/125/150/200%, Narrator/highcontrast/reduced-motion матрица на пользовательских Windows ещё не пройдена.

## 11. Performance

Финальный tagged native run40033786096, один1024×768монитор, короткие2s process samples:

| Окон | Working set bytes | Private bytes | CPU всей машины, % |
|---:|---:|---:|---:|
|0|162168832|57704448|0 наблюдалось|
|1|254054400|127094784|0 наблюдалось|
|5|257597440|129409024|0 наблюдалось|
|10|264376320|134676480|0.39057674424324873|

Нули — результат короткого измерения, не отсутствие потребления. Это не физический idle/battery benchmark. Время открытия Lab, полное время применения профиля и реальный sleep/resume: NOT MEASURED. Сырые итоговые измерения прикреплены к выпуску отдельно.

## 12. Tests

190 тестов,0failed,0skipped на Windows и Linux в финальном PR CI40033118068. Это два выполнения одной suite, не380 уникальных тестов. Linux не заменяет native Windows branches. Отдельно выполнены source hygiene, PowerShell parser, dependency audit Infrastructure без известных уязвимостей в текущем NuGet feed. App-only локальный audit не выполнен из-за отсутствия assets; это не заявляется как полная security certification.

## 13. Windows Runtime Validation

VERIFIED в автоматизированной графической сессии disposable Windows Server2025: настоящий WinUI publish/resources.pri, extracted portable, реальные окна/данные/обои, installer/start/reinstall/uninstall/preservation. Это не ручная приёмка физических Windows10/11. Studio установленной версии не прошёл полную отдельную интерактивную матрицу.

## 14. GitHub PR / commits

[PR13](https://github.com/LosarOS/SHIFT/pull/13) объединён. Финальная feature revision66a1e9f0c999809ee5485419d695961a6e71c36a; merge/source dd9d047d17f395902173c7231d2b2a59fdb843d4. [Финальный PR CI](https://github.com/LosarOS/SHIFT/actions/runs/40033118068), [main CI](https://github.com/LosarOS/SHIFT/actions/runs/40033767062), [релизная проверка](https://github.com/LosarOS/SHIFT/actions/runs/40033786096). Main/RC2 не переписывались, force-push не использовался; Quick Setup PR10 остаётся независимым.

## 15. Release artifacts

Ожидаемые настоящие Windows x64 файлы: portable ZIP, setup EXE, SHA256SUMS.txt, WINDOWS-VALIDATION.json, raw UI/Smoke Evidence ZIP. Бинарники неподписанные; SmartScreen может предупреждать. Публикация требует отдельного успешного tagged build и проверки GitHub-downloaded bytes.

## 16. Download links

[Release page](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.3). Доступность/размеры/SHA256 проверяются в финальном checkpoint ниже, прежде чем эти ссылки считать подтверждёнными загрузками.

## 17. Known Limitations

NOT VERIFIED: физические Windows10/11, несколько мониторов/разные DPI, hot-plug/RDP, реальный sleep/resume, fullscreen/Windows+D, Narrator/highcontrast и полная installed Studio матрица. Spotlight/slideshow/пустые или недоступные исходные обои не поддерживаются. Прозрачность всего окна влияет и на текст; Mica/Acrylic fallback. Автообои не включены. SQLite backup не является архивом ресурсов. Скриншоты CI являются диагностическими, без ретуши и генеративной подмены.

## 18. Roadmap следующего выпуска

P0: ручная приёмка на Windows11 и Windows10 с backed-up профилями; multi-monitor/DPI/accessibility/sleep/fullscreen; дополнительные installed widgets/upgrade сценарии. P1: улучшение Lab и профилей по результатам реального использования, долгие performance/idle замеры, восстановление portable resources. P2/P3: Quick Links, пресеты, расширенные styles/backdrops. HTML/JS/WebView2, taskbar/accent/Widgets Board требуют отдельных ADR и модели безопасности; в пользовательский runtime не включены.

## Финальный checkpoint публикации

2026-10-23: release workflow40033786096 и main CI40033767062 SUCCESS. Публичный prerelease доступен;6assets скачаны независимо от workflow, ненулевые размеры и GitHub asset digests совпали с фактическими SHA256. Бинарные SHA256 дополнительно совпали с SHA256SUMS.txt. WINDOWS-VALIDATION.json указывает точный source dd9d047d17f395902173c7231d2b2a59fdb843d4 и190/190/0/0 tests. Native evidence: note_saved=true, timer_completed=true, wallpaper_journal_restored=true, WALLPAPER_RESTORED. Финальный настоящий desktop screenshot просмотрен:4виджета без перекрытия и без открытых системных ошибок. Исходники RC2 и его релиз сохранены.

| Файл | Размер bytes | SHA256 |
|---|---:|---|
|[SHA256SUMS.txt](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/SHA256SUMS.txt)|207|`f37946aa001c0d669f4f3e0be6d12a50a86802bf26fc3fcad3646f0098e59516`|
|[SHIFT-1.0.0-rc.3-win-x64-portable.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/SHIFT-1.0.0-rc.3-win-x64-portable.zip)|106541220|`ff154af6a22c1565be5f34c176d104202735328e7c6b1a51cd062b0ff4178cef`|
|[SHIFT-1.0.0-rc.3-win-x64-setup.exe](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/SHIFT-1.0.0-rc.3-win-x64-setup.exe)|72504328|`d5cc8d3b9dfaf6c5b05296d3fe8348fb31acfcbeb5dd9152996d624e4e1609cf`|
|[SHIFT-Native-Smoke-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/SHIFT-Native-Smoke-Evidence.zip)|3557|`b2fa1d8ad19f7a0cbe0ec6d6b6794f407da003015bcf0c8f013b08784e9de2c6`|
|[SHIFT-Windows-UI-Evidence.zip](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/SHIFT-Windows-UI-Evidence.zip)|1381877|`79194245ed8482ab25964fb5298506d9d4a46edc3409dededb95fc3bb108fd54`|
|[WINDOWS-VALIDATION.json](https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.3/WINDOWS-VALIDATION.json)|1126|`2f8489be13d6c2cada4216fb292dc2a069239cbc84b495157997871c13044b26`|

Сырые подтверждения: [public verification](evidence/studio-rc3/independent-public-verification.json), [Windows validation](evidence/studio-rc3/WINDOWS-VALIDATION.json), [native smoke](evidence/studio-rc3/studio-smoke.json), [performance](evidence/studio-rc3/studio-performance-raw.json). Этот documentation-only checkpoint не изменяет source tag или опубликованные бинарники.
