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
