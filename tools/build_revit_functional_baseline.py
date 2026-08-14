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
CAPABILITIES = (
    "Stage01 project conditions remain explicit user business declarations; none is never auto-confirmed.",
    "Stage01 payload 0.9.0 is validated before an in-memory 0.9.1 migration candidate is created.",
    "Revit-native fields use per-field authority and drift evidence instead of one global overwrite priority.",
    "Stage02 Preview V2 hashes frozen effective roles, sorted overrides, and semantic Assignment evidence.",
    "Stage02 semantic Assignment create, update, delete, and readback verification commit atomically per element.",
    "The manual workbench and controlled MCP Stage02 entry use the same automatic, bulk, and per-element override semantics.",
    "Stage03 resolves SITE_GREEN_OBJECT owners by Revit export GUID and requires exact IFC entity plus GlobalId matching.",
    "Stage02 and Stage03 consume only Current Stage01 storage, never an unconfirmed migration candidate.",
    "IFCFlux external status remains IFCFLUX_MANUAL_PENDING until user inspection.",
    "The MCP surface contains 13 approved business tools and exposes no arbitrary Revit API execution.",
)


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

    hashes = {
        path: sha256((ROOT / path).read_bytes())
        for path in tracked_sources()
    }
    snapshot = "".join(
        f"{path}\0{digest}\n" for path, digest in hashes.items()
    ).encode("utf-8")
    return {
        "capabilities": list(CAPABILITIES),
        "delivery": {
            "external_acceptance": "IFCFlux manual inspection",
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
        "schema_version": "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V3",
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
