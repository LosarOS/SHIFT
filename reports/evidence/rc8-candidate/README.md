# RC8 candidate checkpoint — independently verified actual bytes

Source `932999ab9f702cbfcd53f308ae985d4bc5cf7a43`; canonical Windows run43659620650 succeeded. GitHub artifact14139648135 was downloaded as a190173278-byte ZIP; independent SHA256 matches the GitHub artifact digest. This is **not a public release** and may be superseded by later source fixes.

[Machine proof](independent-artifact-verification.json) · [Actual CLI output](independent-artifact-verification-cli.txt) · [Workflow API](workflow-run.json) · [Artifact API](artifact-api.json).

Reproduce without executing/extracting binaries:

```sh
python reports/evidence/rc8-candidate/verify_downloaded_candidate.py /path/to/original-canonical-artifact.zip
```

All artifact checks read original ZIP members only. A stale root-level `rc8-final.trx` in the extraction directory belonged to an earlier Linux509-case run and was explicitly excluded. Actual Windows evidence is `test-results/runneradmin_runnervmm7ud1_2026-11-15_00_45_23_net10.0.trx`:512passed,0failed,0skipped. Exact member and SHA256 are in machine proof; no duplicate-platform/run totals are added.

Verified both binary sizes/SHA256, outer/nested CRC and path/case safety, actual AMD64 PE32+ SHIFT.exe, nonempty resources.pri/portable.flag, no named packaged data/database, unsigned application and normal x86 Inno stub, retained portable/installed scenario/inspection gates,21Studio gates including Quick Actions editor/restart/consent/dispatch, wallpaper exact cleanup and actual RC7 schema2→3 upgrade/safety backup/old-version refusal without data loss.

Original RC7 smoke helper did not pass unchanged. The baseline used two explicitly recorded UIA readiness/focus helper adaptations; the published RC7 binary was unchanged. Raw proof retains that limitation. Recovery Center's native scenario is inspection, not a restore/full-resource bundle test. JSON/txt proofs are retained semantically; Git may normalize their line endings. PNG bytes are unchanged and independently hashed.

[Real captures and exact visual limits](../../../docs/screenshots/rc8-candidate/README.md). The Lab stress frame is occluded and retained here only as technical evidence, not a product hero. Physical Windows10/11/DPI/accessibility/sleep/RDP/full manual matrix remains NOT VERIFIED. No Authenticode trust, antivirus or SmartScreen certification is claimed. Canonical workflow success is not a substitute for owner approval to publish.
