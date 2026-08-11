import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.Stage01" / "BIMBaoGui.Stage01.csproj"
SERVICE = ROOT / "src" / "BIMBaoGui.Stage01" / "Revit" / "OfficialHifcWriteService.cs"
PROJECTION = ROOT / "src" / "BIMBaoGui.Stage01" / "Revit" / "OfficialParameterProjectionService.cs"
COMPONENT = ROOT / "src" / "BIMBaoGui.Stage01" / "Stage03OfficialHifcWriteComponent.cs"
CATALOG = ROOT / "src" / "BIMBaoGui.Stage01" / "Hifc" / "OfficialHifcMappingCatalog.cs"
POLICIES = ROOT / "src" / "BIMBaoGui.Stage01" / "Hifc" / "OfficialPluginCompatibilityCatalog.cs"
RULE_SOURCE = ROOT / "specs" / "hbr-rules" / "v1" / "source" / "hbr_rule_source.v1.json"
MAPPING_SNAPSHOT = (
    ROOT
    / "tests"
    / "BIMBaoGui.Stage01.Core.Tests"
    / "Snapshots"
    / "official-hifc-mappings.v1.json"
)


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_mapping_and_evidence_resources_are_embedded():
    text = PROJECT.read_text(encoding="utf-8")
    assert "GH_HIFC_ParameterBindings.json" in text
    assert "GH_HIFC_SharedParameters.txt" in text
    assert "wuhan_planning_rules.v1.json" in text
    assert "official_plugin_compatibility_status.v1.json" in text


def test_mapping_catalog_combines_bindings_with_official_rule_metadata():
    text = CATALOG.read_text(encoding="utf-8")
    source = load_json(RULE_SOURCE)
    snapshot = load_json(MAPPING_SNAPSHOT)
    properties = {item["propertyId"]: item for item in source["properties"]}

    assert len(source["legacyAliases"]) == len(snapshot["mappings"]) == 166
    assert [
        (item["propertyId"], item["alias"])
        for item in source["legacyAliases"]
    ] == [
        (item["propertyId"], item["parameterName"])
        for item in snapshot["mappings"]
    ]
    for actual in snapshot["mappings"]:
        property_rule = properties[actual["propertyId"]]
        legacy = property_rule["officialPlugin"]["legacyProjection"]
        entity, property_set, ifc_property = property_rule["officialPlugin"][
            "originalIdentity"
        ].split("|", 2)
        assert {
            "category": legacy["category"].strip(),
            "carrier": legacy["carrier"],
            "persistenceMode": legacy["persistenceMode"],
            "ifcEntity": entity,
            "propertySet": property_set.removeprefix("Pset_"),
            "ifcProperty": ifc_property,
            "ifcDataType": property_rule["ifc"]["declaredType"],
            "sharedParameterType": legacy["sharedParameterType"],
            "unit": legacy["officialUnit"] or "",
            "sourceParameterOverride": legacy["sourceParameterOverride"],
            "officialSourceParameterGroup": legacy[
                "officialSourceParameterGroup"
            ].strip(),
        } == {
            key: actual[key]
            for key in (
                "category",
                "carrier",
                "persistenceMode",
                "ifcEntity",
                "propertySet",
                "ifcProperty",
                "ifcDataType",
                "sharedParameterType",
                "unit",
                "sourceParameterOverride",
                "officialSourceParameterGroup",
            )
        }

    for contract in (
        "FromDatabase(HbrRuleDatabase.Current)",
        "database.Package.LegacyAliases",
        "database.PropertiesById.TryGetValue",
        "property.OfficialPlugin.LegacyProjection",
        "property.OfficialPlugin.OriginalIdentity",
        "ParameterName = alias.Alias",
        "OfficialSourceParameterName = officialSourceName",
    ):
        assert contract in text
    for removed_fallback in (
        "BindingResourceName",
        "RuleResourceName",
        "GetManifestResourceStream",
        "ReadEmbeddedText",
        "GH_HIFC_ParameterBindings.json",
        "wuhan_planning_rules.v1.json",
    ):
        assert removed_fallback not in text


def test_compatibility_catalog_blocks_unverified_entities():
    text = POLICIES.read_text(encoding="utf-8")
    assert "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT" in text
    assert "GetEntityPolicy" in text
    assert "AllowsProjectInformationDefault" in text


def test_writer_uses_atomic_transaction_and_shared_projection_service():
    service = SERVICE.read_text(encoding="utf-8")
    projection = PROJECTION.read_text(encoding="utf-8")
    assert "TransactionGroup" in service
    assert "OfficialParameterProjectionService.WriteAndVerify" in service
    assert "group.RollBack()" in service
    assert "document.Regenerate()" in projection
    assert "ReadbackMatches" in projection
    assert "get_Parameter(projection.Guid)" in projection
    assert "UnitUtils.ConvertToInternalUnits" in projection
    assert "DUT_DECIMAL_DEGREES" in projection


def test_writer_resolves_targets_per_mapping_and_blocks_wrong_defaults():
    text = SERVICE.read_text(encoding="utf-8")
    assert "ResolveTargetsForMapping" in text
    assert "policy.IsBlocked" in text
    assert "仅 IfcProject/IfcBuilding 属性允许在未提供元素时使用 ProjectInformation" in text
    assert "所提供 ElementId 中没有匹配对象" in text


def test_component_keeps_transaction_logic_out_of_solve_instance():
    text = COMPONENT.read_text(encoding="utf-8")
    solve = text.split("protected override void SolveInstance", 1)[1]
    assert "OfficialHifcWriteService.Enqueue" in solve
    assert "new Transaction(" not in solve
    assert "Parameter.Set" not in solve
    assert "留空仅适用于 IfcProject/IfcBuilding" in text
    assert "待官方验收" in text
