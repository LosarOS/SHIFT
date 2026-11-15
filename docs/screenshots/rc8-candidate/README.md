# RC8 — actual candidate Windows captures

Candidate checkpoint source `932999ab9f702cbfcd53f308ae985d4bc5cf7a43`, [canonical Windows run43659620650](https://github.com/LosarOS/SHIFT/actions/runs/43659620650). **Not a published release.** Later source/fixes may supersede this checkpoint.

Actual disposable Windows Server runner, synthetic test data, one1024×768virtual screen. All six frames were visually inspected and copied from original artifact members with unchanged PNG bytes. [Names, source/archive SHA256 and captions](source-manifest.json).

| Capture | Observed scope |
| --- | --- |
| [Scenario RU](launch-scenario-ru.png) | Local preflight/error policies, with explicit consent/no-retry explanation. |
| [Stopped run](launch-scenario-stopped.png) | Intentional missing test target, readable failure and skipped command. |
| [Recovery preview](recovery-center-preview.png) | Actual SQLite schema/count summary, clear external-resource exclusions. Optional missing-directory warning is visible. No restore performed in this native scenario. |
| [Quick Actions desktop](desktop-quick-actions.png) | One real native widget and workspace action. |
| [RC7 downgrade refusal](rc7-downgrade-refusal.png) | Intentional protective refusal by old immutable RC7 when opening a newer copied database. Technical evidence, not a normal product hero. |

[Quick Actions Lab stress frame](../../../reports/evidence/rc8-candidate/technical-quick-actions-lab-obscured.png) is retained **only as technical evidence**: ten stress-test widget windows obscure the editor. It is not a clean marketing screenshot or proof of complete Lab visual acceptance. Nothing was cropped, retouched or re-generated to hide that limitation.

The raw downgrade UIA JSON has serialization question marks; the original PNG visibly shows the actual Russian refusal. No invented corrected JSON was substituted.

Physical Windows10/11, mixed DPI/monitors, Narrator, sleep/RDP and full manual GUI acceptance remain NOT VERIFIED. [Independent byte/native-proof check](../../../reports/evidence/rc8-candidate/independent-artifact-verification.json).
