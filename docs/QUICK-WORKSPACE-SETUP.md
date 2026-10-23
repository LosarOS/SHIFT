# Quick workspace setup

Status: optional P2 draft, not included in published RC2. Read-only discovery/draft tests and limited native Windows portable wizard automation have passed. Packaging reconciliation and the remaining acceptance below are required before merge or release.

## User flow

1. Choose **Create from apps / Создать из приложений** on Overview or Workspaces.
2. Search the locally discovered shortcuts and select up to 100 entries. Choices remain selected when the search changes. Entries are grouped by source and Start Menu folder. Every entry explicitly says that launch is unverified.
3. Choose **Next**, enter a workspace name and review the exact launch order and shortcut paths. **Back** retains selection. **Save workspace** writes through the existing workspace store and opens workspace details. Saving launches nothing.

Cancel/Escape closes the wizard and cancels discovery. If no candidates are found, the user can create a normal workspace and add items manually. Inaccessible locations or bounded partial results are disclosed without preventing selection of available entries.

## Boundaries

- Sources: current-user Start Menu Programs, shared Start Menu Programs, current-user Desktop directory.
- Read-only filesystem metadata and at most 76 bytes of each `.lnk` header. No shell execution, COM shortcut resolution, process/window enumeration, browser-profile reads, executable icon extraction, file writes, or SDK/network calls.
- Network drives, UNC roots, symlinks/junctions, offline files and ancestors containing those objects are excluded before descending. The scanner rechecks ancestry immediately before enumeration/opening, but it is not a filesystem sandbox against concurrent malicious renames.
- Bounds: 8 roots, 256 directories, depth 8, 5,000 entries, 500 candidates. Partial results are marked explicitly.
- Shortcut targets and embedded command parameters are not inspected or copied into workspace arguments. A recognized header is not proof of a valid target, safe command or successful launch. Review shortcut properties and use Workspace Doctor before the first launch.
- The application layer accepts only manually selected IDs from the current discovery snapshot and applies existing workspace validation. No schema migration or new persistence format is required.

## Validation

Core suite: 129 passed, zero failed/skipped on Linux and Windows, including 19 Quick Setup cases. Tests cover unchanged input bytes/no generated files, Unicode/grouping, corrupt/truncated links, duplicate/missing/relative sources, empty results, bounds, cancellation, access failure, symlink exclusions, safe surrogate truncation, ordered explicit selection, unknown/duplicate/oversized selections and normal domain validation. Windows tests use a file-sharing lock for inaccessible-file coverage; the symlink fixture requires a runner with symlink creation rights.

[Native run 40014081976](https://github.com/LosarOS/SHIFT/actions/runs/40014081976) at source `8d10bf6b13f4b26e6fb5e74aa730f04567fbcaf0` passed actual WinUI/UI Automation: RU/EN discovery, search, empty-search results, manual selection, retained selection, preview/Back, cancel without saving, save without dispatch and saved workspaces after restart. See [exact scope and evidence](../reports/quick-setup-validation-2026-10-23.md). This smoke uses a pre-ZIP portable directory; it does not establish extracted-archive or installed-wizard acceptance.

Pending before merge:

- Reconcile current main packaging, including actual extracted portable ZIP smoke and release evidence, then rerun the wizard from that distribution.
- Exercise the installed wizard, all four themes, unselect/selection limit and cancellation during discovery.
- Distinguish an actually empty/missing Start Menu catalog from the already tested empty search result; verify denied-directory UI behavior.
- Keyboard-only navigation, Narrator names, 100–200% DPI and large text.
- Independently review the final combined diff; keep PR10 as a draft until its gates are satisfied.

This feature is intentionally isolated from the main runtime/release fixes and must not weaken their gates.
