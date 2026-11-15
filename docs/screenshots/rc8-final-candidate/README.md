# RC8 — real final candidate Windows captures

Source `28b69a5dc2e55361339e69e8743f2442691dcf74`, [canonical Windows run43661035655](https://github.com/LosarOS/SHIFT/actions/runs/43661035655), artifact14140392572. **Final candidate, not a published release**: owner approval remains separate.

Actual portable SHIFT.exe on disposable Windows Server, one1024×768virtual monitor and synthetic test data. All six PNGs were visually reviewed; original archive bytes were copied without cropping, retouching or generation. [Source, original names and SHA256](source-manifest.json).

| Capture | What is actually shown |
| --- | --- |
| [Scenario RU](launch-scenario-ru.png) | Preflight and launch-error policies, sequential behavior and explicit command-consent explanation. |
| [Stopped run](launch-scenario-stopped.png) | Intentional missing target: readable error and command skipped before dispatch. |
| [Recovery preview](recovery-center-preview.png) | English schema3/count summary and SQLite-only exclusions. The earlier false optional-missing-directory warning is absent. This native scenario does not perform Restore. |
| [Quick Actions Lab RU](quick-actions-lab-ru.png) | Actual unobscured editor, scrolled to action content. Some controls are above/below the viewport, not cropped out of the PNG. |
| [Desktop Quick Actions](desktop-quick-actions.png) | One real workspace-action widget. A minimized System Properties taskbar tab belongs to the disposable runner; it is not an open application error. |

[Actual RC7 downgrade refusal](../../../reports/evidence/rc8-final-candidate/technical-rc7-downgrade-refusal.png) is technical evidence, not a marketing hero: old immutable RC7 protects a copied newer database. UTF8 UIA title/reason now agree with the visible Russian text.

The older932checkpoint and its occluded stress-frame remain historical evidence in their own folders; they are not reused as this candidate's proof. Physical Windows10/11, DPI/monitor/Narrator/sleep/RDP and full manual acceptance remain NOT VERIFIED. [Independent byte/native-proof verification](../../../reports/evidence/rc8-final-candidate/independent-artifact-verification.json).
