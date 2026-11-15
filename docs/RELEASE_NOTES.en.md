# SHIFT 1.0.0-rc.8 — launch policies and quick actions

Candidate notes. Publication requires completed Windows gates and separate owner approval. [Русский](RELEASE_NOTES.md).

## New capabilities

- Choose a read-only Workspace Doctor preflight policy: continue with findings, stop before dispatch on errors, or skip invalid items. Independently choose whether a dispatch failure stops the remaining sequence. Defaults preserve existing behavior.
- Create native Quick Actions widgets in Widget Lab with up to12 workspace, local file/folder or HTTP(S) destinations. Workspace buttons use the normal launch flow and command approval. Preview and import never execute actions.
- Browse and inspect SQLite backups in Recovery Center before explicitly restoring. Inspection uses an owned WAL-consistent snapshot and shows validated content counts; the existing restore path revalidates and creates a safety copy.

## Reliability and compatibility

Schema3 supports the new widget type through a transactional migration and a validated previous-schema backup. Original four widget kinds retain their identifiers and omit empty new fields in JSON. Older executables refuse the newer schema; downgrade requires the matching old backup. Preflight aborts before dispatch do not apply linked Studio appearance.

Backup inspection is not a complete resource archive. Before upgrade, Quit SHIFT and copy the full data directory including Studio/Images, Studio/RecoveryNotes and migration backups. Do not delete original wallpaper files.

## Installation and evidence

After publication, choose the x64 setup EXE or fully extract the portable ZIP. Existing public downloads remain RC7 until the candidate is approved. [RC8 guide](RC8-GUIDE-EN.md) · [validation journal](../reports/PRODUCT-EVOLUTION-2026-11-14.md).

514 unique core tests passed on Linux and Windows. Candidate Windows publish, native UI/Studio, installer and actual RC7-upgrade checks have separate retained evidence; their final status is recorded in the journal rather than inferred from core compilation.

## Limitations

Unsigned binaries may trigger SmartScreen. Physical Windows10/11, mixed DPI/displays, Narrator, sleep/RDP and long-running acceptance are separate from disposable Windows CI. Successful dispatch does not prove third-party application readiness. There is no automatic retry, process shutdown or arbitrary shell execution in Quick Actions.
