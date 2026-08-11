import hashlib
import os
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
TEST_PROJECT = (
    ROOT
    / "tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj"
)

LEGACY_EMBEDDED_RESOURCES = {
    r"Resources\stage01_file_initialization_registry_v0.1.json",
    r"..\..\specs\hifc-mapping\v1\generated\GH_HIFC_ParameterBindings.json",
    r"..\..\specs\hifc-mapping\v1\generated\GH_HIFC_SharedParameters.txt",
    r"..\..\specs\hifc-mapping\v1\data\wuhan_planning_rules.v1.json",
    r"..\..\specs\hifc-mapping\v1\data\official_plugin_compatibility_status.v1.json",
}

LEGACY_EVIDENCE_FILES = (
    ROOT
    / "src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json",
    ROOT / "specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json",
    ROOT / "specs/hifc-mapping/v1/generated/GH_HIFC_SharedParameters.txt",
    ROOT / "specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json",
    ROOT
    / "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json",
)

LEGACY_RUNTIME_INPUT_NAMES = frozenset(
    path.name for path in LEGACY_EVIDENCE_FILES
)

# Production diagnostics may name a retired input only after an explicit review.
# No such exception exists today.
LEGACY_FILENAME_DIAGNOSTIC_ALLOWLIST = frozenset()


def _normalize_msbuild_path(value: str) -> str:
    return value.replace("/", "\\").casefold()


def _single_text(root, tag: str) -> str:
    matches = list(root.iter(tag))
    assert len(matches) == 1
    return (matches[0].text or "").strip()


def _compile_pack(project: Path, intermediate: Path) -> Path:
    intermediate.mkdir(parents=True)
    result = subprocess.run(
        [
            "dotnet",
            "msbuild",
            str(project),
            "-t:CompileHbrRulePack",
            "-p:Configuration=Release",
            f"-p:IntermediateOutputPath={intermediate}{os.sep}",
            f"-p:HbrPythonExe={sys.executable}",
            "-nologo",
            "-v:minimal",
        ],
        cwd=ROOT,
        env={**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en-US"},
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=120,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    pack = intermediate / "HBR_RulePack.hbrpack"
    assert pack.is_file()
    return pack


def _pack_identity(path: Path):
    pack = path.read_bytes()
    assert pack[:4] == b"HBRP"
    assert int.from_bytes(pack[4:8], "big") == 1
    payload_length = int.from_bytes(pack[8:16], "big")
    payload = pack[48:]
    assert payload_length == len(payload)
    payload_sha = hashlib.sha256(payload).digest()
    assert pack[16:48] == payload_sha
    return hashlib.sha256(pack).hexdigest(), payload_sha.hex()


def test_csproj_removes_five_legacy_embedded_resources():
    root = ElementTree.parse(PROJECT).getroot()
    embedded = {
        _normalize_msbuild_path(item.attrib["Include"])
        for item in root.iter("EmbeddedResource")
        if "Include" in item.attrib
    }
    forbidden = {
        _normalize_msbuild_path(path) for path in LEGACY_EMBEDDED_RESOURCES
    }

    assert embedded.isdisjoint(forbidden), sorted(embedded & forbidden)
    assert all(path.is_file() for path in LEGACY_EVIDENCE_FILES)


def test_production_csharp_never_reads_retired_rule_inputs():
    source_root = ROOT / "src/BIMBaoGui.Stage01"
    occurrences = {
        path.relative_to(ROOT).as_posix(): sorted(
            name for name in LEGACY_RUNTIME_INPUT_NAMES if name in text
        )
        for path in source_root.rglob("*.cs")
        if (text := path.read_text(encoding="utf-8-sig"))
        if any(name in text for name in LEGACY_RUNTIME_INPUT_NAMES)
    }

    assert set(occurrences).issubset(LEGACY_FILENAME_DIAGNOSTIC_ALLOWLIST), (
        "production C# names retired rule inputs outside the explicit "
        f"diagnostic allowlist: {occurrences}"
    )


def test_pack_projects_share_inputs_but_use_project_intermediate_outputs():
    production = ElementTree.parse(PROJECT).getroot()
    test = ElementTree.parse(TEST_PROJECT).getroot()

    for property_name in (
        "HbrRuleSource",
        "HbrRulePackCompiler",
        "HbrCompatibilityBaseline",
    ):
        assert _single_text(production, property_name) == _single_text(
            test, property_name
        )

    expected_pack_expression = (
        "$([MSBuild]::NormalizePath('$(MSBuildProjectDirectory)', "
        "'$(IntermediateOutputPath)', 'HBR_RulePack.hbrpack'))"
    )
    assert _single_text(production, "HbrRulePack") == expected_pack_expression
    assert _single_text(test, "HbrRulePack") == expected_pack_expression

    for project_root in (production, test):
        pack_resources = [
            item
            for item in project_root.iter("EmbeddedResource")
            if item.attrib.get("Include") == "$(HbrRulePack)"
        ]
        assert len(pack_resources) == 1
        assert pack_resources[0].attrib.get("LogicalName") == (
            "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack"
        )


def test_isolated_projects_generate_distinct_identical_packs(tmp_path):
    production_intermediate = tmp_path / "production obj"
    test_intermediate = tmp_path / "test obj"

    production_pack = _compile_pack(PROJECT, production_intermediate)
    test_pack = _compile_pack(TEST_PROJECT, test_intermediate)

    assert production_pack.resolve() != test_pack.resolve()
    assert production_pack.parent == production_intermediate
    assert test_pack.parent == test_intermediate
    assert production_pack.read_bytes() == test_pack.read_bytes()
    assert _pack_identity(production_pack) == _pack_identity(test_pack)
