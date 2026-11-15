# RC8 merged-main candidate — actual binary and retained native evidence

Source `a5effcdcc4861ff099636be7782dd5155a029671`, canonical Windows run43665382675 SUCCESS. **Not published**; owner approval remains separate. PR24 is merged. Older932/28bcheckpoints are preserved separately and are not counted as additional unique tests. [Main CI43665373830](https://github.com/LosarOS/SHIFT/actions/runs/43665373830) also succeeded for the same source; [retained API state](main-ci-run.json).

[Independent machine proof](independent-artifact-verification.json) · [Actual CLI result](independent-artifact-verification-cli.txt) · [Workflow API](workflow-run.json) · [Artifact API](artifact-api.json).

Original artifact14142410522:190105590bytes, SHA256 `21cf63ad6c28c66c0bac74d17619437eea2ed4f1b9532c33ce7a472686752cd5`, independently computed and matched GitHub's digest. The verifier reads only actual members of that downloaded ZIP; no stale extraction-directory files are trusted.

```sh
python reports/evidence/rc8-main-candidate/verify_downloaded_candidate.py /path/to/original-final-candidate.zip
```

Actual Windows TRX member: `test-results/runneradmin_runnervmm7ud1_2026-11-15_01_57_35_net10.0.trx`.514passed,0failed,0skipped; all514 UnitTestResult entries agree. Exact TRX SHA256 is in machine proof. Counts are not summed across reruns/platforms.

Checks passed: both actual binary SHA256/sizes; outer/nested ZIP CRC, path/case/duplicate guards; actual AMD64 PE32+ SHIFT.exe, nonempty resources.pri and portable.flag; no named packaged user data/databases; unsigned application and normal x86 Inno stub. Retained portable/installed scenario and SQLite inspection gates,21Studio gates including actual Quick Actions editor/restart/command-consent/dispatch, installer preservation and RC7 schema2→3 migration/safety backup/refusal were cross-checked. Wallpaper cleanup is WALLPAPER_RESTORED.

Original RC7 UIA helper required two recorded readiness/focus adaptations; original_harness_passed remains false. RC7 binary bytes were unchanged. UTF8 observed downgrade title/reason now exactly match actual Russian refusal. No manufactured text replacement was used.

Native Recovery Center test covers inspection without restoring; this is not full-resource restore acceptance. [Real screenshots and visual limits](../../../docs/screenshots/rc8-main-candidate/README.md). The new Lab capture is unobscured but scrolled, not a full editor/DPI acceptance matrix. PNG bytes remain unchanged; Git may normalize JSON/txt line endings. Physical Windows/DPI/accessibility, live hardware and full manual matrix remain NOT VERIFIED. No signed attestation, antivirus or SmartScreen certification claimed.
