from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def load_json(path: str):
    return json.loads(read(path))


def test_official_rules_without_override_use_exact_ifc_property_name():
    rules = load_json("specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json")
    assert rules["officialSummary"]["sourceParameterOverrideCount"] == 0
    assert len(rules["properties"]) == 166
    for item in rules["properties"]:
        official = item["official"]
        assert official.get("sourceParameterOverride") is None
        assert official["ifcProperty"].strip()


def test_runtime_derives_deterministic_exact_name_aliases():
    catalog = read("src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs")
    policy = read("src/BIMBaoGui.Stage01/Hifc/OfficialSourceAliasPolicy.cs")
    mapping = read("src/BIMBaoGui.Stage01/Hifc/OfficialHifcMapping.cs")
    guid = read("src/BIMBaoGui.Stage01/Hifc/DeterministicGuidV5.cs")
    assert "SourceParameterOverride" in mapping
    assert "OfficialSourceParameterName" in mapping
    assert "OfficialSourceParameterGuid" in mapping
    assert "sourceParameterOverride" in catalog
    assert "? ifcProperty" in catalog or "ifcProperty" in catalog
    assert "OfficialSourceAliasPolicy.CreateGuid" in catalog
    assert "bindingScope" in policy
    assert "revitCategory" in policy
    assert "carrier" in policy
    assert "SHA1.Create" in guid


def test_revit_projection_dual_writes_canonical_and_official_source_parameters():
    projection = read(
        "src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs"
    )
    stage01 = read(
        "src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs"
    )
    generic = read(
        "src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteService.cs"
    )
    assert "CANONICAL_INTERNAL" in projection
    assert "OFFICIAL_EXACT_SOURCE_NAME" in projection
    assert "OfficialSourceParameterGuid" in projection
    assert "OfficialSourceParameterName" in projection
    assert "OFFICIAL_SOURCE_VALUE_CONFLICT" in projection
    assert "OFFICIAL_SOURCE_NAME_AMBIGUOUS" not in projection
    assert "FoldOfficialSourceAliases" in projection
    assert "OfficialParameterProjectionService.WriteAndVerify" in stage01
    assert "OfficialParameterProjectionService.WriteAndVerify" in generic


def test_generated_shared_parameter_groups_precede_parameter_block():
    projection = read(
        "src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs"
    )
    assert "FindParameterHeaderIndex" in projection
    assert "StartsWith(" in projection
    assert '"*PARAM\\t"' in projection
    assert "AppendAliasGroupDefinitions" in projection
    assert "AppendAliasParameterDefinitions" in projection
    assert projection.index("AppendAliasGroupDefinitions") < projection.index(
        "AppendAliasParameterDefinitions"
    )


def test_old_stage01_initialization_is_migrated_without_manual_reinitialize():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "RequiresWorkflowMigration" in service
    assert "旧版初始化待升级" in service
    assert "无需启用“允许重新初始化”" in service


def test_v082_plugin_patch_keeps_v080_context_schema():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    versions = read("src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs")
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert "<Version>0.8.2</Version>" in project
    assert 'public override string Version => "0.8.2"' in assembly
    assert 'FileContextSchema = "0.8.0"' in versions
    assert '0.8.2.0' in workflow
