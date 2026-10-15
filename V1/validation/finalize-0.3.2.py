"""Assemble 0.3.2 evidence from completed reports; never alter model files."""
import hashlib
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'validation/workflow-20261015-123730'
REL = RUN.relative_to(ROOT).as_posix()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


host = read(RUN / 'host-results.json')
loaded = read(RUN / 'loaded-assembly.json')
cleanup = read(RUN / 'cleanup.json')
receipt = read(ROOT / 'outputs/install-20261015-123712-a19f9a4e/install-receipt.json')
package = ROOT / 'dist/Hifc.V1.Revit2020-0.3.2'
assert host['allPassed'] and len(host['models']) == 3
assert loaded['version'] == '0.3.2.0'
assert cleanup['temporary_manifest_removed'] and not cleanup['revit_running'] and cleanup['normal_exit']
assert 'finished recording journal file' in Path(cleanup['journal']).read_bytes().decode('utf-8', errors='replace')
assert read(ROOT / 'validation/assembly-isolation-green.json')['allPassed']
assert not read(ROOT / 'validation/assembly-isolation-red.json')['allPassed']
assert 'RESULT failures=0; tests=74' in (ROOT / 'validation/build-0.3.2-isolated-assembly.log').read_text(encoding='utf-8-sig')
assert read(ROOT / 'validation/ui-0.3.2/ui-tests.json')['passed'] == 21
assert 'Ran 16 tests' in (ROOT / 'validation/python-tests-0.3.2.log').read_text(encoding='utf-8-sig')
assert (ROOT / 'validation/python-tests-0.3.2.log').read_text(encoding='utf-8-sig').strip().endswith('OK')
for item in receipt['files']:
    assert sha(package / item['name']) == sha(Path(receipt['installed_directory']) / item['name']) == item['sha256']
assert loaded['sha256'] == sha(loaded['path']) == sha(package / 'Hifc.V1.Revit.dll')
manifest = Path(receipt['addin_manifest']).read_text(encoding='utf-8-sig')
assert 'Hifc.V1.Revit.dll' in manifest

protected = [x for x in read(ROOT / 'validation/workflow-20261015-095958/preservation.json')['files'] if x['kind'] != 'test_copy_file']
protected += [dict(kind='external_plugin', path=x['path'], expected_sha256=x['sha256']) for x in read(ROOT / 'validation/external-hifc-before-0.3.2.json')]
protected += [dict(kind='test_copy_file', path=x['copy'], expected_sha256=x['sha256'].lower()) for x in read(RUN / 'source-models.json')]
for item in protected:
    item['sha256'] = sha(item['path'])
    item['matches'] = item['sha256'] == item['expected_sha256'].lower()
assert all(x['matches'] for x in protected)
write(RUN / 'preservation.json', dict(allPassed=True, files=protected))

models = []
recognition = []
for model in host['models']:
    stem = Path(model['document']).stem
    native = read(RUN / (stem + '-native.json'))
    assert native['allPassed'] and all(native['checks'].values()) and all(model['checks'].values())
    initial, before, after = native['initial'], model['first_summary'], model['after_summary']
    changes = native['candidate_preview']['Changes']
    classified = [dict(id=c['Id'], name=c['Name'], role=c['NewValue'], evidence=c['Detail']) for c in changes if c['Action'] == '分类']
    models.append(dict(name=Path(model['document']).name, detected_kind=initial['ModelKind'],
        native_passed=True, modeless_passed=True, native_checks=len(native['checks']), modeless_checks=len(model['checks']),
        inventory_count=len(initial['Elements']), automatic_preview_changes=model['preview_changes'],
        expected_classifications=classified, reference_roles_before=before['Roles'], reference_roles_after=after['Roles'],
        fields_present_before=before['PresentFields'], fields_present_after=after['PresentFields'],
        field_count_after=after['FieldCount'], valid_fields_after=after['ValidFields'],
        missing_reference_roles=[r['Slot'] for r in after['Roles'] if r['Mode']=='必要' and r['Count']==0 and r['Candidates']==0],
        pending_reference_roles=[r['Slot'] for r in after['Roles'] if r['Mode']=='必要' and r['Count']==0 and r['Candidates']>0],
        links_uncovered=after['CoverageGaps'], model_ready=after['Ready']))
    recognition.append(dict(model=Path(model['document']).name, classifications_in_preview=classified,
        candidates=[dict(id=r['Id'], name=r['Name'], candidates=r['Candidates']) for r in initial['Elements'] if r['Candidates']],
        written_and_found_after_full_rescan=model['checks']['reviewed_candidates_written_and_found_on_full_rescan']))
assert [len(x['expected_classifications']) for x in models] == [6, 5, 3]
assert all(not x['model_ready'] for x in models)
write(RUN / 'recognition-verification.json', dict(allPassed=True, models=recognition))
native_count = sum(x['native_checks'] for x in models)
modeless_count = sum(x['modeless_checks'] for x in models)
assert native_count == 46 and modeless_count == 51
summary = dict(allPassed=True, model_count=3, native_check_count=native_count, modeless_check_count=modeless_count,
    loaded_version=loaded['version'], loaded_sha256=loaded['sha256'], loaded_matches_installed=True,
    package_hashes_match=True, preservation_file_count=len(protected), inputs_unchanged=True,
    other_hifc_plugin_unchanged=True, plugin_assembly_isolation_verified=True, cleanup=cleanup, models=models)
write(RUN / 'verification-summary.json', summary)

status = read(ROOT / 'validation/management-development-status.json')
status.update(updated_local=datetime.now().astimezone().isoformat(), stage='INSTALLED_NATIVE_RECOGNITION_ACCEPTANCE_PASSED',
    actual_project_external_event_flow='PASSED_THREE_MODEL_COPIES', preservation=REL+'/preservation.json',
    cleanup=cleanup, verification_record=REL+'/验证结论.md',
    native_acceptance=dict(state='PASSED', model_count=3, run=REL, report=REL+'/host-results.json',
        summary=REL+'/verification-summary.json', recognition=REL+'/recognition-verification.json',
        loaded_version=loaded['version'], loaded_sha256=loaded['sha256'], report_present=True,
        native_checks_passed=native_count, modeless_checks_passed=modeless_count, models=models))
status['successful_case_basis']['recognition_evidence'] = 'reference/success-case-01/recognition-evidence.json'
status['successful_case_basis']['recognition_sha256'] = sha(ROOT / status['successful_case_basis']['recognition_evidence'])
status['regressions'].update(recognition_red='validation/recognition-red.log',
    assembly_collision_red='validation/assembly-isolation-red.json', assembly_isolation_green='validation/assembly-isolation-green.json',
    failed_collision_host='validation/workflow-20261015-123029')
write(ROOT / 'validation/management-development-status.json', status)
overall_path = ROOT / 'validation/development-status.json'
overall = read(overall_path)
history_path = ROOT / 'validation/history/development-status-before-0.3.2.json'
if not history_path.exists():
    write(history_path, overall)
overall.update(version='0.3.2', client_date='2026-10-15', updated_local=status['updated_local'],
    phase=status['stage'], core_tests=status['core_tests'], wpf_ui_tests=status['wpf_tests'],
    current_management_validation=status, actual_project_external_event_flow=status['actual_project_external_event_flow'],
    installed_binaries=receipt['files'], install_receipt=status['installed_receipt'], preservation_proof=status['preservation'],
    coverage_basis='Installed 0.3.2: 74 core, 21 standalone production-WPF, 16 Python tests; three model copies passed 46 native service and 51 production WPF/ExternalEvent assertions, including 6 site and 5/3 building classifications. Loaded, installed and packaged hashes match. Historical IFC evidence remains explicitly historical; full multiset IFC, reference-software A/B and platform acceptance are not verified.')
write(overall_path, overall)
print(json.dumps(dict(allPassed=True, native=native_count, modeless=modeless_count, preservation=len(protected),
    models=[dict(name=x['name'], classified=len(x['expected_classifications']), preview=x['automatic_preview_changes']) for x in models]), ensure_ascii=False))
