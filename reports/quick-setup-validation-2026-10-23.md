# Optional Quick Workspace Setup — native acceptance evidence

This P2 feature remains a draft in [PR10](https://github.com/LosarOS/SHIFT/pull/10). It was not merged into main and is not part of the published SHIFT RC2. No release/tag/asset was modified by this work.

## Tested source and environment

- Exact compiled source: `8d10bf6b13f4b26e6fb5e74aa730f04567fbcaf0`.
- [Native CI run 40014081976](https://github.com/LosarOS/SHIFT/actions/runs/40014081976): **SUCCESS**, all four jobs.
- Disposable GitHub Actions `windows-latest`, image `windows-2025-vs2026`, version `20261021.278.1`; .NET 10, WinUI 3 and Windows UI Automation.
- This is automated Windows runner acceptance, not manual Windows 10/11 desktop, Narrator or DPI acceptance.

## Actual outcomes

- Core tests: **129 passed / 0 failed / 0 skipped** on Windows and Linux. Windows packaging also ran the mandatory same 129 tests successfully; these executions are not added together as distinct tests.
- RU wizard log at `19:48:51 UTC`: discovery, search, empty search, manual selection, preview, Back, cancel, save; no shortcut execution.
- EN wizard log at `19:49:03 UTC`: the same scenarios passed.
- Portable log at `19:49:11 UTC`: selection survives search and Back; cancelling does not save; reopening clears cancelled selection; both saved wizard workspaces survive graceful exit/restart; the owned no-dispatch marker remains absent.
- Existing base installed flow, in-place reinstall, uninstall/reinstall and SQLite/workspace/settings preservation passed. The installed wizard itself was not exercised.
- Source hygiene and whitespace checks passed. Source hygiene complements review and is not an exhaustive security audit.

## Test boundaries and evidence

`Build-Release.ps1 -QuickSetupSmoke` opts into the new automation. `Smoke-Windows.ps1 -QuickSetup` rejects non-CI, installed and resumed profiles. The fixture is a newly created uniquely named Start Menu subdirectory containing one owned shortcut. The application discovers it through its normal provider. The marker would expose accidental dispatch; the test never invokes the shortcut. Cleanup is limited to that fixture and test-owned application data.

The native artifact [SHIFT-Windows-candidate](https://github.com/LosarOS/SHIFT/actions/runs/40014081976/artifacts/12527477557) is **178,719,552 bytes**, artifact digest `sha256:1dd50fc6e15c8be08dbb3ca70fccdeb91862600d34393d07315355c7f15e80f7`. This is the GitHub Actions artifact archive digest, not an installer checksum. It contains runtime diagnostics and real capture outputs `portable-wizard-selection-ru/en.png`, `portable-wizard-preview-ru/en.png`, `portable-wizard-saved-ru/en.png`. These screenshots are automation captures, not generated illustrations; visual/manual acceptance remains separate.

The smoke currently exercises a **pre-ZIP portable directory**. It does not prove the extracted distribution. Empty search is distinct from a zero-candidate catalog.

## Defects found and fixes

1. Initial native wizard run [40013269490](https://github.com/LosarOS/SHIFT/actions/runs/40013269490) reached preview, then timed out locating `Setup.Name`. Shared `Field()` overwrote the wizard-specific automation ID with `Field.Name`. Fixed by assigning `Setup.Name` after field decoration; the next native run passed.
2. The preview Back button reused global “All workspaces” wording. Added dedicated “Назад” / “Back” labels.
3. Carried in known main smoke fixes for acknowledged Settings persistence and DWM-composed screenshots. Added an explicit cancellation-dismissal wait before opening the permanent launcher, avoiding an asynchronous dialog race in the harness.

No test was disabled or weakened to obtain a pass.

## Remaining gates

- Reconcile current main packaging and release-evidence changes, including extracted-ZIP validation; rerun on the final combined source.
- Installed-wizard acceptance, actual zero-candidate catalog, denied-directory UI, cancellation during discovery, explicit unselect/selection-limit UI.
- All four themes, keyboard-only operation, Narrator, large text and 100–200% DPI.
- Independent review before removing draft status. No merge or new release is requested by this report.
