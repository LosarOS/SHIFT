#!/usr/bin/env python3
"""Verify actual downloaded canonical artifact without executing or extracting it.

Run: python verify_downloaded_candidate.py /path/to/shift-rc8-final-candidate.zip
Output is printed as JSON. No writes or network requests are performed.
"""
import hashlib
import json
import re
import stat
import struct
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

SOURCE = '932999ab9f702cbfcd53f308ae985d4bc5cf7a43'
RUN = 43659620650
VERSION = '1.0.0-rc.8'
TRX = 'test-results/runneradmin_runnervmm7ud1_2026-11-15_00_45_23_net10.0.trx'


def demand(ok, why):
    if not ok:
        raise ValueError(why)


def measure(stream):
    h = hashlib.sha256(); size = 0
    while block := stream.read(1024 * 1024):
        h.update(block); size += len(block)
    return size, h.hexdigest()


def inspect_zip(z):
    entries = z.infolist(); names = set(); size = 0
    demand(0 < len(entries) <= 25000, 'ZIP entry count outside bounds')
    for e in entries:
        raw = e.filename; parts = raw.rstrip('/').split('/')
        demand(raw and '\\' not in raw and '\0' not in raw and not PurePosixPath(raw).is_absolute(), 'Unsafe raw ZIP path')
        demand(all(p not in {'', '.', '..'} and ':' not in p and not p.endswith((' ', '.')) for p in parts), 'Unsafe ZIP component')
        demand(not any(re.match(r'(?i)^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)', p) for p in parts), 'Reserved Windows name')
        name = raw.rstrip('/').casefold(); demand(name not in names, 'Duplicate/case-colliding ZIP name'); names.add(name)
        demand(not stat.S_ISLNK(e.external_attr >> 16) and not e.flag_bits & 1, 'Symlink/encrypted entry')
        demand(e.file_size <= 512 * 1024**2, 'Excessive individual size'); size += e.file_size
    demand(size <= 3 * 1024**3 and z.testzip() is None, 'ZIP size/CRC failed')
    return {'entries': len(entries), 'uncompressed_bytes': size, 'crc': 'passed', 'paths_and_case_duplicates': 'passed'}


def inspect_pe(stream, x64=False):
    data = stream.read(1024 * 1024); demand(data[:2] == b'MZ' and len(data) >= 64, 'Missing MZ')
    p = struct.unpack_from('<I', data, 60)[0]; demand(p + 200 < len(data) and data[p:p+4] == b'PE\0\0', 'Missing PE')
    machine = struct.unpack_from('<H', data, p+4)[0]; optional_size = struct.unpack_from('<H', data, p+20)[0]; o = p+24
    magic = struct.unpack_from('<H', data, o)[0]; demand(magic in {0x10b, 0x20b}, 'Bad optional PE header')
    if x64: demand(machine == 0x8664 and magic == 0x20b, 'App must be AMD64 PE32+')
    base, count = (112,108) if magic == 0x20b else (96,92)
    demand(optional_size >= base+40 and struct.unpack_from('<I', data, o+count)[0] >= 5, 'No security directory')
    offset, size = struct.unpack_from('<II', data, o+base+32); demand(offset == size == 0, 'Expected unsigned PE')
    return {'machine': hex(machine), 'format': 'PE32+' if magic == 0x20b else 'PE32', 'certificate_table_bytes': size, 'signature_status': 'unsigned (no PE certificate table)'}


def verify(path):
    with path.open('rb') as f: archive_size, archive_sha = measure(f)
    with zipfile.ZipFile(path) as z:
        outer = inspect_zip(z)
        def read_json(n): return json.loads(z.read(n).decode('utf-8-sig'))
        manifest = read_json('WINDOWS-VALIDATION.json')
        demand(manifest['source_commit'] == SOURCE and manifest['version'] == VERSION and manifest['workflow_run'].endswith('/'+str(RUN)), 'Canonical binding mismatch')
        demand(manifest['signature'] == 'unsigned' and manifest['sqlite_schema'] == 3, 'Signature/schema mismatch')
        demand(manifest['interactive_physical_windows_acceptance'] == manifest['dpi_narrator_high_contrast_matrix'] == 'not_verified', 'Acceptance boundary changed')
        core = manifest['core_tests']; demand(core == {'total':512,'passed':512,'failed':0,'skipped':0}, 'Wrong test counts')
        trx_bytes = z.read(TRX); trx = ET.fromstring(trx_bytes); ns = {'t':trx.tag.split('}')[0][1:]}; c = trx.find('.//t:Counters',ns).attrib
        demand(all(int(c[k]) == v for k,v in {'total':512,'executed':512,'passed':512,'failed':0,'error':0,'notExecuted':0}.items()), 'Windows TRX mismatch')
        results = trx.findall('.//t:UnitTestResult',ns); demand(len(results) == 512 and all(r.attrib['outcome']=='Passed' for r in results), 'TRX results disagree')
        sums = {}
        for line in z.read('SHA256SUMS.txt').decode('utf-8-sig').splitlines():
            m = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9.\-]+)',line); demand(m and m[2] not in sums,'Bad checksum entry'); sums[m[2]]=m[1]
        portable = f'SHIFT-{VERSION}-win-x64-portable.zip'; setup = f'SHIFT-{VERSION}-win-x64-setup.exe'
        demand(set(sums)=={portable,setup},'Unexpected binary inventory')
        assets = []
        for name in [portable,setup]:
            with z.open(name) as f: size, sha = measure(f)
            demand(size>0 and sha==sums[name] and size==z.getinfo(name).file_size,'Binary SHA/size mismatch')
            assets.append({'file':name,'bytes':size,'sha256':sha})
        with z.open(setup) as f: setup_pe = inspect_pe(f)
        with tempfile.TemporaryFile() as f:
            with z.open(portable) as src:
                while b:=src.read(1024*1024): f.write(b)
            f.seek(0)
            with zipfile.ZipFile(f) as p:
                package = inspect_zip(p); prefix=f'SHIFT-{VERSION}-win-x64-portable/'
                demand(all(n.startswith(prefix) for n in p.namelist()),'Unexpected portable root')
                demand(all(prefix+n in p.namelist() for n in ['SHIFT.exe','resources.pri','portable.flag']),'Missing EXE/PRI/portable flag')
                demand(p.getinfo(prefix+'resources.pri').file_size>0,'Empty PRI')
                demand(not any(n[len(prefix):].casefold().startswith(('data/','logs/','backups/')) or n.casefold().endswith(('.db','.db-wal','.db-shm','.sqlite','.sqlite3')) for n in p.namelist()),'Packaged user data/database')
                with p.open(prefix+'SHIFT.exe') as exe: app_pe=inspect_pe(exe,True)
                package['required_resources_present']=True; package['packaged_user_data']='none found by named path/database guards'
        native = {}
        for name in ['diagnostics/portable-smoke.json','diagnostics/installed-smoke.json']:
            x=read_json(name); demand(x['status']=='passed' and x['manual_acceptance'] is False,'Native smoke failed')
            gates={'workspace_scenario_preflight_stop_no_dispatch','workspace_scenario_settings_restart_and_cleanup','recovery_center_inspection_no_restore','command_refusal','command_approval','real_dispatch','restart'}
            demand(gates.issubset(x['checks']),'Missing native feature gates'); native[name]=x['checks']
        for name in ['diagnostics/installer-smoke.json','diagnostics-reinstall/installed-smoke.json']:
            x=read_json(name); demand(x['status']=='passed' and x['manual_acceptance'] is False,'Installer/reinstall failed');native[name]=x['checks']
        for mode in ['portable','installed']:
            x=read_json(f'diagnostics/{mode}-recovery-logical-proof.json');demand(x['source_commit']==SOURCE and x['status']=='passed' and x['before']==x['after'],'Recovery inspection modified user rows')
        studio=read_json('diagnostics/studio/studio-smoke.json');demand(studio['source_commit']==SOURCE and studio['status']=='passed' and studio['wallpaper_verified'] is True and studio['manual_acceptance'] is False,'Studio failed')
        demand(len(set(studio['checks']))>=21 and 'quick_actions_lab_restart_consent_dispatch' in studio['checks'],'Missing Studio feature gate')
        actions=read_json('diagnostics/studio/quick-actions-native.json');demand(actions['status']=='passed' and all(actions[k] is True for k in ['editor_persisted','restart','command_consent','actual_dispatch']),'Quick Actions failed')
        demand(z.read('diagnostics/studio/studio-wallpaper-cleanup.txt').decode('utf-8-sig').strip()=='WALLPAPER_RESTORED','Wallpaper not restored')
        upgrade=read_json('diagnostics/rc7-upgrade/rc7-upgrade-smoke.json');demand(upgrade['source_commit']==SOURCE and upgrade['status']=='passed' and upgrade['manual_acceptance'] is False,'Upgrade failed')
        ug=['workspace_history_settings_preserved','actual_schema2_to3_upgrade','actual_rc7_ui_baseline','old_rc7_visible_downgrade_refusal','downgrade_copy_sha256_unchanged','original_upgraded_profile_unchanged','validated_schema2_safety_backup']
        demand(all(upgrade[k] is True for k in ug),'Upgrade preservation/downgrade gate failed')
        demand(upgrade['baseline_source']=='7bb1760b7095bcc254b25807b9eb2934cd807919' and upgrade['baseline_portable_sha256']=='4ee847d358c89eab5b3add2228db9c4d5f51762595ddd60776086595d2d8d98d','RC7 baseline mismatch')
        demand(upgrade['original_harness_passed'] is False and bool(upgrade['harness_adaptation']),'Harness limit must stay disclosed')
        refusal=read_json('diagnostics/rc7-upgrade/rc7-downgrade-observed-ui.json');demand(refusal['visible_refusal'] is True,'No observed downgrade refusal')
        sharing=read_json('diagnostics/studio/studio-sharing-smoke.json');demand(sharing['source_commit']==SOURCE and sharing['status']=='passed' and sharing['existing_data_unchanged'] and sharing['live_widget_hwnds_unchanged'],'Sharing failed')
        for k,v in manifest.items():
            if isinstance(v,str) and k not in {'version','source_commit','workflow_run','environment','signature','rc7_harness_adaptation','interactive_physical_windows_acceptance','dpi_narrator_high_contrast_matrix'}:
                demand(v=='passed','Unexpected manifest failure '+k)
        return {'status':'ACTUAL CANDIDATE ARTIFACT AND RETAINED NATIVE EVIDENCE VERIFIED','source_commit':SOURCE,'workflow_run':RUN,'version':VERSION,'publication_performed':False,'outer_artifact':{'file':path.name,'bytes':archive_size,'sha256':archive_sha,'archive_checks':outer},'assets':assets,'portable':package,'application_pe':app_pe,'installer_pe':setup_pe,'core_tests':core,'windows_trx':{'archive_member':TRX,'sha256':hashlib.sha256(trx_bytes).hexdigest(),'counters':{k:int(v) for k,v in c.items()},'unit_results':len(results),'older_root_trx_excluded':True},'native_feature_gates':native,'studio_gates':studio['checks'],'quick_actions_native':actions,'upgrade_verified_gates':ug,'upgrade_harness_limit':upgrade['harness_adaptation'],'original_rc7_harness_passed':False,'wallpaper_cleanup':'WALLPAPER_RESTORED','physical_windows_dpi_acceptance':'NOT VERIFIED','limits':['Artifact bytes and retained proofs verified; no independent Windows runtime rerun by this script.','Recovery native scenario is inspection without restore, not a GUI full-resource recovery test.','Quick Actions Lab screenshot is stress-occluded and is technical evidence only.','Raw downgrade UIA text has serialization question marks; unaltered PNG visibly contains the actual Russian refusal.','No signed build attestation, antivirus or SmartScreen certification.']}


if __name__=='__main__':
    try:
        demand(len(sys.argv)==2,'Pass one owner-supplied canonical artifact ZIP path')
        print(json.dumps(verify(Path(sys.argv[1])),indent=2))
    except (ValueError,OSError,KeyError,zipfile.BadZipFile,ET.ParseError,json.JSONDecodeError) as ex:
        raise SystemExit('VERIFICATION FAILED: '+str(ex)) from None
