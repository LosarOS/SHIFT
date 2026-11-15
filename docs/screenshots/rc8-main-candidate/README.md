# RC8 — real merged-main candidate Windows captures

Source `a5effcdcc4861ff099636be7782dd5155a029671`, [canonical Windows run43665382675](https://github.com/LosarOS/SHIFT/actions/runs/43665382675), artifact14142410522. **Merged-main candidate, not a published release**: owner approval remains separate.

Actual portable SHIFT.exe on disposable Windows Server, one1024×768virtual monitor and synthetic test data. All six PNGs were visually reviewed; original archive bytes were copied without cropping, retouching or generation. [Source, original names and SHA256](source-manifest.json).

| Capture | What is actually shown |
| --- | --- |
| [Scenario RU](launch-scenario-ru.png) | Preflight and launch-error policies, sequential behavior and explicit command-consent explanation. |
| [Stopped run](launch-scenario-stopped.png) | Intentional missing target: readable error and command skipped before dispatch. |
| [Recovery preview](recovery-center-preview.png) | English schema3/count summary and SQLite-only exclusions. The earlier false optional-missing-directory warning is absent. This native scenario does not perform Restore. |
| [Quick Actions Lab RU](quick-actions-lab-ru.png) | Actual unobscured editor, scrolled to action content. Some controls are above/below the viewport, not cropped out of the PNG. |
| [Desktop Quick Actions](desktop-quick-actions.png) | One real workspace-action widget. No System Properties overlay is present in this capture. |

[Actual RC7 downgrade refusal](../../../reports/evidence/rc8-main-candidate/technical-rc7-downgrade-refusal.png) is technical evidence, not a marketing hero: old immutable RC7 protects a copied newer database. UTF8 UIA title/reason now agree with the visible Russian text.

The older932/28bcheckpoints remain historical evidence in their own folders; they are not reused as this main candidate's proof. Physical Windows10/11, DPI/monitor/Narrator/sleep/RDP and full manual acceptance remain NOT VERIFIED. [Independent byte/native-proof verification](../../../reports/evidence/rc8-main-candidate/independent-artifact-verification.json).
