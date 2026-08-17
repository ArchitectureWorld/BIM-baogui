from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ROOTS = (
    "src/BIMBaoGui.HifcCore",
    "src/BIMBaoGui.RevitAddin",
    "src/BIMBaoGui.McpContracts",
    "src/BIMBaoGui.McpServer",
)
CAPABILITIES_BY_VERSION = {
    "0.4.3": (
        "Stage01 freezes total-plan registration, coordinates, and planning objectives.",
        "Stage02A freezes whole-model or user-selected processing, explicit semantic confirmation, reliable element area, and per-rule geometry evidence.",
        "Stage02B freezes six manually entered actual indicators with per-indicator partial success and no project-level automatic aggregation.",
        "Stage03 freezes four-state strict checklist, issue traceability, and reasoned forced test export.",
        "The MCP surface contains exactly 13 approved business tools and exposes no arbitrary Revit API execution.",
        "Official carrier projection is fail-closed by propertyId.",
    ),
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tracked_sources() -> list[str]:
    process = subprocess.run(
        ["git", "ls-files", "--", *ROOTS],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    paths = []
    for raw in process.stdout.splitlines():
        path = raw.replace("\\", "/")
        parts = Path(path).parts
        if not path.endswith(".cs") or "bin" in parts or "obj" in parts:
            continue
        if not (ROOT / path).is_file():
            raise FileNotFoundError(f"tracked production source is missing: {path}")
        paths.append(path)
    return sorted(set(paths))


def build_manifest(version: str, branch: str) -> dict[str, object]:
    if re.fullmatch(r"\d+\.\d+\.\d+", version) is None:
        raise ValueError("version must use major.minor.patch format")
    if not branch.strip():
        raise ValueError("branch must not be empty")
    if version not in CAPABILITIES_BY_VERSION:
        raise ValueError(f"unsupported functional baseline version: {version}")

    hashes = {
        path: sha256((ROOT / path).read_bytes())
        for path in tracked_sources()
    }
    snapshot = "".join(
        f"{path}\0{digest}\n" for path, digest in hashes.items()
    ).encode("utf-8")
    return {
        "capabilities": list(CAPABILITIES_BY_VERSION[version]),
        "delivery": {
            "external_acceptance": (
                "Golden RVT -> official HIFCTool -> IFCFlux exact identity"
            ),
            "installer_artifact": (
                f"BIMBaoGui-Revit2020-Native-MCP-v{version}"
            ),
            "single_revit_branch": branch,
            "target": "Autodesk Revit 2020",
        },
        "payload_schema_version": "0.9.1",
        "product_line": "BIMBaoGui Revit 2020 Native + MCP",
        "product_version": version,
        "roots": list(ROOTS),
        "schema_version": "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V4",
        "sha256_by_path": hashes,
        "source_branch": branch,
        "source_snapshot_sha256": sha256(snapshot),
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build deterministic BIMBaoGui Revit functional baseline"
    )
    parser.add_argument("--version", required=True)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    manifest = build_manifest(args.version, args.branch)
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(
            manifest,
            ensure_ascii=False,
            indent=2,
            sort_keys=True,
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
