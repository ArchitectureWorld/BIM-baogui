from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
MAPPING_SNAPSHOT = (
    "tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/"
    "official-hifc-mappings.v1.json"
)
SHARED_PARAMETER_SNAPSHOT = (
    "tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/"
    "shared-parameters-canonical.v1.json"
)


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
    write_policy = read(
        "src/BIMBaoGui.Stage01/Hifc/OfficialSourceAliasWritePolicy.cs"
    )
    mapping = read("src/BIMBaoGui.Stage01/Hifc/OfficialHifcMapping.cs")
    guid = read("src/BIMBaoGui.Stage01/Hifc/DeterministicGuidV5.cs")
    snapshot = load_json(MAPPING_SNAPSHOT)
    assert len(snapshot["mappings"]) == 166
    assert len(
        {item["officialSourceParameterGuid"] for item in snapshot["mappings"]}
    ) == 166
    assert all(
        item["sourceParameterOverride"] == ""
        and item["officialSourceParameterName"] == item["ifcProperty"].strip()
        for item in snapshot["mappings"]
    )
    assert "SourceParameterOverride" in mapping
    assert "OfficialSourceParameterName" in mapping
    assert "OfficialSourceParameterGuid" in mapping
    assert "legacy.SourceParameterOverride" in catalog
    assert "identity.Property.Trim()" in catalog
    assert "OfficialSourceAliasPolicy.CreateGuid" in catalog
    assert "database.Package.LegacyAliases" in catalog
    assert "GetManifestResourceStream" not in catalog
    assert "wuhan_planning_rules.v1.json" not in catalog
    assert "bindingScope" in policy
    assert "revitCategory" in policy
    assert "carrier" in policy
    assert "OFFICIAL_SOURCE_VALUE_CONFLICT" in write_policy
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
    assert "OFFICIAL_SOURCE_NAME_AMBIGUOUS" not in projection
    assert "OfficialSourceAliasWritePolicy" in projection
    assert "FoldOfficialSourceAliases" in projection
    assert "OfficialParameterProjectionService.WriteAndVerify" in stage01
    assert "OfficialParameterProjectionService.WriteAndVerify" in generic


def test_generated_shared_parameter_groups_precede_parameter_block():
    service = read(
        "src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs"
    )
    helper = read(
        "src/BIMBaoGui.Stage01/Hifc/HbrSharedParameterTextProjection.cs"
    )
    canonical = load_json(SHARED_PARAMETER_SNAPSHOT)
    official = load_json(MAPPING_SNAPSHOT)

    assert len(canonical["groups"]) == 1
    assert len(canonical["parameters"]) == 141
    assert len({item["propertySet"] for item in official["mappings"]}) == 16
    assert len(official["mappings"]) == 166

    alias_group_loop = helper.index("foreach (AliasGroup group in aliasGroups)")
    parameter_header = helper.index('"*PARAM\\tGUID\\tNAME\\tDATATYPE')
    canonical_parameter_loop = helper.index(
        "foreach (OfficialHifcMapping mapping in mappings.Where"
    )
    alias_parameter_loop = helper.index(
        "foreach (AliasGroup group in aliasGroups)", alias_group_loop + 1
    )
    assert (
        alias_group_loop
        < parameter_header
        < canonical_parameter_loop
        < alias_parameter_loop
    )
    assert "HbrSharedParameterTextProjection.CreateText(HbrRuleDatabase.Current)" in service
    assert 'private const string NewLine = "\\r\\n";' in helper
    assert "new UTF8Encoding(false)" in helper
    for removed_builder in (
        "FindParameterHeaderIndex",
        "AppendAliasGroupDefinitions",
        "AppendAliasParameterDefinitions",
        "BuildCombinedSharedParameterFile",
        "GetManifestResourceStream",
        "GH_HIFC_SharedParameters.txt",
    ):
        assert removed_builder not in service


def test_old_stage01_initialization_is_migrated_without_manual_reinitialize():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "RequiresWorkflowMigration" in service
    assert "旧版初始化待升级" in service
    assert "无需启用“允许重新初始化”" in service


def test_v090_release_uses_v090_context_schema():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    versions = read("src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs")
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert "<Version>0.9.0</Version>" in project
    assert 'public override string Version => "0.9.0"' in assembly
    assert 'FileContextSchema = "0.9.0"' in versions
    assert '0.9.0.0' in workflow
