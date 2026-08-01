from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.Stage01" / "BIMBaoGui.Stage01.csproj"
SERVICE = ROOT / "src" / "BIMBaoGui.Stage01" / "Revit" / "OfficialHifcWriteService.cs"
PROJECTION = ROOT / "src" / "BIMBaoGui.Stage01" / "Revit" / "OfficialParameterProjectionService.cs"
COMPONENT = ROOT / "src" / "BIMBaoGui.Stage01" / "Stage03OfficialHifcWriteComponent.cs"
CATALOG = ROOT / "src" / "BIMBaoGui.Stage01" / "Hifc" / "OfficialHifcMappingCatalog.cs"
POLICIES = ROOT / "src" / "BIMBaoGui.Stage01" / "Hifc" / "OfficialPluginCompatibilityCatalog.cs"


def test_mapping_and_evidence_resources_are_embedded():
    text = PROJECT.read_text(encoding="utf-8")
    assert "GH_HIFC_ParameterBindings.json" in text
    assert "GH_HIFC_SharedParameters.txt" in text
    assert "wuhan_planning_rules.v1.json" in text
    assert "official_plugin_compatibility_status.v1.json" in text


def test_mapping_catalog_combines_bindings_with_official_rule_metadata():
    text = CATALOG.read_text(encoding="utf-8")
    assert "BindingResourceName" in text
    assert "RuleResourceName" in text
    assert "IfcEntity = ifcEntity" in text
    assert "IfcDataType = rule.official.ifcDataType" in text
    assert "SharedParameterType = rule.canonical.sharedParameterType" in text
    assert "OfficialSourceParameterName" in text


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
