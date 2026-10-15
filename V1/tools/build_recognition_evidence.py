"""Build a reviewable recognition supplement; never alter frozen case profiles."""
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'validation/workflow-20261015-095958'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    source = ROOT / 'validation/success-case-01/Pset挂载全清单.csv'
    profiles = read(ROOT / 'reference/success-case-01/case-profiles.json')
    copies = read(RUN / 'source-models.json')
    associations, fixtures, unresolved = [], [], []
    with source.open(encoding='utf-8-sig', newline='') as handle:
        attached = list(csv.DictReader(handle))
    for model in profiles['models']:
        stem = model['file'].split('-属性写入-')[0]
        original = next(x for x in copies if Path(x['source']).name.startswith(stem))
        if sha(Path(model['path'])) != model['sha256'] or sha(Path(original['source'])) != original['sha256'].lower():
            raise ValueError('Source differs from accepted extraction: ' + stem)
        native_path = RUN / (Path(original['source']).stem + '-native.json')
        rows = {str(row['Id']): row for row in read(native_path)['initial']['Elements']}
        for item in attached:
            if item['文件'] != model['file'] or item['参考HIFC集'] != 'True' or item['来源'] != 'direct':
                continue
            if item['IFC类'] in ('IfcBuilding', 'IfcBuildingStorey') or item['IFC类'].endswith('Type'):
                continue
            slot = item['属性集'].removeprefix('Pset_').removesuffix('信息属性集')
            row = rows.get(item['Revit_Tag'])
            if row is None:
                unresolved.append({'SourceIfc': model['file'], 'StepId': int(item['STEP_ID']), 'Slot': slot,
                                   'IfcEntity': item['IFC类'], 'Reason': 'No matching Revit Tag; do not invent a carrier relationship'})
                continue
            caution = '案例名称为停车位却挂道路边界线；保留为待核对证据，不能自动套用' if slot == '道路边界线' and '停车位' in row['Name'] else ''
            associations.append({'Scope': model['scope'], 'SourceIfc': model['file'], 'SourceIfcSha256': model['sha256'],
                'SourceRvt': Path(original['source']).name, 'SourceRvtSha256': original['sha256'].lower(),
                'SnapshotSha256': sha(native_path), 'StepId': int(item['STEP_ID']), 'IfcGlobalId': item['GlobalId'],
                'RevitTag': int(item['Revit_Tag']), 'UniqueId': row['UniqueId'], 'Name': row['Name'],
                'TypeName': row['TypeName'], 'Category': row['Category'], 'Entity': row['Entity'],
                'IfcEntity': item['IFC类'], 'Slot': slot, 'Caution': caution})
            fixtures.append({'Scope': model['scope'], 'ExpectedSlot': slot, 'Row': {key: row[key] for key in
                ('Id', 'UniqueId', 'Name', 'TypeName', 'Category', 'Entity', 'Slots', 'Classified')}})
    evidence = {'Schema': 'hifc.recognition-evidence/1.0', 'Policy': 'Candidates only; match UniqueId and verify current identity; no source values copied',
                'SourceTableSha256': sha(source), 'Correspondences': associations, 'Unresolved': unresolved}
    out = ROOT / 'reference/success-case-01/recognition-evidence.json'
    out.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    fixture = ROOT / 'tests/fixtures/recognition-rows.json'
    fixture.parent.mkdir(exist_ok=True)
    fixture.write_text(json.dumps(fixtures, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'correspondences': len(associations), 'unresolved': len(unresolved), 'sha256': sha(out)}))


if __name__ == '__main__':
    main()
