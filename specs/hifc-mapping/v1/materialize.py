#!/usr/bin/env python3
"""Materialize the GH H-IFC baseline from checked-in base64 archive chunks."""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import shutil
import tarfile
import tempfile
import warnings
from pathlib import Path

ARCHIVE_SHA256 = "341db436faa8410fb19695a727810dba262e3ab73c11b0c54566d502f3c759b9"
SOURCE_DIR_NAME = "GH_HIFC_开发基线_v1"
SECTIONS = ("docs", "data", "generated", "schemas")


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def safe_extract(archive: tarfile.TarFile, destination: Path) -> None:
    root = destination.resolve()
    for member in archive.getmembers():
        target = (destination / member.name).resolve()
        if target != root and root not in target.parents:
            raise RuntimeError(f"Unsafe archive path: {member.name}")

    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        archive.extractall(destination)


def load_manifest(root: Path) -> dict:
    path = root / "source-manifest.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("archiveSha256") != ARCHIVE_SHA256:
        raise RuntimeError("source-manifest.json archive SHA-256 does not match the script")
    return data


def reconstruct_archive(root: Path) -> bytes:
    chunks = sorted((root / "archive").glob("chunk-*.b64"))
    if not chunks:
        raise FileNotFoundError("No archive chunks were found")

    encoded = b"".join(path.read_bytes().strip() for path in chunks)
    try:
        payload = base64.b64decode(encoded, validate=True)
    except Exception as exc:
        raise RuntimeError("Archive chunks are not valid base64") from exc

    digest = sha256_bytes(payload)
    if digest != ARCHIVE_SHA256:
        raise RuntimeError(
            f"Archive SHA-256 mismatch: expected {ARCHIVE_SHA256}, got {digest}"
        )
    return payload


def verify_extracted(source: Path, manifest: dict) -> None:
    expected = {item["path"]: item for item in manifest["files"]}
    actual_paths = {
        str(path.relative_to(source)).replace("\\", "/")
        for path in source.rglob("*")
        if path.is_file()
    }
    if actual_paths != set(expected):
        missing = sorted(set(expected) - actual_paths)
        extra = sorted(actual_paths - set(expected))
        raise RuntimeError(f"Extracted file set mismatch; missing={missing}, extra={extra}")

    for relative, item in expected.items():
        path = source / relative
        content = path.read_bytes()
        if len(content) != item["size"]:
            raise RuntimeError(f"Size mismatch: {relative}")
        digest = sha256_bytes(content)
        if digest != item["sha256"]:
            raise RuntimeError(f"SHA-256 mismatch: {relative}")


def write_materialized_manifest(root: Path, manifest: dict) -> None:
    output = {
        "schemaVersion": "1.0.0",
        "sourceArchiveSha256": ARCHIVE_SHA256,
        "fileCount": len(manifest["files"]),
        "files": manifest["files"],
    }
    (root / "manifest.sha256.json").write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def materialize(root: Path, clean: bool) -> None:
    manifest = load_manifest(root)
    payload = reconstruct_archive(root)

    with tempfile.TemporaryDirectory(prefix="gh-hifc-") as temp_name:
        temp = Path(temp_name)
        with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as archive:
            safe_extract(archive, temp)

        source = temp / SOURCE_DIR_NAME
        if not source.is_dir():
            raise RuntimeError(f"Archive is missing {SOURCE_DIR_NAME}")
        verify_extracted(source, manifest)

        for section in SECTIONS:
            target = root / section
            if target.exists():
                if not clean:
                    raise FileExistsError(
                        f"{target} already exists; rerun with --clean to replace it"
                    )
                shutil.rmtree(target)
            shutil.copytree(source / section, target)

    write_materialized_manifest(root, manifest)
    print(
        f"Materialized {len(manifest['files'])} verified files under {root} "
        f"from archive {ARCHIVE_SHA256}."
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--clean",
        action="store_true",
        help="replace existing docs/data/generated/schemas directories",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    materialize(Path(__file__).resolve().parent, clean=args.clean)


if __name__ == "__main__":
    main()
