#!/usr/bin/env python3
"""Build a fail-closed validation artifact manifest for the release GHA."""

import argparse
import hashlib
import re
import sys
from collections.abc import Mapping
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from tools.build_hbr_rules_manifest import (  # noqa: E402
    BASELINE_PATH,
    FIXTURE_MANIFEST_PATH,
    FIXTURE_PATH,
    FROZEN_PATHS,
    SOURCE_PATH,
    atomic_replace_bytes,
    build_rules_manifest_document,
    canonical_json_bytes,
)


ARTIFACT_NAME = "BIMBaoGui.Stage01.gha"
ASSEMBLY_VERSION = "0.9.0.0"
RULES_MANIFEST_PATH = "specs/hbr-rules/v1/manifest.sha256.json"
RULES_MANIFEST_ID = "HBR-WUHAN-PLANNING-1.0.0-BASELINE"
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
COMMIT_SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")


def _same_file(first, second):
    first = Path(first).resolve(strict=False)
    second = Path(second).resolve(strict=False)
    if first == second:
        return True
    if first.exists() and second.exists():
        try:
            return first.samefile(second)
        except OSError:
            return False
    return False


def _mapping(document, key):
    value = document.get(key)
    if not isinstance(value, Mapping):
        raise ValueError(f"rules manifest {key} must be an object")
    return value


def _string(document, key, label="rules manifest"):
    value = document.get(key)
    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} {key} must be a non-empty string")
    return value


def _sha256(document, key):
    value = _string(document, key)
    if SHA256_PATTERN.fullmatch(value) is None:
        raise ValueError(f"rules manifest {key} must be a lowercase SHA-256")
    return value


def _fixed_path(document, key, expected):
    value = _string(document, key)
    if value != expected:
        raise ValueError(f"rules manifest {key} must be {expected}")
    return value


def _validated_rules_identity(document):
    if not isinstance(document, Mapping):
        raise ValueError("rules manifest document must be an object")
    if _string(document, "schemaVersion") != "1.0.0":
        raise ValueError("rules manifest schemaVersion must be 1.0.0")
    manifest_id = _string(document, "manifestId")
    if manifest_id != RULES_MANIFEST_ID:
        raise ValueError(f"rules manifest manifestId must be {RULES_MANIFEST_ID}")
    package_id = _string(document, "packageId")
    package_version = _string(document, "packageVersion")

    rule_source = _mapping(document, "ruleSource")
    rule_source_path = _fixed_path(rule_source, "path", SOURCE_PATH)
    rule_source_sha256 = _sha256(rule_source, "sha256")
    rule_source_canonical_sha256 = _sha256(rule_source, "canonicalSha256")

    baseline = _mapping(document, "compatibilityBaseline")
    baseline_path = _fixed_path(baseline, "path", BASELINE_PATH)
    baseline_sha256 = _sha256(baseline, "sha256")

    rule_pack = _mapping(document, "rulePack")
    rule_pack_sha256 = _sha256(rule_pack, "sha256")
    rule_pack_payload_sha256 = _sha256(rule_pack, "payloadSha256")
    if not isinstance(rule_pack.get("bytes"), int) or rule_pack["bytes"] <= 0:
        raise ValueError("rules manifest rulePack bytes must be a positive integer")

    fixture = _mapping(document, "fixture")
    fixture_path = _fixed_path(fixture, "path", FIXTURE_PATH)
    fixture_sha256 = _sha256(fixture, "sha256")
    fixture_manifest_path = _fixed_path(
        fixture, "manifestPath", FIXTURE_MANIFEST_PATH
    )
    fixture_manifest_sha256 = _sha256(fixture, "manifestSha256")

    return {
        "rulePackageId": package_id,
        "rulePackageVersion": package_version,
        "ruleSourcePath": rule_source_path,
        "ruleSourceSha256": rule_source_sha256,
        "ruleSourceCanonicalSha256": rule_source_canonical_sha256,
        "compatibilityBaselinePath": baseline_path,
        "compatibilityBaselineSha256": baseline_sha256,
        "rulePackSha256": rule_pack_sha256,
        "rulePackPayloadSha256": rule_pack_payload_sha256,
        "fixturePath": fixture_path,
        "fixtureSha256": fixture_sha256,
        "fixtureManifestPath": fixture_manifest_path,
        "fixtureManifestSha256": fixture_manifest_sha256,
        "rulesManifestId": manifest_id,
    }


def build_artifact_manifest(root, gha, rules_manifest, commit_sha, output):
    root = Path(root).resolve(strict=True)
    gha = Path(gha).resolve(strict=False)
    rules_manifest = Path(rules_manifest).resolve(strict=False)
    output = Path(output).resolve(strict=False)

    if COMMIT_SHA_PATTERN.fullmatch(str(commit_sha)) is None:
        raise ValueError("commit SHA must be exactly 40 lowercase hexadecimal characters")
    if not gha.is_file():
        raise FileNotFoundError(f"GHA file is missing: {gha}")
    if gha.name != ARTIFACT_NAME:
        raise ValueError(f"GHA file name must be {ARTIFACT_NAME}")
    gha_bytes = gha.read_bytes()
    if not gha_bytes:
        raise ValueError("GHA file must not be empty")
    if not rules_manifest.is_file():
        raise FileNotFoundError(f"rules manifest is missing: {rules_manifest}")

    protected_inputs = [gha, rules_manifest]
    protected_inputs.extend(root / path for path in FROZEN_PATHS)
    if any(_same_file(output, path) for path in protected_inputs):
        raise ValueError("output must not conflict with any input")

    source = root / SOURCE_PATH
    fixture = root / FIXTURE_PATH
    fixture_manifest = root / FIXTURE_MANIFEST_PATH
    expected_rules = build_rules_manifest_document(
        root,
        source,
        fixture.read_bytes(),
        fixture_manifest.read_bytes(),
    )
    expected_rules_bytes = canonical_json_bytes(expected_rules)
    actual_rules_bytes = rules_manifest.read_bytes()
    if actual_rules_bytes != expected_rules_bytes:
        raise ValueError(
            "rules manifest bytes do not match the canonical rebuilt document"
        )
    identity = _validated_rules_identity(expected_rules)

    document = {
        "artifactName": ARTIFACT_NAME,
        "assemblyVersion": ASSEMBLY_VERSION,
        "sha256": hashlib.sha256(gha_bytes).hexdigest(),
        "sizeBytes": len(gha_bytes),
        "commitSha": commit_sha,
        **identity,
        "rulesManifestPath": RULES_MANIFEST_PATH,
        "rulesManifestSha256": hashlib.sha256(actual_rules_bytes).hexdigest(),
    }
    atomic_replace_bytes(output, canonical_json_bytes(document))
    return document


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--gha", type=Path, required=True)
    parser.add_argument("--rules-manifest", type=Path, required=True)
    parser.add_argument("--commit-sha", required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args(argv)
    try:
        build_artifact_manifest(
            arguments.root,
            arguments.gha,
            arguments.rules_manifest,
            arguments.commit_sha,
            arguments.output,
        )
    except Exception as error:
        print(f"Artifact manifest build failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
