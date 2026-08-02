from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def load_json(path: str):
    return json.loads(read(path))


def canonical_parameter_name(field: dict) -> str:
    pset = field["pset"]
    if pset.startswith("Pset_"):
        pset = pset[len("Pset_") :]
    return f"HIFC.{pset}.{field['property']}"


def normalized_alias(value: str) -> str:
    return "".join(value.split()).casefold()


def test_stage01_project_fields_have_parameter_mapping_or_explicit_exception():
    registry = load_json(
        "src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json"
    )
    bindings = load_json(
        "specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json"
    )
    status = load_json(
        "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json"
    )

    mapped_names = {
        normalized_alias(item["parameterName"]) for item in bindings["bindings"]
    }
    explicit_exceptions = set(status["stage01ProjectFieldExceptions"])
    missing = []
    for field in registry["mvd_fields"]:
        if not field.get("write_in_stage01") or field.get("entity") != "IfcProject":
            continue
        name = normalized_alias(canonical_parameter_name(field))
        if name not in mapped_names and field["field_key"] not in explicit_exceptions:
            missing.append(field["field_key"])

    assert not missing, "Stage 01 project fields lack mapping or explicit exception:\n" + "\n".join(missing)


def test_stage01_exceptions_are_explicitly_reasoned():
    status = load_json(
        "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json"
    )
    exceptions = status["stage01ProjectFieldExceptions"]
    reasons = status["stage01ProjectFieldExceptionReasons"]
    assert exceptions
    assert set(exceptions) == set(reasons)
    assert all(reasons[key].strip() for key in exceptions)
    assert any("规划控制目标" in reason for reason in reasons.values())
    assert any("Pset_Manifest" in key for key in exceptions)


def test_organization_fields_are_not_silently_claimed_as_officially_exportable():
    status = load_json(
        "specs/hifc-mapping/v1/data/official_plugin_compatibility_status.v1.json"
    )
    organization = status["entities"]["IfcOrganization"]
    assert organization["officialObjectMappingEvidence"] == "UNVERIFIED"
    assert organization["writePolicy"] == "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT"
    assert organization["officialExportVerified"] is False


def test_standard_coordinate_semantics_x_is_northsouth_y_is_eastwest():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    compact = re.sub(r"\s+", "", service)
    assert "Stage01Keys.BaseX,Format(UnitUtils.ConvertFromInternalUnits(position.NorthSouth" in compact
    assert "Stage01Keys.BaseY,Format(UnitUtils.ConvertFromInternalUnits(position.EastWest" in compact
    assert "doublenorthMeters=ParseRequiredNumber(model,Stage01Keys.BaseX);" in compact
    assert "doubleeastMeters=ParseRequiredNumber(model,Stage01Keys.BaseY);" in compact
    assert '"基点坐标X（南北）",ParseRequiredNumber(model,Stage01Keys.BaseX),north' in compact
    assert '"基点坐标Y（东西）",ParseRequiredNumber(model,Stage01Keys.BaseY),east' in compact


def test_storage_layer_has_no_hifc_projection_side_effect():
    storage = read("src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs")
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Stage01OfficialHifcProjectionService" not in storage
    assert "Stage01OfficialHifcProjectionService.WriteAndVerify" in service


def test_stage01_projection_is_registry_driven_not_ten_field_hardcoding():
    projection = read(
        "src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs"
    )
    compatibility = read(
        "src/BIMBaoGui.Stage01/Hifc/Stage01OfficialCompatibilityPolicy.cs"
    )
    assert "FieldMappings" not in projection
    assert "TryResolveStage01FieldKey" in projection
    assert "payload.organizations" in projection
    assert "Stage01OfficialCompatibilityPolicy.Evaluate" in projection
    assert "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT" in compatibility


def test_non_project_properties_never_default_to_project_information():
    writer = read("src/BIMBaoGui.Stage01/Revit/OfficialHifcWriteService.cs")
    component = read("src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs")
    assert "ResolveTargetsForMapping" in writer
    assert "仅 IfcProject/IfcBuilding 属性允许在未提供元素时使用 ProjectInformation" in writer
    assert "留空仅适用于 IfcProject/IfcBuilding" in component


def test_active_plan_requires_official_export_and_checker_roundtrip():
    design = read(
        "docs/superpowers/specs/2026-08-01-official-plugin-compatible-write-design.md"
    )
    plan = read(
        "docs/superpowers/plans/2026-08-01-official-plugin-compatible-write.md"
    )
    review = read(
        "docs/reviews/2026-08-01-official-plugin-write-deep-review.md"
    )
    for text in (design, plan, review):
        assert "官方插件导出" in text
        assert "检查软件" in text
        assert "Golden RVT" in text
        assert "Golden IFC" in text
    assert "不以 Revit 参数回读作为最终兼容性结论" in design
    assert "禁止将 POST_EXPORT_ENRICH 作为当前产品路径" in design
