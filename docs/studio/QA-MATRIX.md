# Studio validation matrix

This file records scope, not an assertion that every row passed. Current RC6 evidence is in [the final report](../../reports/SHIFT-RC6-VALIDATION-RU.md) and attached native JSON/TRX. The [physical manual protocol](RC5-MANUAL-ACCEPTANCE-RU.md) remains uncompleted.

| Area | Automation | Outstanding manual scope |
|---|---|---|
| RC2 migration | Real published RC2 creates owned v1 profile; SQLite snapshot then v2 upgrade verifies workspace/history | Upgrade on owner's backed-up machine |
| Validation/core | Domain, partial apply/cancel, intent/ack ambiguity, restore conflicts, managed-original fallback, no auto replay | External provider/theme changes |
| Native adapters | Windows image decode/corruption/limits, local guards, topology, real CPU/RAM | Removable/offline media, multi-adapter displays |
| Widgets | Actual extracted EXE windows, Lab preview/cancel/save, notes/timer, no duplicates, gather, disable/enable, restart/quit | Mixed DPI, mouse/touch drag detail, fullscreen/Windows+D, Narrator |
| Wallpaper | Disposable runner only; static apply and exact restore, original state captured before mutation | Physical Windows10/11, Spotlight unsupported |
| Backups | v1/v2 private-snapshot upgrade, corruption/DDL failures preserve current DB, WAL/CAS/FKs | Copy entire data folder to retain managed images/draft files |
| Performance | Raw process memory/CPU at0/1/5/10windows; report only actual artifacts | Physical-PC idle, sleep/resume, battery and RDP |
| Installer | Existing install/start/repair/uninstall/preservation gates | Interactive Studio widgets under installed paths |
| Accessibility | Localized labels, keyboard reachable recovery actions, high-contrast opaque fallback | DPI100/125/150/200%, large text, Narrator, transparency/reduced-motion system changes |

RC6 passed its required prerelease native gates; stable1.0 remains blocked on the outstanding physical/manual matrix. A Windows Server runner is not physical Windows11 acceptance. A matching HWND alpha attribute is distinct from a full compositor/pixel/accessibility matrix. Unsigned artifacts may trigger SmartScreen.
