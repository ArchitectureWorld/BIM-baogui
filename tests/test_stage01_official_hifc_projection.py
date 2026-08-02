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
    policy = read("src/BIMBaoGui.Stage01/Hifc/Stage01OfficialCompatibilityPolicy.cs")
    assert "payload.organizations" in service
    assert "Stage01OfficialCompatibilityPolicy.Evaluate" in service
    assert "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT" in policy
    assert "不伪装成 IfcProject 参数" in policy


def test_stage01_projection_delegates_dual_write_and_revit_readback():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    projection = read("src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs")
    assert "OfficialParameterProjectionService.WriteAndVerify" in service
    assert "OfficialParameterWriteItem" in service
    assert "ParameterBindings.Insert" in projection
    assert "ParameterBindings.ReInsert" in projection
    assert "ResolveParameterGroup(projection)" in projection
    assert "BuiltInParameterGroup.PG_MATERIALS" in projection
    assert "BuiltInParameterGroup.PG_PHASING" in projection
    assert "OfficialSourceParameterGroup" in projection
    assert "projection.SharedParameterType" in projection
    assert "OfficialSourceParameterType" in projection
    assert "OfficialSourceValuePolicy.Normalize" in projection
    assert "RemoveLegacyOfficialBindings" in projection
    assert "SharedParameterElement.Lookup" in projection
    assert "get_Parameter(projection.Guid)" in projection
    assert "document.Regenerate()" in projection
    assert "ReadbackMatches" in projection
    assert "DUT_METERS" in projection
    assert "DUT_SQUARE_METERS" in projection
    assert "DUT_CUBIC_METERS" in projection
    assert "DUT_DECIMAL_DEGREES" in projection
    assert "REVIT_WRITE_VERIFIED" in projection
    assert "官方精确源参数" in service


def test_stage01_projection_runs_explicitly_inside_initialization_transaction():
    storage = read("src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs")
    revit_service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Stage01OfficialHifcProjectionService" not in storage
    assert "Stage01Storage.Write" in revit_service
    assert "Stage01OfficialHifcProjectionService.WriteAndVerify" in revit_service
    assert "using (var transaction = new Transaction" in revit_service
    assert "group.RollBack()" in revit_service
    assert "待官方重新导出验收" in revit_service


def test_parameter_binding_failures_identify_operation_and_projection():
    projection = read("src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs")
    assert "BINDING_INSERT_FAILED" in projection
    assert "BINDING_REINSERT_FAILED" in projection
    assert "projection.Guid.ToString(\"D\")" in projection
    assert "projection.Mapping.Category" in projection


def test_revit_2020_temporary_shared_parameter_file_uses_utf16le_bom():
    projection = read("src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs")
    assert "BuildCombinedSharedParameterFile(definitions),\n        Encoding.Unicode);" in projection
