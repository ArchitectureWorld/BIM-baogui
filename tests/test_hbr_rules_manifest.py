import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / "tools/build_hbr_rules_manifest.py"
MANIFEST = ROOT / "specs/hbr-rules/v1/manifest.sha256.json"
SOURCE = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
BASELINE = ROOT / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
FIXTURE = ROOT / "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc"
FIXTURE_MANIFEST = FIXTURE.with_suffix(".manifest.json")
ACCEPTANCE = ROOT / "docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json"

EXPECTED_PATHS = (
    "docs/hifc/HBR_HIFC_mapping_authority_v1.md",
    "docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json",
    "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json",
    "specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json",
    "specs/hbr-rules/v1/source/hbr_rule_source.v1.json",
    "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc",
    "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json",
    "tools/build_hbr_rulepack.py",
    "tools/build_hbr_rules_manifest.py",
    "tools/hifc/generate_hifc_mapping_smoke.py",
    "tools/hifc/validate_hifc_mapping_smoke.py",
)


def test_committed_rules_manifest_exists():
    MANIFEST.read_bytes()


def test_manifest_freezes_exact_authoritative_paths_and_real_bytes():
    from tools.build_hbr_rules_manifest import FROZEN_PATHS

    document = json.loads(MANIFEST.read_text(encoding="utf-8"))
    paths = [item["path"] for item in document["files"]]

    assert FROZEN_PATHS == EXPECTED_PATHS
    assert paths == sorted(EXPECTED_PATHS)
    assert len(paths) == len(set(paths))
    assert all("\\" not in path and not Path(path).is_absolute() for path in paths)
    assert MANIFEST.relative_to(ROOT).as_posix() not in paths
    for item in document["files"]:
        content = (ROOT / item["path"]).read_bytes()
        assert item == {
            "path": item["path"],
            "bytes": len(content),
            "sha256": hashlib.sha256(content).hexdigest(),
        }


def test_manifest_contract_hashes_counts_and_identity_are_exact():
    from tools.build_hbr_rulepack import build_rulepack_bytes, canonical_bytes, load_validated_rule_source

    document = json.loads(MANIFEST.read_text(encoding="utf-8"))
    source = load_validated_rule_source(SOURCE, BASELINE)
    pack = build_rulepack_bytes(source)
    payload = canonical_bytes(source)

    assert document["schemaVersion"] == "1.0.0"
    assert document["manifestId"] == "HBR-WUHAN-PLANNING-1.0.0-BASELINE"
    assert document["packageId"] == "HBR-WUHAN-PLANNING"
    assert document["packageVersion"] == "1.0.0"
    assert document["runtimeStatusCounts"] == {
        "NOT_IMPLEMENTED": 57,
        "UNCLASSIFIED_REQUIREMENT": 302,
    }
    assert document["officialIdentityMatches"] == 166
    _assert_file_record(document["ruleSource"], SOURCE)
    assert document["ruleSource"]["canonicalSha256"] == hashlib.sha256(payload).hexdigest()
    _assert_file_record(document["compatibilityBaseline"], BASELINE)
    _assert_file_record(document["fixture"], FIXTURE)
    assert document["fixture"]["manifestPath"] == FIXTURE_MANIFEST.relative_to(ROOT).as_posix()
    assert document["fixture"]["manifestSha256"] == _sha256(FIXTURE_MANIFEST.read_bytes())
    assert document["fixture"]["acceptancePath"] == ACCEPTANCE.relative_to(ROOT).as_posix()
    assert document["fixture"]["acceptanceSha256"] == _sha256(ACCEPTANCE.read_bytes())
    assert document["rulePack"] == {
        "logicalPath": "src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack",
        "bytes": len(pack),
        "sha256": _sha256(pack),
        "payloadSha256": _sha256(payload),
    }


def test_builds_are_deterministic_and_committed_bytes_are_canonical(tmp_path):
    from tools.build_hbr_rules_manifest import build_rules_manifest, canonical_json_bytes

    first = tmp_path / "first manifest.json"
    second = tmp_path / "second manifest.json"
    first_document = build_rules_manifest(ROOT, first)
    second_document = build_rules_manifest(ROOT, second)

    assert first_document == second_document
    assert first.read_bytes() == second.read_bytes() == MANIFEST.read_bytes()
    assert MANIFEST.read_bytes() == canonical_json_bytes(first_document)


def test_tampered_fixture_bytes_fail_closed():
    from tools.build_hbr_rules_manifest import build_rules_manifest_document

    fixture_bytes = FIXTURE.read_bytes()
    with pytest.raises(ValueError, match="fixture bytes"):
        build_rules_manifest_document(
            ROOT,
            SOURCE,
            fixture_bytes + b"TAMPERED",
            FIXTURE_MANIFEST.read_bytes(),
        )


def test_missing_frozen_file_fails_closed_without_output_or_temp(tmp_path):
    from tools.build_hbr_rules_manifest import build_rules_manifest

    copied_root = _copy_frozen_root(tmp_path)
    (copied_root / EXPECTED_PATHS[0]).unlink()
    output = copied_root / "specs/hbr-rules/v1/missing.json"

    with pytest.raises((FileNotFoundError, ValueError)):
        build_rules_manifest(copied_root, output)

    assert not output.exists()
    assert not list(output.parent.glob(f".{output.name}.*.tmp"))


@pytest.mark.parametrize(
    "bad_paths",
    [
        (EXPECTED_PATHS[0], EXPECTED_PATHS[0]),
        ("C:/absolute.json",),
        ("../outside.json",),
    ],
)
def test_invalid_frozen_paths_fail_closed(monkeypatch, tmp_path, bad_paths):
    import tools.build_hbr_rules_manifest as builder

    monkeypatch.setattr(builder, "FROZEN_PATHS", bad_paths)
    output = tmp_path / "invalid.json"
    with pytest.raises(ValueError):
        builder.build_rules_manifest(ROOT, output)
    assert not output.exists()
    assert not list(tmp_path.glob(".invalid.json.*.tmp"))


def test_output_cannot_conflict_with_any_input_and_cli_supports_spaces(tmp_path):
    from tools.build_hbr_rules_manifest import build_rules_manifest

    with pytest.raises(ValueError, match="output"):
        build_rules_manifest(ROOT, SOURCE)

    spaced = tmp_path / "folder with spaces" / "manifest output.json"
    subprocess.run(
        [sys.executable, str(BUILDER), "--root", str(ROOT), "--output", str(spaced)],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    assert spaced.read_bytes() == MANIFEST.read_bytes()


def test_manifest_contains_no_environment_metadata():
    text = MANIFEST.read_text(encoding="utf-8")
    document = json.loads(text)
    forbidden = {"timestamp", "createdAt", "generatedAt", "commit", "gitCommit", "root", "username", "user"}

    assert forbidden.isdisjoint(document)
    assert str(ROOT) not in text
    assert "2899" not in text


def _sha256(content):
    return hashlib.sha256(content).hexdigest()


def _assert_file_record(record, path):
    content = path.read_bytes()
    assert record["path"] == path.relative_to(ROOT).as_posix()
    assert record["bytes"] == len(content)
    assert record["sha256"] == _sha256(content)


def _copy_frozen_root(tmp_path):
    copied_root = tmp_path / "copied worktree"
    (copied_root / ".git").mkdir(parents=True)
    for logical_path in EXPECTED_PATHS:
        source = ROOT / logical_path
        target = copied_root / logical_path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    return copied_root
