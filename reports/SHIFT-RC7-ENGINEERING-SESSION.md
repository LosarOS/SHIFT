# SHIFT RC7 — Productivity & Reliability

2026-11-02. Baseline main ace0e8ba5462a05cd0146bf8705472c74b1f6ea1; existing RC6 release/tag preserved.

## Environment

The managed executor failed provisioning (gateway_unavailable). No local builds/tests ran. Changes are isolated on feature/shift-studio-rc7-productivity via GitHub Git API, no force update. Actual checks run in disposable GitHub Actions. Owner desktop/data is untouched.

## Confirmed baseline defect

Latest main CI40972038240 passed384core tests but failed Studio. Read-only audit41510313816 extracted the retained owned CI log: Widget update failed / Studio data changed / SaveProfileAsync revision conflict at23:20:58. The database prevented overwrite. No generic exception filter or failing gate was removed.

## Implemented scope

WorkspaceComposer: bounded format-only batches, explicit URL/App/Folder/File kind, no command inference, duplicate detection, row selection, disabled defaults and stale snapshot refusal. WorkspaceLaunchPlanner: exact coordinator order, enabled delay budget, explicit commands/repeats, no dispatch or history. UI is RU/EN and uses accessible controls. A committed append is not retried after shell refresh failure.

## Reviewed concurrency fix

Isolated agent branch fix/rc7-layout-concurrency, final f74788bb3033e574b99a7feb502c1850ca889889, merged as a real two-parent commit af1e153dd2af5280380b07c3b67528f43e0d9cc6. Atomic LastApplied metadata, per-instance layout compare/write, conflict retention. Independent review found and closed Mode/Gather rebase overwrite routes before integration. InvokeAsync already serializes host operations; no non-reentrant nested hostGate was added.26new agent cases not yet independently executed at integration time; report is not a test result.

## Evidence checkpoint

First feature commit504997350e97fe9fd01ccba25c37f774af8b91ee: CI41513026662 all four jobs SUCCESS.433unique tests passed Windows/Linux (384baseline +49composition/planning/localization/persistence). Existing native suite and installer passed, but new Composer/Plan UI cases were not yet part of that revision. They are added next and require a new full Windows pass. No RC7 release is claimed until final source gates pass.
