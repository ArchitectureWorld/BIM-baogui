from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_stage01_projects_all_nonempty_project_fields_through_catalog():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    catalog = read("src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs")
    assert "FieldMappings" not in service
    assert 'field.Key.StartsWith("IfcProject|"' in service
    assert "TryResolveStage01FieldKey" in service
    assert "PlanningTargetCatalog.IsManagedMvdField" in service
    assert "TryResolveStage01FieldKey" in catalog
    assert '"HIFC." + propertySet + "." + ifcProperty' in catalog


def test_stage01_projection_preserves_but_blocks_unverified_organizations():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    assert "payload.organizations" in service
    assert "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT" in service
    assert "不伪装成 ProjectInformation 参数" in service


def test_stage01_projection_installs_binds_writes_and_verifies_by_guid():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    assert "ParameterBindings.Insert" in service
    assert "ParameterBindings.ReInsert" in service
    assert "get_Parameter(item.Key.ParameterGuid)" in service
    assert "document.Regenerate()" in service
    assert "ReadbackMatches" in service
    assert "DUT_METERS" in service
    assert "DUT_SQUARE_METERS" in service
    assert "DUT_CUBIC_METERS" in service
    assert "DUT_DECIMAL_DEGREES" in service
    assert "REVIT_WRITE_VERIFIED" in service
    assert "仍需官方插件导出与检查软件验收" in service


def test_stage01_projection_runs_explicitly_inside_initialization_transaction():
    storage = read("src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs")
    revit_service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Stage01OfficialHifcProjectionService" not in storage
    assert "Stage01Storage.Write" in revit_service
    assert "Stage01OfficialHifcProjectionService.WriteAndVerify" in revit_service
    assert "using (var transaction = new Transaction" in revit_service
    assert "group.RollBack()" in revit_service
    assert "待官方导出验收" in revit_service
