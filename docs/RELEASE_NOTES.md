# SHIFT 1.0.0-rc.8 — сценарии и быстрые действия

Release candidate. Публикация ожидает проверок и отдельного подтверждения владельца. [Full English notes](RELEASE_NOTES.en.md).

## Что нового

- Настраиваемая предварительная проверка перед запуском и политика ошибки: продолжить или остановить оставшиеся шаги. Команды сохраняют явное согласие.
- Quick Actions: пятый native-виджет для выбранных пространств, локальных файлов/папок и HTTP(S), без произвольного shell.
- Центр восстановления: список SQLite-копий, проверка на приватном снимке, сводка содержимого и существующий безопасный restore с safety-копией.
- Исправлено ложное предупреждение о доступе, когда необязательные папки backup ещё не созданы; настоящие ошибки доступа сохраняются.
- Русский основной README, English и новые продуктовые SVG.

## Обновление

SQLite schema3: транзакционная миграция с резервной копией для поддержки нового типа виджета. Старые версии не открывают новую схему. Для возврата используйте сохранённую копию. Перед обновлением Quit и копия всего каталога данных с Studio/Images и Studio/RecoveryNotes. SQLite backup не содержит внешних файлов.

## Проверки и ограничения

514 core tests passed/0 skipped на Windows и Linux. Реальные portable/installed/Studio/RC7-upgrade gates прошли; downloaded bytes проверены отдельно. [Подробная валидация и ограничения](../reports/RC8-VALIDATION-RU.md). Unsigned; возможен SmartScreen. Физические Windows10/11, mixed DPI/мониторы, Narrator, sleep/RDP и полный runtime matrix отдельно от Windows Server CI. Проверка до запуска не гарантирует готовность приложений. Нет автоматического закрытия сторонних процессов, повторного dispatch или исполнения импортированных действий.

## English

RC8 adds opt-in launch preflight/error policies, a native Quick Actions widget and a validated SQLite Recovery Center. Existing sequential dispatch and command consent remain. Schema3 migrates transactionally with backup; restore the previous backup for downgrade. Unsigned candidate with physical Windows acceptance still required.
