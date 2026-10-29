# SHIFT RC6 — Notes newline interoperability

2026-10-29. Baseline source/tag RC5: `176c27040929c37058354e35aa147f5634a204ec`. RC5 tag/public bytes verified and preserved.

## Investigation

Review of the actual final RC5 screenshots showed initial Notes edited in WinUI rendering with adjacent lines in native windows. WinUI can persist CR-only newlines; the native edit control expects CRLF. This is a hypothesis pending the new native EM_LINEFROMCHAR regression, not a claim that the complete existing suite covered this case.

The baseline probe uses the unchanged downloaded RC5 distribution in disposable Windows CI, checks source inputs unchanged and records actual text plus native line number before asserting two logical lines. Existing test assertions are retained. No owner desktop is used.

## Confirmed baseline and implementation

- [Baseline40969409884](https://github.com/LosarOS/SHIFT/actions/runs/40969409884), probe commit `55505c2330bb13bd3ee21cb63da29ed6c51e140a`, downloaded unchanged RC5 ZIP/EXE matched published SHA256. Actual Notes text contained CR-only; EM_LINEFROMCHAR returned0 for the second logical line. This is a confirmed P1, not inference from compilation. Wallpaper cleanup restored the baseline. Original assets were not replaced or re-uploaded.
- `WidgetPresentation.ToNativeNotesText` renders conventional CR/LF/CRLF as CRLF; `FromNativeNotesText` persists LF. Other Unicode whitespace is untouched. Three load paths use the same adapter; Snapshot normalizes before model validation, preventing CRLF expansion beyond the20k model bound.
- Nine tests include CR/LF/CRLF, empty/Unicode, mixed/trailing whitespace, repeated conversion and max-length validation. Full local Linux suite: **384 passed,0failed,0skipped**. PowerShell syntax and fixed Windows runtime gates remain to verify before release.
- Native probe records text/line/source in UTF-8 and adds a mandatory20th Studio gate. Release manifest verifies actual native line1 independently. No new schema/packages/permissions.

## PR acceptance and immutable tag

- PR20 [CI40970021017](https://github.com/LosarOS/SHIFT/actions/runs/40970021017), head `0e06a7e3fc2db6650e7f782659609432142ac502`: four jobs SUCCESS, **384 passed/0failed/0skipped** on Windows and Linux. The actual fixed EXE passed the new native multiline assertion and the entire existing Studio/portable/installed/installer preservation suite.
- [PR20](https://github.com/LosarOS/SHIFT/pull/20) merged. Binary/tag source `de8e744d6993b3293b55ed56675cb6472ef68fab`; merged tree matched the accepted head. Annotated `v1.0.0-rc.6` pushed, no existing tags/assets replaced.
- Canonical tag workflow [40970869902](https://github.com/LosarOS/SHIFT/actions/runs/40970869902) and main CI [40970866586](https://github.com/LosarOS/SHIFT/actions/runs/40970866586) now run on that exact source. Publication and independent public-byte verification are still pending at this checkpoint.
- RC5 factual report, nine unchanged Windows screenshots, hashes and the post-publication CR-only limitation are merged via [PR19](https://github.com/LosarOS/SHIFT/pull/19). RC6 docs/evidence are prepared on a separate docs-only branch after the application source is frozen.

## Final published evidence — 2026-10-29 23:14 UTC

- Tagged workflow40970869902 and main CI40970866586 **SUCCESS** on `de8e744d6993b3293b55ed56675cb6472ef68fab`.384unique core tests passed, no failures/skips;20Studio/native gates and installed repair/uninstall/reinstall preservation passed.
- Public [v1.0.0-rc.6](https://github.com/LosarOS/SHIFT/releases/tag/v1.0.0-rc.6) contains real portable ZIP/setup EXE, checksums, validation and native/UI evidence. All6assets independently re-downloaded over their public URLs, nonzero sizes/API digests checked; both binary hashes matched SHA256SUMS. Portable ZIP integrity/MZ/resources.pri/portable.flag and absence of user DB/data checked.
- New native proof: **line1**, exact `Initial CI note\r\nSecond line — local only`; source matches the tag. Before: unchanged RC5 line0 with CR-only. Full proof preserved in reports/evidence/rc6 plus the original RC5 failure.
- Six byte-identical native PNGs selected from the37-frame public archive, with source manifest. The initial diagnostic context shot had the main window covering widgets; it was not labelled as visual multiline proof. Instead the genuine later stress-frame shows both Notes lines, explicitly labelled as stress context. No image editing or generated UI.
- Release status: **VERIFIED PRERELEASE WITH DISCLOSED LIMITATIONS**. Unsigned; physical Windows10/11, DPI/accessibility/multi-monitor/sleep/RDP/fullscreen, long performance soak and full installed Studio matrix remain outstanding. No stable1.0 claim.
