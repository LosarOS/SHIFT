# SHIFT RC4 engineering — 2026-10-27

Baseline main6b6ef5b; RC3 tagged/built source dd9d047 retained. Main tree clean; independent feature/russian-localization-rc4 worktree. Target next1.0.0-rc.4, no stable claim. User clarified100%quota, explicitly requests substantive features and polished UI alongside RU localization.

Plan: reuse/review Quick Setup PR10; safe starter Studio profiles with atomic storage; profile/gallery UX and Russian labels/diagnostics/date formatting; native widget usability and confirmed unsaved-layout fix; meaningful tests; real disposable Windows RU/EN UI/packaging acceptance; PR/merge/tagged prerelease only after gates. Four isolated agents own quick setup, preset factory, preset storage and native widget files. Root owns integration/localization/UI/Windows gates/release. No overlapping migrations.

Early evidence: current public release RC3; main unchanged since prior session, only dependency branch updates. Environment policy enforced/unrestricted, no VPN. No requested destructive owner-data operations.

## Integration checkpoint

Integrated reviewed Quick Setup including search/explicit selection/order/back/save/cancel; safe3preset draft factory and transactional storage; native widget Russian/live culture/controls; localized presentation errors, dates, templates, unit labels; responsive Studio cards/search/filter, two-pane wide Lab, visible-per-monitor grid arrangement and workspace profile actions. Root initial combined suite282passed.

Read-only review identified additional real P1s, now fixed: valid values coerced by narrower editor bounds, disconnected display/custom style lost on unrelated edit, library-only choice lost on language switch, disabled profiles automatically restored, hidden monitor layouts changed by grid action, non-atomic Lab definition+placement save. Atomic editor adds13tests with rollback/FK/CAS/state-preservation coverage. Active grid restarts only after explicit confirmation and flushes before saving new positions; hidden layouts remain unchanged.

Native acceptance expanded to actual RU/EN switch with10live HWNDs, Russian metric captions, timed restart, mode+geometry persistence, valid20K-note/boundary/custom-style/disconnected-monitor preservation across editor language changes, preset cancel/save/apply, Russian Quick Setup order/back/save/cancel and disabled-profile restart. All destructive fixture cleanup is disposable/GUID-owned. New gates are required before publishing RC4. No native PASS is claimed yet.
