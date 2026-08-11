import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

import pytest


ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / "tools/build_hbr_artifact_manifest.py"
RULES_MANIFEST = ROOT / "specs/hbr-rules/v1/manifest.sha256.json"
COMMIT_SHA = "a" * 40
EXPECTED_FIELDS = {
    "artifactName",
    "assemblyVersion",
    "sha256",
    "sizeBytes",
    "commitSha",
    "rulePackageId",
    "rulePackageVersion",
    "ruleSourcePath",
    "ruleSourceSha256",
    "ruleSourceCanonicalSha256",
    "compatibilityBaselinePath",
    "compatibilityBaselineSha256",
    "rulePackSha256",
    "rulePackPayloadSha256",
    "fixturePath",
    "fixtureSha256",
    "fixtureManifestPath",
    "fixtureManifestSha256",
    "rulesManifestPath",
    "rulesManifestId",
    "rulesManifestSha256",
}


def test_builder_entrypoint_exists():
    assert BUILDER.is_file()


def test_builder_writes_exact_validated_fields_and_is_deterministic(tmp_path):
    from tools.build_hbr_artifact_manifest import build_artifact_manifest
    from tools.build_hbr_rules_manifest import canonical_json_bytes

    gha = _write_dummy_gha(tmp_path)
    first = tmp_path / "first.json"
    second = tmp_path / "second.json"

    first_document = build_artifact_manifest(
        ROOT, gha, RULES_MANIFEST, COMMIT_SHA, first
    )
    second_document = build_artifact_manifest(
        ROOT, gha, RULES_MANIFEST, COMMIT_SHA, second
    )
    rules = json.loads(RULES_MANIFEST.read_text(encoding="utf-8"))
    gha_bytes = gha.read_bytes()

    assert first_document == second_document
    assert first.read_bytes() == second.read_bytes() == canonical_json_bytes(first_document)
    assert b"\r" not in first.read_bytes()
    assert set(first_document) == EXPECTED_FIELDS
    assert first_document == json.loads(first.read_text(encoding="utf-8"))
    assert first_document["artifactName"] == "BIMBaoGui.Stage01.gha"
    assert first_document["assemblyVersion"] == "0.9.0.0"
    assert first_document["sha256"] == _sha256(gha_bytes)
    assert first_document["sizeBytes"] == len(gha_bytes)
    assert first_document["commitSha"] == COMMIT_SHA
    assert first_document["rulePackageId"] == rules["packageId"]
    assert first_document["rulePackageVersion"] == rules["packageVersion"]
    assert first_document["ruleSourcePath"] == rules["ruleSource"]["path"]
    assert first_document["ruleSourceSha256"] == rules["ruleSource"]["sha256"]
    assert (
        first_document["ruleSourceCanonicalSha256"]
        == rules["ruleSource"]["canonicalSha256"]
    )
    assert (
        first_document["compatibilityBaselinePath"]
        == rules["compatibilityBaseline"]["path"]
    )
    assert (
        first_document["compatibilityBaselineSha256"]
        == rules["compatibilityBaseline"]["sha256"]
    )
    assert first_document["rulePackSha256"] == rules["rulePack"]["sha256"]
    assert (
        first_document["rulePackPayloadSha256"]
        == rules["rulePack"]["payloadSha256"]
    )
    assert first_document["fixturePath"] == rules["fixture"]["path"]
    assert first_document["fixtureSha256"] == rules["fixture"]["sha256"]
    assert (
        first_document["fixtureManifestPath"]
        == rules["fixture"]["manifestPath"]
    )
    assert (
        first_document["fixtureManifestSha256"]
        == rules["fixture"]["manifestSha256"]
    )
    assert first_document["rulesManifestPath"] == (
        "specs/hbr-rules/v1/manifest.sha256.json"
    )
    assert first_document["rulesManifestId"] == rules["manifestId"]
    assert first_document["rulesManifestSha256"] == _sha256(
        RULES_MANIFEST.read_bytes()
    )
    assert all(
        isinstance(first_document[key], str) and first_document[key]
        for key in EXPECTED_FIELDS - {"sizeBytes"}
    )
    for key in (field for field in EXPECTED_FIELDS if field.endswith("Sha256") or field == "sha256"):
        assert re.fullmatch(r"[0-9a-f]{64}", first_document[key])
    assert not any("archive" in key.casefold() or "zip" in key.casefold() for key in first_document)


@pytest.mark.parametrize("mutation", ["missing", "null", "hash"])
def test_rules_manifest_schema_or_hash_drift_fails_closed(tmp_path, mutation):
    from tools.build_hbr_artifact_manifest import build_artifact_manifest
    from tools.build_hbr_rules_manifest import canonical_json_bytes

    document = json.loads(RULES_MANIFEST.read_text(encoding="utf-8"))
    if mutation == "missing":
        del document["ruleSource"]["canonicalSha256"]
    elif mutation == "null":
        document["compatibilityBaseline"]["sha256"] = None
    else:
        document["fixture"]["manifestSha256"] = "0" * 64
    tampered = tmp_path / f"{mutation}.manifest.json"
    tampered.write_bytes(canonical_json_bytes(document))
    output = tmp_path / f"{mutation}.artifact.json"

    with pytest.raises(ValueError, match="rules manifest"):
        build_artifact_manifest(
            ROOT, _write_dummy_gha(tmp_path), tampered, COMMIT_SHA, output
        )

    _assert_no_output_or_temp(output)


@pytest.mark.parametrize(
    "commit_sha",
    ("", "a" * 39, "A" * 40, "g" * 40),
)
def test_invalid_commit_sha_fails_without_output(tmp_path, commit_sha):
    from tools.build_hbr_artifact_manifest import build_artifact_manifest

    output = tmp_path / "invalid-commit.json"
    with pytest.raises(ValueError, match="commit SHA"):
        build_artifact_manifest(
            ROOT, _write_dummy_gha(tmp_path), RULES_MANIFEST, commit_sha, output
        )
    _assert_no_output_or_temp(output)


@pytest.mark.parametrize("mode", ("missing", "empty"))
def test_missing_or_empty_gha_fails_without_output(tmp_path, mode):
    from tools.build_hbr_artifact_manifest import build_artifact_manifest

    gha = tmp_path / "BIMBaoGui.Stage01.gha"
    if mode == "empty":
        gha.write_bytes(b"")
    output = tmp_path / f"{mode}.json"
    with pytest.raises((FileNotFoundError, ValueError), match="GHA"):
        build_artifact_manifest(ROOT, gha, RULES_MANIFEST, COMMIT_SHA, output)
    _assert_no_output_or_temp(output)


@pytest.mark.parametrize("conflict", ("gha", "rules-manifest"))
def test_output_conflict_fails_without_overwriting_inputs(tmp_path, conflict):
    from tools.build_hbr_artifact_manifest import build_artifact_manifest

    gha = _write_dummy_gha(tmp_path)
    rules_manifest = tmp_path / "rules manifest.json"
    rules_manifest.write_bytes(RULES_MANIFEST.read_bytes())
    output = gha if conflict == "gha" else rules_manifest
    before = output.read_bytes()

    with pytest.raises(ValueError, match="output"):
        build_artifact_manifest(ROOT, gha, rules_manifest, COMMIT_SHA, output)

    assert output.read_bytes() == before
    assert not list(output.parent.glob(f".{output.name}.*.tmp"))


def test_cli_supports_paths_with_spaces(tmp_path):
    spaced = tmp_path / "folder with spaces"
    spaced.mkdir()
    gha = _write_dummy_gha(spaced)
    rules_manifest = spaced / "rules manifest.json"
    rules_manifest.write_bytes(RULES_MANIFEST.read_bytes())
    output = spaced / "artifact manifest.json"

    subprocess.run(
        [
            sys.executable,
            str(BUILDER),
            "--root",
            str(ROOT),
            "--gha",
            str(gha),
            "--rules-manifest",
            str(rules_manifest),
            "--commit-sha",
            COMMIT_SHA,
            "--output",
            str(output),
        ],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    assert json.loads(output.read_text(encoding="utf-8"))["sha256"] == _sha256(
        gha.read_bytes()
    )


def _write_dummy_gha(directory):
    gha = Path(directory) / "BIMBaoGui.Stage01.gha"
    gha.write_bytes(b"deterministic-dummy-gha\n")
    return gha


def _sha256(content):
    return hashlib.sha256(content).hexdigest()


def _assert_no_output_or_temp(output):
    assert not output.exists()
    assert not list(output.parent.glob(f".{output.name}.*.tmp"))
