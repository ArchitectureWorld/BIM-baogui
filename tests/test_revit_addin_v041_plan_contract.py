from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PLAN = ROOT / (
    "docs/superpowers/plans/"
    "2026-08-12-revit-native-v0.4.1-runtime-identity-and-stage01-defaults.md"
)


def _plan() -> str:
    return PLAN.read_text(encoding="utf-8")


def test_project_condition_declaration_remains_explicit_business_input():
    plan = _plan()

    assert "不得默认勾选“无上述项目条件（已确认）”" in plan
    assert "workflow.project_conditions.none = false" in plan
    assert "旧 Payload 保持未声明" in plan
    assert "defaultToNoneWhenEmpty: true" not in plan
    assert "无上述项目条件（已确认） = true" not in plan


def test_payload_migration_is_versioned_and_decode_stays_pure():
    plan = _plan()

    assert 'PayloadSchemaVersion = "0.9.1"' in plan
    assert "0.9.0 → 0.9.1" in plan
    assert "MigratableLegacy" in plan
    assert "TryDecode 只负责语法解析和类型校验" in plan
    assert "不得在 TryDecode 内补默认值、补条件声明或重写 canonical JSON" in plan
    assert "先验证原始 Payload SHA-256 与 canonical 状态，再执行显式迁移" in plan


def test_stage01_uses_per_field_authority_instead_of_one_global_priority():
    plan = _plan()

    assert "逐字段权威来源矩阵" in plan
    assert "ProjectPosition.NorthSouth" in plan
    assert "ProjectPosition.EastWest" in plan
    assert "ProjectInformation.Name / Number" in plan
    assert "Stage01 Payload" in plan
    assert "固定 GUID 参数" in plan
    assert "不得使用一个全局的 RVT > Payload > 默认值优先级" in plan
    assert "当前 RVT 实际值\n> 当前 RVT 已有 Stage01 Payload" not in plan


def test_plan_preserves_revit_and_ifc_acceptance_boundaries():
    plan = _plan()

    assert "CI 通过不等于 Revit 2020 实机通过" in plan
    assert "IFCFLUX_MANUAL_PENDING" in plan
    assert "不得宣称 IFCFlux 已通过" in plan
    assert "Stage02、Stage03、H-IFC 转译和 13 个 MCP 工具保持兼容" in plan
