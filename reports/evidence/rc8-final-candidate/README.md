# RC8 final candidate — actual binary and retained native evidence

Source `28b69a5dc2e55361339e69e8743f2442691dcf74`, canonical Windows run43661035655 SUCCESS. **Not published**; owner approval remains separate. Older932checkpoint is preserved separately and is not counted as a second final suite.

[Independent machine proof](independent-artifact-verification.json) · [Actual CLI result](independent-artifact-verification-cli.txt) · [Workflow API](workflow-run.json) · [Artifact API](artifact-api.json).

Original artifact14140392572:190367048bytes, SHA256 `0b69bb713bb4970c24b2b84eae3ec78a9b867dedb27dba4a245eba7e2ca39d09`, independently computed and matched GitHub's digest. The verifier reads only actual members of that downloaded ZIP; no stale extraction-directory files are trusted.

```sh
python reports/evidence/rc8-final-candidate/verify_downloaded_candidate.py /path/to/original-final-candidate.zip
```

Actual Windows TRX member: `test-results/runneradmin_runnervmm7ud1_2026-11-15_01_02_23_net10.0.trx`.514passed,0failed,0skipped; all514 UnitTestResult entries agree. Exact TRX SHA256 is in machine proof. Counts are not summed across reruns/platforms.

Checks passed: both actual binary SHA256/sizes; outer/nested ZIP CRC, path/case/duplicate guards; actual AMD64 PE32+ SHIFT.exe, nonempty resources.pri and portable.flag; no named packaged user data/databases; unsigned application and normal x86 Inno stub. Retained portable/installed scenario and SQLite inspection gates,21Studio gates including actual Quick Actions editor/restart/command-consent/dispatch, installer preservation and RC7 schema2→3 migration/safety backup/refusal were cross-checked. Wallpaper cleanup is WALLPAPER_RESTORED.

Original RC7 UIA helper required two recorded readiness/focus adaptations; original_harness_passed remains false. RC7 binary bytes were unchanged. UTF8 observed downgrade title/reason now exactly match actual Russian refusal. No manufactured text replacement was used.

Native Recovery Center test covers inspection without restoring; this is not full-resource restore acceptance. [Real screenshots and visual limits](../../../docs/screenshots/rc8-final-candidate/README.md). The new Lab capture is unobscured but scrolled, not a full editor/DPI acceptance matrix. PNG bytes remain unchanged; Git may normalize JSON/txt line endings. Physical Windows/DPI/accessibility, live hardware and full manual matrix remain NOT VERIFIED. No signed attestation, antivirus or SmartScreen certification claimed.
