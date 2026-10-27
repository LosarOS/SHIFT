# Quick workspace setup

Status: integrated RC4 feature candidate. Source/discovery tests are automated; final downloadable release requires the parent native Windows wizard and packaging gates.

## User flow

1. Choose **Create from apps / Создать из приложений** on Overview or Workspaces.
2. Search the locally discovered shortcuts and select up to 100 entries. Choices remain selected when the search changes. Entries are grouped by source and Start Menu folder. Every entry explicitly says that launch is unverified.
3. Choose **Next**, enter a workspace name and review the exact launch order and shortcut paths. Move selected apps up/down before saving; search and Back preserve explicit selection order. **Back** retains selection. **Save workspace** writes through the existing workspace store and opens workspace details. Saving launches nothing.

Cancel/Escape closes the wizard and cancels discovery. If no candidates are found, the user can create a normal workspace and add items manually. Inaccessible locations or bounded partial results are disclosed without preventing selection of available entries. Clear selection is available on the first step; an empty or failed scan offers **Add manually** without reopening SHIFT. Saving disables editor fields until persistence finishes.

## Boundaries

- Sources: current-user Start Menu Programs, shared Start Menu Programs, current-user Desktop directory.
- Read-only filesystem metadata and at most 76 bytes of each `.lnk` header. No shell execution, COM shortcut resolution, process/window enumeration, browser-profile reads, executable icon extraction, file writes, or SDK/network calls.
- Network drives, UNC roots, symlinks/junctions, offline files and ancestors containing those objects are excluded before descending. The scanner rechecks ancestry immediately before enumeration/opening, but it is not a filesystem sandbox against concurrent malicious renames.
- Bounds: 8 roots, 256 directories, depth 8, 5,000 entries, 500 candidates. Partial results are marked explicitly.
- Shortcut targets and embedded command parameters are not inspected or copied into workspace arguments. A recognized header is not proof of a valid target, safe command or successful launch. Review shortcut properties and use Workspace Doctor before the first launch.
- The application layer accepts only manually selected IDs from the current discovery snapshot and applies existing workspace validation. No schema migration or new persistence format is required.

## Validation

Focused tests: 27 passed, 0failed/0skipped on Linux, .NET10.0.402. Application-reference metadata compilation passed with0errors/0warnings; this uses a scratch XAML stub and is not a native Windows runtime claim. Tests cover unchanged input bytes/no generated files, Unicode/grouping, corrupt/truncated links, duplicate/missing/relative sources, empty results, bounds, cancellation, access failure, symlink exclusions, safe surrogate truncation, ordered explicit selection, unknown/duplicate/oversized selections, malformed catalogs, Unicode multi-term search and normal domain validation. Windows tests use a file-sharing lock for inaccessible-file coverage; the symlink fixture requires a runner with symlink creation rights.

Pending native acceptance:

- Open wizard from Overview and Workspaces in RU/EN and all four themes.
- Search, select/unselect, reach selection limit, preview, Back, save; confirm no process launch.
- Cancel during discovery and after preview; no workspace should be saved.
- Empty/missing Start Menu and denied-directory behavior.
- Keyboard-only navigation, Narrator names, 100–200% DPI and large text.
- Portable and installed startup; restart confirms the saved workspace and launch order.

This feature is intentionally isolated from the main runtime/release fixes and must not weaken their gates.
