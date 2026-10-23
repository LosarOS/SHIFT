# SHIFT 1.0.0-rc.3 — SHIFT Studio experimental preview

SHIFT Studio — One workspace. One environment. Windows 10 build 19041+ / Windows 11 x64; локально, без регистрации, телеметрии и платных API. Подсистема по умолчанию выключена.

- Визуальные профили с отдельной привязкой к пространствам, управляемыми копиями статичных обоев и журналом восстановления.
- Четыре собственных виджета: Clock, System Monitor, Focus Timer и Notes. Локальные значения, заметки с CAS/recovery drafts, таймер с monotonic/deadline моделью.
- Widget Lab: черновик, внешний предпросмотр, отмена, сохранение, шесть шаблонов и строгий декларативный JSON.
- Обязательные действия: отключить Studio, восстановить оформление и собрать виджеты на доступном экране.
- SQLite v2: backup перед миграцией, сохранение пространств/истории, проверка восстановления на приватном снимке. RC2 отказывается открывать новую схему; для возврата используйте сохранённую v1-копию и резервную копию всей папки данных.

## Ограничения безопасности

Обои изменяются только после текущего подтверждения статичных изображений. Windows Spotlight/provider configuration не восстанавливается; слайд-шоу, пустые/недоступные обои и неподдерживаемые remote-состояния отклоняются. Автоприменение при запуске пространства пока поддерживает профили без обоев. Команды пространства сохраняют отдельное подтверждение; ошибка оформления не запускает приложения повторно.

Виджеты — обычные управляемые окна, без внедрения в Explorer. Always on Top выключен по умолчанию. Mica/Acrylic используют явно обозначенный fallback; native alpha не равен Acrylic. Ручная Windows10/11/DPI/Narrator/fullscreen/sleep/RDP-приёмка остаётся отдельным этапом. Исчерпывающая матрица installed widgets и ресурсных измерений не заявляется.

## Установка

Скачайте SHIFT-1.0.0-rc.3-win-x64-setup.exe либо распакуйте весь SHIFT-1.0.0-rc.3-win-x64-portable.zip. Проверьте SHA256SUMS.txt из того же выпуска. Бинарники неподписанные; возможны предупреждения SmartScreen. Перед обновлением закройте SHIFT и сохраните всю папку данных, включая Studio/Images и Studio/RecoveryNotes. SQLite-only backup содержит ссылки на изображения, не архив всех ресурсов. Старый workspace JSON по-прежнему переносит приложения; определения виджетов имеют отдельный формат v1.

## English

Experimental opt-in Studio adds desktop profiles, staged static-wallpaper apply/recovery, four owned native widgets, a draft/preview/save Lab and transactional RC2→SQLite v2 migration. No Explorer injection, scripts, cloud or elevated changes. Current static-image confirmation is mandatory; Spotlight/provider restoration is unsupported and automatic workspace apply initially supports widgets only. Acrylic/Mica use an explicit fallback. Artifacts are unsigned. Read the engineering report/native evidence for exact verified gates and remaining physical Windows/accessibility acceptance.

Evidence: reports/SHIFT-STUDIO-ENGINEERING-SESSION.md, docs/studio/QA-MATRIX.md, WINDOWS-VALIDATION.json and SHIFT-Native-Smoke-Evidence.zip. Do not interpret this note as proof of a pending native gate; publication requires completed evidence.
