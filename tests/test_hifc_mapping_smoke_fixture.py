import copy
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
BASELINE = (
    ROOT
    / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
)
GENERATOR = ROOT / "tools/hifc/generate_hifc_mapping_smoke.py"
FIXTURE = ROOT / "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc"
FIXTURE_MANIFEST = FIXTURE.with_suffix(".manifest.json")
IFCFLUX_B_SHA256 = (
    "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632"
)


def test_generator_entrypoint_exists():
    assert GENERATOR.is_file()


def _source():
    return json.loads(SOURCE.read_text(encoding="utf-8"))


def _actual_mapping(ifc_bytes):
    from tools.hifc.validate_hifc_mapping_smoke import (
        build_actual_mapping,
        parse_entities,
    )

    entities = parse_entities(ifc_bytes.decode("utf-8"))
    actual, property_sets, attachments = build_actual_mapping(entities)
    return entities, actual, property_sets, attachments


def test_generator_is_deterministic_for_identical_validated_source():
    from tools.hifc.generate_hifc_mapping_smoke import build_ifc_bytes

    first_bytes, first_summary = build_ifc_bytes(_source())
    second_bytes, second_summary = build_ifc_bytes(_source())

    assert first_bytes == second_bytes
    assert first_summary == second_summary
    assert first_summary.step_entities == 616
    assert first_summary.properties == 359
    assert first_summary.property_sets == first_summary.attachments == 52
    assert first_summary.owner_types == (
        "IfcActor",
        "IfcBuilding",
        "IfcBuildingStorey",
        "IfcDoor",
        "IfcDuctSegment",
        "IfcProject",
        "IfcRoof",
        "IfcSite",
        "IfcSlab",
        "IfcSpace",
        "IfcSpatialZone",
        "IfcStairFlight",
        "IfcWall",
        "IfcWindow",
    )
    assert first_summary.extruded_solids == 9


def test_generator_emits_every_effective_identity_exactly_once():
    from tools.build_hbr_rulepack import effective_ifc_identity
    from tools.hifc.generate_hifc_mapping_smoke import build_ifc_bytes

    source = _source()
    ifc_bytes, _ = build_ifc_bytes(source)
    _, actual, property_sets, attachments = _actual_mapping(ifc_bytes)
    expected = []
    for rule in source["properties"]:
        entity, property_set, property_name = effective_ifc_identity(rule)
        expected.append(
            (
                "IFCACTOR" if entity == "IfcOrganization" else entity.upper(),
                property_set,
                property_name,
            )
        )

    assert len(expected) == len(set(expected)) == len(actual) == 359
    assert set(actual) == set(expected)
    assert len(property_sets) == len(attachments) == 52


def test_generator_emits_only_unspaced_xy_output_identities():
    from tools.hifc.generate_hifc_mapping_smoke import build_ifc_bytes

    ifc_bytes, _ = build_ifc_bytes(_source())
    _, actual, _, _ = _actual_mapping(ifc_bytes)

    assert ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标X") in actual
    assert ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标Y") in actual
    assert ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 X") not in actual
    assert ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标 Y") not in actual
    assert actual[("IFCPROJECT", "Pset_申报信息属性集", "基点坐标X")][
        "typed_token"
    ] == "IFCREAL(3353559.52)"
    assert actual[("IFCPROJECT", "Pset_申报信息属性集", "基点坐标Y")][
        "typed_token"
    ] == "IFCREAL(38345264.397)"


def test_generator_writes_utf8_without_bom_lf_and_no_environment_metadata():
    from tools.hifc.generate_hifc_mapping_smoke import build_ifc_bytes

    ifc_bytes, _ = build_ifc_bytes(_source())
    text = ifc_bytes.decode("utf-8")

    assert not ifc_bytes.startswith(b"\xef\xbb\xbf")
    assert b"\r" not in ifc_bytes
    assert ifc_bytes.endswith(b"\n") and not ifc_bytes.endswith(b"\n\n")
    assert "FILE_SCHEMA(('IFC4'));" in text
    assert "ViewDefinition [ReferenceView_V1.2]" in text
    assert "2026-08-07T18:00:00+08:00" in text
    assert "\\X2\\57FA70B9575068070058\\X0\\" in text
    forbidden = (
        str(ROOT),
        str(Path.home()),
        os.environ.get("USERNAME", "__missing_username__"),
        ".tmp",
        "sourceCommit",
    )
    assert all(value not in text for value in forbidden if value)


def test_generator_rejects_invalid_source_without_outputs_or_temp_files(tmp_path):
    from tools.hifc.generate_hifc_mapping_smoke import generate_fixture

    source = _source()
    source["properties"].pop()
    invalid_source = tmp_path / "invalid source.json"
    invalid_source.write_text(json.dumps(source, ensure_ascii=False), encoding="utf-8")
    output = tmp_path / "invalid.ifc"
    manifest = tmp_path / "invalid.manifest.json"

    with pytest.raises(ValueError):
        generate_fixture(invalid_source, BASELINE, output, manifest)

    assert not output.exists()
    assert not manifest.exists()
    assert not list(tmp_path.glob(".*.tmp"))


def test_generator_cli_supports_paths_with_spaces(tmp_path):
    spaced = tmp_path / "fixture path with spaces"
    spaced.mkdir()
    source = spaced / "rule source.json"
    baseline = spaced / "compatibility baseline.json"
    output = spaced / "mapping smoke.ifc"
    manifest = spaced / "mapping smoke.manifest.json"
    source.write_bytes(SOURCE.read_bytes())
    baseline.write_bytes(BASELINE.read_bytes())

    result = subprocess.run(
        [
            sys.executable,
            str(GENERATOR),
            "--source",
            str(source),
            "--baseline",
            str(baseline),
            "--output",
            str(output),
            "--manifest",
            str(manifest),
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert output.is_file() and manifest.is_file()
    manifest_document = json.loads(manifest.read_text(encoding="utf-8"))
    assert manifest_document["fixture"]["sha256"] == hashlib.sha256(
        output.read_bytes()
    ).hexdigest()
    assert manifest_document["summary"]["stepEntities"] == 616
