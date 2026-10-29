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
