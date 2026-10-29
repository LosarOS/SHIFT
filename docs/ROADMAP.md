# План развития SHIFT

## RC5 — опубликовано как prerelease

- Переносимые профили Studio: строгий JSON, preview/back/cancel и атомарный импорт без запуска.
- Полный Notes export в UTF-8/clipboard, включая текущие открытые и сохранённые скрытые заметки.
- Повторное использование оформления в мастерской, рамка/тень, сохранение многострочного содержимого.
- Доступный Compact Launcher: выделение, поиск по словам/тегам, просмотр без запуска.
- Native lifecycle/menu fixes, единый style contract, RU/EN новых действий.
- **375 уникальных тестов** и реальные tagged portable/installed/Studio/installer gates. [RC5 evidence](../reports/SHIFT-RC5-VALIDATION-RU.md).

## RC4 — реализовано

- Локальные пространства: CRUD, clone, избранное, теги, поиск, пять типов элементов, последовательный запуск, отмена и command consent.
- Workspace Doctor, компактный лаунчер, горячая клавиша, трей/second-instance activation, история, JSON и SQLite backup/restore.
- Studio: визуальные профили, обратимое применение статичных обоев, четыре настоящих виджета, нормализованное расположение, recovery и безопасный opt-in.
- Quick Setup из локальных ярлыков с явным выбором/предпросмотром/порядком/Back/cancel.
- Готовые окружения «Разработка», «Стриминг», «Фокус» с атомарным сохранением.
- Русская локализация, поиск/фильтр галереи, адаптивные профили, улучшенная мастерская и атомарное редактирование.
- Windows x64 portable/installer, контрольные суммы, реальные UIA-сценарии и 296 автоматических тестов. Точные факты и границы: [RC4 validation](../reports/SHIFT-RC4-VALIDATION-RU.md).

## До stable1.0 — обязательная приёмка

1. Физические Windows11 и поддерживаемые Windows10; чистые prerequisites/install/update/uninstall.
2. DPI100/125/150/200%, большие шрифты, Narrator/highcontrast/reduced motion.
3. Несколько мониторов, hotplug, смена primary/resolution, RDP, sleep/resume/fullscreen.
4. Длительная работа заметок/таймеров, сбои диска/питания, backup ресурсов и восстановление данных.
5. Полная installed Studio матрица, длительные idle/performance измерения и политика подписи бинарников.

Успешный CI на Server не заменяет эту физическую матрицу. Никакой фиктивной stable1.0 по одному номеру версии.

## После приёмки

- Quick Links и компактные интерактивные действия с тем же command consent.
- Улучшенный Widget Lab и пользовательские style templates по обратной связи.
- Документированные backdrops с проверенными fallback/accessibility режимами.
- Session capture, window layouts, расписания и bounded parallel launch — отдельные модули с явными разрешениями.
- При желании локальные рекомендации по реальным данным, без фиктивных показателей.

Необязательные исследования: WebView2/HTML widgets с отдельной моделью безопасности; packaged Windows Widgets Board. Explorer injection/WorkerW hacks, изменения системных политик, обязательное облако, платные API и повышенные службы не планируются как основа продукта.
