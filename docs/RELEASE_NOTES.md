# SHIFT 1.0.0-rc.5 — ваше окружение, с собой

Нативный Windows x64 prerelease. **Studio включается только по выбору пользователя.** Без регистрации, облака, телеметрии и платных API.

## Что нового

- **Перенос профилей Studio.** Экспорт набора виджетов, оформления и относительных позиций в версионированный JSON. Импорт с проверкой, предпросмотром, возвратом и отменой создаёт отдельный профиль одной транзакцией. Существующее окружение не включается и не меняется.
- **Сохранение заметок.** Отдельный читаемый UTF-8 файл для всего профиля: фактический текст открытых окон и сохранённые скрытые заметки. Копирование — только явной кнопкой. Перед удалением профиля можно сохранить нужный текст.
- **Повторное использование оформления.** Скопируйте полный стиль другого виджета в черновик мастерской: палитру, шрифт, прозрачность, рамку и тень. Отмена ничего не сохраняет. Содержимое показывается по типу виджета; скрытые значения сохраняются.
- **Поиск профилей.** Название, описание и связанное рабочее пространство.
- **Доступный компактный лаунчер.** Настоящий выбор списка, поиск по словам/описанию/тегам, стрелки и Enter, понятное пустое состояние и просмотр пространства без запуска.
- **Клавиатура виджетов.** Ctrl+S — сохранение, Ctrl+Shift+S — копия отдельной заметки, Shift+F10 / клавиша меню — действия в закреплённом режиме.

## Исправления

Исправлено аварийное отключение Studio после закрытия меню виджета клавишей Escape. Многострочные описания, команды, заметки и шаблоны больше не теряют строки при открытии редактора; экспорт заметок сохраняет полный исходный текст. Скопированный текст остаётся в буфере после выхода из SHIFT. Согласована проверка шрифтов/пользовательских стилей и переведена подсказка неправильного формата даты.

Защищён финальный hand-off заметок при смене профиля и закрытии. Ожидающие вызовы завершаются при выходе STA; Stop подтверждается до остановки message pump. Конфликты состояния разрешаются и для Focus Timer. Версия приложения теперь берётся из общего источника. Сохранены schema2, пространства, история и подтверждение команд.

## Установка и данные

Windows10 build19041+ / Windows11, x64. Скачайте **setup.exe** либо распакуйте **весь portable.zip**. Сверьте SHA256SUMS.txt. Бинарники **неподписанные**, возможны предупреждения SmartScreen.

Перед обновлением полностью выйдите из SHIFT и скопируйте всю папку данных: `%LOCALAPPDATA%\SHIFT` для установленной версии либо `data\` для portable. Сохраните также Studio/Images и Studio/RecoveryNotes. Удаление программы не означает разрешение удалять пользовательские данные.

Пакет профиля — **конфигурация, не backup**: без обоев и их путей, monitor IDs, workspace binding, автоприменения и текущих notes/timer state. Начальный текст шаблонов включается только отдельно. Лимит JSON — 2МиБ /64виджета; новые ID, Edit Mode, обычный z-order. Экспорт заметок и clipboard не зашифрованы; неоткрытые recovery drafts исключены.

## Проверки и ограничения

Публикация workflow разрешена только после locked restore, полного core suite, Windows publish, запуска извлечённого ZIP, реальных native UIA/Studio/file-picker/clipboard/compact checks, installer preservation и проверки скачанных GitHub-байтов. Точные результаты — приложенные WINDOWS-VALIDATION.json, Native Smoke Evidence и engineering report.

Windows Server UIA не заменяет физическую приёмку Windows10/11. Mixed DPI, multi-monitor/hotplug, Narrator/highcontrast, sleep/RDP/fullscreen и полная отдельная installed Studio матрица остаются непроверенными. Только подтверждённые статичные локальные обои; Spotlight/slideshow не поддерживаются. Mica/Acrylic используют явно обозначенный fallback. Виджеты — собственные окна, без внедрения в Explorer. Stable1.0 не присваивается.

[Руководство по переносу и заметкам](https://github.com/LosarOS/SHIFT/blob/main/docs/STUDIO-SHARING-RU.md) · [Инженерный журнал](https://github.com/LosarOS/SHIFT/blob/main/reports/SHIFT-RC5-ENGINEERING-SESSION.md)

## English

RC5 adds strict privacy-first Studio profile packs, atomic reviewed import, current/hidden Notes text export, searchable profiles and an accessible compact launcher with multi-term search and Review without dispatch. Multiline editors retain their complete text; note exports retain their authoritative snapshot and clipboard content survives exit. Repeated widget menu Escape no longer disables Studio. Persisted fonts and custom style limits match the editor. Native widgets gain keyboard actions, safe final input hand-off and bounded host shutdown; timer-state conflicts can be resolved explicitly.

Profile packs transfer configuration, never wallpaper/files/workspace links/current state or automatic consent. Initial note templates are opt-in. Existing schema2 data and command confirmation stay compatible. Back up the whole data directory before updating. Unsigned Windows x64 prerelease; physical Windows/DPI/accessibility acceptance remains separate from disposable Server UIA. See attached validation and known limitations.
