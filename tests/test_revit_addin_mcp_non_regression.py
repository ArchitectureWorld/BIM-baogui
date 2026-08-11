import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "specs" / "revit-addin" / "v0.2.0-functional-baseline.sha256.json"


def git_bytes(commit: str, path: str) -> bytes:
    return subprocess.check_output(
        ["git", "show", f"{commit}:{path}"],
        cwd=ROOT,
    )


def git_paths(commit: str, roots: list[str]) -> list[str]:
    output = subprocess.check_output(
        ["git", "ls-tree", "-r", "--name-only", commit, "--", *roots],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
    )
    return sorted(
        line.strip()
        for line in output.splitlines()
        if line.strip().endswith(".cs")
    )


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def test_stage01_stage02_business_files_match_v020_baseline():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    assert manifest["schema_version"] == "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V1"
    source_commit = manifest["source_commit"]
    roots = manifest["roots"]
    expected_paths = git_paths(source_commit, roots)
    current_paths = sorted(
        path.relative_to(ROOT).as_posix()
        for root in roots
        for path in (ROOT / root).rglob("*.cs")
    )
    assert current_paths == expected_paths

    drift = []
    for path in expected_paths:
        expected_hash = sha256(git_bytes(source_commit, path))
        actual_hash = sha256((ROOT / path).read_bytes())
        if expected_hash != actual_hash:
            drift.append(
                {
                    "path": path,
                    "expected_sha256": expected_hash,
                    "actual_sha256": actual_hash,
                }
            )
    assert drift == []
