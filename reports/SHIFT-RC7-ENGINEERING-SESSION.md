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

## Integrated core and native readiness

Commit36f6d9:459unique core cases passed on Windows/Linux (384baseline +49Composer/Plan +26layout/metadata). Native portable probe failed before new scenarios: UIA Invoke reached Item.New while the previous edit dialog was still closing; application correctly rejected a second dialog. Previous intermediate af1e153 passed portable/Studio but installed combo selection timed out after popup peer replacement. Probe now waits for stable owned keyboard focus before a single Invoke, scopes desktop choice peers to the app PID and verifies actual ComboBox selection with idempotent re-expansion. No dispatch retries or checks removed. Requires fresh complete Windows run.


## Final source and successful regression

Final feature head247342ee898d6397afa767896253f307eba62027; PR22 merged at7bb1760b7095bcc254b25807b9eb2934cd807919. [Final PR CI41520052346](https://github.com/LosarOS/SHIFT/actions/runs/41520052346) and [main CI41521398106](https://github.com/LosarOS/SHIFT/actions/runs/41521398106) all four jobs SUCCESS. **461 unique cases** pass Windows and Linux,0failed,0skipped:384baseline +51composition/planning/localization/persistence +26Studio concurrency/acknowledgement. Repeated platforms/runs are not additional cases.

Intermediate failures recorded: wrong UIA SelectionPattern method corrected to Current.GetSelection; template framework local AutomationIds required ShiftDialog local IDs; StrictMode evidence collection now initialized before merging checks. New credentials-identity regressions preserve URL UserInfo case. No test disabled, blanket exception filter or launch retry added.

Native new scenarios verify RU/EN Plan and Composer review/cancel, exact command shell, enabled/skipped totals, selected disabled append and cleanup without command marker/history change. Passed for extracted portable and first installed smoke. Existing data/resumed smoke keeps its own bounded scope.

## Tagged publication and public verification

Annotated v1.0.0-rc.7 points to7bb1760b7095bcc254b25807b9eb2934cd807919. [Canonical release41522532771](https://github.com/LosarOS/SHIFT/actions/runs/41522532771) SUCCESS: locked restore,461core cases, actual WinUI resources/native publish, portable,20Studio gates, realRC2 migration, exact wallpaper restoration, sharing/file picker/clipboard, installed UI and installer preservation. Signature unsigned.

[Independent verifier41522718987](https://github.com/LosarOS/SHIFT/actions/runs/41522718987) SUCCESS. All6public assets actually redownloaded, nonzero sizes/API digests/SHA256SUMS/source and native proof checked. Raw JSON/txt and unchanged PNGs committed separately to docs/rc7-release-evidence. Six real captures visually reviewed; synthetic data, Windows Server watermark retained. Source manifest records original screenshot names and hashes.

Release: https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.7 .
Final status: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**. Full Russian report [SHIFT-RC7-VALIDATION-RU.md](SHIFT-RC7-VALIDATION-RU.md), [public-byte proof](evidence/rc7/independent-public-verification.json), [RU guide](../docs/WORKSPACE-PRODUCTIVITY-RU.md).

## Remaining gates / next action

Physical Windows10/11, mixed DPI/multi-monitor/hotplug, Narrator/highcontrast/reducedmotion, sleep/RDP/fullscreen, long soak, full installed Studio/fault matrix and forced live conflicting editor/layout operations remain NOT VERIFIED. New SQL interleavings are tested, not a claim that every hardware/UI interleaving was exercised. [Manual protocol](../docs/studio/RC7-MANUAL-ACCEPTANCE-RU.md).

No local managed executor builds/tests ran; all actual execution above is GitHub Actions. RC6 release/tag retained. Schema2 and existing workspaces/history retained; full data-directory backup before upgrade. Documentation-only followup must not move the binary tag or replace public assets.
