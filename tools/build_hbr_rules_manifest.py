#!/usr/bin/env python3
"""Build the deterministic, auditable HBR mapping-baseline manifest."""

import argparse
import hashlib
import json
import os
import sys
import tempfile
from collections import Counter
from pathlib import Path, PurePosixPath


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from tools.build_hbr_rulepack import (  # noqa: E402
    build_rulepack_bytes,
    canonical_bytes,
    effective_ifc_identity,
    effective_runtime_status,
    load_validated_rule_source,
)
from tools.hifc.validate_hifc_mapping_smoke import validate  # noqa: E402


FROZEN_PATHS = (
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

SOURCE_PATH = "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
BASELINE_PATH = "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
FIXTURE_PATH = "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc"
FIXTURE_MANIFEST_PATH = "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json"
ACCEPTANCE_PATH = "docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json"
GENERATOR_PATH = "tools/build_hbr_rules_manifest.py"
RULEPACK_LOGICAL_PATH = "src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack"


def canonical_json_bytes(value):
    return (
        json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
        + "\n"
    ).encode("utf-8")


def atomic_replace_bytes(path, content):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = None
    temporary_path = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            dir=path.parent,
            prefix=f".{path.name}.",
            suffix=".tmp",
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(descriptor, "wb") as stream:
            descriptor = None
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if descriptor is not None:
            os.close(descriptor)
        if temporary_path is not None:
            try:
                temporary_path.unlink()
            except FileNotFoundError:
                pass


def _sha256(content):
    return hashlib.sha256(content).hexdigest()


def _record(path, content):
    return {"path": path, "bytes": len(content), "sha256": _sha256(content)}


def _resolved_root(root):
    root = Path(root).resolve(strict=True)
    if not root.is_dir() or not (root / ".git").exists():
        raise ValueError("root must identify a Git worktree")
    return root


def _validate_frozen_paths(root):
    if not FROZEN_PATHS or tuple(sorted(FROZEN_PATHS)) != FROZEN_PATHS:
        raise ValueError("frozen paths must use UTF-8 ordinal sort order")
    if len(FROZEN_PATHS) != len(set(FROZEN_PATHS)):
        raise ValueError("frozen paths must be unique")

    resolved = []
    for logical_path in FROZEN_PATHS:
        pure = PurePosixPath(logical_path)
        if (
            not logical_path
            or "\\" in logical_path
            or pure.is_absolute()
            or pure.parts[0].endswith(":")
            or ".." in pure.parts
        ):
            raise ValueError(f"invalid frozen path: {logical_path}")
        actual = (root / Path(*pure.parts)).resolve(strict=False)
        try:
            actual.relative_to(root)
        except ValueError as error:
            raise ValueError(f"frozen path escapes root: {logical_path}") from error
        if not actual.is_file():
            raise FileNotFoundError(f"frozen file is missing: {logical_path}")
        resolved.append((logical_path, actual))
    return resolved


def _same_file(first, second):
    first = Path(first).resolve(strict=False)
    second = Path(second).resolve(strict=False)
    if first == second:
        return True
    if first.exists() and second.exists():
        try:
            return os.path.samefile(first, second)
        except OSError:
            return False
    return False


def build_rules_manifest_document(root, source, fixture_bytes, fixture_manifest_bytes):
    root = _resolved_root(root)
    frozen = _validate_frozen_paths(root)
    paths = dict(frozen)
    source_path = Path(source).resolve(strict=True)
    expected_source = paths[SOURCE_PATH]
    if not _same_file(source_path, expected_source):
        raise ValueError("source must be the frozen authoritative rule source")

    actual_fixture_bytes = paths[FIXTURE_PATH].read_bytes()
    actual_fixture_manifest_bytes = paths[FIXTURE_MANIFEST_PATH].read_bytes()
    if fixture_bytes != actual_fixture_bytes:
        raise ValueError("fixture bytes do not match the frozen fixture")
    if fixture_manifest_bytes != actual_fixture_manifest_bytes:
        raise ValueError("fixture manifest bytes do not match the frozen manifest")

    validated_source = load_validated_rule_source(source_path, paths[BASELINE_PATH])
    validation = validate(
        source_path,
        paths[BASELINE_PATH],
        paths[FIXTURE_PATH],
        paths[FIXTURE_MANIFEST_PATH],
    )
    if validation.get("status") != "PASS":
        raise ValueError("committed fixture validation did not pass")

    rulepack = build_rulepack_bytes(validated_source)
    payload = canonical_bytes(validated_source)
    status_counts = Counter(
        effective_runtime_status(validated_source, rule)
        for rule in validated_source["properties"]
    )
    expected_status_counts = {
        "NOT_IMPLEMENTED": 57,
        "UNCLASSIFIED_REQUIREMENT": 302,
    }
    if dict(status_counts) != expected_status_counts:
        raise ValueError(f"unexpected runtime status counts: {dict(status_counts)}")

    official = [
        rule
        for rule in validated_source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    ]
    matches = sum(
        "|".join(effective_ifc_identity(rule))
        == rule["officialPlugin"]["originalIdentity"]
        for rule in official
    )
    if len(official) != 166 or matches != 166:
        raise ValueError(f"official identity mismatch: {matches}/{len(official)}")

    all_bytes = {logical: actual.read_bytes() for logical, actual in frozen}
    document = {
        "schemaVersion": "1.0.0",
        "manifestId": "HBR-WUHAN-PLANNING-1.0.0-BASELINE",
        "packageId": validated_source["packageId"],
        "packageVersion": validated_source["packageVersion"],
        "generator": {
            "path": GENERATOR_PATH,
            "version": "1.0.0",
            "sha256": _sha256(all_bytes[GENERATOR_PATH]),
        },
        "ruleSource": {
            **_record(SOURCE_PATH, all_bytes[SOURCE_PATH]),
            "canonicalSha256": _sha256(payload),
        },
        "compatibilityBaseline": _record(BASELINE_PATH, all_bytes[BASELINE_PATH]),
        "fixture": {
            **_record(FIXTURE_PATH, actual_fixture_bytes),
            "manifestPath": FIXTURE_MANIFEST_PATH,
            "manifestSha256": _sha256(actual_fixture_manifest_bytes),
            "acceptancePath": ACCEPTANCE_PATH,
            "acceptanceSha256": _sha256(all_bytes[ACCEPTANCE_PATH]),
        },
        "rulePack": {
            "logicalPath": RULEPACK_LOGICAL_PATH,
            "bytes": len(rulepack),
            "sha256": _sha256(rulepack),
            "payloadSha256": _sha256(payload),
        },
        "runtimeStatusCounts": expected_status_counts,
        "officialIdentityMatches": matches,
        "files": [_record(path, all_bytes[path]) for path in FROZEN_PATHS],
    }
    return document


def build_rules_manifest(root, output):
    root = _resolved_root(root)
    frozen = _validate_frozen_paths(root)
    output = Path(output).resolve(strict=False)
    if any(_same_file(output, actual) for _, actual in frozen):
        raise ValueError("output must not conflict with a frozen input")

    source = root / SOURCE_PATH
    fixture = root / FIXTURE_PATH
    fixture_manifest = root / FIXTURE_MANIFEST_PATH
    document = build_rules_manifest_document(
        root,
        source,
        fixture.read_bytes(),
        fixture_manifest.read_bytes(),
    )
    atomic_replace_bytes(output, canonical_json_bytes(document))
    return document


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args(argv)
    build_rules_manifest(arguments.root, arguments.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
