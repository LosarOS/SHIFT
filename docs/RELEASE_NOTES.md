# SHIFT 1.0.0-rc.4 — ваш рабочий контекст, по-русски

Экспериментальный Windows x64 prerelease. Windows 10 build19041+ / Windows11. Studio по умолчанию выключен; без регистрации, телеметрии и платных API.

## Новое

- **Создать из приложений**: поиск локальных ярлыков, ручной выбор, предпросмотр и изменение порядка, возврат без потери черновика, сохранение пространства. Никакого чтения браузерной истории или скрытого запуска.
- **Готовые окружения Studio**: «Разработка», «Стриминг», «Фокус». Три виджета и согласованная палитра в одном безопасном шаге. Создание не меняет обои и не включает Studio автоматически.
- **Новая галерея**: поиск, фильтр по типу, предпросмотр оформления, быстрые действия и адаптивные карточки.
- **Мастерская виджетов**: разделы свойств и двухпанельный режим на широком окне; атомарное сохранение определения и размещения.
- **Сетка и режимы**: раскладка видимых виджетов на своих экранах, сохранение скрытых расположений, закрепление/редактирование из главного окна.
- **Русский интерфейс**: названия шаблонов, диагностика восстановления, даты, единицы ЦП/ОЗУ, доступные подписи и живое переключение виджетов RU/EN.

## Исправления

Исправлены потеря расположения при смене режима или ожидании SQLite, запуск завершённого таймера, частичное сохранение Widget Lab при конфликте, уменьшение допустимых импортированных значений, потеря недоступного монитора/пользовательского оформления, потеря выбора «Только библиотека» при смене языка и автоматическое восстановление отключённых профилей. Существующие пространства, история, command consent и schema2 сохранены.

## Установка и данные

Скачайте setup.exe либо распакуйте весь portable.zip из этого выпуска. Проверьте SHA256SUMS.txt. Полностью выйдите из SHIFT перед обновлением и скопируйте всю папку данных, включая Studio/Images и Studio/RecoveryNotes. SQLite-only backup содержит ссылки на изображения, а не сами изображения. Исходники и бинарники RC3 остаются доступны.

## Проверки и ограничения

Фактические результаты — WINDOWS-VALIDATION.json, Native Smoke Evidence и reports/SHIFT-RC4-ENGINEERING-SESSION.md. Наличие release notes не означает, что незавершённая проверка прошла. Публикация разрешена workflow только после обязательных native gates и проверки скачанных с GitHub байтов.

Windows Server UI Automation не заменяет ручную приёмку физических Windows10/11, mixedDPI, нескольких мониторов, Narrator/highcontrast, sleep/RDP/fullscreen. Полная отдельная installed Studio матрица ещё нужна. Только статичные локальные обои с текущим подтверждением; Spotlight/slideshow не поддерживаются. Автоприменение пространства пока для профилей без обоев. Mica/Acrylic — явно обозначенный fallback. Виджеты являются обычными управляемыми окнами; Explorer не изменяется. Бинарники неподписанные; возможен SmartScreen.

## English

RC4 adds explicit local-shortcut workspace setup, safe Development/Streaming/Focus presets, searchable widget gallery, responsive profile cards, grouped/wide Widget Lab, per-display grid arrangement, live RU/EN widget formatting and atomic editor saves. It fixes lost layout edits, completed-timer restart, disabled-profile recovery and silent editor coercion of valid/custom/disconnected values.

Existing schema2/workspaces/history and command consent remain compatible. Studio defaults off; presets never launch apps or change wallpaper on save. Back up the whole data folder before upgrading. Only confirmed static wallpaper is supported; Spotlight/slideshow and arbitrary JS are excluded. Native alpha is distinct from Mica/Acrylic fallback. Artifacts are unsigned. See attached validation for exact executed Windows gates and outstanding physical/accessibility acceptance.
