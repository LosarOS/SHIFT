# SHIFT RC6 — Notes newline interoperability

2026-10-29. Baseline source/tag RC5: `176c27040929c37058354e35aa147f5634a204ec`. RC5 tag/public bytes verified and preserved.

## Investigation

Review of the actual final RC5 screenshots showed initial Notes edited in WinUI rendering with adjacent lines in native windows. WinUI can persist CR-only newlines; the native edit control expects CRLF. This is a hypothesis pending the new native EM_LINEFROMCHAR regression, not a claim that the complete existing suite covered this case.

The baseline probe uses the unchanged downloaded RC5 distribution in disposable Windows CI, checks source inputs unchanged and records actual text plus native line number before asserting two logical lines. Existing test assertions are retained. No owner desktop is used.
