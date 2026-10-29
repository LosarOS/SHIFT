# SHIFT 1.0.0-rc.6 — заметки, строка за строкой

Неподписанный Windows x64 prerelease. Обновление исправляет подтверждённый после выпуска RC5 дефект на границе WinUI и native Notes. Публичные файлы предыдущих релизов не заменены.

## Исправление

Редактор WinUI может возвращать перенос строки как CR, а импортированный текст — как LF. Native Windows edit-контрол ожидает CRLF: на неизменённом RC5 вторая строка шаблона оказалась на native line0 вместо1. Теперь отображение получает CRLF, а runtime-снимки сохраняют LF. Повторный запуск не наращивает переносы; Unicode, пробелы и пустые строки сохраняются. Предел данных20тыс.символов остаётся прежним даже при расширении CRLF в окне.

Нормализация применяется при начальном открытии Notes, восстановлении recovery draft и выборе состояния базы. Для других Unicode-разделителей и пробельных символов подмен нет. Старые CR/LF/CRLF данные читаются без миграции и массовой перезаписи.

## Возможности RC5 включены

- Перенос профилей Studio: приватный JSON, preview/back/cancel и атомарный импорт без применения.
- Читаемый экспорт Notes в UTF-8/clipboard, включая live snapshot и скрытые заметки.
- Повторное использование оформления в мастерской, рамка/тень и draft/cancel.
- Поиск профилей и доступный Compact Launcher с клавиатурой и просмотром без запуска.
- Исправления меню после Escape, многострочных редакторов, lifecycle и style validation; RU/EN.

## Проверки

384 уникальных core tests (375 RC5 +9 новых). Каноническая публикация разрешена только после locked restore, полного Windows suite, publish, запуска извлечённого ZIP,20Studio gates, реального EM_LINEFROMCHAR=1, файловых диалогов/clipboard, installed smoke, repair/uninstall/reinstall/SQLite preservation и скачивания GitHub-байтов. Фактические результаты — приложенные WINDOWS-VALIDATION.json и native evidence.

[Исходное воспроизведение на RC5](https://github.com/LosarOS/SHIFT/actions/runs/40969409884) · [Журнал RC6](https://github.com/LosarOS/SHIFT/blob/main/reports/SHIFT-RC6-ENGINEERING-SESSION.md) · [Русский быстрый старт](https://github.com/LosarOS/SHIFT/blob/main/docs/QUICKSTART-RU.md).

## Установка и ограничения

Windows10 build19041+ / Windows11, x64. Установщик — для текущего пользователя. Portable распаковать полностью, portable.flag оставить рядом с SHIFT.exe. Проверить SHA256SUMS.txt. Перед обновлением Quit и копия **всего** каталога данных: `%LOCALAPPDATA%\SHIFT` либо `data` у portable, включая Studio/Images и Studio/RecoveryNotes. Schema2 и command consent сохранены.

Бинарники unsigned; SmartScreen может предупреждать. Native CI — disposable Windows Server2025, один виртуальный монитор. Физическая Windows10/11, mixed DPI/multi-monitor, Narrator/highcontrast, sleep/RDP/fullscreen и полная отдельная installed Studio матрица не подтверждены. Только явные статичные обои; Spotlight/slideshow не поддерживаются. Mica/Acrylic имеют обозначенный fallback. Stable1.0 не объявлен.

## English

RC6 fixes a reproduced WinUI/native Notes interoperability defect. CR-only and LF text is converted to CRLF only for the native edit control; persisted snapshots use LF, retaining the20,000-character model bound. Initial load, recovery draft and database-conflict reload share the boundary. Unicode whitespace and trailing blank lines survive; repeated restore does not grow newlines. Existing schema2 and command consent are unchanged.

Includes RC5 Studio packs, readable Notes export, reusable styles and accessible launcher improvements.384 unique tests, plus actual native line-rendering and the existing Windows release gates. Unsigned prerelease; disposable Server UIA is separate from physical Windows/DPI/accessibility acceptance. Back up the complete data directory before updating.
