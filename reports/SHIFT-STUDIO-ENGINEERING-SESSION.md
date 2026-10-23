# SHIFT Studio engineering session — 2026-10-23

## Baseline

Actual main: a5bbd0457e39e3040ad33e94fb35f66fd777aac4; clean tree. RC2 public and retained. Quick Setup PR10 remains independent/draft; no blind merge. Existing eight dependency PRs excluded from this feature scope. Linux managed executor; Windows-specific acceptance will use disposable GitHub Actions runners.

## Architecture decisions and ownership

Studio extends the existing Domain/Application/Infrastructure/App direction. Definitions, instances, layouts, profile associations and runtime state are separate records/tables. Studio defaults disabled; auto-apply on workspace dispatch defaults off. Applications and visual apply remain separate operations.

Recovery persists captured state and per-action intent before external mutations, including ambiguous crash outcomes. Restore compares current values with SHIFT's intended values; explicit conflict decisions only. Slideshow configurations that cannot be restored faithfully are refused before wallpaper mutation. No Explorer injection, WorkerW parenting, system policies or elevation.

Independent agent branches will implement persistence/migration, native adapters, application orchestration, managed widget windows and Studio UI. Root owns shared contracts, shell integration, pipeline, review and final evidence. No concurrent migration edits. Integration follows reviewed diffs; no agent report substitutes for actual logs.

## Plan

1. Contracts, bounded validation and RC2 baseline tests.
2. SQLite v2 transactional migration with pre-migration backup; profile/recovery services and Windows adapters.
3. Four working widgets, centralized scheduler, autosave and timer recovery; gallery/Lab/profile UI.
4. Shell/tray/workspace integration, regression, native build and disposable desktop recovery smoke.
5. Publish RC3 only after required gates; otherwise retain reviewable preview PR with blockers.

Current state: implementation started; no Studio Windows runtime or performance PASS claimed. Benchmarks: NOT MEASURED.

## Integrated foundation checkpoint

Integration branch `feature/shift-studio-desktop-engine`, current implementation SHA `211788a69b8881ec3f951a4541dbced70ae878ad`; [draft PR13](https://github.com/LosarOS/SHIFT/pull/13). Existing main/RC2 unchanged. Target experimental RC3. User later constrained remaining weekly quota to26%; P2/P3 and new agent groups deferred. Four independent agent branches reviewed/cherry-picked; root owns shell, preview UI, CI and release.

Implemented: Domain contracts/validation/6templates, staged application service and durable per-action intent, managed originals and conflict-aware partial recovery, transactional SQLitev2 and private-snapshot restore migration, strict declaration JSON, native COM/image/topology/CPU/RAM adapters, four owned Forms/Win32 widget windows on one STA with centralized1s scheduler, notes CAS/recovery drafts, monotonic/deadline timer, draft Lab/profile/gallery/recovery UI and separate workspace integration. Main shell remains WinUI3. Studio defaults off.

Safety restriction: IDesktopWallpaper cannot guarantee Spotlight-provider restoration. Every wallpaper apply requires current explicit static-image confirmation; slideshow/empty/remote/unrestorable configurations are refused. Automatic workspace profiles currently support widgets only. No Explorer integration, arbitrary JS, elevated/system-policy changes or paid API.

Evidence reviewed:
- RC2 baseline Linux110passed.
- Initial combined Linux185passed; recovery-copy followup added3tests.
- [Native baseline CI40027655276](https://github.com/LosarOS/SHIFT/actions/runs/40027655276), source8fcb985: **188passed/0failed/0skipped** on Linux and Windows; native WinUI publish, standard portable and installer/reinstall/preservation smoke passed. This establishes existing UI with Studio disabled, not four-widget acceptance.
- Whole App Windows-reference metadata compilation0errors/0warnings, Linux. A temporary XAML stub was used only in scratch; this is not Windows runtime evidence. Final native baseline above compiled real XAML/resources.
- New `packaging/Smoke-Studio.ps1` runs actual extracted SHIFT.exe; `tools/Shift.StudioAcceptance` prepares/inspects owned data only on disposable Windows CI. It downloads/verifies actual RC2, creates real v1 data, snapshots/migrates it, then exercises Lab preview/cancel/save, four real windows, Notes/timer, reapply, restoration,1/5/10windows and restart. [CI40028855636](https://github.com/LosarOS/SHIFT/actions/runs/40028855636) pending. PowerShell parser and tool build passed locally. No result is assumed.

Remaining gates: actual Studio UI/alpha/wallpaper/notes/timer/lifecycle smoke, real raw screenshots, metrics, release workflow Studio proof enforcement, final regression and PR review. Until success, release status BLOCKED. Physical Win10/11, mixed DPI, actual sleep/RDP, Narrator/highcontrast and original-provider restoration are NOT VERIFIED; benchmarks NOT MEASURED until raw CI artifacts exist.

## Native acceptance corrections

CI40028855636 captured a real unavailable Studio view: navigation could render before async initialization and never refresh. Fixed by rerendering the selected Studio page after initialization. CI40030164878 then caught an invalid WinUI StackPanel.IsEnabled use in the new save guard; replaced with an enabled ContentControl ancestor. These runs are failures, not accepted runtime proof.

Additional review retained multiple instances per definition, exposed hidden-instance visibility, kept the origin instance in Lab, bounded JSON reads even if a file grows after stat, and made export replacement atomic. Recovery now distinguishes readable original capture from comparable current paths: a missing active image after moving portable storage must not prevent undoing a known owned path; uncomparable provider/session states remain blocked even with explicit override. Two regression tests added. Current Linux full suite: **190 passed,0failed,0skipped**. Whole-app reference metadata build:0errors/0warnings. Native Studio gates remain pending.

CI40031121387 at291df690f88f57978eff4ad1fceccabbc0462055:190 tests passed on Windows and Linux; real publish/resources and base portable passed. Actual RC2 download/owned v1 upgrade, Studio Lab preview/cancel/save, four native windows, Unicode Notes persistence and real3-second timer completion reached. The test then timed out holding a stale UIA Notes root after reapply: count4 matched old windows before replacement completed. Test now waits for entirely new HWNDs and reacquires the Notes root. No application error was logged. Cleanup proof is WALLPAPER_RESTORED. These are partial observations, not a passed Studio gate. Actual screenshots were reviewed; runner console obscured the desktop view, so disposable console windows are now minimized before recording. Native alpha is now an enforced .85 HWND-attribute gate, separate from the unverified full compositor/accessibility matrix. Raw samples are saved incrementally even if a later check fails.

Dependency audit: Shift.Infrastructure direct/transitive NuGet audit reported no known vulnerable packages from current nuget.org feed. Source hygiene passed123text files; this is not an exhaustive security proof.
