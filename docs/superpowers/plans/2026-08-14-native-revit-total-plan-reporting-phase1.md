# Native Revit Total-Plan Reporting Phase 1 v0.4.3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从已验证的原生 Revit v0.4.2 基线交付 v0.4.3：完成总平模型第一期的 Stage01 项目输入、02A 构件语义与属性准备、独立 02B 项目实际指标、Stage03 严格清单/问题回溯/测试强制导出，并形成可安装、可复查的 Revit 2020 证据链。

**Architecture:** 唯一基础规则源保持冻结；v0.4.3 受控 overlay 只引用已有字段 identity，并生成 `nativeReporting` 目录。Stage01、02A、02B 分别产生带文档/模型/规则身份和稳定 hash 的结果，Stage03 只读取并检查，不代替来源阶段补值；WPF 与既有 MCP 入口共享相同领域策略，所有 Revit API 操作继续经 ExternalEvent 执行。

**Tech Stack:** C# / .NET Framework 4.8、Autodesk Revit 2020 API、WPF、Extensible Storage、.NET 8 MCP Server、xUnit、Python 3 / pytest、PowerShell、GitHub Actions Windows runner。

## Global Constraints

- 实施起点固定为 `eca4639af65e165827810e06340ecb700ffe3e09`，即 `feat/revit-stage02-manual-semantic-v0.4.2` 的干净 HEAD。
- 目标分支固定为 `feat/revit-native-total-plan-phase1-v0.4.3`；目标产品版本为 `0.4.3`，程序集版本为 `0.4.3.0`。
- 不在脏的旧 `main` 工作树实施；执行前必须使用 `superpowers:using-git-worktrees` 创建隔离工作树。
- 只交付 Autodesk Revit 2020 原生插件；不新增 Web、Grasshopper 或外部常驻服务作为替代路径。
- 第一阶段只支持 `总平模型`；`单体建筑—地上`、`单体建筑—地下` 必须明确返回 `MODEL_PROFILE_NOT_IMPLEMENTED_PHASE1`，不得回退到总平清单。
- Stage03 清单没有 `NotApplicable` 状态或人工“不适用”入口；不满足条件的定义在生成阶段排除。
- 02B 的六个项目实际指标只允许人工录入；本期不读取几何、不计算总建筑面积、建筑密度、容积率、绿地率或停车位数量。
- 规划目标和实际指标以完整 `IFC Entity + PropertySet + Property + propertyId` 区分，任何同名字段不得合并。
- 02A 以单构件为写入原子；02B 以单指标为写入原子。成功项保留，失败项仅回滚自身并可单独重试；v0.4.2 的 `CustomSelection` 表示读取当前 Revit 选择集，内部解析为排序后的 UniqueId，不得把它误写成既有公开 ElementId payload。
- `IfcProject`、`IfcSite`、`IfcSpatialZone` 当前 `officialExportVerified=false`。允许保存内部真实输入；未完成 Golden RVT → 官方插件 → IFCFlux 证据前，官方载体状态必须保持 `PENDING_GOLDEN_RVT`，不得从 `legacyProjection.carrier` 猜测为已验证。
- 测试强制导出必须要求非空原因、保留全部红项、写入测试身份；它只能绕过业务缺项，不能绕过文档、规则、hash、输出、导出器、RAW、转译或报告错误。
- 现有 MCP 工具总数保持 13；本期不新增 02B MCP 工具，只恢复 Stage03 既有工具的 `force_reason` 参数。
- 自动化测试、内部回读、标准 IFC 导出均不得冒充官方 HIFCTool/IFCFlux 验收通过。
- 不提交 `bin/`、`obj/`、`TestResults/`、`artifacts/`、RVT、IFC、截图、ZIP、日志或临时文件；v0.4.2 功能基线作为历史证据保留。

## 执行前准备

先读取 `superpowers:using-git-worktrees`，然后执行：

```powershell
$repo = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui'
$worktree = 'C:\Users\2899\.config\superpowers\worktrees\BIM-baogui\revit-native-total-plan-phase1-v0.4.3'
$branch = 'feat/revit-native-total-plan-phase1-v0.4.3'
$base = 'eca4639af65e165827810e06340ecb700ffe3e09'
$planPath = 'docs/superpowers/plans/2026-08-14-native-revit-total-plan-reporting-phase1.md'
$planCommit = (git -C $repo log -1 --format=%H -- $planPath).Trim()

git -C $repo fetch origin
git -C $repo cat-file -e "$base^{commit}"
git -C $repo cat-file -e "$planCommit^{commit}"
git -C $repo show-ref --verify --quiet "refs/heads/$branch"
git -C $repo ls-remote --exit-code --heads origin "refs/heads/$branch"
```

Expected: 两个 `cat-file` exit 0；`show-ref` exit 1；`ls-remote` 无输出且 exit 2。若目标分支在本地或远端已存在，停止并检查，不复用未知状态。

```powershell
git -C $repo worktree add $worktree -b $branch $base
git -C $worktree cherry-pick 6f646a5 $planCommit
git -C $worktree status --short --branch
git -C $worktree rev-parse HEAD
```

Expected: `$planCommit` 为本计划的已提交版本；新工作树干净；历史包含 v0.4.2 基线、已确认设计规格和本实施计划。后续所有命令均在 `$worktree` 执行。

## 文件结构与职责

| 区域 | 文件 | 单一职责 |
|---|---|---|
| 规则构建 | `specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json` | 引用总平清单、02A 语义、02B 指标和官方载体证据，不复制基础属性定义 |
| 规则构建 | `tools/build_hbr_rulepack_v043.py` | 校验 overlay 引用并确定性生成含 `nativeReporting` 的 rule pack |
| 规则投影 | `Rules/NativeReportingRuleCatalog.cs` | 读取模型 profile、检查定义、语义角色和官方载体证据 |
| 规则投影 | `Rules/NativeStage02BMetricCatalog.cs` | 将六个 propertyId 连接到唯一属性定义 |
| 公共合同 | `Workflow/NativeWorkflowIdentity.cs` | 文档、模型和规则三元身份 |
| 公共合同 | `Workflow/NativeWorkflowResultModels.cs` | Stage01/02A/02B 统一结果 envelope 和 item evidence |
| 公共合同 | `Workflow/NativeWorkflowResultCanonicalizer.cs` | 确定性 JSON、item hash 和 result hash |
| 公共合同 | `Workflow/NativeWorkflowResultStorage.cs` | 在 RVT 中按来源功能保存最新结果 |
| 公共合同 | `Workflow/NativeWorkflowFreshnessPolicy.cs` | 拒绝跨文档、跨模型、跨规则、hash 错误和输入过期结果 |
| 问题中心 | `Issues/NativeIssueModels.cs` | 稳定 issue、构件引用和修复路由 |
| 问题中心 | `Issues/NativeIssueCanonicalizer.cs` | 稳定 issueId |
| 问题中心 | `Issues/NativeIssueHub.cs` | 合并 Stage01/02A/02B/03 问题快照 |
| 问题中心 | `Issues/NativeRevitIssueNavigationService.cs` | 选中、缩放、临时隔离和恢复视图 |
| 02B | `Stage02B/*` | 人工指标、存储、载体门禁、指标级事务和独立 WPF UI |
| Stage03 | `Stage03/NativeStage03Checklist*.cs` | 动态清单生成、证据求值、四态结果与稳定 hash |

依赖顺序固定为：规则包 → 运行时目录 → 公共结果合同 → Stage01 → 02A → 问题定位 → 02B → Stage03 → 导出门禁/报告 → v0.4.3 发布与实机验收。

---

### Task 1: 生成 v0.4.3 总平报规规则目录

**Files:**
- Create: `specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json`
- Create: `tools/build_hbr_rulepack_v043.py`
- Create: `tests/test_hbr_rulepack_v043.py`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `tests/test_revit_addin_scaffold_contract.py`

**Interfaces:**
- Consumes: `hbr_rule_source.v1.json`（359 属性/14 roles）和 v0.4.2 的 4 个绿地属性/1 role。
- Produces: 确定性 `HBR_RulePack.hbrpack`，仍为 363 属性/15 carrier roles，并新增顶层 `nativeReporting.schemaVersion = "1.0.0"`。

- [ ] **Step 1: 写规则包 RED 测试**

在 `tests/test_hbr_rulepack_v043.py` 建立以下核心断言：

```python
import hashlib
import importlib.util
import json
import struct
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
COMPILER = ROOT / "tools" / "build_hbr_rulepack_v043.py"
SOURCE = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
OVERLAY = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json"
BASELINE = ROOT / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"

METRICS = {
    "ca21e324-046b-5bfd-84c8-0d3470082303": "IfcProject|Pset_登记信息属性集|总建筑面积",
    "93e51676-237e-56a8-8f28-2da845422e2e": "IfcSite|Pset_场地信息属性集|建筑密度",
    "201a00ac-3672-5ded-83d2-ed96f81bfabf": "IfcSite|Pset_场地信息属性集|容积率",
    "f630ad47-b006-5127-badd-b1660cf996c3": "IfcSite|Pset_场地信息属性集|绿地率",
    "c62cfd5f-2a50-5230-9c5d-4037c39061bf": "IfcSpatialZone|Pset_停车场信息属性集|机动车位数量",
    "84df74c2-a7e5-5a98-a5e0-4458e49a3973": "IfcSpatialZone|Pset_停车场信息属性集|非机动车位数量",
}

def compile_pack(output: Path) -> bytes:
    subprocess.run([
        sys.executable, str(COMPILER), "--source", str(SOURCE),
        "--overlay", str(OVERLAY), "--baseline", str(BASELINE),
        "--output", str(output),
    ], check=True)
    return output.read_bytes()

def load_compiler():
    spec = importlib.util.spec_from_file_location("hbr_rulepack_v043", COMPILER)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def test_v043_reporting_pack_is_deterministic_and_exact(tmp_path):
    first = compile_pack(tmp_path / "a.hbrpack")
    second = compile_pack(tmp_path / "b.hbrpack")
    assert first == second
    assert hashlib.sha256(first).hexdigest() == hashlib.sha256(second).hexdigest()
    assert first[:4] == b"HBRP"
    assert struct.unpack(">I", first[4:8])[0] == 1
    payload_length = struct.unpack(">Q", first[8:16])[0]
    payload_bytes = first[48:]
    assert payload_length == len(payload_bytes)
    assert first[16:48] == hashlib.sha256(payload_bytes).digest()
    payload = json.loads(payload_bytes.decode("utf-8"))
    assert len(payload["properties"]) == 363
    assert len(payload["carrierRoles"]) == 15
    reporting = payload["nativeReporting"]
    assert reporting["schemaVersion"] == "1.0.0"
    assert reporting["profiles"][0]["modelFileType"] == "总平模型"
    assert len(reporting["stage01FieldKeys"]) == 24
    assert len(reporting["planningTargetPropertyIds"]) == 10
    assert {m["propertyId"]: m["identity"] for m in reporting["stage02BMetrics"]} == METRICS
    assert all(not m["officialExportVerified"] for m in reporting["stage02BMetrics"])
    assert {item["checkId"] for item in reporting["systemChecks"]} == {
        "CROSS.DOCUMENT_IDENTITY", "CROSS.MODEL_PROFILE",
        "CROSS.RULE_PACKAGE", "CROSS.RESULT_FRESHNESS",
        "EXPORT.REVIT_DOCUMENT", "EXPORT.OUTPUT_DIRECTORY",
        "EXPORT.RAW_IFC_PIPELINE", "EXPORT.REPORT_WRITER",
    }

@pytest.mark.parametrize("case", [
    "duplicate_role", "duplicate_metric", "aliases_unsorted",
    "aliases_duplicate", "invalid_role_status", "orphan_carrier",
    "orphan_evidence", "derived_check_id_collision",
])
def test_invalid_native_reporting_overlay_is_rejected_atomically(tmp_path, case):
    overlay = json.loads(OVERLAY.read_text(encoding="utf-8"))
    reporting = overlay["nativeReporting"]
    metric = reporting["stage02BMetrics"][0]
    property_id = metric["propertyId"]

    if case == "duplicate_role":
        reporting["semanticRoles"].append(dict(reporting["semanticRoles"][0]))
    elif case == "duplicate_metric":
        reporting["stage02BMetrics"].append(dict(metric))
    elif case == "aliases_unsorted":
        role = reporting["semanticRoles"][0]
        role["candidateAliases"] = list(reversed(role["candidateAliases"]))
    elif case == "aliases_duplicate":
        role = reporting["semanticRoles"][0]
        role["candidateAliases"].append(role["candidateAliases"][0])
    elif case == "invalid_role_status":
        reporting["semanticRoles"][0]["officialCarrierStatus"] = "BOGUS"
    elif case == "orphan_carrier":
        reporting["officialProjectionCarriers"].append({
            "carrierId": "OFFICIAL.ORPHAN.V1",
            "propertyId": property_id,
            "selectorKind": "PROJECT_INFORMATION",
            "roleId": "",
            "categoryBuiltInId": "",
            "elementClass": "Autodesk.Revit.DB.ProjectInfo",
            "bindingScope": "INSTANCE",
            "parameterGuid": property_id,
        })
    elif case == "orphan_evidence":
        reporting["officialEvidenceRecords"].append({
            "evidenceId": "EVIDENCE.ORPHAN.000000000000",
            "propertyId": property_id,
            "goldenRvtSha256": "0" * 64,
            "hifctoolManifestSha256": "1" * 64,
            "hifctoolDllSha256": "2" * 64,
            "hifctoolProductVersion": "1.0.0",
            "officialIfcSha256": "3" * 64,
            "ifcFluxProductVersion": "0.1.0",
            "ifcFluxReportSha256": "4" * 64,
            "observedRevitUniqueId": "orphan-revit-unique-id",
            "observedIfcGlobalId": "orphan-ifc-global-id",
            "observedBindingScope": "INSTANCE",
            "observedParameterGuid": property_id,
        })
    else:
        reporting["systemChecks"].append({
            "sequence": 99999,
            "checkId": f"STAGE02B.METRIC.{property_id}",
            "displayName": "派生检查编号冲突",
            "sourceStage": "CROSS_STAGE",
            "applicableBasis": "负向合同测试",
            "remediationTarget": "RECHECK_ALL",
        })

    invalid_overlay = tmp_path / f"{case}.json"
    invalid_overlay.write_text(
        json.dumps(overlay, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    output = tmp_path / "existing.hbrpack"
    output.write_bytes(b"existing-output-must-survive")
    compiler = load_compiler()

    with pytest.raises(ValueError):
        compiler.compile_rulepack(SOURCE, BASELINE, invalid_overlay, output)

    assert output.read_bytes() == b"existing-output-must-survive"
    assert list(tmp_path.glob(f".{output.name}.*.tmp")) == []

def test_replace_failure_preserves_output_and_cleans_temporary_file(
    tmp_path, monkeypatch
):
    compiler = load_compiler()
    output = tmp_path / "existing.hbrpack"
    output.write_bytes(b"existing-output-must-survive")

    def fail_replace(source, destination):
        raise OSError("replace failed for atomic-write contract test")

    monkeypatch.setattr(compiler.os, "replace", fail_replace)
    with pytest.raises(OSError, match="atomic-write contract test"):
        compiler.compile_rulepack(SOURCE, BASELINE, OVERLAY, output)

    assert output.read_bytes() == b"existing-output-must-survive"
    assert list(tmp_path.glob(f".{output.name}.*.tmp")) == []
```

- [ ] **Step 2: 运行测试确认 RED**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
python -m pytest tests/test_hbr_rulepack_v043.py -q
```

Expected: FAIL，因为 v0.4.3 overlay/compiler 尚不存在。

- [ ] **Step 3: 建立精确 overlay 元数据**

以 v0.4.2 overlay 的 `carrierRoles` 和 `properties` 原样作为 v0.4.3 起点，并新增以下顶层对象；不得改写六个 property identity：

```json
{
  "nativeReporting": {
    "schemaVersion": "1.0.0",
    "profiles": [
      {
        "modelFileType": "总平模型",
        "strictNoNotApplicable": true,
        "taskIds": [
          "SITE.SKELETON", "SITE.TOTAL_LAND", "SITE.NET_LAND",
          "SITE.BUILDING_FOOTPRINT", "SITE.OTHER_LAND",
          "SITE.ROAD_REDLINE", "SITE.ROAD_CENTERLINE",
          "SITE.INTERNAL_ROADS", "SITE.FIRE_LANE", "SITE.FIRE_FIELD",
          "SITE.GREEN", "SITE.OUTDOOR_PARKING", "SITE.CIVIL_DEFENSE",
          "SITE.STRUCTURES", "SITE.TARGET_CHECK"
        ]
      }
    ],
    "semanticRoles": [
      {"roleId":"SITE_TOTAL_LAND","taskId":"SITE.TOTAL_LAND","displayName":"规划总用地","candidateAliases":["总用地","规划总用地"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_NET_LAND","taskId":"SITE.NET_LAND","displayName":"规划净用地","candidateAliases":["净用地","规划净用地"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_BUILDING_FOOTPRINT","taskId":"SITE.BUILDING_FOOTPRINT","displayName":"建筑轮廓或建筑占地表达","candidateAliases":["建筑占地","建筑基底","建筑轮廓"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_OTHER_LAND","taskId":"SITE.OTHER_LAND","displayName":"其他分类用地","candidateAliases":["其他分类用地","其它用地"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_ROAD_REDLINE","taskId":"SITE.ROAD_REDLINE","displayName":"道路红线","candidateAliases":["道路红线"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_ROAD_CENTERLINE","taskId":"SITE.ROAD_CENTERLINE","displayName":"道路中心线","candidateAliases":["道路中心线"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_INTERNAL_ROADS","taskId":"SITE.INTERNAL_ROADS","displayName":"区内道路","candidateAliases":["内部道路","区内道路"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_FIRE_LANE","taskId":"SITE.FIRE_LANE","displayName":"消防道路","candidateAliases":["消防车道","消防道路"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_FIRE_FIELD","taskId":"SITE.FIRE_FIELD","displayName":"消防登高或操作场地","candidateAliases":["消防操作场地","消防登高场地"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_GREEN_OBJECT","taskId":"SITE.GREEN","displayName":"绿地","candidateAliases":["绿地"],"linkedCarrierRoleId":"SITE_GREEN_OBJECT","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_OUTDOOR_PARKING","taskId":"SITE.OUTDOOR_PARKING","displayName":"室外停车场或车位","candidateAliases":["室外停车场","室外车位"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_CIVIL_DEFENSE","taskId":"SITE.CIVIL_DEFENSE","displayName":"人防区域","candidateAliases":["人防","人防区域"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"},
      {"roleId":"SITE_STRUCTURES","taskId":"SITE.STRUCTURES","displayName":"室外构筑物与设施","candidateAliases":["室外构筑物","室外设施"],"linkedCarrierRoleId":"","officialCarrierStatus":"PENDING_GOLDEN_RVT"}
    ],
    "stage01FieldKeys": [
      "HBR|FileIdentity|ModelFileType",
      "HBR|FileIdentity|FileGuid",
      "IfcProject|Pset_申报信息属性集|项目编号",
      "IfcProject|Pset_申报信息属性集|项目名称",
      "IfcProject|Pset_登记信息属性集|建设性质",
      "IfcProject|Pset_登记信息属性集|建筑物编码",
      "IfcProject|Pset_登记信息属性集|现状名称",
      "IfcProject|Pset_登记信息属性集|审批名称",
      "IfcProject|Pset_登记信息属性集|详细地址",
      "IfcProject|Pset_登记信息属性集|不动产地址",
      "IfcProject|Pset_登记信息属性集|建成时间",
      "IfcProject|Pset_登记信息属性集|建筑状态",
      "IfcProject|Pset_登记信息属性集|建设性质代码",
      "IfcProject|Pset_申报信息属性集|经度",
      "IfcProject|Pset_申报信息属性集|纬度",
      "IfcProject|Pset_申报信息属性集|基点坐标X",
      "IfcProject|Pset_申报信息属性集|基点坐标Y",
      "IfcProject|Pset_申报信息属性集|基点高程",
      "IfcProject|Pset_申报信息属性集|坐标系名称",
      "IfcProject|Pset_申报信息属性集|高程系名称",
      "HBR|SpatialReference|TrueNorthAngle",
      "HBR|ProjectUnits|Length",
      "HBR|ProjectUnits|Area",
      "HBR|ProjectUnits|Angle"
    ],
    "planningTargetPropertyIds": [
      "c94f1ae2-0a02-5479-aae4-c8f59af71fe0",
      "35675fd2-c3d2-5553-8db6-855980a201a4",
      "5d5f3dba-3ae9-59c6-9aee-aa24e88f312c",
      "ddc7523d-e3aa-527e-9689-6ed93b2ba850",
      "504e3237-da89-5de9-a39a-4e5df0008903",
      "aef64f95-dc27-5aff-9f13-3121f6c896a0",
      "11110e9f-aaae-5576-ac0d-447a6f4b8524",
      "ce26e8a2-a98b-57b6-8c37-798d17c553cb",
      "85c3a1fe-4965-53d3-828c-bdf2298f3db8",
      "20c734f0-64ea-52a9-a73b-a335d6a811db"
    ],
    "stage02BMetrics": [
      {"sequence":10,"propertyId":"ca21e324-046b-5bfd-84c8-0d3470082303","identity":"IfcProject|Pset_登记信息属性集|总建筑面积","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""},
      {"sequence":20,"propertyId":"93e51676-237e-56a8-8f28-2da845422e2e","identity":"IfcSite|Pset_场地信息属性集|建筑密度","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""},
      {"sequence":30,"propertyId":"201a00ac-3672-5ded-83d2-ed96f81bfabf","identity":"IfcSite|Pset_场地信息属性集|容积率","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""},
      {"sequence":40,"propertyId":"f630ad47-b006-5127-badd-b1660cf996c3","identity":"IfcSite|Pset_场地信息属性集|绿地率","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""},
      {"sequence":50,"propertyId":"c62cfd5f-2a50-5230-9c5d-4037c39061bf","identity":"IfcSpatialZone|Pset_停车场信息属性集|机动车位数量","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""},
      {"sequence":60,"propertyId":"84df74c2-a7e5-5a98-a5e0-4458e49a3973","identity":"IfcSpatialZone|Pset_停车场信息属性集|非机动车位数量","source":"MANUAL_INPUT","officialExportVerified":false,"officialCarrierStatus":"PENDING_GOLDEN_RVT","officialProjectionCarrierId":"","officialEvidenceRef":""}
    ],
    "officialProjectionCarriers": [],
    "officialEvidenceRecords": [],
    "officialCarrierPolicies": [
      {"ifcEntity":"IfcProject","internalCarrier":"PROJECT_INFORMATION","projectionPolicy":"ALLOW_WITH_EXPORT_VERIFICATION_REQUIRED","officialExportVerified":false,"evidenceStatus":"PENDING_GOLDEN_RVT","evidenceRefs":[]},
      {"ifcEntity":"IfcSite","internalCarrier":"","projectionPolicy":"BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT","officialExportVerified":false,"evidenceStatus":"PENDING_GOLDEN_RVT","evidenceRefs":[]},
      {"ifcEntity":"IfcSpatialZone","internalCarrier":"","projectionPolicy":"BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT","officialExportVerified":false,"evidenceStatus":"PENDING_GOLDEN_RVT","evidenceRefs":[]}
    ],
    "systemChecks": [
      {"sequence":90010,"checkId":"CROSS.DOCUMENT_IDENTITY","displayName":"跨阶段文档身份一致","sourceStage":"CROSS_STAGE","applicableBasis":"当前文档的 01、02A、02B 结果必须同源","remediationTarget":"RECHECK_ALL"},
      {"sequence":90020,"checkId":"CROSS.MODEL_PROFILE","displayName":"跨阶段模型类型一致","sourceStage":"CROSS_STAGE","applicableBasis":"模型类型仅来自 Stage01","remediationTarget":"OPEN_STAGE01"},
      {"sequence":90030,"checkId":"CROSS.RULE_PACKAGE","displayName":"跨阶段规则包一致","sourceStage":"CROSS_STAGE","applicableBasis":"01、02A、02B 必须使用当前规则三元身份","remediationTarget":"RECHECK_ALL"},
      {"sequence":90040,"checkId":"CROSS.RESULT_FRESHNESS","displayName":"跨阶段结果未过期","sourceStage":"CROSS_STAGE","applicableBasis":"输入快照与稳定 hash 必须为当前值","remediationTarget":"RECHECK_ALL"},
      {"sequence":91010,"checkId":"EXPORT.REVIT_DOCUMENT","displayName":"Revit 文档可用于导出","sourceStage":"EXPORT_PREPARATION","applicableBasis":"活动文档有效且非族文档","remediationTarget":"STAY_STAGE03"},
      {"sequence":91020,"checkId":"EXPORT.OUTPUT_DIRECTORY","displayName":"导出目录可写","sourceStage":"EXPORT_PREPARATION","applicableBasis":"输出目录存在或可创建且可写","remediationTarget":"STAY_STAGE03"},
      {"sequence":91030,"checkId":"EXPORT.RAW_IFC_PIPELINE","displayName":"RAW IFC 与转译链可用","sourceStage":"EXPORT_PREPARATION","applicableBasis":"导出器、RAW 产物与转译依赖可用","remediationTarget":"STAY_STAGE03"},
      {"sequence":91040,"checkId":"EXPORT.REPORT_WRITER","displayName":"证据报告可写","sourceStage":"EXPORT_PREPARATION","applicableBasis":"fields、validation 和 failure 报告可持久化","remediationTarget":"STAY_STAGE03"}
    ]
  }
}
```

- [ ] **Step 4: 实现 v0.4.3 编译器的引用校验**

`build_hbr_rulepack_v043.py` 是完整可执行 wrapper，不是片段。文件先定义 imports、默认路径和公共 helper：

```python
import argparse
import hashlib
import importlib.util
import json
import os
import re
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
V042_COMPILER_PATH = ROOT / "tools" / "build_hbr_rulepack_v042.py"
DEFAULT_OVERLAY = (
    ROOT / "specs" / "hbr-rules" / "v1" / "source"
    / "hbr_rule_source.v0.4.3-overlay.json"
)

def _load_json(path):
    with Path(path).open(encoding="utf-8") as stream:
        return json.load(stream)

def _require(condition, message):
    if not condition:
        raise ValueError(message)

def _sha16(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:16]

def _load_v042_compiler():
    spec = importlib.util.spec_from_file_location(
        "hbr_rulepack_v042_compiler", V042_COMPILER_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load v0.4.2 HBR rule-pack compiler")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def build_native_reporting_catalog(merged, overlay):
    reporting = overlay["nativeReporting"]
    properties = {item["propertyId"]: item for item in merged["properties"]}
    tasks = {item["taskId"]: item for item in merged["tasks"]}
    roles = {item["roleId"] for item in merged["carrierRoles"]}
    valid_evidence_statuses = {
        "VERIFIED", "PENDING_GOLDEN_RVT", "INTERNAL_ONLY"
    }
    profiles = reporting["profiles"]
    _require(
        [item["modelFileType"] for item in profiles] == ["总平模型"],
        "phase1 native reporting must contain exactly the total-plan profile",
    )
    semantic_roles = reporting["semanticRoles"]
    semantic_role_id_list = [item["roleId"] for item in semantic_roles]
    _require(
        len(semantic_role_id_list) == len(set(semantic_role_id_list)),
        "duplicate semantic roleId",
    )
    semantic_role_ids = set(semantic_role_id_list)
    metrics = reporting["stage02BMetrics"]
    metric_property_ids = [item["propertyId"] for item in metrics]
    _require(
        len(metric_property_ids) == len(set(metric_property_ids)),
        "duplicate 02B propertyId",
    )
    metric_sequences = [item["sequence"] for item in metrics]
    _require(
        metric_sequences == sorted(metric_sequences)
        and len(metric_sequences) == len(set(metric_sequences)),
        "02B metric sequence must be sorted and unique",
    )
    projection_carriers = {
        item["carrierId"]: item
        for item in reporting["officialProjectionCarriers"]
    }
    evidence_records = {
        item["evidenceId"]: item
        for item in reporting["officialEvidenceRecords"]
    }
    _require(
        len(projection_carriers) == len(reporting["officialProjectionCarriers"]),
        "duplicate official projection carrierId",
    )
    _require(
        len(evidence_records) == len(reporting["officialEvidenceRecords"]),
        "duplicate official evidenceId",
    )
    for carrier in projection_carriers.values():
        _require(
            carrier["selectorKind"] in {
                "PROJECT_INFORMATION", "CONFIRMED_SEMANTIC_ROLE"
            },
            f"unsupported official selector: {carrier['carrierId']}",
        )
        _require(carrier["bindingScope"] == "INSTANCE", "official binding must be INSTANCE")
        _require(carrier["parameterGuid"] == carrier["propertyId"], "official parameter GUID mismatch")
        if carrier["selectorKind"] == "PROJECT_INFORMATION":
            _require(not carrier["roleId"] and not carrier["categoryBuiltInId"], "ProjectInformation selector cannot use role/category")
            _require(carrier["elementClass"] == "Autodesk.Revit.DB.ProjectInfo", "ProjectInformation class mismatch")
        else:
            _require(carrier["roleId"] in semantic_role_ids, "semantic selector role is unknown")
            _require(bool(carrier["categoryBuiltInId"].strip()), "semantic selector category missing")
            _require(bool(carrier["elementClass"].strip()), "semantic selector class missing")
    sha256 = re.compile(r"^[0-9a-f]{64}$")
    for evidence in evidence_records.values():
        for key in (
            "goldenRvtSha256", "hifctoolManifestSha256", "hifctoolDllSha256",
            "officialIfcSha256", "ifcFluxReportSha256",
        ):
            _require(bool(sha256.fullmatch(evidence[key])), f"invalid {key}: {evidence['evidenceId']}")
        for key in (
            "hifctoolProductVersion", "ifcFluxProductVersion",
            "observedRevitUniqueId", "observedIfcGlobalId",
        ):
            _require(bool(evidence[key].strip()), f"missing {key}: {evidence['evidenceId']}")
        _require(evidence["observedBindingScope"] == "INSTANCE", "evidence binding mismatch")
        _require(evidence["observedParameterGuid"] == evidence["propertyId"], "evidence parameter GUID mismatch")
    referenced_carrier_ids = set()
    referenced_evidence_ids = set()
    for metric in metrics:
        prop = properties.get(metric["propertyId"])
        _require(prop is not None, f"unknown 02B propertyId: {metric['propertyId']}")
        identity = "|".join([
            prop["ifc"]["entity"], prop["ifc"]["propertySet"],
            prop["ifc"]["property"],
        ])
        _require(identity == metric["identity"], f"02B identity mismatch: {metric['propertyId']}")
        _require(metric["source"] == "MANUAL_INPUT", "02B phase1 source must be manual")
        _require(isinstance(metric["officialExportVerified"], bool), "metric verified flag must be boolean")
        _require(
            metric["officialCarrierStatus"] in valid_evidence_statuses,
            f"invalid metric carrier status: {metric['propertyId']}",
        )
        if metric["officialCarrierStatus"] == "VERIFIED":
            _require(metric["officialExportVerified"] is True, "verified metric flag mismatch")
            carrier = projection_carriers.get(metric["officialProjectionCarrierId"])
            evidence = evidence_records.get(metric["officialEvidenceRef"])
            _require(carrier is not None, "verified metric carrier ref missing")
            _require(evidence is not None, "verified metric evidence ref missing")
            _require(carrier["propertyId"] == metric["propertyId"], "carrier propertyId mismatch")
            _require(evidence["propertyId"] == metric["propertyId"], "evidence propertyId mismatch")
            referenced_carrier_ids.add(metric["officialProjectionCarrierId"])
            referenced_evidence_ids.add(metric["officialEvidenceRef"])
        else:
            _require(metric["officialExportVerified"] is False, "unproved metric cannot be verified")
            _require(not metric["officialProjectionCarrierId"], "pending metric carrier ref must be empty")
            _require(not metric["officialEvidenceRef"], "pending metric evidence ref must be empty")
    _require(
        set(projection_carriers) == referenced_carrier_ids,
        "orphan or missing official projection carrier",
    )
    _require(
        set(evidence_records) == referenced_evidence_ids,
        "orphan or missing official evidence record",
    )
    for profile in profiles:
        _require(
            len(profile["taskIds"]) == len(set(profile["taskIds"])),
            f"duplicate profile taskId: {profile['modelFileType']}",
        )
        for task_id in profile["taskIds"]:
            _require(task_id in tasks, f"unknown reporting taskId: {task_id}")
    for role in semantic_roles:
        _require(role["taskId"] in tasks, f"unknown semantic taskId: {role['taskId']}")
        linked = role["linkedCarrierRoleId"]
        _require(not linked or linked in roles, f"unknown linked carrier role: {linked}")
        aliases = role["candidateAliases"]
        _require(
            aliases == sorted(aliases) and len(aliases) == len(set(aliases)),
            f"candidateAliases must be ordinal-sorted and unique: {role['roleId']}",
        )
        _require(
            role["officialCarrierStatus"] in valid_evidence_statuses,
            f"invalid semantic role carrier status: {role['roleId']}",
        )
    policies = reporting["officialCarrierPolicies"]
    policy_entities = [item["ifcEntity"] for item in policies]
    _require(
        len(policy_entities) == len(set(policy_entities)),
        "duplicate official carrier policy entity",
    )
    controlled_entities = sorted(
        {item["identity"].split("|", 1)[0] for item in metrics}
    )
    _require(
        sorted(policy_entities) == controlled_entities,
        "official carrier policies must exactly cover 02B entities",
    )
    for policy in policies:
        _require(
            isinstance(policy["officialExportVerified"], bool),
            f"entity verified flag must be boolean: {policy['ifcEntity']}",
        )
        _require(
            policy["evidenceStatus"] in valid_evidence_statuses,
            f"invalid entity carrier status: {policy['ifcEntity']}",
        )
        entity_metrics = [
            item for item in metrics
            if item["identity"].split("|", 1)[0] == policy["ifcEntity"]
        ]
        expected_refs = sorted(item["officialEvidenceRef"] for item in entity_metrics)
        if policy["evidenceStatus"] == "VERIFIED":
            _require(entity_metrics and all(item["officialExportVerified"] for item in entity_metrics), "entity policy cannot outrun properties")
            _require(policy["officialExportVerified"] is True, "verified entity flag mismatch")
            _require(policy["evidenceRefs"] == expected_refs, "entity evidenceRefs mismatch")
        else:
            _require(policy["officialExportVerified"] is False, "pending entity cannot be verified")
            _require(policy["evidenceRefs"] == [], "pending entity evidenceRefs must be empty")
    stage01_keys = {
        item["fieldKey"] for item in merged["stage01"]["fieldRefs"]
    } | {
        item["fieldKey"] for item in merged["stage01"]["internalWorkflowFields"]
    }
    _require(
        len(reporting["stage01FieldKeys"])
        == len(set(reporting["stage01FieldKeys"])),
        "duplicate Stage01 fieldKey",
    )
    for field_key in reporting["stage01FieldKeys"]:
        _require(field_key in stage01_keys, f"unknown Stage01 fieldKey: {field_key}")
    planning_target_ids = reporting["planningTargetPropertyIds"]
    _require(
        len(planning_target_ids) == len(set(planning_target_ids)),
        "duplicate planning target propertyId",
    )
    for property_id in planning_target_ids:
        _require(property_id in properties, f"unknown planning target: {property_id}")
        _require(
            properties[property_id]["ifc"]["propertySet"] == "Pset_项目控制指标信息属性集",
            f"planning target identity mismatch: {property_id}",
        )
    system_check_ids = []
    for check in reporting["systemChecks"]:
        check_id = check.get("checkId")
        _require(
            isinstance(check_id, str) and bool(check_id.strip()),
            "systemChecks.checkId must be non-empty",
        )
        check_id = check_id.strip()
        system_check_ids.append(check_id)
        _require(
            check.get("sourceStage") in {"CROSS_STAGE", "EXPORT_PREPARATION"},
            f"invalid system check sourceStage: {check_id}",
        )
    _require(
        len(system_check_ids) == len(set(system_check_ids)),
        "duplicate system checkId",
    )
    derived_check_ids = []
    derived_check_ids.extend(
        f"STAGE01.FIELD.{_sha16(value)}"
        for value in reporting["stage01FieldKeys"]
    )
    derived_check_ids.extend(
        f"STAGE01.TARGET.{value}" for value in planning_target_ids
    )
    derived_check_ids.extend(
        f"STAGE02A.ROLE.{value}" for value in semantic_role_id_list
    )
    for task_id in profiles[0]["taskIds"]:
        task = tasks[task_id]
        derived_check_ids.extend(
            f"STAGE02A.ATTRIBUTE.{task_id}.{_sha16(value)}"
            for value in task["attributeRequirements"]
        )
        derived_check_ids.extend(
            f"STAGE02A.GEOMETRY.{task_id}.{_sha16(value)}"
            for value in task["geometryChecks"]
        )
        derived_check_ids.extend(
            f"STAGE02A.PROPERTY.{task_id}.{_sha16(value)}"
            for value in task["propertyChecks"]
        )
        derived_check_ids.extend(
            f"STAGE03.TARGET.{task_id}.{value}"
            for value in task["targetComparisons"]
        )
    derived_check_ids.extend(
        f"STAGE02B.METRIC.{value}" for value in metric_property_ids
    )
    derived_check_ids.extend(system_check_ids)
    _require(
        len(derived_check_ids) == len(set(derived_check_ids)),
        "duplicate derived native reporting checkId",
    )
    return reporting

def merge_overlay(base_source, overlay):
    v042 = _load_v042_compiler()
    v042._validate_overlay(overlay, base_source)
    merged = v042.merge_overlay(base_source, overlay)
    merged["nativeReporting"] = build_native_reporting_catalog(merged, overlay)
    return merged

def compile_rulepack(source_path, baseline_path, overlay_path, output_path):
    v042 = _load_v042_compiler()
    base = v042._load_base_compiler()
    source = base.load_validated_rule_source(
        Path(source_path), Path(baseline_path)
    )
    overlay = _load_json(overlay_path)
    merged = merge_overlay(source, overlay)
    payload = base.build_rulepack_bytes(merged)
    output = Path(output_path)
    output.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=str(output.parent), prefix=f".{output.name}.", suffix=".tmp"
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, output)
    except BaseException:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass
        raise

def _parser():
    parser = argparse.ArgumentParser(
        description="Compile HBR v0.4.3 native total-plan reporting rule-pack"
    )
    parser.add_argument("--source", required=True)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--overlay", default=str(DEFAULT_OVERLAY))
    parser.add_argument("--output", required=True)
    return parser

def main(argv=None):
    args = _parser().parse_args(argv)
    try:
        compile_rulepack(
            args.source, args.baseline, args.overlay, args.output
        )
    except (KeyError, OSError, TypeError, UnicodeError, ValueError, RuntimeError) as error:
        print(
            f"HBR v0.4.3 rule-pack compilation failed: {error}",
            file=sys.stderr,
        )
        return 1
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
```

上述实现必须保持 fail-closed：生成后的全局 `checkId`、overlay `roleId`、02B `propertyId`、carrierId、evidenceId 均唯一；aliases 按 ordinal 排序去重；孤儿 carrier/evidence 被拒绝；`systemChecks.sourceStage` 只能是 `CROSS_STAGE/EXPORT_PREPARATION`；`evidenceStatus` 只能显式取 `VERIFIED/PENDING_GOLDEN_RVT/INTERNAL_ONLY`。只有逐 propertyId 的 `officialEvidenceRef` 能解析到完整结构记录（Golden RVT/manifest/DLL/IFC/IFCFlux 报告 SHA、两个工具版本、observed UniqueId/GlobalId/scope/GUID），且 `officialProjectionCarrierId` 同时解析到同 propertyId 的结构 carrier 时才允许对应 `officialExportVerified=true`。负向参数化测试逐项覆盖重复 role/metric、aliases 乱序/重复、非法状态、孤儿结构记录和派生 checkId 冲突；并直接 import wrapper 调用 `compile_rulepack(...)`，断言异常时临时文件被清理且旧输出不被截断。

- [ ] **Step 5: 切换构建入口并运行 PASS**

在 `BIMBaoGui.RevitAddin.csproj` 把 overlay/compiler 默认路径切到 v0.4.3；在 workflow 的 paths 中加入 overlay、compiler 和 `tests/test_hbr_rulepack_v043.py`，并把该测试加入 `Verify shared HBR rule database` 的 pytest 命令。运行：

```powershell
$rulePack = Join-Path $env:TEMP 'BIMBaoGui-v043-HBR_RulePack.hbrpack'
python tools/build_hbr_rulepack_v043.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --overlay specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --output $rulePack
python -m pytest tests/test_hbr_rulepack_v043.py tests/test_revit_addin_scaffold_contract.py -q
```

Expected: PASS；生成包 363 属性、15 carrier roles、1 个总平 reporting profile。

- [ ] **Step 6: 提交规则生成链**

```powershell
git add specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json `
  tools/build_hbr_rulepack_v043.py `
  src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  .github/workflows/build-revit-mcp.yml `
  tests/test_hbr_rulepack_v043.py `
  tests/test_revit_addin_scaffold_contract.py
git commit -m "build(rules): add v0.4.3 total-plan reporting catalog"
```

---

### Task 2: 投影运行时清单、任务和 02B 指标目录

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Rules/NativeReportingRuleCatalog.cs`
- Create: `src/BIMBaoGui.RevitAddin/Rules/NativeStage02BMetricCatalog.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeReportingRuleCatalogTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BMetricCatalogTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Rules/NativeRuleCatalog.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Rules/NativeStage02RuleCatalog.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeRuleCatalogTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RuleCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1 的嵌入 `nativeReporting` 和现有 363 属性/15 roles。
- Produces: `NativeReportingRuleCatalog.Current.GetChecks(...)`、`GetSemanticRoles(...)`、`GetCarrierPolicy(...)`；`NativeStage02BMetricCatalog.Current.MetricsFor("总平模型")`。

- [ ] **Step 1: 写目录 RED 测试**

```csharp
[Fact]
public void Total_plan_catalog_exposes_all_tasks_and_exact_manual_metrics()
{
  NativeReportingRuleCatalog reporting = NativeReportingRuleCatalog.Current;
  Assert.Equal(15, reporting.GetTaskIds("总平模型").Count);
  Assert.Equal(13, reporting.GetSemanticRoles("总平模型").Count);

  IReadOnlyList<NativeStage02BMetricDefinition> metrics =
    NativeStage02BMetricCatalog.Current.MetricsFor("总平模型");
  Assert.Equal(6, metrics.Count);
  Assert.Equal("ca21e324-046b-5bfd-84c8-0d3470082303", metrics[0].PropertyId);
  Assert.Equal("IfcProject|Pset_登记信息属性集|总建筑面积", metrics[0].Identity);
  Assert.All(metrics, value => Assert.Equal("MANUAL_INPUT", value.Source));
  Assert.All(metrics, value => Assert.False(value.OfficialExportVerified));

  IReadOnlyList<NativeReportingCheckDefinition> checks =
    reporting.GetChecks("总平模型");
  Assert.Equal(checks.Count, checks.Select(value => value.CheckId).Distinct().Count());
  Assert.Contains(checks, value =>
    value.CheckId == "STAGE02B.METRIC.ca21e324-046b-5bfd-84c8-0d3470082303" &&
    value.PropertyId == "ca21e324-046b-5bfd-84c8-0d3470082303");
  Assert.Contains(checks, value => value.CheckId == "CROSS.DOCUMENT_IDENTITY");
  Assert.Contains(checks, value => value.CheckId == "EXPORT.REPORT_WRITER");
}

[Fact]
public void Planning_targets_are_not_actual_metrics()
{
  string[] actual = NativeStage02BMetricCatalog.Current
    .MetricsFor("总平模型")
    .Select(value => value.PropertyId)
    .ToArray();
  Assert.DoesNotContain("c94f1ae2-0a02-5479-aae4-c8f59af71fe0", actual);
  Assert.DoesNotContain("35675fd2-c3d2-5553-8db6-855980a201a4", actual);
  Assert.DoesNotContain("5d5f3dba-3ae9-59c6-9aee-aa24e88f312c", actual);
}
```

- [ ] **Step 2: 运行测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeReportingRuleCatalogTests|FullyQualifiedName~NativeStage02BMetricCatalogTests"
```

Expected: FAIL，因为运行时 reporting 类型不存在。

- [ ] **Step 3: 扩展基础 profile/task DTO**

在 `NativeRuleCatalog.cs` 中把 profile/task 完整投影：

```csharp
internal sealed class NativeModelProfile
{
  internal string ProfileId { get; set; } = string.Empty;
  internal IReadOnlyList<string> TaskIds { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> ActivationRuleIds { get; set; } = Array.Empty<string>();
}

internal sealed class NativeTaskDefinition
{
  internal string TaskId { get; set; } = string.Empty;
  internal string ModelFileType { get; set; } = string.Empty;
  internal string Name { get; set; } = string.Empty;
  internal string ObjectCode { get; set; } = string.Empty;
  internal string Requirement { get; set; } = string.Empty;
  internal string ConditionId { get; set; } = string.Empty;
  internal int Sequence { get; set; }
  internal bool SkeletonTask { get; set; }
  internal IReadOnlyList<string> AttributeRequirements { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> GeometryChecks { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> PropertyChecks { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> TargetComparisons { get; set; } = Array.Empty<string>();
  internal string Source { get; set; } = string.Empty;
}
```

公开 `Tasks`、`TasksById`，构造时拒绝重复 taskId、profile 引用不存在和跨模型 task 引用。

- [ ] **Step 4: 实现 reporting 与 metric 目录**

使用以下稳定签名：

```csharp
internal enum NativeReportingSourceStage
{
  Unknown, Stage01, Stage02A, Stage02B, CrossStage, ExportPreparation
}

internal enum NativeReportingCheckKind
{
  Unknown, Stage01Field, PlanningTarget, SemanticRole,
  AttributeRequirement, Geometry, PropertyConsistency, TargetComparison,
  Stage02BMetric, System
}

internal enum NativeOfficialCarrierEvidenceStatus
{
  Unknown, Verified, PendingGoldenRvt, InternalOnly
}

internal sealed class NativeReportingCheckDefinition
{
  internal string CheckId { get; set; } = string.Empty;
  internal string ModelFileType { get; set; } = string.Empty;
  internal int Sequence { get; set; }
  internal string DisplayName { get; set; } = string.Empty;
  internal NativeReportingSourceStage SourceStage { get; set; }
  internal NativeReportingCheckKind CheckKind { get; set; }
  internal string ApplicableBasis { get; set; } = string.Empty;
  internal string ConditionId { get; set; } = string.Empty;
  internal string TaskId { get; set; } = string.Empty;
  internal string FieldKey { get; set; } = string.Empty;
  internal string PropertyId { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal string RuleText { get; set; } = string.Empty;
  internal string TargetKey { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal string RemediationTarget { get; set; } = string.Empty;
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
  internal string OfficialEvidenceRef { get; set; } = string.Empty;
}

internal sealed class NativeReportingSemanticRole
{
  internal string RoleId { get; set; } = string.Empty;
  internal string TaskId { get; set; } = string.Empty;
  internal string DisplayName { get; set; } = string.Empty;
  internal IReadOnlyList<string> CandidateAliases { get; set; } = Array.Empty<string>();
  internal string LinkedCarrierRoleId { get; set; } = string.Empty;
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
}

internal sealed class NativeOfficialCarrierPolicy
{
  internal string IfcEntity { get; set; } = string.Empty;
  internal string InternalCarrier { get; set; } = string.Empty;
  internal string ProjectionPolicy { get; set; } = string.Empty;
  internal bool OfficialExportVerified { get; set; }
  internal NativeOfficialCarrierEvidenceStatus EvidenceStatus { get; set; }
  internal IReadOnlyList<string> EvidenceRefs { get; set; } = Array.Empty<string>();
}

internal sealed class NativeOfficialProjectionCarrierDefinition
{
  internal string CarrierId { get; set; } = string.Empty;
  internal string PropertyId { get; set; } = string.Empty;
  internal string SelectorKind { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal string CategoryBuiltInId { get; set; } = string.Empty;
  internal string ElementClass { get; set; } = string.Empty;
  internal string BindingScope { get; set; } = string.Empty;
  internal string ParameterGuid { get; set; } = string.Empty;
}

internal sealed class NativeOfficialEvidenceRecord
{
  internal string EvidenceId { get; set; } = string.Empty;
  internal string PropertyId { get; set; } = string.Empty;
  internal string GoldenRvtSha256 { get; set; } = string.Empty;
  internal string HifctoolManifestSha256 { get; set; } = string.Empty;
  internal string HifctoolDllSha256 { get; set; } = string.Empty;
  internal string HifctoolProductVersion { get; set; } = string.Empty;
  internal string OfficialIfcSha256 { get; set; } = string.Empty;
  internal string IfcFluxProductVersion { get; set; } = string.Empty;
  internal string IfcFluxReportSha256 { get; set; } = string.Empty;
  internal string ObservedRevitUniqueId { get; set; } = string.Empty;
  internal string ObservedIfcGlobalId { get; set; } = string.Empty;
  internal string ObservedBindingScope { get; set; } = string.Empty;
  internal string ObservedParameterGuid { get; set; } = string.Empty;
}

internal sealed class NativeStage02BMetricDefinition
{
  internal string PropertyId { get; set; } = string.Empty;
  internal string Identity { get; set; } = string.Empty;
  internal int Sequence { get; set; }
  internal string Source { get; set; } = string.Empty;
  internal NativeStage02PropertyDefinition Property { get; set; }
  internal bool OfficialExportVerified { get; set; }
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
  internal string OfficialEvidenceRef { get; set; } = string.Empty;
}

internal sealed class NativeReportingRuleCatalog
{
  internal static NativeReportingRuleCatalog Current { get; }
  internal IReadOnlyList<string> GetTaskIds(string modelFileType);
  internal IReadOnlyList<NativeReportingCheckDefinition> GetChecks(string modelFileType);
  internal IReadOnlyList<NativeReportingSemanticRole> GetSemanticRoles(string modelFileType);
  internal NativeOfficialCarrierPolicy GetCarrierPolicy(string ifcEntity);
  internal NativeOfficialProjectionCarrierDefinition GetProjectionCarrier(string carrierId);
  internal NativeOfficialEvidenceRecord GetOfficialEvidence(string evidenceId);
}

internal sealed class NativeStage02BMetricCatalog
{
  internal static NativeStage02BMetricCatalog Current { get; }
  internal IReadOnlyList<NativeStage02BMetricDefinition> MetricsFor(string modelFileType);
}
```

`NativeReportingRuleCatalog` 从 `stage01FieldKeys`、`planningTargetPropertyIds`、`semanticRoles`、`stage02BMetrics`、profile task/condition、每个 task 的 `GeometryChecks/PropertyChecks/TargetComparisons` 和 `systemChecks` 确定性投影 `GetChecks`；不得在 UI 重新维护一份字段表，也不得把 task 的几何规则吞并成一个可以凭角色确认直接变绿的总项。

派生 checkId 固定为：

```text
Stage01 字段       STAGE01.FIELD.<sha16(fieldKey)>
规划目标输入       STAGE01.TARGET.<propertyId>
02A 语义角色       STAGE02A.ROLE.<roleId>
02A 属性要求       STAGE02A.ATTRIBUTE.<taskId>.<sha16(ruleText)>
02A 几何规则       STAGE02A.GEOMETRY.<taskId>.<sha16(ruleText)>
02A 属性一致性     STAGE02A.PROPERTY.<taskId>.<sha16(ruleText)>
目标/实际比较      STAGE03.TARGET.<taskId>.<targetKey>
02B 实际指标       STAGE02B.METRIC.<propertyId>
跨阶段/导出准备    直接使用 systemChecks.checkId
```

`sha16` 是 UTF-8 SHA-256 的前 16 个小写十六进制字符。Sequence 固定分段为：Stage01 字段 `10000 + index*10`、规划目标 `20000 + index*10`、02A 角色 `30000 + index*10`、属性要求 `35000 + taskIndex*100 + ruleIndex`、几何规则 `45000 + taskIndex*100 + ruleIndex`、属性一致性 `55000 + taskIndex*100 + ruleIndex`、目标/实际比较 `65000 + taskIndex*100 + ruleIndex`、02B 指标 `75000 + metric.sequence`、systemChecks 使用 overlay 的 90010–91040。属性要求通过 task→semantic role→linked carrier role→property 的 exact IFC property 名连接；无 mapping 时仍生成检查并返回 `ATTRIBUTE_MAPPING_MISSING`，不得靠中文近似词猜测。`SITE.SKELETON` 的坐标/高程/真北属性和几何规则使用显式 Stage01 fieldKey mapping；其他几何和属性一致性规则读取 Task 5 的构件几何证据。目标比较只允许使用明确映射的 `planning.building_density → IfcSite/Pset_场地信息属性集/建筑密度`、`planning.floor_area_ratio → .../容积率`、`planning.green_rate → .../绿地率`；其他 targetKey 若无实际值 identity，生成红色 `TARGET_COMPARISON_MAPPING_MISSING`，不得猜测。

Stage01 内部字段的 carrier 状态为 `InternalOnly`；规则属性和 02A/02B owner 使用其显式官方证据，未证实即 `PendingGoldenRvt`。最终按 `Sequence`、`CheckId` ordinal 排序并拒绝重复。`officialProjectionCarriers` 与 `officialEvidenceRecords` 是两个按 ID 唯一索引的结构目录，pending 初始包中均为空；metric 只存 `OfficialProjectionCarrierId/OfficialEvidenceRef` 外键。只有 Verified metric 的两个外键都存在、各自 `PropertyId` 与 metric 相同、carrier 的 `ParameterGuid == PropertyId` 且 evidence 的五个 SHA/两个工具版本/observed UniqueId+GlobalId+scope+GUID 全部有效时才加载成功；pending metric 两个外键必须都为空。`NativeStage02BMetricCatalog` 连接时重新计算完整 identity 并与 overlay 比较；任何缺失/不一致直接 `InvalidDataException`。JSON 的 `VERIFIED/PENDING_GOLDEN_RVT/INTERNAL_ONLY` 必须显式映射到 enum，未知值 fail-closed。目录测试逐项断言 15 个 task 的所有 attribute/geometry/property/target 文本均恰好投影一次，并覆盖断链、跨 propertyId 引用和同 entity 连带升级均 fail-closed。

- [ ] **Step 5: 投影官方属性证据并运行回归**

在 `NativeStage02PropertyDefinition` 增加：

```csharp
internal string OfficialPropertyEvidenceStatus { get; set; } = string.Empty;
internal bool OfficialExportVerified { get; set; }
internal string OfficialCarrierCandidate { get; set; } = string.Empty;
```

`OFFICIAL_EXTRACTED` 只表示字段 identity 被提取，不能把 `OfficialExportVerified` 改为 true。运行：

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeRuleCatalogTests|FullyQualifiedName~NativeStage02RuleCatalogTests|FullyQualifiedName~NativeReportingRuleCatalogTests|FullyQualifiedName~NativeStage02BMetricCatalogTests"
```

Expected: PASS；旧 363/15 合同保持不变。

- [ ] **Step 6: 提交运行时目录**

```powershell
git add src/BIMBaoGui.RevitAddin/Rules `
  tests/BIMBaoGui.RevitAddin.Tests/NativeRuleCatalogTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RuleCatalogTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeReportingRuleCatalogTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BMetricCatalogTests.cs
git commit -m "feat(rules): project total-plan runtime contracts"
```

---

### Task 3: 建立跨阶段身份、结果、hash 和 RVT 存储合同

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Workflow/NativeWorkflowIdentity.cs`
- Create: `src/BIMBaoGui.RevitAddin/Workflow/NativeWorkflowResultModels.cs`
- Create: `src/BIMBaoGui.RevitAddin/Workflow/NativeWorkflowResultCanonicalizer.cs`
- Create: `src/BIMBaoGui.RevitAddin/Workflow/NativeWorkflowResultStorage.cs`
- Create: `src/BIMBaoGui.RevitAddin/Workflow/NativeWorkflowFreshnessPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeIssueModels.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeIssueCanonicalizer.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowIdentityTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowResultCanonicalizerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowFreshnessPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeIssueCanonicalizerTests.cs`
- Create: `tests/test_revit_addin_workflow_result_contract.py`

**Interfaces:**
- Consumes: `RulePackageIdentity`、Stage01 FileGuid/payload hash 和当前 Revit 文档信息。
- Produces: `NativeWorkflowIdentityFactory.Create(...)`、`NativeWorkflowResultCanonicalizer.Build(...)`、`NativeWorkflowResultStorage.Read/Write(...)`、`NativeWorkflowFreshnessPolicy.Evaluate(...)`，以及供 Task 5 起使用的 `NativeIssueRecord`/稳定 `IssueId`。

- [ ] **Step 1: 写 canonical/hash/freshness RED 测试**

```csharp
[Fact]
public void Canonical_result_is_order_independent_and_hash_bound()
{
  NativeWorkflowIdentity identity = TestIdentity("TOTAL_PLAN");
  NativeWorkflowResultEnvelope a = Build(identity, new[] { Item("B"), Item("A") });
  NativeWorkflowResultEnvelope b = Build(identity, new[] { Item("A"), Item("B") });
  Assert.Equal(a.CanonicalJson, b.CanonicalJson);
  Assert.Equal(a.ResultHash, b.ResultHash);
  Assert.Matches("^[0-9a-f]{64}$", a.ResultHash);
}

[Fact]
public void Cross_document_rule_or_input_hash_is_rejected()
{
  NativeWorkflowResultEnvelope result = Build(TestIdentity("TOTAL_PLAN"), new[] { Item("A") });
  Assert.Equal(NativeWorkflowFreshnessState.Current,
    NativeWorkflowFreshnessPolicy.Evaluate(result, TestIdentity("TOTAL_PLAN"), result.InputSnapshotHash).State);
  Assert.Equal(NativeWorkflowFreshnessState.DocumentMismatch,
    NativeWorkflowFreshnessPolicy.Evaluate(result, TestIdentity("OTHER_DOCUMENT"), result.InputSnapshotHash).State);
  Assert.Equal(NativeWorkflowFreshnessState.InputStale,
    NativeWorkflowFreshnessPolicy.Evaluate(result, TestIdentity("TOTAL_PLAN"), new string('0', 64)).State);
}

[Fact]
public void Issue_id_uses_field_identity_but_not_message_text()
{
  NativeIssueRecord first = Issue("MISSING_FIELD", "旧文案", "HBR|FileIdentity|FileGuid");
  NativeIssueRecord second = Issue("MISSING_FIELD", "新文案", "HBR|FileIdentity|FileGuid");
  NativeIssueRecord other = Issue("MISSING_FIELD", "旧文案", "HBR|FileIdentity|ModelFileType");
  Assert.Equal(NativeIssueCanonicalizer.ComputeId(first),
    NativeIssueCanonicalizer.ComputeId(second));
  Assert.NotEqual(NativeIssueCanonicalizer.ComputeId(first),
    NativeIssueCanonicalizer.ComputeId(other));
}

private static NativeWorkflowIdentity TestIdentity(string document)
{
  return new NativeWorkflowIdentity
  {
    DocumentFingerprint = document,
    ModelFileType = "总平模型",
    RulePackageId = "HBR-WUHAN-PLANNING",
    RulePackageVersion = "1.0.0",
    RulePackageSha256 = new string('a', 64)
  };
}

private static NativeWorkflowItemEvidence Item(string identity)
{
  return new NativeWorkflowItemEvidence
  {
    Identity = identity,
    CurrentValue = identity + "-value",
    Source = "TEST",
    WriteSucceeded = true,
    ReadbackSucceeded = true,
    InputHash = new string('c', 64),
    UpdatedUtc = "2026-08-14T00:00:00.0000000Z"
  };
}

private static NativeWorkflowResultEnvelope Build(
  NativeWorkflowIdentity identity,
  IEnumerable<NativeWorkflowItemEvidence> items)
{
  return NativeWorkflowResultCanonicalizer.Build(
    "run-1", "TEST", "TEST_FUNCTION", identity, new string('b', 64),
    items, "2026-08-14T00:00:00.0000000Z");
}

private static NativeIssueRecord Issue(
  string code,
  string message,
  string fieldKey)
{
  return new NativeIssueRecord
  {
    DocumentFingerprint = "TOTAL_PLAN",
    Severity = NativeIssueSeverity.Blocker,
    SourceFeature = "STAGE01",
    CheckId = "STAGE01.FIELD",
    Code = code,
    Missing = message,
    FieldKey = fieldKey,
    Route = NativeIssueNavigationAction.OpenStage01
  };
}
```

测试文件显式加入 `using System; using System.Collections.Generic;`；helper 就定义在各自测试类内，以上代码可独立编译，不引用计划外夹具。

- [ ] **Step 2: 运行测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeWorkflow|FullyQualifiedName~NativeIssueCanonicalizerTests"
```

Expected: FAIL，因为公共 workflow 类型不存在。

- [ ] **Step 3: 实现不可变身份和结果模型**

使用以下字段名，后续任务不得改名：

```csharp
internal sealed class NativeWorkflowIdentity
{
  internal string DocumentFingerprint { get; set; } = string.Empty;
  internal string ModelFileType { get; set; } = string.Empty;
  internal string RulePackageId { get; set; } = string.Empty;
  internal string RulePackageVersion { get; set; } = string.Empty;
  internal string RulePackageSha256 { get; set; } = string.Empty;
}

internal sealed class NativeWorkflowItemEvidence
{
  internal string Identity { get; set; } = string.Empty;
  internal string CurrentValue { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal string Source { get; set; } = string.Empty;
  internal bool WriteSucceeded { get; set; }
  internal bool ReadbackSucceeded { get; set; }
  internal string InputHash { get; set; } = string.Empty;
  internal string UpdatedUtc { get; set; } = string.Empty;
  internal string StableHash { get; set; } = string.Empty;
  internal string ErrorCode { get; set; } = string.Empty;
}

internal sealed class NativeWorkflowResultEnvelope
{
  internal string SchemaVersion { get; set; } = "HBR_NATIVE_WORKFLOW_RESULT_V1";
  internal string RunId { get; set; } = string.Empty;
  internal string SourceFeature { get; set; } = string.Empty;
  internal string SourceFunction { get; set; } = string.Empty;
  internal NativeWorkflowIdentity Identity { get; set; }
  internal string InputSnapshotHash { get; set; } = string.Empty;
  internal string UpdatedUtc { get; set; } = string.Empty;
  internal IReadOnlyList<NativeWorkflowItemEvidence> Items { get; set; } = Array.Empty<NativeWorkflowItemEvidence>();
  internal string CanonicalJson { get; set; } = string.Empty;
  internal string ResultHash { get; set; } = string.Empty;
}

internal enum NativeIssueSeverity { Blocker, Warning }

internal enum NativeIssueNavigationAction
{
  None, Select, Zoom, Isolate, RestoreView,
  OpenStage01, OpenStage02A, OpenStage02B, StayStage03
}

internal sealed class NativeIssueElementReference
{
  internal int ElementId { get; set; }
  internal string UniqueId { get; set; } = string.Empty;
  internal string ElementName { get; set; } = string.Empty;
  internal string CategoryName { get; set; } = string.Empty;
}

internal sealed class NativeIssueRecord
{
  internal string IssueId { get; set; } = string.Empty;
  internal string DocumentFingerprint { get; set; } = string.Empty;
  internal NativeIssueSeverity Severity { get; set; }
  internal string SourceFeature { get; set; } = string.Empty;
  internal string CheckId { get; set; } = string.Empty;
  internal string Code { get; set; } = string.Empty;
  internal string Missing { get; set; } = string.Empty;
  internal string Impact { get; set; } = string.Empty;
  internal string Remediation { get; set; } = string.Empty;
  internal string FieldKey { get; set; } = string.Empty;
  internal string PropertyId { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
    Array.Empty<NativeIssueElementReference>();
  internal NativeIssueNavigationAction Route { get; set; }
}

internal static class NativeIssueCanonicalizer
{
  internal static string ComputeId(NativeIssueRecord issue);
}
```

`NativeIssueCanonicalizer.ComputeId` 的 UTF-8 hash 输入固定为 `documentFingerprint|sourceFeature|checkId|code|fieldKey|propertyId|roleId|sorted uniqueIds`，输出 lowercase SHA-256；`Missing/Impact/Remediation`、显示名称、类别名称和 ElementId 不进入 identity。空文档指纹或空 `UniqueId` 的构件引用在 canonicalize 前拒绝，避免跨文档碰撞或复用会变化的 ElementId 作为 issue 身份。

- [ ] **Step 4: 提取统一文档指纹并实现 canonicalizer**

把 `NativeStage02RevitService.ComputeDocumentFingerprint` 提取为：

```csharp
internal static class NativeWorkflowIdentityFactory
{
  internal static NativeWorkflowIdentity Create(
    UIApplication application,
    string modelFileType,
    string stage01FileGuid,
    string stage01PayloadHash,
    RulePackageIdentity rulePackage);

  internal static string ComputeDocumentFingerprint(
    string documentPath,
    string documentTitle,
    string revitVersion,
    string stage01FileGuid,
    string stage01PayloadHash);
}

internal static class NativeWorkflowResultCanonicalizer
{
  internal static NativeWorkflowResultEnvelope Build(
    string runId,
    string sourceFeature,
    string sourceFunction,
    NativeWorkflowIdentity identity,
    string inputSnapshotHash,
    IEnumerable<NativeWorkflowItemEvidence> items,
    string updatedUtc);
}
```

`Create` 只从当前 `UIApplication.ActiveUIDocument.Document`、Stage01 已回读值和嵌入规则身份构建身份，拒绝空模型类型/FileGuid/payload hash。原始连接顺序保持 `path|title|revitVersion|fileGuid|payloadHash`。Canonical JSON 中 items 按 `Identity` ordinal 排序，时间使用 UTC ISO-8601 `O` 格式，所有 hash 为 lowercase SHA-256；hash 输入不含 `CanonicalJson`/`ResultHash` 自身。

- [ ] **Step 5: 实现 RVT 存储与 freshness policy**

`NativeWorkflowResultStorage` 使用固定 schema GUID `9f1de04a-406b-4c15-b693-1f3b7f1ea043`、schema 名 `HBR_NATIVE_WORKFLOW_RESULTS_V1`、DataStorage 名 `HBR Native Workflow Results`，保存三个独立字符串字段：`Stage01Json`、`Stage02AJson`、`Stage02BJson`。

```csharp
internal enum NativeWorkflowFreshnessState
{
  Unknown, Current, SchemaMismatch, ResultHashMismatch,
  DocumentMismatch, ModelTypeMismatch, RulePackageMismatch, InputStale
}

internal sealed class NativeWorkflowFreshnessDecision
{
  internal NativeWorkflowFreshnessState State { get; set; }
  internal string Code { get; set; } = string.Empty;
}

internal static class NativeWorkflowResultStorage
{
  internal static NativeWorkflowResultEnvelope Read(Document document, string sourceFeature);
  internal static void Write(Document document, NativeWorkflowResultEnvelope envelope);
}

internal static class NativeWorkflowFreshnessPolicy
{
  internal static NativeWorkflowFreshnessDecision Evaluate(
    NativeWorkflowResultEnvelope result,
    NativeWorkflowIdentity currentIdentity,
    string currentInputSnapshotHash);
}
```

`Write` 要求调用方已有 Revit transaction；只更新对应 source 字段，不清除其他阶段。freshness 顺序固定为：schema → result hash → document → model type → package id/version/SHA → input snapshot。

- [ ] **Step 6: 运行领域测试和静态合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeWorkflow|FullyQualifiedName~NativeIssueCanonicalizerTests"
python -m pytest tests/test_revit_addin_workflow_result_contract.py -q
```

Expected: PASS；静态合同确认固定 GUID、三个来源字段和无静态全局文档缓存。

- [ ] **Step 7: 提交公共结果合同**

```powershell
git add src/BIMBaoGui.RevitAddin/Workflow `
  src/BIMBaoGui.RevitAddin/Issues/NativeIssueModels.cs `
  src/BIMBaoGui.RevitAddin/Issues/NativeIssueCanonicalizer.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitService.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowIdentityTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowResultCanonicalizerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeWorkflowFreshnessPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeIssueCanonicalizerTests.cs `
  tests/test_revit_addin_workflow_result_contract.py
git commit -m "feat(workflow): add cross-stage identity and result contracts"
```

---

### Task 4: 完成 Stage01 总平输入、坐标回读和字段级状态

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01GeoLocationPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01FieldPresentationPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01FieldOutcome.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01GeoLocationPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01FieldPresentationPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01TotalPlanFieldTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01LiveEvidence.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitReadService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01RevitService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ParameterProjectionService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01ViewModel.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01Validator.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ValidatorTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs`
- Modify: `tests/test_revit_addin_stage01_revit_contract.py`
- Modify: `tests/test_revit_addin_stage01_ui_contract.py`

**Interfaces:**
- Consumes: Task 2 的 Stage01 字段定义、Task 3 的 workflow identity/result；保持 `NativeStage01Canonicalizer.PayloadSchemaVersion = "0.9.1"`。
- Produces: Stage01 登记/坐标/规划目标的字段卡片和 `NativeStage01WriteResult.FieldOutcomes`、`WorkflowResult`；总建筑面积只产生 `STAGE02B_REFERENCE`。

- [ ] **Step 1: 写字段归属和坐标 RED 测试**

```csharp
[Fact]
public void Total_building_area_is_a_stage02b_reference_not_an_editor()
{
  NativeStage01FieldDefinition field = NativeRuleCatalog.Current.Stage01FieldsByKey[
    "IfcProject|Pset_登记信息属性集|总建筑面积"];
  NativeStage01FieldPresentation card = NativeStage01FieldPresentationPolicy.Build(
    field, new NativeStage01Model(), new NativeStage01LiveEvidence(),
    new Dictionary<string, NativeStage01FieldOutcome>(), null);
  Assert.True(card.ReadOnly);
  Assert.Equal("STAGE02B_REFERENCE", card.Source);
  Assert.Equal("02B", card.NavigationTarget);
}

[Fact]
public void Planning_and_actual_ratio_identities_remain_distinct()
{
  Assert.NotEqual(
    "IfcProject|Pset_项目控制指标信息属性集|容积率",
    NativeStage02BMetricCatalog.Current.MetricsFor("总平模型")[2].Identity);
}

[Theory]
[InlineData("114.300000", "30.600000")]
[InlineData("-180", "-90")]
[InlineData("180", "90")]
public void Longitude_and_latitude_round_trip_numerically(
  string longitude,
  string latitude)
{
  NativeGeoLocationValue value = NativeStage01GeoLocationPolicy.Parse(longitude, latitude);
  Assert.Equal(
    double.Parse(longitude, CultureInfo.InvariantCulture),
    NativeStage01GeoLocationPolicy.RadiansToDegrees(value.LongitudeRadians),
    10);
  Assert.Equal(
    double.Parse(latitude, CultureInfo.InvariantCulture),
    NativeStage01GeoLocationPolicy.RadiansToDegrees(value.LatitudeRadians),
    10);
}

[Theory]
[InlineData(114.3, "114.3")]
[InlineData(0.0, "0")]
[InlineData(-90.0, "-90")]
public void Degree_format_is_canonical_not_raw_text_preserving(
  double degrees,
  string expected)
{
  Assert.Equal(
    expected,
    NativeStage01GeoLocationPolicy.FormatDegrees(
      NativeStage01GeoLocationPolicy.DegreesToRadians(degrees)));
}
```

- [ ] **Step 2: 运行测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage01GeoLocationPolicyTests|FullyQualifiedName~NativeStage01FieldPresentationPolicyTests|FullyQualifiedName~NativeStage01TotalPlanFieldTests"
```

Expected: FAIL，因为字段 presentation、经纬度 policy 和字段结果不存在。

- [ ] **Step 3: 实现字段结果和呈现合同**

```csharp
internal enum NativeStage01FieldOperationState
{
  NotAttempted, Succeeded, Failed, Blocked
}

internal sealed class NativeStage01FieldOutcome
{
  internal string FieldKey { get; set; } = string.Empty;
  internal string Identity { get; set; } = string.Empty;
  internal string CurrentValue { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal string Source { get; set; } = string.Empty;
  internal NativeStage01FieldOperationState WriteState { get; set; }
  internal NativeStage01FieldOperationState ReadbackState { get; set; }
  internal string ErrorCode { get; set; } = string.Empty;
  internal string Message { get; set; } = string.Empty;
}

internal sealed class NativeStage01FieldPresentation
{
  internal string FieldKey { get; set; } = string.Empty;
  internal string Identity { get; set; } = string.Empty;
  internal string Label { get; set; } = string.Empty;
  internal string CurrentValue { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal string Source { get; set; } = string.Empty;
  internal bool InCurrentChecklist { get; set; }
  internal bool ReadOnly { get; set; }
  internal NativeStage01FieldOperationState WriteState { get; set; }
  internal NativeStage01FieldOperationState ReadbackState { get; set; }
  internal string IssueCode { get; set; } = string.Empty;
  internal string IssueMessage { get; set; } = string.Empty;
  internal string NavigationTarget { get; set; } = string.Empty;
}

internal static class NativeStage01FieldPresentationPolicy
{
  internal static NativeStage01FieldPresentation Build(
    NativeStage01FieldDefinition field,
    NativeStage01Model model,
    NativeStage01LiveEvidence live,
    IReadOnlyDictionary<string, NativeStage01FieldOutcome> outcomes,
    NativeWorkflowResultEnvelope stage02BResult);
}
```

`Build` 的优先级固定为：从 `stage02BResult.Items` 按完整总建筑面积 identity 取得的 02B 当前引用 → Revit live evidence → 本次 outcome → 人工模型值。Task 4 不引用尚未创建的 02B 专用类型；空 envelope 或对应 item 非成功回读时显示未完成。字段卡片必须公开 identity、单位、来源、是否进入总平清单、写入状态、回读状态、问题和跳转目标。

- [ ] **Step 4: 增加经纬度 Revit 读写和回读**

在 `NativeStage01LiveEvidence` 增加 `Longitude`、`Latitude`。Policy 使用：

```csharp
internal sealed class NativeGeoLocationValue
{
  internal double LongitudeRadians { get; set; }
  internal double LatitudeRadians { get; set; }
}

internal static class NativeStage01GeoLocationPolicy
{
  internal static NativeGeoLocationValue Parse(
    string longitudeDegrees,
    string latitudeDegrees);
  internal static double DegreesToRadians(double degrees);
  internal static double RadiansToDegrees(double radians);
  internal static string FormatDegrees(double radians);
}
```

经度范围 `[-180, 180]`、纬度范围 `[-90, 90]`，拒绝 NaN/Infinity/空值。`FormatDegrees` 对换算结果先按 12 位小数舍入，再以 invariant `0.############` 输出；它不承诺恢复用户输入的尾随零。测试文件显式 `using System.Globalization;`。Revit 服务写入并回读：

```csharp
SiteLocation site = document.SiteLocation;
site.Longitude = geo.LongitudeRadians;
site.Latitude = geo.LatitudeRadians;
document.Regenerate();
double longitudeReadback = site.Longitude;
double latitudeReadback = site.Latitude;
```

角度误差上限 `1e-10` radians；失败时本次 Stage01 transaction group 回滚并返回字段 identity 对应错误码，不写零值。

- [ ] **Step 5: 生成 Stage01 workflow result 并保持 payload 兼容**

在 `NativeStage01WriteResult` 增加：

```csharp
internal IReadOnlyList<NativeStage01FieldOutcome> FieldOutcomes { get; set; } =
  Array.Empty<NativeStage01FieldOutcome>();
internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
```

成功提交前把所有字段 outcome 投影为 `NativeWorkflowItemEvidence`，`SourceFeature="STAGE01"`、`SourceFunction="PROJECT_INPUT"`，`InputSnapshotHash` 使用 Stage01 canonical payload SHA。调用 `NativeWorkflowResultStorage.Write` 必须位于同一成功事务组；测试明确断言 Payload 仍为 `0.9.1`，读取动作不自动迁移或改写存储。

- [ ] **Step 6: 改造 Stage01 WPF 分组和状态卡片**

`NativeStage01View` 固定显示：

```text
项目登记信息
项目位置与坐标
规划目标与限值
其他项目输入
```

规划目标卡片使用现有 `NativePlanningTargetValue`，标题追加“规划目标/限值”；实际指标不得出现在此编辑器。`总建筑面积`卡片只读，按钮文字为“转到 02B 填写”。

- [ ] **Step 7: 运行 Stage01 领域和静态合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage01"
python -m pytest tests/test_revit_addin_stage01_revit_contract.py `
  tests/test_revit_addin_stage01_ui_contract.py -q
```

Expected: PASS；目标/实际 identity 不混用，经纬度与项目位置均有回读状态。

- [ ] **Step 8: 提交 Stage01 扩展**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage01 `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01GeoLocationPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01FieldPresentationPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01TotalPlanFieldTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ValidatorTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage01ViewModelTests.cs `
  tests/test_revit_addin_stage01_revit_contract.py `
  tests/test_revit_addin_stage01_ui_contract.py
git commit -m "feat(stage01): complete total-plan project inputs"
```

---

### Task 5: 将现有 Stage02 收敛为 02A 候选确认与结构化结果

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02ElementSnapshotCanonicalizer.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02GeometryEvidence.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitGeometryEvidenceService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02CandidatePolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RoleConfirmationPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02IssueCompiler.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ElementSnapshotCanonicalizerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02GeometryEvidencePolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02CandidatePolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RoleConfirmationPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02AIssuePolicyTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02Inventory.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02SemanticAssignmentModels.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02SemanticAssignmentCanonicalizer.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02SemanticAssignmentCodec.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02SemanticAssignmentStoragePolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewModels.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02PreviewCompiler.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02RevitWriteService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02WorkbenchRequestPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02PreviewCompilerTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualPreviewCompilerTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02WorkbenchRequestPolicyTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SemanticValueSuggestionPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage02_revit_contract.py`

**Interfaces:**
- Consumes: Task 2 的 13 个总平 semantic roles、现有自动 matcher/人工 assignment、Task 3 workflow result。
- Produces: `HBR_NATIVE_STAGE02A_PREVIEW_V3`、显式 `NativeStage02RoleConfirmation`、构件级位置/边界框/可靠投影面积/逐规则几何证据、构件/字段 outcomes 和 `STAGE02A/ELEMENT_PREPARATION` result envelope；不产生任何项目级汇总指标。

- [ ] **Step 1: 写“候选不能直接写入”RED 测试**

```csharp
[Fact]
public void Automatic_candidate_without_confirmation_is_blocked()
{
  var element = new NativeStage02ElementSnapshot
  {
    DocumentFingerprint = "doc", UniqueId = "A", ElementId = 1,
    Category = "OST_BuildingPad", ElementKind = "BuildingPad",
    IsModelElement = true
  };
  var candidate = new NativeStage02SemanticCandidate
  {
    RoleId = "SITE_GREEN_OBJECT", Confidence = "HIGH"
  };
  var identity = new NativeWorkflowIdentity
  {
    DocumentFingerprint = "doc", ModelFileType = "总平模型",
    RulePackageId = "HBR-WUHAN-PLANNING", RulePackageVersion = "1.0.0",
    RulePackageSha256 = new string('a', 64)
  };
  NativeStage02RoleConfirmationDecision decision =
    NativeStage02RoleConfirmationPolicy.Resolve(
      element, candidate, null, null, identity, "snapshot-hash");
  Assert.False(decision.Confirmed);
  Assert.Equal("ROLE_CONFIRMATION_REQUIRED", decision.Code);
}

[Fact]
public void Confirmation_is_rejected_after_element_or_rule_change()
{
  var element = new NativeStage02ElementSnapshot
  {
    DocumentFingerprint = "doc", UniqueId = "A", ElementId = 1,
    Category = "OST_BuildingPad", ElementKind = "BuildingPad",
    IsModelElement = true
  };
  var candidate = new NativeStage02SemanticCandidate
  {
    RoleId = "SITE_GREEN_OBJECT", Confidence = "HIGH"
  };
  var identity = new NativeWorkflowIdentity
  {
    DocumentFingerprint = "doc", ModelFileType = "总平模型",
    RulePackageId = "HBR-WUHAN-PLANNING", RulePackageVersion = "1.0.0",
    RulePackageSha256 = new string('a', 64)
  };
  NativeStage02RoleConfirmation confirmation = new NativeStage02RoleConfirmation
  {
    ElementUniqueId = "A",
    RoleId = "SITE_GREEN_OBJECT",
    ElementSnapshotHash = "old",
    RulePackageSha256 = new string('a', 64)
  };
  NativeStage02RoleConfirmationDecision decision =
    NativeStage02RoleConfirmationPolicy.Resolve(
      element, candidate, null, confirmation, identity, "new");
  Assert.False(decision.Confirmed);
  Assert.Equal("ROLE_CONFIRMATION_STALE", decision.Code);
}
```

另加测试：全模型和自主选择都要求确认；确认写入 request 后刷新预览仍能解析；保存确认后再次扫描不会因 `AssignedRoleId` 自行过期；preview→write 以同一 request 重建时 `PreviewHash` 相同；自主选择不产生任何 02B metric；一个构件失败不改变其他构件 outcome；v0.4.2 绿地字段仍为 4 个。

同一步在 `NativeStage02GeometryEvidencePolicyTests.cs` 写完整对象初始化，不使用隐藏 helper，覆盖：可靠 BuildingPad 面积能成为 `APPROVED_REVIT_AREA` 建议；零/NaN/缺失面积阻断；边界框不能冒充投影面积；不支持的 task 几何规则返回 `GEOMETRY_CHECK_UNSUPPORTED_PHASE1`；任何几何证据都不产生总建筑面积、建筑密度、容积率、绿地率或停车位汇总。

- [ ] **Step 2: 运行 02A 测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02ElementSnapshotCanonicalizerTests|FullyQualifiedName~NativeStage02GeometryEvidencePolicyTests|FullyQualifiedName~NativeStage02CandidatePolicyTests|FullyQualifiedName~NativeStage02RoleConfirmationPolicyTests|FullyQualifiedName~NativeStage02AIssuePolicyTests"
```

Expected: FAIL，因为 confirmation 和 snapshot policy 尚不存在。

- [ ] **Step 3: 增加候选、确认和快照模型**

```csharp
internal sealed class NativeStage02SemanticCandidate
{
  internal string RoleId { get; set; } = string.Empty;
  internal string Confidence { get; set; } = string.Empty;
  internal IReadOnlyList<string> Evidence { get; set; } = Array.Empty<string>();
}

internal sealed class NativeStage02RoleConfirmation
{
  internal string ElementUniqueId { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal string ElementSnapshotHash { get; set; } = string.Empty;
  internal string RulePackageSha256 { get; set; } = string.Empty;
  internal string ConfirmedUtc { get; set; } = string.Empty;
}

internal sealed class NativeStage02BoundingBoxEvidence
{
  internal bool Available { get; set; }
  internal double MinXFeet { get; set; }
  internal double MinYFeet { get; set; }
  internal double MinZFeet { get; set; }
  internal double MaxXFeet { get; set; }
  internal double MaxYFeet { get; set; }
  internal double MaxZFeet { get; set; }
}

internal enum NativeStage02GeometryCheckState
{
  Passed, Failed, Unsupported
}

internal sealed class NativeStage02GeometryCheckEvidence
{
  internal string RuleText { get; set; } = string.Empty;
  internal NativeStage02GeometryCheckState State { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal string Basis { get; set; } = string.Empty;
}

internal sealed class NativeStage02GeometryEvidence
{
  internal NativeStage02BoundingBoxEvidence BoundingBox { get; set; } =
    new NativeStage02BoundingBoxEvidence();
  internal string LocationKind { get; set; } = string.Empty;
  internal IReadOnlyList<double> LocationCoordinatesFeet { get; set; } =
    Array.Empty<double>();
  internal double? ApprovedProjectedAreaSquareMetres { get; set; }
  internal string ProjectedAreaSource { get; set; } = string.Empty;
  internal string EvidenceHash { get; set; } = string.Empty;
}

internal sealed class NativeStage02TaskGeometryEvaluation
{
  internal string TaskId { get; set; } = string.Empty;
  internal string ElementUniqueId { get; set; } = string.Empty;
  internal IReadOnlyList<NativeStage02GeometryCheckEvidence> Checks { get; set; } =
    Array.Empty<NativeStage02GeometryCheckEvidence>();
  internal string EvaluationHash { get; set; } = string.Empty;
}

internal sealed class NativeStage02RoleConfirmationDecision
{
  internal bool Confirmed { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal string ResolvedRoleId { get; set; } = string.Empty;
  internal string Source { get; set; } = string.Empty;
  internal NativeStage02RoleConfirmation Confirmation { get; set; }
}

internal static class NativeStage02ElementSnapshotCanonicalizer
{
  internal static string Build(NativeStage02ElementSnapshot snapshot);
  internal static string Sha256(NativeStage02ElementSnapshot snapshot);
}
```

在 `NativeStage02ElementSnapshot` 增加 `Geometry` 属性，默认新建空 `NativeStage02GeometryEvidence`。快照 canonical 输入固定包含：DocumentFingerprint、UniqueId、ElementId、Category、CategoryName、ClrType、ElementKind、ElementName、FamilyName、TypeName、LevelName，以及 `Geometry` 的边界框、位置、可靠面积和 evidence hash；double 全部以 invariant `G17` 格式。**确认前事实 hash 明确排除 `AssignedRoleId`、候选置信度、RoleId、task-specific check、RunId、UI 文案和时间戳**，避免保存确认后自身立即 stale；RoleId 只属于 confirmation。

`NativeStage02RevitGeometryEvidenceService.Capture(Document, Element)` 在角色决策前、Revit ExternalEvent 内读取原始几何事实：`element.get_BoundingBox(null)` 的 8 个角点经 `BoundingBoxXYZ.Transform` 转为模型内部坐标后求 world min/max；`LocationPoint` 保存 XYZ，`LocationCurve` 保存两个端点 XYZ；其他位置为空。面积第一期唯一批准的自动源是 `BuildingPad` 的只读 `BuiltInParameter.HOST_AREA_COMPUTED`，要求 `StorageType.Double`、finite、`> 0`，再用 Revit 2020 API `UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_SQUARE_METERS)` 转为平方米。不得使用包围盒长乘宽、显示字符串、族名称或参数别名估算面积。

```csharp
internal static class NativeStage02RevitGeometryEvidenceService
{
  internal static NativeStage02GeometryEvidence Capture(
    Document document,
    Element element);
}

internal static class NativeStage02GeometryEvidencePolicy
{
  internal static NativeStage02TaskGeometryEvaluation Evaluate(
    NativeTaskDefinition task,
    NativeStage02ElementSnapshot element,
    NativeStage02GeometryEvidence geometry,
    IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters);
}
```

角色确定后才调用 `NativeStage02GeometryEvidencePolicy.Evaluate(...)` 生成 task-specific checks；这些 checks 不反向改变 confirmation snapshot hash。逐规则证据使用 exact ordinal 文本匹配。对有效 `BuildingPad`，Revit 已接受的 host profile 可证明 `边界闭合`、`无自交`、`绿地边界闭合`；可靠面积可证明 `面积大于零`。其余关系/连续性/越界/重复检查本期都返回 `Unsupported + GEOMETRY_CHECK_UNSUPPORTED_PHASE1`，在 Stage03 中是红色业务缺项，不得由 bbox 或角色确认变绿。`投影面积与几何一致` 在字段 GUID 回读值与 `ApprovedProjectedAreaSquareMetres` 同时存在时计算，容差为 `max(0.01 m², geometryArea * 0.001)`；其他 property check 没有受控 evaluator 时同样返回 unsupported。`NativeStage02RevitService.ReadParameterEvidence` 将该可靠面积传给现有 `NativeStage02SemanticValueSuggestionPolicy.Evaluate(...)`，替换当前硬编码 `null`，但只影响 `suggestionKind == APPROVED_REVIT_AREA`。

`NativeStage02ElementEvidence` 增加 `NativeStage02TaskGeometryEvaluation TaskGeometry`；未匹配/未确认角色时为 null，确认角色后通过 role 的 TaskId 取得 `NativeTaskDefinition` 再求值。Preview canonical JSON 纳入 `TaskGeometry.EvaluationHash`，workflow geometry outcomes 从这里生成。

- [ ] **Step 4: 实现候选与显式确认 policy**

`NativeStage02CandidatePolicy.Suggest(...)` 只使用 Task 1 aliases、现有批准的 category/element-kind 规则和已有参数；名称命中只是候选 evidence，不升级官方 carrier 状态。使用：

```csharp
internal static class NativeStage02CandidatePolicy
{
  internal static IReadOnlyList<NativeStage02SemanticCandidate> Suggest(
    NativeStage02ElementSnapshot element,
    IReadOnlyList<NativeReportingSemanticRole> roles);
}

internal static class NativeStage02RoleConfirmationPolicy
{
  internal static NativeStage02RoleConfirmationDecision Resolve(
    NativeStage02ElementSnapshot element,
    NativeStage02SemanticCandidate candidate,
    NativeStage02SemanticAssignmentRecord persisted,
    NativeStage02RoleConfirmation explicitConfirmation,
    NativeWorkflowIdentity identity,
    string currentElementSnapshotHash);
}

internal static class NativeStage02IssueCompiler
{
  internal static IReadOnlyList<NativeIssueRecord> Compile(
    NativeStage02Preview preview);
}
```

优先级固定为：本次显式确认 → 完全匹配 identity/snapshot 的已保存确认 → 阻断。candidate 绝不自行成为最终角色。新增 `NativeStage02FieldStatus.PendingConfirmation`，并让 `NativeStage02ElementPlan.IsBlocked` 包含它及 `RuntimeBlocked`。

- [ ] **Step 5: 升级 assignment/preview 协议并提供迁移**

Assignment payload 版本升为 `1.1.0`，记录增加 `RulePackageSha256`、`ElementSnapshotHash`、`ConfirmedUtc`。读取 `1.0.0` 时返回 `NeedsReconfirmation`，保留旧记录但不把它视为损坏或当前有效。

`NativeStage02PreviewRequest` 必须增加确认的输入通路，不能只把确认放在 preview 输出：

```csharp
internal IReadOnlyList<NativeStage02RoleConfirmation> Confirmations { get; set; } =
  Array.Empty<NativeStage02RoleConfirmation>();
```

`Clone()` 深复制 confirmation 并按 `ElementUniqueId, RoleId` ordinal 排序，拒绝同一 ElementUniqueId 的冲突角色；`ResolvedRequest` 原样带回确认。`NativeStage02WorkbenchRequestPolicy`、ExternalEvent dispatcher 和 WPF 刷新预览都继续传同一个 request 对象的 clone。UI 接受候选时只替换 `request.Confirmations` 中对应 element 的记录，再发起 `CreatePreview`；不得依赖静态内存、当前行选中状态或 persisted assignment 作为本次确认输入。

Preview schema 升为：

```csharp
internal string SchemaVersion { get; set; } = "HBR_NATIVE_STAGE02A_PREVIEW_V3";
internal NativeStage02ScopeMode ScopeMode { get; set; }
internal string RunId { get; set; } = string.Empty;
internal IReadOnlyList<NativeStage02RoleConfirmation> Confirmations { get; set; } =
  Array.Empty<NativeStage02RoleConfirmation>();
internal IReadOnlyList<NativeIssueRecord> Issues { get; set; } =
  Array.Empty<NativeIssueRecord>();
```

Canonical JSON 必须包含 scope、候选、确认、快照 hash、几何 evidence hash、规则 SHA、名称/族/类型/楼层；任一写入相关输入变化都改变 PreviewHash。`RunId` 和 `ConfirmedUtc` 是审计元数据，明确排除在 PreviewHash 外；写前使用 `ResolvedRequest` 重建 preview，即使生成新的 RunId，只要业务输入未变，PreviewHash 必须相同。测试同时证明修改位置、边界框、可靠面积、几何规则状态、角色或字段最终值都会改变 hash。

- [ ] **Step 6: 保留构件级事务并输出 workflow result**

`NativeStage02RevitWriteService` 继续每个 element 一个 transaction：assignment 存储、参数写入和 GUID 回读任一失败即回滚该 element；其他 element 继续。`NativeStage02WriteResult` 增加：

```csharp
internal sealed class NativeStage02ElementWriteOutcome
{
  internal int ElementId { get; set; }
  internal string ElementUniqueId { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal bool Succeeded { get; set; }
  internal string GeometryEvidenceHash { get; set; } = string.Empty;
  internal IReadOnlyList<NativeWorkflowItemEvidence> GeometryOutcomes { get; set; } =
    Array.Empty<NativeWorkflowItemEvidence>();
  internal IReadOnlyList<NativeWorkflowItemEvidence> FieldOutcomes { get; set; } =
    Array.Empty<NativeWorkflowItemEvidence>();
  internal string ErrorCode { get; set; } = string.Empty;
}

internal IReadOnlyList<NativeStage02ElementWriteOutcome> ElementOutcomes { get; set; } =
  Array.Empty<NativeStage02ElementWriteOutcome>();
internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
```

最后以所有 element 的角色、geometry/property-check 和 field outcomes 建立 `SourceFeature="STAGE02A"`、`SourceFunction="ELEMENT_PREPARATION"` envelope；geometry item key 固定为 `ElementUniqueId|CheckId`，值包含 `GeometryEvidenceHash`、状态和稳定 code。unsupported 几何规则保持失败证据，但不回滚同构件已经成功的独立参数写入。Envelope 的 `InputSnapshotHash` 由本次全模型/所选范围中排序后的 element snapshot hash 生成；Stage03 全量检查时必须重新全模型捕获以判定当前性，不能靠上次 WPF preview 缓存。workflow result 在独立短 transaction 写入；若该写入失败必须返回技术失败，不能声称结果已持久化。

- [ ] **Step 7: 改造 02A WPF 两段预览**

界面流程固定为：

```text
选择范围 → 生成候选 → 人工逐项/批量确认 → 刷新写入预览 → 确认写入
```

未确认行显示红色“待确认”，低置信候选显示黄色；“批量接受当前候选”只生成 confirmation、写回 request 并刷新预览，不直接调用写入。全模型和自主选择都显示 confirmation 列。字段表增加“几何来源/当前面积/几何检查”列：可靠面积显示平方米和 `HOST_AREA_COMPUTED`；无可靠来源显示“待填写”；unsupported check 明确显示红色“本期未实现”，不得显示绿色。

- [ ] **Step 8: 运行完整 02A 回归**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage02"
python -m pytest tests/test_revit_addin_stage02_revit_contract.py `
  tests/test_revit_addin_stage02_ui_contract.py -q
```

Expected: PASS；现有 v0.4.2 绿地写入、构件级回滚和部分成功测试不回退。

- [ ] **Step 9: 提交 02A 确认流程**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02 `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ElementSnapshotCanonicalizerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02GeometryEvidencePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02CandidatePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RoleConfirmationPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02AIssuePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02PreviewCompilerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02ManualPreviewCompilerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02WorkbenchRequestPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SemanticValueSuggestionPolicyTests.cs `
  tests/test_revit_addin_stage02_revit_contract.py `
  tests/test_revit_addin_stage02_ui_contract.py
git commit -m "feat(stage02a): require confirmed total-plan semantics"
```

---

### Task 6: 补齐自主点选、问题中心和构件定位

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02InteractionService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeIssueHub.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeIssueNavigationPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeRevitIssueNavigationService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Issues/NativeIssueCenterView.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeIssueNavigationPolicyTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02Inventory.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02WorkbenchRequestPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02InventoryPolicyTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SelectionInventoryPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage02_ui_contract.py`

**Interfaces:**
- Consumes: Task 3 的 issue 核心合同、Task 5 的 02A issues、ElementId/UniqueId、当前 `UIApplication`。
- Produces: `RequestStage02PickElements(...)`、`RequestIssueNavigation(...)`、共享 `NativeIssueHub` 和五种修复路由。

- [ ] **Step 1: 写导航门禁 RED 测试**

```csharp
[Fact]
public void Missing_element_issue_cannot_request_fake_revit_location()
{
  NativeIssueNavigationDecision decision = NativeIssueNavigationPolicy.Evaluate(
    new NativeIssueNavigationRequest
    {
      Action = NativeIssueNavigationAction.Zoom,
      DocumentFingerprint = "doc",
      Elements = Array.Empty<NativeIssueElementReference>()
    }, "doc");
  Assert.False(decision.Allowed);
  Assert.Equal("ISSUE_ELEMENT_MISSING", decision.Code);
}
```

- [ ] **Step 2: 运行 issue 测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeIssue"
```

Expected: FAIL，因为 navigation policy/request/result 尚不存在；Task 3 的 issueId 测试保持 PASS。

- [ ] **Step 3: 实现导航请求、决策和结果合同**

```csharp
internal sealed class NativeIssueNavigationRequest
{
  internal string IssueId { get; set; } = string.Empty;
  internal NativeIssueNavigationAction Action { get; set; }
  internal string DocumentFingerprint { get; set; } = string.Empty;
  internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
    Array.Empty<NativeIssueElementReference>();
  internal NativeIssueNavigationRequest Clone();
}

internal sealed class NativeIssueNavigationDecision
{
  internal bool Allowed { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal IReadOnlyList<NativeIssueElementReference> ResolvedElements { get; set; } =
    Array.Empty<NativeIssueElementReference>();
}

internal sealed class NativeIssueNavigationResult
{
  internal bool Succeeded { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal NativeIssueNavigationAction Action { get; set; }
  internal IReadOnlyList<int> AffectedElementIds { get; set; } = Array.Empty<int>();
}

internal static class NativeIssueNavigationPolicy
{
  internal static NativeIssueNavigationDecision Evaluate(
    NativeIssueNavigationRequest request,
    string currentDocumentFingerprint);
}
```

`Clone()` 深复制完整 element refs。policy 拒绝空文档指纹、跨文档、空 refs、空/重复 UniqueId、非正 ElementId，以及非 Revit 定位动作。`RestoreView` 是唯一允许 refs 为空的动作。执行服务先 `document.GetElement(uniqueId)`，再核对实际 `Id.IntegerValue == request.ElementId`；UniqueId 不存在或 ElementId 不符返回 `ISSUE_ELEMENT_STALE`，绝不按旧整数定位新构件。

- [ ] **Step 4: 扩展 02A 原生 UI 范围并保留显式选择合同**

将 scope 明确为：

```csharp
internal enum NativeStage02ScopeMode
{
  FullModel,
  CustomSelection,
  CurrentSelection,
  InteractiveSelection
}

internal sealed class NativeStage02SelectionResult
{
  internal bool Succeeded { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal NativeStage02ScopeMode ScopeMode { get; set; }
  internal IReadOnlyList<int> ElementIds { get; set; } = Array.Empty<int>();
  internal IReadOnlyList<string> ElementUniqueIds { get; set; } = Array.Empty<string>();
}

internal static class NativeStage02InteractionService
{
  internal static NativeStage02SelectionResult ReadCurrentSelection(
    UIApplication application);
  internal static NativeStage02SelectionResult PickElements(
    UIApplication application);
}
```

`CustomSelection` 保持 v0.4.2 的旧语义：当 request 尚无 `CustomUniqueIds` 时读取当前 Revit 选择集，并把解析后的 UniqueId 固化到 `ResolvedRequest.CustomUniqueIds`；它不是既有公开 ElementId payload。`CurrentSelection` 是新 WPF 显式动作，同样读取 `UIDocument.Selection.GetElementIds()`，但立即返回并写入 request 的排序 UniqueId 快照。`InteractiveSelection` 使用 `PickObjects(ObjectType.Element, filter, "请选择报规构件，完成后点击完成")`，从每个 `Reference.ElementId` 解析 live element 并同时返回 ElementId/UniqueId；UI 将 `ElementUniqueIds` 复制到下一次 `NativeStage02PreviewRequest.CustomUniqueIds`，不得假设 `PickObjects` 会改变 Revit 当前选择。取消选择返回 `SELECTION_CANCELLED`，空选择返回 `SELECTION_EMPTY`，任何自主选择来源都不偷偷切到全模型。

Dispatcher 增加：

```csharp
internal static void RequestStage02PickElements(
  Action<NativeStage02SelectionResult> completed,
  Action<Exception> failed);

internal static void RequestIssueNavigation(
  NativeIssueNavigationRequest request,
  Action<NativeIssueNavigationResult> completed,
  Action<Exception> failed);
```

- [ ] **Step 5: 实现 Revit 定位动作**

`NativeRevitIssueNavigationService.Execute(...)` 首先核对 document fingerprint，再逐个按 UniqueId 解析并核对 ElementId，然后按动作执行：

```csharp
internal static class NativeRevitIssueNavigationService
{
  internal static NativeIssueNavigationResult Execute(
    UIApplication application,
    NativeIssueNavigationRequest request);
}
```

```csharp
uiDocument.Selection.SetElementIds(elementIds);
uiDocument.ShowElements(elementIds);
activeView.IsolateElementsTemporary(elementIds);
activeView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
```

`Isolate` 与 `RestoreView` 各自放入独立 Revit `Transaction`；定位不存在的 element 返回结构化失败，不抛到 WPF dispatcher。缺少必要构件的问题不携带 ElementId，只路由到 `OpenStage02A`。

- [ ] **Step 6: 实现共享 IssueHub 和问题中心列表**

```csharp
internal sealed class NativeIssueHub
{
  internal event Action IssuesChanged;
  internal string DocumentFingerprint { get; }
  internal void ResetForDocument(string documentFingerprint);
  internal void Replace(string sourceFeature, IEnumerable<NativeIssueRecord> issues);
  internal IReadOnlyList<NativeIssueRecord> Snapshot();
}

internal sealed class NativeIssueCenterView : UserControl
{
  internal NativeIssueCenterView(
    NativeIssueHub hub,
    Action<NativeIssueRecord> navigateToSource,
    Action<NativeIssueNavigationRequest> requestRevitAction);
  internal void Refresh();
}
```

`NativeIssueHub` 以当前 `DocumentFingerprint` 分区；构造或切换文档时 `ResetForDocument(...)` 清空旧文档快照。`Replace` 只接受当前文档并仅替换同一 sourceFeature，最终按 severity/source/checkId/issueId 排序。`NativeIssueCenterView` 显示“构件名｜类别｜缺什么｜影响什么｜去哪里补”，构件存在时显示选中/缩放/隔离/恢复按钮。

- [ ] **Step 7: 运行范围和问题定位合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeIssue|FullyQualifiedName~NativeStage02Inventory|FullyQualifiedName~NativeStage02Selection"
python -m pytest tests/test_revit_addin_stage02_ui_contract.py `
  tests/test_revit_addin_stage02_revit_contract.py -q
```

Expected: PASS；FullModel、既有 CustomSelection、CurrentSelection、InteractiveSelection 不互相降级，定位动作均经 ExternalEvent。

- [ ] **Step 8: 提交问题中心与定位能力**

```powershell
git add src/BIMBaoGui.RevitAddin/Issues `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02InteractionService.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02Inventory.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02WorkbenchRequestPolicy.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs `
  src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeIssueNavigationPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02InventoryPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02SelectionInventoryPolicyTests.cs `
  tests/test_revit_addin_stage02_ui_contract.py `
  tests/test_revit_addin_stage02_revit_contract.py
git commit -m "feat(issues): add Revit issue navigation and selection"
```

---

### Task 7: 建立独立 02B 指标、校验、记录和载体门禁

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BModels.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BValuePolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BCanonicalizer.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BStoragePolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BOwnerPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BCurrentResultPolicy.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BValuePolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BCanonicalizerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BStoragePolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BOwnerPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BCurrentResultPolicyTests.cs`

**Interfaces:**
- Consumes: Task 2 的六指标目录、Task 3 的 identity。
- Produces: `NativeStage02BValuePolicy.Validate(...)`、`NativeStage02BOwnerPolicy.Resolve(...)`、确定性 metric record 和 current/export-ready 判定。

- [ ] **Step 1: 写六指标和旧值失效 RED 测试**

```csharp
[Theory]
[InlineData("ca21e324-046b-5bfd-84c8-0d3470082303", "12345.6", true)]
[InlineData("ca21e324-046b-5bfd-84c8-0d3470082303", "0", false)]
[InlineData("93e51676-237e-56a8-8f28-2da845422e2e", "0.25", true)]
[InlineData("201a00ac-3672-5ded-83d2-ed96f81bfabf", "NaN", false)]
[InlineData("c62cfd5f-2a50-5230-9c5d-4037c39061bf", "120", true)]
[InlineData("84df74c2-a7e5-5a98-a5e0-4458e49a3973", "1.5", false)]
public void Metric_values_follow_rule_types(string propertyId, string raw, bool valid)
{
  NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
    .MetricsFor("总平模型")
    .Single(value => value.PropertyId == propertyId);
  NativeStage02BValueDecision decision = NativeStage02BValuePolicy.Validate(
    metric, raw);
  Assert.Equal(valid, decision.Accepted);
}

[Fact]
public void Failed_latest_attempt_invalidates_old_success()
{
  var identity = new NativeWorkflowIdentity
  {
    DocumentFingerprint = "doc", ModelFileType = "总平模型",
    RulePackageId = "HBR-WUHAN-PLANNING", RulePackageVersion = "1.0.0",
    RulePackageSha256 = new string('a', 64)
  };
  NativeStage02BMetricRecord record = NativeStage02BCanonicalizer.SealRecord(
    new NativeStage02BMetricRecord
    {
      PropertyId = "201a00ac-3672-5ded-83d2-ed96f81bfabf",
      Identity = "IfcSite|Pset_场地信息属性集|容积率",
      RequestedCanonicalValue = "1.2",
      LastSuccessfulCanonicalValue = "1.2",
      LastAttemptRunId = "run-new",
      LastSuccessfulRunId = "run-old",
      WriteStatus = "FAILED",
      ReadbackStatus = "SUCCEEDED",
      IdentityContext = identity
    });
  NativeStage02BCurrentResultDecision decision =
    NativeStage02BCurrentResultPolicy.Evaluate(record, identity);
  Assert.False(decision.Current);
  Assert.Equal("LATEST_ATTEMPT_FAILED", decision.Code);
}
```

该测试文件显式加入 `using System.Linq;`；上述两个测试不依赖未定义的 `Metric/SuccessfulRecord/Identity` helper。

- [ ] **Step 2: 运行 02B 领域测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage02B"
```

Expected: FAIL，因为 02B 领域尚不存在。

- [ ] **Step 3: 实现指标输入、outcome 和持久记录**

```csharp
internal sealed class NativeStage02BMetricInput
{
  internal string PropertyId { get; set; } = string.Empty;
  internal string RawValue { get; set; } = string.Empty;
}

internal sealed class NativeStage02BWriteRequest
{
  internal string RunId { get; set; } = string.Empty;
  internal IReadOnlyList<NativeStage02BMetricInput> Metrics { get; set; } =
    Array.Empty<NativeStage02BMetricInput>();
  internal IReadOnlyList<string> PropertyIdsToRetry { get; set; } =
    Array.Empty<string>();
  internal NativeStage02BWriteRequest Clone();
}

internal sealed class NativeStage02BMetricRecord
{
  internal string PropertyId { get; set; } = string.Empty;
  internal string Identity { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal string Source { get; set; } = "MANUAL_INPUT";
  internal string RequestedCanonicalValue { get; set; } = string.Empty;
  internal string LastSuccessfulCanonicalValue { get; set; } = string.Empty;
  internal string LastAttemptRunId { get; set; } = string.Empty;
  internal string LastSuccessfulRunId { get; set; } = string.Empty;
  internal string WriteStatus { get; set; } = string.Empty;
  internal string ReadbackStatus { get; set; } = string.Empty;
  internal string ProjectionStatus { get; set; } = string.Empty;
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
  internal string OfficialEvidenceRef { get; set; } = string.Empty;
  internal NativeWorkflowIdentity IdentityContext { get; set; }
  internal string UpdatedUtc { get; set; } = string.Empty;
  internal string ResultHash { get; set; } = string.Empty;
  internal string ErrorCode { get; set; } = string.Empty;
}

internal sealed class NativeStage02BMetricOutcome
{
  internal string PropertyId { get; set; } = string.Empty;
  internal string Identity { get; set; } = string.Empty;
  internal string RequestedCanonicalValue { get; set; } = string.Empty;
  internal string PersistedCanonicalValue { get; set; } = string.Empty;
  internal bool Succeeded { get; set; }
  internal bool InternalWriteSucceeded { get; set; }
  internal bool ParameterWriteSucceeded { get; set; }
  internal bool ReadbackSucceeded { get; set; }
  internal string ProjectionStatus { get; set; } = string.Empty;
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
  internal string OfficialEvidenceRef { get; set; } = string.Empty;
  internal string ErrorCode { get; set; } = string.Empty;
  internal string Message { get; set; } = string.Empty;
  internal NativeStage02BMetricRecord Record { get; set; }
}

internal sealed class NativeStage02BWriteResult
{
  internal string RunId { get; set; } = string.Empty;
  internal IReadOnlyList<NativeStage02BMetricOutcome> MetricOutcomes { get; set; } =
    Array.Empty<NativeStage02BMetricOutcome>();
  internal IReadOnlyList<string> FailedPropertyIds { get; set; } = Array.Empty<string>();
  internal bool PartialSuccess { get; set; }
  internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
  internal string TechnicalErrorCode { get; set; } = string.Empty;
}

internal sealed class NativeStage02BStorageSnapshot
{
  internal string SchemaVersion { get; set; } = "HBR_NATIVE_STAGE02B_METRICS_V1";
  internal IReadOnlyList<NativeStage02BMetricRecord> Records { get; set; } =
    Array.Empty<NativeStage02BMetricRecord>();
  internal string CanonicalJson { get; set; } = string.Empty;
  internal string SnapshotHash { get; set; } = string.Empty;
}

internal sealed class NativeStage02BReadResult
{
  internal NativeWorkflowIdentity Identity { get; set; }
  internal IReadOnlyList<NativeStage02BMetricRecord> Records { get; set; } =
    Array.Empty<NativeStage02BMetricRecord>();
  internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
  internal IReadOnlyList<NativeIssueRecord> Issues { get; set; } =
    Array.Empty<NativeIssueRecord>();
}
```

`Clone()` 必须复制每个 input 和两个集合，不能把 WPF 可变集合交给 ExternalEvent。`Succeeded` 表示本期内部真实值已保存并回读；`ParameterWriteSucceeded` 只描述可投影的 ProjectInformation 参数，不得用它把 Site/SpatialZone 的内部保存误判为失败，也不得用内部成功把官方载体状态改绿。

- [ ] **Step 4: 实现 rule-driven 值校验**

```csharp
internal sealed class NativeStage02BValueDecision
{
  internal bool Accepted { get; set; }
  internal string CanonicalValue { get; set; } = string.Empty;
  internal string Code { get; set; } = string.Empty;
  internal string Message { get; set; } = string.Empty;
}

internal static class NativeStage02BValuePolicy
{
  internal static NativeStage02BValueDecision Validate(
    NativeStage02BMetricDefinition metric,
    string rawValue);
}
```

规则固定为：空值拒绝；Double 必须 finite/invariant；总建筑面积必须 `> 0`；其他 real 必须 `>= 0` 且不做百分比缩放；Integer 必须为 `>= 0` 的整数。UI 初始值保持空，不以 `0` 作为默认值。canonical double 用 `G17`，integer 用 invariant decimal。

- [ ] **Step 5: 实现官方载体 fail-closed policy**

```csharp
internal enum NativeStage02BProjectionMode
{
  ProjectInformation,
  VerifiedElementParameter,
  InternalStorageOnly
}

internal sealed class NativeStage02BOwnerDecision
{
  internal bool InternalSaveAllowed { get; set; }
  internal bool ParameterProjectionAllowed { get; set; }
  internal NativeStage02BProjectionMode ProjectionMode { get; set; }
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string Code { get; set; } = string.Empty;
}

internal static class NativeStage02BOwnerPolicy
{
  internal static NativeStage02BOwnerDecision Resolve(
    NativeStage02BMetricDefinition metric,
    NativeOfficialCarrierPolicy carrierPolicy,
    NativeOfficialProjectionCarrierDefinition projectionCarrier,
    NativeOfficialEvidenceRecord officialEvidence);
}
```

精确决策：

```text
IfcProject      → ProjectInformation，可内部参数写入；official=PENDING_GOLDEN_RVT
IfcSite         → InternalStorageOnly，code=OFFICIAL_CARRIER_PENDING_GOLDEN_RVT
IfcSpatialZone  → InternalStorageOnly，code=OFFICIAL_CARRIER_PENDING_GOLDEN_RVT
其他 entity     → 阻断，code=UNSUPPORTED_METRIC_OWNER
```

内部保存成功与官方导出准备成功是两个独立状态；不得把 `legacyProjection.carrier` 作为目标 Element。Owner 决策以 metric 自身的 `propertyId + officialCarrierStatus + officialProjectionCarrierId + officialEvidenceRef` 为准，并通过 Task 2 目录解析结构化 carrier/evidence；空外键、断链或跨 propertyId 引用一律 pending/blocked。entity policy 只是上限，不能因为同一 IfcSite/IfcSpatialZone 的一个属性取得证据，就把同 entity 的其他指标一起变绿。

- [ ] **Step 6: 实现 canonical、storage merge 和当前性判定**

`NativeStage02BCanonicalizer` 按 propertyId 排序并 hash 每条 record。`NativeStage02BStoragePolicy.Merge(current, attempted)` 只替换同 propertyId，保留其他指标。精确签名为：

```csharp
internal static class NativeStage02BCanonicalizer
{
  internal static NativeStage02BMetricRecord SealRecord(NativeStage02BMetricRecord record);
  internal static bool VerifyRecord(NativeStage02BMetricRecord record);
  internal static NativeStage02BStorageSnapshot SealSnapshot(
    IEnumerable<NativeStage02BMetricRecord> records);
}

internal static class NativeStage02BStoragePolicy
{
  internal static NativeStage02BStorageSnapshot Merge(
    NativeStage02BStorageSnapshot current,
    NativeStage02BMetricRecord attempted);
}

internal sealed class NativeStage02BCurrentResultDecision
{
  internal bool Current { get; set; }
  internal bool ExportReady { get; set; }
  internal string CurrentCanonicalValue { get; set; } = string.Empty;
  internal string Code { get; set; } = string.Empty;
}

internal static class NativeStage02BCurrentResultPolicy
{
  internal static NativeStage02BCurrentResultDecision Evaluate(
    NativeStage02BMetricRecord record,
    NativeWorkflowIdentity currentIdentity);
}
```

`Evaluate` 必须依次验证 record hash、document/model/rule identity，然后把 `LastAttemptRunId != LastSuccessfulRunId`、`WriteStatus != SUCCEEDED` 或 `ReadbackStatus != SUCCEEDED` 统一返回 `LATEST_ATTEMPT_FAILED`；只有最新 attempt 完整成功才继续判断值和载体。测试中的失败 record 必须在设置失败字段后重新 `SealRecord`，另单测篡改已密封 record 返回 `RECORD_HASH_MISMATCH`。`ExportReady=false` 直到该 propertyId 自身为 Verified 且具有非空 projection carrier/evidence ref。

- [ ] **Step 7: 运行全部 02B 领域测试**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage02B"
```

Expected: PASS；六指标顺序、类型、identity 和 carrier 决策全部固定。

- [ ] **Step 8: 提交 02B 领域**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02B `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BValuePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BCanonicalizerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BStoragePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BOwnerPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BCurrentResultPolicyTests.cs
git commit -m "feat(stage02b): add manual actual metric domain"
```

---

### Task 8: 实现 02B 指标级 Revit 事务、独立 UI 和工作台导航

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BStorage.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BRevitReadService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BRevitWriteService.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BProjectionCarrierResolver.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BWriteBatchPolicy.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BViewModel.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BView.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BWriteBatchPolicyTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BResultCanonicalizerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BProjectionCarrierResolverTests.cs`
- Create: `tests/test_revit_addin_stage02b_revit_contract.py`
- Create: `tests/test_revit_addin_stage02b_ui_contract.py`
- Modify: `src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01FieldPresentationPolicy.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs`

**Interfaces:**
- Consumes: Task 7 metric request/record/policies，复用 `NativeStage02ParameterBindingService.Ensure` 与 `NativeStage02ValueCodec.WriteAndVerify`。
- Produces: `RequestStage02BRead/Write`、结构化官方 carrier resolver、独立 `NativeStage02BView`、指标级部分成功结果和 `STAGE02B/PROJECT_ACTUAL_METRICS` envelope。

- [ ] **Step 1: 写部分成功和 retry RED 测试**

```csharp
[Fact]
public void One_metric_failure_preserves_other_successes()
{
  NativeStage02BWriteBatchDecision decision = NativeStage02BWriteBatchPolicy.Merge(
    new[]
    {
      new NativeStage02BMetricOutcome { PropertyId = "A", Succeeded = true },
      new NativeStage02BMetricOutcome
      {
        PropertyId = "B", Succeeded = false,
        ErrorCode = "READBACK_FAILED"
      },
      new NativeStage02BMetricOutcome { PropertyId = "C", Succeeded = true }
    });
  Assert.Equal(new[] { "A", "C" }, decision.SuccessfulPropertyIds);
  Assert.Equal(new[] { "B" }, decision.FailedPropertyIds);
  Assert.True(decision.PartialSuccess);
}

[Fact]
public void Retry_request_contains_only_latest_failed_metrics()
{
  var last = new NativeStage02BWriteResult
  {
    RunId = "run-1",
    FailedPropertyIds = new[] { "B", "D" }
  };
  NativeStage02BMetricInput[] inputs =
  {
    new NativeStage02BMetricInput { PropertyId = "A", RawValue = "1" },
    new NativeStage02BMetricInput { PropertyId = "B", RawValue = "2" },
    new NativeStage02BMetricInput { PropertyId = "D", RawValue = "4" }
  };
  NativeStage02BWriteRequest retry = NativeStage02BWriteBatchPolicy.BuildRetry(
    last, inputs);
  Assert.Equal(new[] { "B", "D" }, retry.Metrics.Select(value => value.PropertyId));
}
```

`NativeStage02BWriteBatchPolicy` 使用以下精确合同：

```csharp
internal sealed class NativeStage02BWriteBatchDecision
{
  internal IReadOnlyList<string> SuccessfulPropertyIds { get; set; } = Array.Empty<string>();
  internal IReadOnlyList<string> FailedPropertyIds { get; set; } = Array.Empty<string>();
  internal bool PartialSuccess { get; set; }
}

internal static class NativeStage02BWriteBatchPolicy
{
  internal static NativeStage02BWriteBatchDecision Merge(
    IEnumerable<NativeStage02BMetricOutcome> outcomes);

  internal static NativeStage02BWriteRequest BuildRetry(
    NativeStage02BWriteResult lastResult,
    IReadOnlyList<NativeStage02BMetricInput> currentInputs);
}
```

`BuildRetry` 按 metric catalog 顺序输出，只包含 `lastResult.FailedPropertyIds` 与当前 input 的交集；找不到当前输入时抛出消息精确为 `RETRY_INPUT_MISSING` 的 `InvalidOperationException`，不复用上次 raw value。测试文件不定义 `Outcome/LastResultWithFailures/CurrentInputs` 隐藏 helper；直接使用上述完整对象初始化。

- [ ] **Step 2: 运行写入测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage02BWriteBatchPolicyTests|FullyQualifiedName~NativeStage02BResultCanonicalizerTests|FullyQualifiedName~NativeStage02BProjectionCarrierResolverTests"
```

Expected: FAIL，因为 Revit 写入 batch/result 尚不存在。

- [ ] **Step 3: 实现 02B Extensible Storage**

使用 schema GUID `420ba043-1d47-4f29-a97e-f33c75e18385`、schema 名 `HBR_NATIVE_STAGE02B_METRICS_V1`、DataStorage 名 `HBR Native Stage02B Metrics`，字段为 `SchemaVersion` 和 `CanonicalJson`。

```csharp
internal static class NativeStage02BStorage
{
  internal static NativeStage02BStorageSnapshot Read(Document document);
  internal static void Write(Document document, NativeStage02BStorageSnapshot snapshot);
  internal static void WriteMetric(Document document, NativeStage02BMetricRecord record);
}
```

`WriteMetric` 读取当前 snapshot、按 propertyId merge、重新 canonicalize；要求调用方已有 transaction。

- [ ] **Step 4: 实现每指标独立写入/回读**

`NativeStage02BRevitWriteService.Execute(...)` 对每个输入执行固定顺序：validate → owner decision → transaction → internal record → optional parameter projection → regenerate/readback → commit。

```csharp
internal static class NativeStage02BRevitReadService
{
  internal static NativeStage02BReadResult Read(UIApplication application);
}

internal static class NativeStage02BRevitWriteService
{
  internal static NativeStage02BWriteResult Execute(
    UIApplication application,
    NativeStage02BWriteRequest request);
}

internal sealed class NativeStage02BResolvedProjectionCarrier
{
  internal Element Element { get; set; }
  internal Guid ParameterGuid { get; set; }
  internal string BindingScope { get; set; } = string.Empty;
  internal string CarrierId { get; set; } = string.Empty;
}

internal static class NativeStage02BProjectionCarrierResolver
{
  internal static NativeStage02BResolvedProjectionCarrier Resolve(
    Document document,
    NativeOfficialProjectionCarrierDefinition definition,
    NativeStage02SemanticAssignmentSnapshot assignments);
}
```

ProjectInformation 路径：

```csharp
NativeStage02ParameterBindingService.Ensure(
  document, metric.Property, new[] { "OST_ProjectInformation" });
Parameter parameter = document.ProjectInformation.get_Parameter(
  metric.Property.ParameterGuid);
NativeStage02ValueCodec.WriteAndVerify(
  parameter, metric.Property, canonicalValue);
string readback = NativeStage02ValueCodec.Read(parameter, metric.Property);
```

pending 的 IfcSite/IfcSpatialZone 路径只写 internal record，`ProjectionStatus="BLOCKED_PENDING_GOLDEN_RVT"`；不得创建 ProjectInformation 参数。若未来 metric 已 Verified，服务必须先按 `OfficialProjectionCarrierId` 取结构定义：`PROJECT_INFORMATION` 只允许 `document.ProjectInformation`；`CONFIRMED_SEMANTIC_ROLE` 只读取当前文档 02A 已确认且未 stale 的 assignment，按 RoleId、BuiltInCategory 和完整 CLR type 精确过滤后解析 live UniqueId。0 个、多个、类型漂移、scope/GUID 不等于结构记录分别返回稳定码 `OFFICIAL_CARRIER_NOT_FOUND/OFFICIAL_CARRIER_AMBIGUOUS/OFFICIAL_CARRIER_TYPE_MISMATCH/OFFICIAL_CARRIER_CONTRACT_MISMATCH`，不得回退到名称搜索、ElementId 或 legacy carrier。resolver 单测覆盖两个 selector、跨文档/stale assignment、0/1/多候选和参数 GUID 错配；Revit contract 断言最终只按结构 DTO 写入。任何异常回滚当前指标 transaction，然后用新的短 transaction 写入失败审计 record；其他指标继续。

- [ ] **Step 5: 生成本次 envelope，避免旧值冒充成功**

`NativeStage02BWriteResult` 使用 Task 7 已定义的精确模型，最终必须填满：

```csharp
internal string RunId { get; set; } = string.Empty;
internal IReadOnlyList<NativeStage02BMetricOutcome> MetricOutcomes { get; set; } =
  Array.Empty<NativeStage02BMetricOutcome>();
internal IReadOnlyList<string> FailedPropertyIds { get; set; } = Array.Empty<string>();
internal bool PartialSuccess { get; set; }
internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
internal string TechnicalErrorCode { get; set; } = string.Empty;
```

执行完尝试项后，服务必须重新读取并校验 storage 中**全部六个 propertyId**，再从这份全量当前快照生成 `WorkflowResult.Items`；retry request 虽只写失败项，envelope 仍恰好含六项。未重试的既有成功项保留其 value、LastSuccessfulRunId 和 record hash；本次失败 item 的 `WriteSucceeded/ReadbackSucceeded=false`，即使 record 中还有 LastSuccessfulCanonicalValue。新增测试：先让 A–F 成功，再只重试 B/D，其中 D 失败；最终 envelope 六项齐全，A/C/E/F 的值与 hash 不变，B 为新成功，D 为最新失败。Envelope 写入失败是技术错误，但不得回滚已提交的指标；返回 `RESULT_ENVELOPE_WRITE_FAILED` 并让 Stage03 拒绝旧 envelope。

- [ ] **Step 6: 增加 ExternalEvent 请求**

```csharp
internal static void RequestStage02BRead(
  Action<NativeStage02BReadResult> completed,
  Action<Exception> failed);

internal static void RequestStage02BWrite(
  NativeStage02BWriteRequest request,
  Action<NativeStage02BWriteResult> completed,
  Action<Exception> failed);
```

请求入队前深复制 inputs/retry IDs；WPF 线程不得直接访问 `Document`。

- [ ] **Step 7: 建立独立 02B UI 和五项工作台导航**

```csharp
internal sealed class NativeStage02BViewModel
{
  internal IReadOnlyList<NativeStage02BMetricInput> Inputs { get; }
  internal NativeStage02BWriteRequest BuildSaveAllRequest();
  internal NativeStage02BWriteRequest BuildRetryRequest(
    NativeStage02BWriteResult lastResult);
  internal void ApplyRead(NativeStage02BReadResult result);
  internal void ApplyWrite(NativeStage02BWriteResult result);
}

internal sealed class NativeStage02BView : UserControl
{
  internal void NavigateToMetric(string propertyId);
}
```

`NativeStage02BView` 只显示规则生成的六行：指标名称、完整 identity、单位、人工输入、上次成功值、本次状态、官方载体状态。按钮固定为“保存全部”和“仅重试失败项”；不出现模型扫描、ElementId 或构件选择。

`WorkspaceControl` 导航固定为：

```text
01 项目初始化
02A 构件与属性准备
02B 项目实际指标
03 检测与 H-IFC
问题中心
```

构造一个共享 `NativeIssueHub` 传给 02A、02B、03 和问题中心。新增：

```csharp
internal void NavigateToMetric(string propertyId);
internal void NavigateToField(string fieldKey);
```

Stage01 总建筑面积卡片调用 `NavigateToMetric("ca21e324-046b-5bfd-84c8-0d3470082303")`。

基线 `NativeStage03View` 有无参构造和 `NativeStage03View(NativeStage03OutputDirectoryStore)`。本 Task 增加主构造 `NativeStage03View(NativeStage03OutputDirectoryStore store, NativeIssueHub hub)`；现有两个构造分别委托到它并新建仅供设计器/旧测试使用的 hub，同时新增 `NativeStage03View(NativeIssueHub hub)` 供 `WorkspaceControl` 注入共享实例。Task 10 再扩展清单回溯行为；这样本 Task 改完即可编译，不能提前传入不存在的构造参数。

- [ ] **Step 8: 运行 02B 领域、Revit 和 UI 合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage02B"
python -m pytest tests/test_revit_addin_stage02b_revit_contract.py `
  tests/test_revit_addin_stage02b_ui_contract.py `
  tests/test_revit_addin_stage01_ui_contract.py -q
```

Expected: PASS；静态合同确认 02B 不调用几何 collector/area 计算，也不把 Site/SpatialZone 写到 ProjectInformation。

- [ ] **Step 9: 提交 02B 原生功能和工作台拆分**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage02B `
  src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs `
  src/BIMBaoGui.RevitAddin/WorkspaceControl.cs `
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs `
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01FieldPresentationPolicy.cs `
  src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BWriteBatchPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BResultCanonicalizerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage02BProjectionCarrierResolverTests.cs `
  tests/test_revit_addin_stage02b_revit_contract.py `
  tests/test_revit_addin_stage02b_ui_contract.py `
  tests/test_revit_addin_stage01_ui_contract.py
git commit -m "feat(stage02b): add independent project actual metrics"
```

---

### Task 9: 生成和求值 Stage03 严格总平检查清单

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Checklist.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ChecklistGenerator.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ChecklistEvaluator.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03SourceEvidenceBundle.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03TechnicalPreflightService.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistGeneratorTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistEvaluatorTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03TechnicalPreflightPolicyTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Scanner.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03WorkflowService.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03Stage01ValidationPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage03_revit_contract.py`

**Interfaces:**
- Consumes: Stage01 read/result、02A 当前全模型几何 preview/result、02B read/result、规范化输出目录/技术 preflight 和 Task 2 reporting 目录。
- Produces: 仅由 Stage01 模型类型驱动的四态 `NativeStage03ChecklistItem`，以及包含清单的 `NativeStage03ScanResult`。

- [ ] **Step 1: 写动态生成和四态 RED 测试**

```csharp
[Fact]
public void Total_plan_checklist_is_deterministic_and_has_no_not_applicable()
{
  NativeStage03ChecklistGenerationResult first =
    NativeStage03ChecklistGenerator.Generate(
      "总平模型",
      new Dictionary<string, bool>(StringComparer.Ordinal)
      {
        ["site.green"] = true
      },
      NativeReportingRuleCatalog.Current);
  NativeStage03ChecklistGenerationResult second =
    NativeStage03ChecklistGenerator.Generate(
      "总平模型",
      new Dictionary<string, bool>(StringComparer.Ordinal)
      {
        ["site.green"] = true
      },
      NativeReportingRuleCatalog.Current);
  Assert.True(first.Supported);
  Assert.Equal(first.Definitions.Select(value => value.CheckId),
    second.Definitions.Select(value => value.CheckId));
  Assert.DoesNotContain(first.Definitions,
    value => value.DisplayName.Contains("不适用"));
}

[Fact]
public void Unsupported_profile_never_falls_back_to_total_plan()
{
  NativeStage03ChecklistGenerationResult result =
    NativeStage03ChecklistGenerator.Generate(
      "单体建筑—地上",
      new Dictionary<string, bool>(StringComparer.Ordinal),
      NativeReportingRuleCatalog.Current);
  Assert.False(result.Supported);
  Assert.Equal("MODEL_PROFILE_NOT_IMPLEMENTED_PHASE1", result.Code);
  Assert.Empty(result.Definitions);
}
```

测试文件显式加入 `using System; using System.Collections.Generic; using System.Linq;`，不依赖未定义的 `Conditions/EmptyConditions` helper。

Evaluator 测试必须覆盖：缺构件红、缺数据红、低置信候选黄、完整且当前证据绿、扫描前灰、旧 02B 成功但最新失败为红；每种 task geometry/property check 都从逐规则 evidence 求值，unsupported 为红，任何角色确认或 bbox 单独存在都不能让几何项变绿；目标/实际比较使用显式映射且按 operator 求值。

- [ ] **Step 2: 运行清单测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage03ChecklistGeneratorTests|FullyQualifiedName~NativeStage03ChecklistEvaluatorTests"
```

Expected: FAIL，因为清单领域不存在。

- [ ] **Step 3: 实现清单定义和结果模型**

```csharp
internal sealed class NativeStage03ChecklistGenerationResult
{
  internal bool Supported { get; set; }
  internal string Code { get; set; } = string.Empty;
  internal string ModelFileType { get; set; } = string.Empty;
  internal IReadOnlyList<NativeReportingCheckDefinition> Definitions { get; set; } =
    Array.Empty<NativeReportingCheckDefinition>();
}

internal enum NativeStage03ChecklistStatus
{
  NotChecked,
  Passed,
  Failed,
  Warning
}

internal sealed class NativeStage03ChecklistItem
{
  internal string CheckId { get; set; } = string.Empty;
  internal string DisplayName { get; set; } = string.Empty;
  internal NativeReportingSourceStage SourceStage { get; set; }
  internal NativeReportingCheckKind CheckKind { get; set; }
  internal string ApplicableBasis { get; set; } = string.Empty;
  internal string CurrentValue { get; set; } = string.Empty;
  internal string Unit { get; set; } = string.Empty;
  internal NativeStage03ChecklistStatus Status { get; set; }
  internal string IssueCode { get; set; } = string.Empty;
  internal string IssueMessage { get; set; } = string.Empty;
  internal string RemediationTarget { get; set; } = string.Empty;
  internal int? ElementId { get; set; }
  internal string ElementUniqueId { get; set; } = string.Empty;
  internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
    Array.Empty<NativeIssueElementReference>();
  internal string FieldKey { get; set; } = string.Empty;
  internal string PropertyId { get; set; } = string.Empty;
  internal string RoleId { get; set; } = string.Empty;
  internal string RuleText { get; set; } = string.Empty;
  internal string TargetKey { get; set; } = string.Empty;
  internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus { get; set; }
  internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
  internal string OfficialEvidenceRef { get; set; } = string.Empty;
}
```

不得增加 `NotApplicable`。

- [ ] **Step 4: 实现生成器和来源分组**

```csharp
internal static class NativeStage03ChecklistGenerator
{
  internal static NativeStage03ChecklistGenerationResult Generate(
    string modelFileType,
    IReadOnlyDictionary<string, bool> projectConditions,
    NativeReportingRuleCatalog catalog);
}
```

生成顺序固定为：Stage01 登记/坐标/目标 → 02A role/attribute/geometry/property checks → target comparisons → 02B 六指标 → 跨阶段 identity/hash → 导出准备。REQUIRED task 总是进入；CONDITIONAL task 仅当 Stage01 condition=true 时进入，因此没有“不适用”结果。

模型类型唯一来源必须是：

```csharp
stage01.Model.GetValue(NativeStage01Keys.ModelFileType)
```

`NativeStage03ScanRequest`、WPF 和 MCP 不得增加 model type 输入。

- [ ] **Step 5: 实现 evidence bundle 和 evaluator**

```csharp
internal sealed class NativeStage03ScanRequest
{
  internal NativeStage03Mode Mode { get; set; } = NativeStage03Mode.Strict;
  internal string ForceReason { get; set; } = string.Empty;
  internal string OutputDirectory { get; set; } = string.Empty;
  internal NativeStage03ScanRequest Clone();
}

internal sealed class NativeStage03SourceEvidenceBundle
{
  internal NativeWorkflowIdentity CurrentIdentity { get; set; }
  internal NativeStage01ReadResult Stage01 { get; set; }
  internal NativeWorkflowResultEnvelope Stage01Result { get; set; }
  internal NativeStage02Preview Stage02A { get; set; }
  internal string Stage02ACurrentInputSnapshotHash { get; set; } = string.Empty;
  internal NativeWorkflowResultEnvelope Stage02AResult { get; set; }
  internal NativeStage02BReadResult Stage02B { get; set; }
  internal NativeWorkflowResultEnvelope Stage02BResult { get; set; }
  internal NativeStage03TechnicalPreflightEvidence TechnicalPreflight { get; set; }
  internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } = Array.Empty<string>();
}

internal sealed class NativeStage03TechnicalPreflightEvidence
{
  internal string NormalizedOutputDirectory { get; set; } = string.Empty;
  internal bool DocumentReady { get; set; }
  internal bool OutputDirectoryWritable { get; set; }
  internal bool RevitIfcExporterAvailable { get; set; }
  internal bool TranslatorDependenciesAvailable { get; set; }
  internal bool ReportWriterAvailable { get; set; }
  internal IReadOnlyList<string> FatalCodes { get; set; } = Array.Empty<string>();
  internal string ProbeHash { get; set; } = string.Empty;
}

internal static class NativeStage03ChecklistEvaluator
{
  internal static IReadOnlyList<NativeStage03ChecklistItem> Evaluate(
    IReadOnlyList<NativeReportingCheckDefinition> definitions,
    NativeStage03SourceEvidenceBundle evidence);
}
```

每个来源 result 先经 Task 3 freshness policy。Stage03 同时调用 02A 的只读全模型捕获，重新计算 element/geometry evidence 和排序后的当前 input snapshot hash；它不得写参数或保存 confirmation。hash/identity/最新 attempt 失败均为红；未执行扫描时才是灰；低置信且非阻断提示为黄；只有写入成功、回读成功、输入 hash 当前且必要 carrier 状态满足该 check 时为绿。

`AttributeRequirement` 按目录的 exact mapping 读取每个适用构件 GUID 回读；mapping 不存在、值空或回读失败分别为稳定红码。`Geometry` check 按 `TaskId + RuleText + ElementUniqueId` 连接 live evidence 与已保存 outcome：任一适用构件失败/unsupported 即红；没有已确认构件为 `MISSING_REQUIRED_ELEMENT`；全部适用构件都有 current `Passed` evidence 才绿。`PropertyConsistency` 读取对应 GUID 回读和几何 evidence，例如“投影面积与几何一致”使用 Task 5 容差；无 evaluator 返回 `PROPERTY_CHECK_UNSUPPORTED_PHASE1` 红。`TargetComparison` 同时读取 Stage01 目标 operator/value 和 02B 最新实际值，映射缺失、任一值缺失或比较不满足分别返回稳定红码。

求值码固定为：Stage01/02B 空值 `MISSING_REQUIRED_DATA`；02A 无已确认构件 `MISSING_REQUIRED_ELEMENT`；候选未确认 `ROLE_CONFIRMATION_REQUIRED`；属性映射/值缺失 `ATTRIBUTE_MAPPING_MISSING/ATTRIBUTE_VALUE_MISSING`；写入失败 `WRITE_FAILED`；回读失败 `READBACK_FAILED`；几何失败 `GEOMETRY_CHECK_FAILED`；几何未实现 `GEOMETRY_CHECK_UNSUPPORTED_PHASE1`；属性一致性失败/未实现 `PROPERTY_CHECK_FAILED/PROPERTY_CHECK_UNSUPPORTED_PHASE1`；目标映射/值/比较失败 `TARGET_COMPARISON_MAPPING_MISSING/TARGET_VALUE_MISSING/TARGET_COMPARISON_FAILED`；freshness 非 Current 使用对应 `WORKFLOW_<STATE>`；官方载体未证实 `OFFICIAL_CARRIER_PENDING_GOLDEN_RVT`；低置信但不阻断 `LOW_CONFIDENCE_CANDIDATE`。除低置信为 `Warning`、扫描前为 `NotChecked` 外，上述业务码均为 `Failed`；不得用自由文案决定颜色。

- [ ] **Step 6: 改造 Scanner 和 scan hash**

`NativeStage03ScanRequest` 增加 `OutputDirectory`，`Clone()` 复制后由 scanner 以 `Path.GetFullPath` 规范化；相对/空路径是 `INVALID_OUTPUT_DIRECTORY` technical fatal。`NativeStage03TechnicalPreflightService.Probe(...)` 使用创建后立即删除的随机 probe 文件验证目录/报告写入，不覆盖任何用户文件；验证 Revit 2020 IFC export API 可构造、既有转译依赖和配置文件可读。它只证明 preflight dependency 可用，不能冒充 RAW IFC 已生成；真实 RAW/转译/报告失败仍由执行阶段返回 technical error。输出目录和 `ProbeHash` 都进入 ScanHash，导出请求的目录必须与 confirmed scan 规范化路径相同，否则重新扫描后 lease 失效。

`NativeStage03Scanner.Scan` 不再把 02A preview fields 当完整清单。它必须读取三阶段结果、重算 02A 全模型几何、执行 technical preflight、生成 definitions、求值 checklist。`DOCUMENT_UNAVAILABLE`、`MODEL_PROFILE_NOT_IMPLEMENTED_PHASE1`、workflow schema/result-hash/document/model-type/rule-package mismatch、`INVALID_OUTPUT_DIRECTORY`、`OUTPUT_DIRECTORY_NOT_WRITABLE`、`IFC_EXPORTER_UNAVAILABLE`、`TRANSLATOR_DEPENDENCY_UNAVAILABLE`、`REPORT_WRITER_UNAVAILABLE` 进入 `TechnicalFatalCodes`；其余 `Status == Failed` 才派生 `BusinessBlockers`。同一个 code 不得同时出现在两组，`Warning` 不阻断；Strict 和 ForcedTest 都不能绕过 technical fatal。

`NativeStage03ScanResult` 增加：

```csharp
internal sealed class NativePluginRuntimeIdentity
{
  internal string ProductVersion { get; set; } = string.Empty;
  internal string AssemblyVersion { get; set; } = string.Empty;
  internal string InformationalVersion { get; set; } = string.Empty;
  internal string CommitSha { get; set; } = string.Empty;
  internal string AddinDllPath { get; set; } = string.Empty;
  internal string AddinDllSha256 { get; set; } = string.Empty;
}

internal string ModelFileType { get; set; } = string.Empty;
internal string RevitVersion { get; set; } = string.Empty;
internal string NormalizedOutputDirectory { get; set; } = string.Empty;
internal string PreflightHash { get; set; } = string.Empty;
internal string Stage02ACurrentInputSnapshotHash { get; set; } = string.Empty;
internal NativeWorkflowResultEnvelope Stage01WorkflowResult { get; set; }
internal NativeWorkflowResultEnvelope Stage02AWorkflowResult { get; set; }
internal NativeWorkflowResultEnvelope Stage02BWorkflowResult { get; set; }
internal NativePluginRuntimeIdentity PluginRuntime { get; set; }
internal IReadOnlyList<NativeStage03ChecklistItem> Checklist { get; set; } =
  Array.Empty<NativeStage03ChecklistItem>();
internal int PassedCount { get; set; }
internal int FailedCount { get; set; }
internal int WarningCount { get; set; }
internal int NotCheckedCount { get; set; }
```

`NativePluginRuntimeIdentity` 由 scanner 从当前已加载 `BIMBaoGui.RevitAddin` assembly 捕获，不允许 UI/MCP 传入；Product/Assembly/InformationalVersion、程序集绝对 Location、当前 DLL SHA 和 informational version 中 `.sha.<40hex>`（若存在）分别原样保存。Task 11 的报告只投影该 confirmed scan 身份，不在写报告时悄悄换成另一程序集。

聚合 check 的 `Elements` 按 UniqueId 排序去重；标量 `ElementId/ElementUniqueId` 仅在恰好一个构件时填充，多个构件时为空并由列表驱动定位。ScanHash 包含规范化文档路径、规则三元身份、插件 runtime 六字段、规范化输出目录、preflight hash、02A 当前全模型 input snapshot hash、每项 checkId/kind/source/status/fieldKey/propertyId/roleId/ruleText/targetKey/sorted element UniqueIds/officialCarrierStatus/officialProjectionCarrierId/officialEvidenceRef 和三个来源 ResultHash；不得把可变问题文案或时间戳作为 identity。这样相同业务数据由不同 DLL 扫描时不会复用同一个 scan-evidence 文件名。

- [ ] **Step 7: 运行 Stage03 核心合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage03Checklist|FullyQualifiedName~NativeStage03TechnicalPreflight|FullyQualifiedName~NativeStage03Stage01ValidationPolicyTests"
python -m pytest tests/test_revit_addin_stage03_revit_contract.py -q
```

Expected: PASS；静态合同确认 Stage03 无 model type 输入、无 `NotApplicable`、同时读取 01/02A/02B。

- [ ] **Step 8: 提交 Stage03 清单核心**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage03 `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistGeneratorTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistEvaluatorTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03TechnicalPreflightPolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03Stage01ValidationPolicyTests.cs `
  tests/test_revit_addin_stage03_revit_contract.py
git commit -m "feat(stage03): compile strict total-plan checklist"
```

---

### Task 10: 完成 Stage03 四色列表、问题回溯和复查交互

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03IssueCompiler.cs`
- Create: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ChecklistPresentation.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03IssueCompilerTests.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistPresentationTests.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs`
- Modify: `src/BIMBaoGui.RevitAddin/WorkspaceControl.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BView.cs`
- Modify: `tests/test_revit_addin_stage03_ui_contract.py`

**Interfaces:**
- Consumes: Task 9 checklist、Task 6 IssueHub/navigation、Task 8 跨阶段视图跳转。
- Produces: 红/黄项到 Stage01/02A/02B 的精确路由、全部检查和“复查该项（重读全部依赖）”。

- [ ] **Step 1: 写颜色和修复路由 RED 测试**

```csharp
[Theory]
[InlineData(NativeStage03ChecklistStatus.NotChecked, "#FFE5E7EB")]
[InlineData(NativeStage03ChecklistStatus.Passed, "#FFDCFCE7")]
[InlineData(NativeStage03ChecklistStatus.Failed, "#FFFEE2E2")]
[InlineData(NativeStage03ChecklistStatus.Warning, "#FFFEF3C7")]
public void Checklist_status_has_stable_background(
  NativeStage03ChecklistStatus status,
  string expected)
{
  Assert.Equal(expected, NativeStage03ChecklistPresentation.Background(status));
}

[Fact]
public void Project_metric_issue_routes_to_exact_02b_property()
{
  NativeIssueRecord issue = NativeStage03IssueCompiler.Compile(
    new NativeStage03ChecklistItem
    {
      CheckId = "STAGE02B.METRIC.201a00ac-3672-5ded-83d2-ed96f81bfabf",
      SourceStage = NativeReportingSourceStage.Stage02B,
      Status = NativeStage03ChecklistStatus.Failed,
      IssueCode = "MISSING_REQUIRED_DATA",
      PropertyId = "201a00ac-3672-5ded-83d2-ed96f81bfabf",
      RemediationTarget = "OPEN_STAGE02B"
    });
  Assert.Equal(NativeIssueNavigationAction.OpenStage02B, issue.Route);
  Assert.Equal("201a00ac-3672-5ded-83d2-ed96f81bfabf", issue.PropertyId);
}
```

该测试直接构造 checklist item，不定义 `FailedMetric` 隐藏 helper。

- [ ] **Step 2: 运行 presentation/issue 测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo `
  --filter "FullyQualifiedName~NativeStage03IssueCompilerTests|FullyQualifiedName~NativeStage03ChecklistPresentationTests"
```

Expected: FAIL，因为 Stage03 issue/presentation policy 不存在。

- [ ] **Step 3: 实现清单到问题的稳定映射**

使用以下接口：

```csharp
internal static class NativeStage03ChecklistPresentation
{
  internal static string Background(NativeStage03ChecklistStatus status);
  internal static string StatusText(NativeStage03ChecklistStatus status);
}

internal static class NativeStage03IssueCompiler
{
  internal static NativeIssueRecord Compile(NativeStage03ChecklistItem item);
}
```

`NativeStage03IssueCompiler.Compile(item)` 固定输出：

```text
Stage01 field/target       → OpenStage01 + FieldKey
02A existing element      → Route=Select + ElementId/UniqueId；列表另提供 Zoom/Isolate/RestoreView 请求
02A missing semantic role → OpenStage02A + RoleId，不附造假 ElementId
02B metric                → OpenStage02B + PropertyId
CrossStage/Export         → 保持 Stage03，显示复查或证据说明
```

每条红项必须填满 `Missing`、`Impact`、`Remediation`；黄色可以没有 Missing，但必须有影响与复查建议。

- [ ] **Step 4: 建立 Stage03 列表和固定颜色**

`NativeStage03View` 使用一个可滚动 `ListView/GridView`，列顺序固定为：

```text
检查项名称 | 来源阶段 | 适用依据 | 当前值 | 状态 | 问题说明 | 处理入口
```

颜色使用 Step 1 的 ARGB；文字状态分别为“未检查/通过/失败/警告”。初次打开由 generator 建空证据清单显示灰色，点击“执行全部检查”后替换为求值结果。

- [ ] **Step 5: 实现全部检查和单项聚焦复查**

Task 9 已加入 `OutputDirectory`；本 Task 只再加入聚焦字段，最终请求形状相关部分为：

```csharp
internal string OutputDirectory { get; set; } = string.Empty;
internal string FocusCheckId { get; set; } = string.Empty;
```

`Clone()` 同时复制 `OutputDirectory` 和 `FocusCheckId`。“复查该项”仍调用完整 `RequestStage03Scan` 并重读所有依赖，输出目录取当前 UI 规范化值；完成后滚动并选中 FocusCheckId。不得只沿用其他旧绿项。按钮 tooltip 明确“为避免依赖过期，本操作会重新读取完整清单”。

- [ ] **Step 6: 接通 Workspace 路由**

```csharp
private void Navigate(NativeIssueRecord issue)
{
  switch (issue.Route)
  {
    case NativeIssueNavigationAction.OpenStage01:
      ShowStage01(); _stage01View.NavigateToField(issue.FieldKey); break;
    case NativeIssueNavigationAction.OpenStage02A:
      ShowStage02A(); _stage02AView.NavigateToIssue(issue); break;
    case NativeIssueNavigationAction.OpenStage02B:
      ShowStage02B(); _stage02BView.NavigateToMetric(issue.PropertyId); break;
    default:
      ShowStage03(); _stage03View.NavigateToCheck(issue.CheckId); break;
  }
}
```

构件定位 action 仍交给 Task 6 ExternalEvent，不在 Workspace 直接访问 Revit API。

- [ ] **Step 7: 运行 UI/路由合同**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage03Issue|FullyQualifiedName~NativeStage03ChecklistPresentation"
python -m pytest tests/test_revit_addin_stage03_ui_contract.py `
  tests/test_revit_addin_stage02b_ui_contract.py `
  tests/test_revit_addin_stage02_ui_contract.py -q
```

Expected: PASS；UI 中不存在“不适用”控件，红项有处理入口，Stage03 不包含数据编辑器。

- [ ] **Step 8: 提交 Stage03 交互**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage03/NativeStage03IssueCompiler.cs `
  src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ChecklistPresentation.cs `
  src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs `
  src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs `
  src/BIMBaoGui.RevitAddin/WorkspaceControl.cs `
  src/BIMBaoGui.RevitAddin/Stage01/NativeStage01View.cs `
  src/BIMBaoGui.RevitAddin/Stage02/NativeStage02View.cs `
  src/BIMBaoGui.RevitAddin/Stage02B/NativeStage02BView.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03IssueCompilerTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ChecklistPresentationTests.cs `
  tests/test_revit_addin_stage03_ui_contract.py
git commit -m "feat(stage03): add checklist navigation and recheck"
```

---

### Task 11: 恢复有理由的测试强制导出并扩展报告/MCP

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Models.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03Scanner.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03WorkflowService.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03ReportWriter.cs`
- Modify: `src/BIMBaoGui.RevitAddin/Stage03/NativeStage03View.cs`
- Modify: `src/BIMBaoGui.McpContracts/ToolContracts.cs`
- Modify: `src/BIMBaoGui.McpServer/BimBaoGuiTools.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs`
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage03Adapter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ReportWriterTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03GatePolicyTests.cs`
- Modify: `tests/BIMBaoGui.RevitAddin.Tests/NativeStage03Stage01ValidationPolicyTests.cs`
- Modify: `tests/test_revit_addin_stage03_ui_contract.py`
- Modify: `tests/test_revit_addin_mcp_stage03_contract.py`
- Modify: `docs/revit-addin/README.md`

**Interfaces:**
- Consumes: Task 9/10 红黄绿灰 checklist 和现有 Strict/ForcedTest export pipeline。
- Produces: 强制理由门禁、`IsTestExport/CountsAsNormalExportPass/OfficialAcceptanceStatus`、含 checklist/文档路径/插件运行身份的 scan/fields/validation/failure 报告，以及 MCP `force_reason + output_directory` scan parity 和显式结果投影。

- [ ] **Step 1: 反转现有 Force 合同为 RED 测试**

把“空原因也可 Force”的旧断言替换为：

```csharp
[Fact]
public void Forced_test_requires_reason_and_never_bypasses_technical_fatal()
{
  NativeStage03GateDecision noReason = NativeStage03GatePolicy.Evaluate(
    NativeStage03Mode.ForcedTest, " ", Array.Empty<string>(),
    new[] { "MISSING_DATA" }, 1);
  Assert.False(noReason.AllowExport);
  Assert.Contains(NativeStage03Codes.ForceReasonRequired, noReason.Blockers);

  NativeStage03GateDecision businessOnly = NativeStage03GatePolicy.Evaluate(
    NativeStage03Mode.ForcedTest, "开发测试缺项导出", Array.Empty<string>(),
    new[] { "MISSING_DATA" }, 1);
  Assert.True(businessOnly.AllowExport);
  Assert.Contains("MISSING_DATA", businessOnly.BypassedBusinessBlockers);

  NativeStage03GateDecision technical = NativeStage03GatePolicy.Evaluate(
    NativeStage03Mode.ForcedTest, "开发测试", new[] { "DOCUMENT_UNAVAILABLE" },
    Array.Empty<string>(), 1);
  Assert.False(technical.AllowExport);
}
```

- [ ] **Step 2: 运行 Gate/UI/MCP 测试确认 RED**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage03GatePolicyTests|FullyQualifiedName~NativeStage03ReportWriterTests"
python -m pytest tests/test_revit_addin_stage03_ui_contract.py `
  tests/test_revit_addin_mcp_stage03_contract.py -q
```

Expected: FAIL；当前 Gate 忽略 forceReason，UI/Server 不传理由。

- [ ] **Step 3: 修正 Gate 和执行结果身份**

保留现有 `Evaluate` 签名，在 ForcedTest 分支先执行：

```csharp
if (string.IsNullOrWhiteSpace(forceReason))
  blockers.Add(NativeStage03Codes.ForceReasonRequired);
```

技术 fatal 与 `NoExportableFields` 始终留在 Blockers；只有在它们为空且 reason 非空时，business blockers 进入 `BypassedBusinessBlockers`。

`NativeStage03ExecutionResult` 增加：

```csharp
internal bool IsTestExport { get; set; }
internal bool CountsAsNormalExportPass { get; set; }
internal string OfficialAcceptanceStatus { get; set; } = "PENDING";
internal IReadOnlyList<NativeStage03ChecklistItem> Checklist { get; set; } =
  Array.Empty<NativeStage03ChecklistItem>();
```

ForcedTest 成功必须固定为 `true/false/PENDING`；红色 checklist 原样保留，不能由 H-IFC exact readback 改绿。

- [ ] **Step 4: 恢复 UI 强制原因与明显测试标识**

`NativeStage03View` 恢复 `_forceReason`，仅 ForcedTest 导出模式启用；空值时禁用“生成强制导出许可/导出”，但“执行全部检查”和单项复查始终可用。视觉文案固定为：

```text
测试强制导出（不会计为正常通过）
强制原因（必填）
```

ForcedTest 产物继续使用 `_FORCED_TEST_HIFC.ifc` 后缀；Strict 不显示测试 badge。

- [ ] **Step 5: 扩展 JSON/Markdown 报告**

`fields.json` 新增 `checklist` 数组；每次完成求值且报告目录可写的 scan 都生成 `<scan_hash>-stage03-scan-evidence.json`，即使存在业务红项也生成，供“只检查未导出”的场景审计。该文件、`validation.json` 和失败报告共享以下身份键：

```json
{
  "report_kind": "STAGE03_SCAN",
  "is_test_export": true,
  "counts_as_normal_export_pass": false,
  "official_acceptance_status": "PENDING",
  "checklist_counts": {
    "passed": 0,
    "failed": 1,
    "warning": 0,
    "not_checked": 0
  },
  "workflow_results": {
    "stage01": {"run_id":"...","result_hash":"...","input_snapshot_hash":"..."},
    "stage02a": {"run_id":"...","result_hash":"...","input_snapshot_hash":"..."},
    "stage02b": {"run_id":"...","result_hash":"...","input_snapshot_hash":"..."}
  },
  "rule_package": {"id":"HBR-WUHAN-PLANNING","version":"1.0.0","sha256":"..."},
  "document_fingerprint": "...",
  "document_path": "D:\\absolute\\current.rvt",
  "plugin_runtime": {
    "product_version": "0.4.3",
    "assembly_version": "0.4.3.0",
    "informational_version": "0.4.3+build.123.sha.<40-hex-commit>",
    "commit_sha": "<40-hex-commit>",
    "addin_dll_path": "D:\\absolute\\BIMBaoGui.RevitAddin.dll",
    "addin_dll_sha256": "<64-hex-sha>"
  },
  "revit_version": "2020",
  "scan_hash": "...",
  "normalized_output_directory": "...",
  "preflight_hash": "...",
  "technical_fatals": []
}
```

省略号和尖括号只表示上面展示的是 schema 形状；实现必须从当前 scan/envelope 填入非空真实值，不把占位文本写入报告。`document_path` 使用已保存项目的 `Path.GetFullPath(Document.PathName)`；未保存文档返回 `UNSAVED_DOCUMENT` technical fatal。`plugin_runtime` 必须原样投影 Task 9 confirmed scan 捕获的程序集身份，并在写报告前重新 hash 同一绝对 DLL；字节变化返回 `RUNTIME_ARTIFACT_CHANGED`，不接受 UI/MCP 传入版本或 commit。普通本地开发 build 可以留下空 CommitSha，但 Task 12 的 CI 发布合同必须生成 `0.4.3+build.<run>.sha.<40hex>`，Task 14 会把任何空值或格式偏差判为不可验收，不能冒充最终 artifact。

`NativeStage03ReportWriter.WriteScanEvidence(...)` 在 scanner 完成 checklist/scan hash 后、创建 lease 前调用；写入失败追加 `REPORT_WRITER_UNAVAILABLE` 并禁止 lease。目标目录必须是本次 preflight 的规范化目录；若同名 scan evidence 已存在，只在字节完全一致时复用，否则返回 `SCAN_EVIDENCE_COLLISION`，不得覆盖。每个 checklist item 输出 source_stage、applicable_basis、status、issue_code、remediation_target、field_key、property_id、role_id、rule_text、target_key、sorted element refs、official_carrier_status、official_projection_carrier_id、official_evidence_ref。`IFCFLUX_MANUAL_PENDING` 保留；内部 exact 成功不能改 official_acceptance_status。报告测试回读 JSON 并验证三个 result hash、rule identity、document fingerprint/path、插件运行身份、DLL SHA、scan/preflight hash 与输入对象和当前程序集完全一致；另测业务红项仍写 scan evidence、未保存文档/伪造 informational version/同名异内容均阻断。

- [ ] **Step 6: 恢复 MCP `force_reason`，工具数保持 13**

`Stage03ScanCommand.ForceReason` 已存在；连同 Task 9 新增的预检输出目录贯通服务端和 adapter：

```csharp
public static Task<string> Stage03Scan(
  NamedPipeBridgeService bridge,
  string mode,
  string force_reason,
  string output_directory,
  int? revit_process_id,
  CancellationToken cancellationToken);

internal Task<string> ScanAsync(
  string mode,
  string forceReason,
  string outputDirectory,
  CancellationToken cancellationToken);
```

Server payload 使用 `new { mode, force_reason, output_directory }`；router 的 `Stage03ScanPayload` 增加对应 snake_case 字段；adapter 映射到 `NativeStage03ScanRequest.ForceReason/OutputDirectory`。WPF 也必须在 scan 时传当前输出目录，并在 export 时复用 confirmed scan 的规范化目录。strict 允许空理由；forced_test 空理由或任何 technical fatal 都返回 blocker，且 `_scanLeases.Create(...)` 不得执行。

`McpStage03Adapter.ProjectScan` 和 `ProjectExecution` 是手工字典，必须显式新增而不是依赖序列化器自动发现：

```text
ProjectScan:
  normalized_output_directory, preflight_hash,
  checklist_counts, checklist

ProjectExecution:
  is_test_export, counts_as_normal_export_pass,
  official_acceptance_status, checklist_counts, checklist
```

每个 MCP checklist item 投影 `check_id/check_kind/display_name/source_stage/applicable_basis/current_value/unit/status/issue_code/remediation_target/field_key/property_id/role_id/rule_text/target_key/element_id/element_unique_id/official_carrier_status/official_projection_carrier_id/official_evidence_ref`。行为测试分别覆盖 strict、forced_test 有理由、forced_test 空理由、output directory 不可写和 translator 缺失；后三种不得创建 lease，强制成功 execution 必须返回 `true/false/PENDING` 且红项仍为红。

- [ ] **Step 7: 运行 Stage03/MCP/report 回归**

```powershell
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~NativeStage03"
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj `
  -c Release --nologo
python -m pytest tests/test_revit_addin_stage03_ui_contract.py `
  tests/test_revit_addin_mcp_stage03_contract.py `
  tests/test_revit_addin_mcp_contract.py -q
```

Expected: PASS；`McpToolNames.Approved` 仍正好 13 项。

- [ ] **Step 8: 提交门禁、报告和 MCP parity**

```powershell
git add src/BIMBaoGui.RevitAddin/Stage03 `
  src/BIMBaoGui.McpContracts/ToolContracts.cs `
  src/BIMBaoGui.McpServer/BimBaoGuiTools.cs `
  src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs `
  src/BIMBaoGui.RevitAddin/McpBridge/McpStage03Adapter.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03ReportWriterTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03GatePolicyTests.cs `
  tests/BIMBaoGui.RevitAddin.Tests/NativeStage03Stage01ValidationPolicyTests.cs `
  tests/test_revit_addin_stage03_ui_contract.py `
  tests/test_revit_addin_mcp_stage03_contract.py `
  docs/revit-addin/README.md
git commit -m "feat(stage03): enforce marked forced-test exports"
```

---

### Task 12: 冻结 v0.4.3 产品、安装器、CI 和功能基线

**Files:**
- Create: `tests/test_revit_addin_v043_contract.py`
- Create: `specs/revit-addin/v0.4.3-functional-baseline.json`
- Create: `docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-checklist.md`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Modify: `src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj`
- Modify: `src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj`
- Modify: `src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj`
- Modify: `installer/Install-Revit2020.ps1`
- Modify: `installer/McpProbe.cmd`
- Modify: `installer/mcp-server-config.example.json`
- Modify: `.github/workflows/build-revit-mcp.yml`
- Modify: `docs/revit-addin/README.md`
- Modify: `tools/build_revit_functional_baseline.py`
- Modify: `tests/test_revit_addin_installer_contract.py`
- Modify: `tests/test_revit_addin_mcp_installer_contract.py`
- Modify: `tests/test_revit_addin_mcp_non_regression.py`
- Modify: `tests/test_revit_addin_v042_contract.py`

**Interfaces:**
- Consumes: Tasks 1–11 冻结后的生产源。
- Produces: 全表面 `0.4.3/0.4.3.0`、artifact `BIMBaoGui-Revit2020-Native-MCP-v0.4.3` 和确定性 source baseline。

- [ ] **Step 1: 写版本发布 RED 测试**

把 `test_revit_addin_v042_contract.py` 中读取当前 csproj/installer/workflow/README 的 0.4.2 断言移到新 `test_revit_addin_v043_contract.py` 并改为 0.4.3；旧文件只保留历史证据断言：`v0.4.2-functional-baseline.json` 的 `product_version/source_branch/installer_artifact` 仍为冻结的 0.4.2 值，且生成 v0.4.3 时不会改写该文件。

```python
def test_all_product_and_installer_versions_are_043():
    for project in PRODUCT_PROJECTS:
        text = project.read_text(encoding="utf-8")
        assert "<Version>0.4.3</Version>" in text
        assert "<FileVersion>0.4.3.0</FileVersion>" in text
        assert "<AssemblyVersion>0.4.3.0</AssemblyVersion>" in text
    installer = INSTALLER.read_text(encoding="utf-8-sig")
    assert '$mcpVersion = "0.4.3"' in installer
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "BIMBaoGui-Revit2020-Native-MCP-v0.4.3" in workflow
    assert "build_hbr_rulepack_v043.py" in workflow
    assert "feat/revit-native-total-plan-phase1-v0.4.3" in workflow
```

- [ ] **Step 2: 运行发布合同确认 RED**

```powershell
python -m pytest tests/test_revit_addin_v043_contract.py `
  tests/test_revit_addin_v042_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py -q
```

Expected: FAIL，因为所有发布表面仍为 0.4.2。

- [ ] **Step 3: 统一产品和安装器身份**

四个产品 csproj 统一为：

```xml
<Version>0.4.3</Version>
<FileVersion>0.4.3.0</FileVersion>
<AssemblyVersion>0.4.3.0</AssemblyVersion>
```

安装目录固定为 `%LOCALAPPDATA%\BIMBaoGui\McpServer\0.4.3`。安装分支定义 `$legacyMcpVersions = @('0.4.0','0.4.1','0.4.2')`，只逐个 `Join-Path $mcpBaseRoot $version` 清理这三项和待替换的 `0.4.3`；删除基线按 semver 枚举所有目录的循环。卸载分支只删除 `$mcpServerRoot`（即 0.4.3）、本插件 manifest/product/config/bridge 文件，绝不枚举删除其他版本。installer 合同和 smoke 在 `9.9.9` 目录写 sentinel，安装及卸载后都必须仍存在。CI artifact 名精确为 `BIMBaoGui-Revit2020-Native-MCP-v0.4.3`。

`install-evidence.json` 保留现有字段，并补全四个源路径：`sourceDll/sourceContractsDll/sourceHifcCoreDll/sourceMcpServerExe`；每个源路径和对应 installed path 都必须为绝对路径，分别现场计算 SHA 后一致。不得用一个 hash 字段代替其他三个文件的源/安装比对。

- [ ] **Step 4: 更新 CI paths 和 artifact 合同**

workflow 触发路径必须覆盖：

```text
specs/hbr-rules/v1/source/hbr_rule_source.v0.4.3-overlay.json
tools/build_hbr_rulepack_v043.py
src/BIMBaoGui.RevitAddin/Stage02B/**
src/BIMBaoGui.RevitAddin/Workflow/**
src/BIMBaoGui.RevitAddin/Issues/**
tests/test_hbr_rulepack_v043.py
```

`on.push.branches` 必须新增精确分支 `feat/revit-native-total-plan-phase1-v0.4.3`，保留历史分支触发，不用宽泛分支替代当前发布合同。

workflow 中所有 live 发布字面量一次性迁移并由 `test_revit_addin_v043_contract.py` 精确断言，不能只改 artifact 名：AssemblyVersion `0.4.3.0`、InformationalVersion 前缀 `0.4.3+build.`、三个产品 DLL 版本检查 `0.4.3.0`、隔离安装 MCP 目录 `0.4.3`、install evidence `productVersion=0.4.3`、artifact `...v0.4.3`。测试另断言 workflow live 路径中不再出现 `0.4.2`，历史分支触发字符串除外。

`Verify native and MCP contracts` 的显式 pytest 列表必须新增：

```text
tests/test_revit_addin_workflow_result_contract.py
tests/test_revit_addin_stage02b_revit_contract.py
tests/test_revit_addin_stage02b_ui_contract.py
tests/test_revit_addin_v043_contract.py
```

`Verify shared HBR rule database` 必须包含 `tests/test_hbr_rulepack_v043.py`。artifact 仍包含 Install/Uninstall/McpProbe/PowerShell installer/addin/config/README/SHA256SUMS、三个 add-in DLL 和单文件 MCP exe。

- [ ] **Step 5: 生成冻结功能基线**

```powershell
python tools/build_revit_functional_baseline.py `
  --version 0.4.3 `
  --branch feat/revit-native-total-plan-phase1-v0.4.3 `
  --output specs/revit-addin/v0.4.3-functional-baseline.json
```

生成器必须对四个生产源码根下的 tracked `*.cs` 排序 hash，并输出 `source_snapshot_sha256`。将静态旧 `CAPABILITIES` 改为按产品版本选择的 `CAPABILITIES_BY_VERSION`；`0.4.3` 精确冻结以下能力边界：Stage01 总平登记/坐标/规划目标；02A 全模型/自主选择、显式语义确认、可靠构件面积与逐规则几何证据；02B 六项人工实际指标、指标级部分成功且无项目自动汇总；Stage03 四态严格清单/问题回溯/有理由测试强制导出；MCP 仍 13 tools；官方载体按 propertyId fail-closed。delivery 的 external acceptance 固定为 `Golden RVT -> official HIFCTool -> IFCFlux exact identity`，schema 升为 `BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V4`，payload schema 仍 `0.9.1`。

`tests/test_revit_addin_mcp_non_regression.py` 的 live source/hash 比较改读 `v0.4.3-functional-baseline.json`，并断言上述 capabilities、delivery、branch/artifact/version；v0.4.2 测试只读取冻结 JSON 验证历史 metadata/hash 自洽，不再拿当前源码与 v0.4.2 `sha256_by_path` 比较。生成器拒绝未定义版本，且测试证明生成 v0.4.3 不改写 v0.4.2 文件。

- [ ] **Step 6: 写实机验收清单文档**

`native-total-plan-phase1-v0.4.3-checklist.md` 固定包含三场景：空模型、不完整模型、Golden RVT；每场景记录插件 build/commit、RVT SHA、规则 SHA、01/02A/02B/03 结果、保存重开、普通/强制导出、官方 HIFCTool、IFC SHA、IFCFlux 版本/报告 SHA。状态字段固定为：

```text
AUTOMATED_VERIFIED
REVIT2020_HOST_VERIFIED
OFFICIAL_HIFC_EXPORT_VERIFIED
IFCFLUX_CHECKER_VERIFIED
```

- [ ] **Step 7: 运行发布合同和 identity 测试**

```powershell
python -m pytest tests/test_revit_addin_v043_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py `
  tests/test_revit_addin_mcp_non_regression.py `
  tests/test_revit_addin_v042_contract.py -q
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release --nologo --filter "FullyQualifiedName~PluginRuntimeIdentityTests"
```

Expected: PASS；所有版本表面一致，无分支 skip。

- [ ] **Step 8: 提交 v0.4.3 发布身份**

```powershell
git add src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj `
  src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj `
  src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  installer .github/workflows/build-revit-mcp.yml `
  docs/revit-addin/README.md `
  docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-checklist.md `
  tools/build_revit_functional_baseline.py `
  specs/revit-addin/v0.4.3-functional-baseline.json `
  tests/test_revit_addin_v043_contract.py `
  tests/test_revit_addin_installer_contract.py `
  tests/test_revit_addin_mcp_installer_contract.py `
  tests/test_revit_addin_mcp_non_regression.py `
  tests/test_revit_addin_v042_contract.py
git commit -m "chore(release): freeze native product v0.4.3"
```

---

### Task 13: 运行完整自动化、隔离安装 smoke、远端 CI 和 artifact 验证

**Files:**
- Modify only if a verification command exposes a reproducible defect; every production fix requires a failing regression test first.
- Do not commit: `bin/`, `obj/`, `TestResults/`, `artifacts/`, downloaded Actions artifacts, ZIP、RVT、IFC 或日志。

**Interfaces:**
- Consumes: Task 12 的冻结分支。
- Produces: 全部本地门禁、隔离安装/卸载证据、成功 Windows workflows 和已校验 v0.4.3 artifact。

- [ ] **Step 1: 恢复所有项目依赖**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
$env:PYTHONDONTWRITEBYTECODE='1'
python -m pip install --disable-pip-version-check pytest==8.3.5 jsonschema==4.23.0

dotnet restore src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj
dotnet restore src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj
dotnet restore src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj
dotnet restore src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj
dotnet restore src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj
dotnet restore tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj
dotnet restore tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj
dotnet restore tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj
dotnet restore tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
dotnet restore tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj
```

Expected: 全部 restore exit 0。

- [ ] **Step 2: 运行全部 Python 和 .NET 测试**

```powershell
python -m pytest tests -q

dotnet test tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj -c Release --no-restore --nologo
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release --no-restore --nologo
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release --no-restore --nologo
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release --no-restore --nologo
```

Expected: 所有项目 0 failed、0 skipped；记录实际通过数量，不能沿用旧 v0.4.2 数字。

- [ ] **Step 3: 严格构建全部产品和历史共享规则消费者**

```powershell
$commit = git rev-parse HEAD
dotnet build src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true -p:HbrBuildNumber=local -p:HbrCommitSha=$commit
dotnet build src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
dotnet build tools/BIMBaoGui.McpSmoke/BIMBaoGui.McpSmoke.csproj -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
```

Expected: 每个 build 0 warnings、0 errors。

- [ ] **Step 4: 本地复现 artifact 布局**

```powershell
$releaseRoot = Join-Path $env:TEMP ('BIMBaoGui-v043-local-release-' + [Guid]::NewGuid().ToString('N'))
$artifactRoot = Join-Path $releaseRoot 'BIMBaoGui-Revit2020-Native-MCP-v0.4.3'
$addinPayload = Join-Path $artifactRoot 'BIMBaoGui.RevitAddin'
$mcpPayload = Join-Path $artifactRoot 'BIMBaoGui.McpServer'
New-Item -ItemType Directory -Force -Path $addinPayload,$mcpPayload | Out-Null

dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  -c Release -r win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:TreatWarningsAsErrors=true

Copy-Item -LiteralPath src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.RevitAddin.dll -Destination $addinPayload
Copy-Item -LiteralPath src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.McpContracts.dll -Destination $addinPayload
Copy-Item -LiteralPath src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.HifcCore.dll -Destination $addinPayload
Copy-Item -LiteralPath src/BIMBaoGui.McpServer/bin/Release/net8.0/win-x64/publish/BIMBaoGui.McpServer.exe -Destination $mcpPayload
foreach ($copy in @(
  @('src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.RevitAddin.pdb', $addinPayload),
  @('src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.McpContracts.pdb', $addinPayload),
  @('src/BIMBaoGui.RevitAddin/bin/Release/net48/BIMBaoGui.HifcCore.pdb', $addinPayload),
  @('src/BIMBaoGui.McpServer/bin/Release/net8.0/win-x64/publish/BIMBaoGui.McpServer.pdb', $mcpPayload)
)) {
  if (Test-Path -LiteralPath $copy[0] -PathType Leaf) {
    Copy-Item -LiteralPath $copy[0] -Destination $copy[1]
  }
}
Copy-Item -LiteralPath installer/Install.cmd,installer/Uninstall.cmd,installer/McpProbe.cmd,installer/Install-Revit2020.ps1,installer/BIMBaoGui.RevitAddin.addin,installer/mcp-server-config.example.json -Destination $artifactRoot
Copy-Item -LiteralPath docs/revit-addin/README.md -Destination (Join-Path $artifactRoot 'README.md')

$artifactRootFull = (Resolve-Path -LiteralPath $artifactRoot).Path
$hashLines = Get-ChildItem -LiteralPath $artifactRootFull -Recurse -File |
  Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
  Sort-Object FullName |
  ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($artifactRootFull, $_.FullName).Replace('\','/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
  }
Set-Content -LiteralPath (Join-Path $artifactRootFull 'SHA256SUMS.txt') `
  -Value $hashLines -Encoding utf8NoBOM
```

Expected: artifact 布局与 workflow 一致；四个 PDB 都按“存在则复制”的相同规则处理，MCP payload 只有一个主 exe 及其明确附属文件。

- [ ] **Step 5: 在重定向 APPDATA/LOCALAPPDATA 下安装/探测/卸载**

```powershell
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$savedErrorActionPreference = $ErrorActionPreference
$env:APPDATA = Join-Path $releaseRoot 'Roaming'
$env:LOCALAPPDATA = Join-Path $releaseRoot 'Local'
$packagedInstaller = Join-Path $artifactRoot 'Install-Revit2020.ps1'
$sentinelRoot = Join-Path $env:LOCALAPPDATA 'BIMBaoGui\McpServer\9.9.9'
$sentinelPath = Join-Path $sentinelRoot 'keep.txt'
New-Item -ItemType Directory -Force -Path $sentinelRoot | Out-Null
Set-Content -LiteralPath $sentinelPath -Value 'preserve-non-target-version' -Encoding utf8NoBOM
$ErrorActionPreference = 'Stop'
try {
  & $packagedInstaller -SourceRoot $artifactRoot -Force
  if (-not $?) { throw 'isolated install failed' }
  $installedMcp = Join-Path $env:LOCALAPPDATA 'BIMBaoGui\McpServer\0.4.3\BIMBaoGui.McpServer.exe'
  $probeOutput = (& $installedMcp --probe 2>&1 | Out-String).Trim()
  $probeExit = $LASTEXITCODE
  if ($probeExit -ne 2 -or $probeOutput -notmatch 'REVIT_NOT_CONNECTED') {
    throw "probe mismatch: exit=$probeExit output=$probeOutput"
  }
  $productRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin'
  $manifestPath = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin'
  $evidencePath = Join-Path $productRoot 'install-evidence.json'
  $evidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
  $pairs = @(
    @($evidence.sourceDll, $evidence.installedDll, $evidence.sourceDllSha256),
    @($evidence.sourceContractsDll, $evidence.installedContractsDll, $evidence.contractsDllSha256),
    @($evidence.sourceHifcCoreDll, $evidence.installedHifcCoreDll, $evidence.hifcCoreDllSha256),
    @($evidence.sourceMcpServerExe, $evidence.installedMcpServerExe, $evidence.mcpServerExeSha256)
  )
  foreach ($pair in $pairs) {
    if (-not [IO.Path]::IsPathFullyQualified($pair[0]) -or
        -not [IO.Path]::IsPathFullyQualified($pair[1])) { throw 'install evidence path is not absolute' }
    $sourceHash = (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash.ToLowerInvariant()
    $installedHash = (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne $pair[2] -or $installedHash -ne $pair[2]) { throw "source/install hash mismatch: $($pair[1])" }
  }
  if (-not [IO.Path]::IsPathFullyQualified($evidence.manifestPath) -or
      -not [IO.Path]::IsPathFullyQualified($evidence.mcpConfigPath)) {
    throw 'manifest/config evidence path is not absolute'
  }
  $config = Get-Content -Raw -LiteralPath $evidence.mcpConfigPath | ConvertFrom-Json
  if (-not [IO.Path]::IsPathFullyQualified($config.mcpServers.'bimbaogui-revit'.command)) {
    throw 'MCP command path is not absolute'
  }
  if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) { throw 'install removed non-target version sentinel' }
  & $packagedInstaller -Uninstall -Force
  if (-not $?) { throw 'isolated uninstall failed' }
  foreach ($removed in @(
    $manifestPath,
    $productRoot,
    (Join-Path $env:LOCALAPPDATA 'BIMBaoGui\McpServer\0.4.3'),
    $evidence.mcpConfigPath
  )) {
    if (Test-Path -LiteralPath $removed) { throw "controlled uninstall residue: $removed" }
  }
  if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) { throw 'uninstall removed non-target version sentinel' }
}
finally {
  $env:APPDATA = $savedAppData
  $env:LOCALAPPDATA = $savedLocalAppData
  $ErrorActionPreference = $savedErrorActionPreference
}
```

Expected: probe 单独保存的 exit code 为 2 且输出 `REVIT_NOT_CONNECTED`；manifest/config 使用绝对路径；四个源/安装文件 SHA 相同；安装和卸载均保留 9.9.9 sentinel；卸载后仅受控 manifest/add-in/MCP 0.4.3/config 被移除。

- [ ] **Step 6: 检查仓库卫生和提交完整性**

```powershell
git diff --check
git status --short
git ls-files | rg '(^|/)(artifacts|bin|obj|TestResults|logs|tmp)/|\.zip$|\.log$|\.tmp$|\.bak$'
git log --oneline eca4639af65e165827810e06340ecb700ffe3e09..HEAD
```

Expected: worktree 干净；无构建产物；每个 Task 是独立绿提交。

- [ ] **Step 7: 推送功能分支并等待两个 Windows workflow**

```powershell
$branch = 'feat/revit-native-total-plan-phase1-v0.4.3'
git fetch origin
$remoteRef = "refs/heads/$branch"
$remoteExists = @(git ls-remote --heads origin $remoteRef).Count -gt 0
if ($remoteExists) {
  $aheadBehind = git rev-list --left-right --count "HEAD...origin/$branch"
  if (($aheadBehind -split '\s+')[1] -ne '0') {
    throw "remote-only commits require inspection: $aheadBehind"
  }
}
git push -u origin $branch
$commit = (git rev-parse HEAD).Trim()
$workflowNames = @('Build BIMBaoGui Revit MCP','Build BIMBaoGui GHA')
$runs = foreach ($workflowName in $workflowNames) {
  $run = $null
  for ($attempt = 0; $attempt -lt 18 -and $null -eq $run; $attempt++) {
    $items = @(gh run list --repo ArchitectureWorld/BIM-baogui `
      --workflow $workflowName --branch $branch `
      --commit $commit --limit 1 --json databaseId,headSha,status,conclusion,url |
      ConvertFrom-Json)
    $run = $items | Select-Object -First 1
    if ($null -eq $run) { Start-Sleep -Seconds 10 }
  }
  if ($null -eq $run) { throw "workflow run not created: $workflowName @ $commit" }
  if ($run.headSha -ne $commit) { throw "workflow SHA mismatch: $workflowName" }
  gh run watch $run.databaseId --repo ArchitectureWorld/BIM-baogui --exit-status
  if ($LASTEXITCODE -ne 0) { throw "workflow failed: $workflowName" }
  $run
}
$runs | Format-Table headSha,conclusion,url
```

若远端同名分支有 remote-only commits，脚本在 push 前停止；先检查并非破坏性整合，再重跑 Steps 2–6。要求当前 HEAD 对应的 `Build BIMBaoGui Revit MCP` 和 `Build BIMBaoGui GHA` 都为 success。

- [ ] **Step 8: 下载并校验 CI artifact**

```powershell
$commit = (git rev-parse HEAD).Trim()
$run = @(gh run list --repo ArchitectureWorld/BIM-baogui `
  --workflow 'Build BIMBaoGui Revit MCP' `
  --branch feat/revit-native-total-plan-phase1-v0.4.3 `
  --commit $commit --status success --limit 1 --json databaseId,headSha,url |
  ConvertFrom-Json) | Select-Object -First 1
if ($null -eq $run -or $run.headSha -ne $commit) { throw 'missing successful Revit MCP run for HEAD' }
$download = Join-Path $env:TEMP ('BIMBaoGui-Revit2020-Native-MCP-v0.4.3-' + [Guid]::NewGuid().ToString('N'))
gh run download $run.databaseId --repo ArchitectureWorld/BIM-baogui `
  --name BIMBaoGui-Revit2020-Native-MCP-v0.4.3 --dir $download

$downloadRoot = (Resolve-Path -LiteralPath $download).Path
$checksumPath = Join-Path $downloadRoot 'SHA256SUMS.txt'
$checksumLines = @(Get-Content -LiteralPath $checksumPath)
if ($checksumLines.Count -eq 0) { throw 'checksum manifest is empty' }
$entries = [ordered]@{}
foreach ($line in $checksumLines) {
  if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "invalid checksum line: $line" }
  $expectedHash = $Matches[1]
  $relativePath = $Matches[2]
  if ($relativePath.Contains('\') -or [IO.Path]::IsPathRooted($relativePath)) {
    throw "checksum path is not canonical relative POSIX: $relativePath"
  }
  $segments = @($relativePath -split '/')
  if ($segments.Count -eq 0 -or @($segments | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
    throw "checksum path contains empty/dot segment: $relativePath"
  }
  $canonicalRelative = $segments -join '/'
  if ($canonicalRelative -cne $relativePath -or $entries.Contains($relativePath)) {
    throw "checksum path duplicate or non-canonical: $relativePath"
  }
  $target = [IO.Path]::GetFullPath((Join-Path $downloadRoot ($segments -join [IO.Path]::DirectorySeparatorChar)))
  if (-not $target.StartsWith($downloadRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "checksum path escapes artifact: $relativePath"
  }
  if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "missing artifact file: $relativePath" }
  $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $expectedHash) { throw "checksum mismatch: $relativePath" }
  $entries[$relativePath] = $expectedHash
}
$actualFiles = @(Get-ChildItem -LiteralPath $downloadRoot -Recurse -File |
  Where-Object { $_.FullName -ne $checksumPath } |
  ForEach-Object { [IO.Path]::GetRelativePath($downloadRoot, $_.FullName).Replace('\','/') } |
  Sort-Object -Unique)
$manifestFiles = @($entries.Keys | Sort-Object)
$setDifference = @(Compare-Object -ReferenceObject $manifestFiles -DifferenceObject $actualFiles)
if ($setDifference.Count -ne 0) {
  throw "checksum manifest/file-set mismatch: $($setDifference | Out-String)"
}

$managedDlls = @(
  'BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll',
  'BIMBaoGui.RevitAddin\BIMBaoGui.McpContracts.dll',
  'BIMBaoGui.RevitAddin\BIMBaoGui.HifcCore.dll'
)
foreach ($relativePath in $managedDlls) {
  $version = [Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $downloadRoot $relativePath)).Version.ToString()
  if ($version -ne '0.4.3.0') { throw "assembly version mismatch: $relativePath = $version" }
}
$mcpExe = Get-Item -LiteralPath (Join-Path $downloadRoot 'BIMBaoGui.McpServer\BIMBaoGui.McpServer.exe')
if ($mcpExe.VersionInfo.FileVersion -notlike '0.4.3.0*') {
  throw "MCP file version mismatch: $($mcpExe.VersionInfo.FileVersion)"
}
$installerText = Get-Content -Raw -LiteralPath (Join-Path $downloadRoot 'Install-Revit2020.ps1')
if ($installerText -notmatch '\$mcpVersion\s*=\s*"0\.4\.3"') { throw 'installer version mismatch' }
```

Expected: `SHA256SUMS.txt` 全部命中；三个托管 DLL 的 AssemblyVersion 和 MCP exe 的 FileVersion 为 `0.4.3.0`；installer 源为 `0.4.3`；必要布局完整。CI 同一 HEAD 的安装 smoke 已生成并校验 productVersion `0.4.3` 的 install evidence；Task 14 对这份下载 artifact 再做真实安装。记录 workflow URL、commit 和 artifact 绝对路径。

---

### Task 14: 安装 v0.4.3 并完成 Revit 2020 / 官方 H-IFC / IFCFlux 验收

**Files:**
- Create after evidence capture: `docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-evidence.json`
- Modify after evidence capture: `docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-checklist.md`
- Do not commit: acceptance RVT/IFC、Revit journal、截图或 IFCFlux 报告原件。

**Interfaces:**
- Consumes: Task 13 已验证 CI artifact、Revit 2020、官方 HIFCTool、IFCFlux 0.1.0。
- Produces: 空模型、不完整模型、Golden 副本的可追溯证据；只有相同 Golden RVT/IFC 的四级状态均满足时才允许宣称完成。

- [ ] **Step 1: 核对官方工具和候选 RVT，不修改原件**

```powershell
$hifcManifest = 'C:\ProgramData\Autodesk\Revit\Addins\2020\00.HIFCTool.addin'
$hifcDll = 'C:\Program Files\HIFCTool\REVIT2020\net48\Hust.XAR.Shell.dll'
$ifcFlux = 'C:\Users\2899\AppData\Local\IFCFlux\IFCFlux.exe'
$sourceRvt = 'D:\18_建模项目\2026.07_湖北银行报规\3D\20260731test02.rvt'
Test-Path -LiteralPath $hifcManifest,$hifcDll,$ifcFlux,$sourceRvt
Get-Item -LiteralPath $hifcDll,$ifcFlux | Select-Object FullName,@{n='ProductVersion';e={$_.VersionInfo.ProductVersion}}
Get-FileHash -LiteralPath $sourceRvt -Algorithm SHA256
```

Expected: RVT SHA-256 为 `10e589f788ff611edd071ef629aa2effee4f45511d8ceba27fc805d5c25adf17`；HIFCTool DLL 当前产品版本 `1.0.0+c16467df84b2a4c8d7e6984e015c511eaa545859`；IFCFlux 为 `0.1.0`。版本不一致则记录实际值，不套用旧证据。

- [ ] **Step 2: 建立独立验收目录和三份副本**

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "invalid commit SHA: $commit" }
$hifcManifest = 'C:\ProgramData\Autodesk\Revit\Addins\2020\00.HIFCTool.addin'
$hifcDll = 'C:\Program Files\HIFCTool\REVIT2020\net48\Hust.XAR.Shell.dll'
$ifcFlux = 'C:\Users\2899\AppData\Local\IFCFlux\IFCFlux.exe'
$sourceRvt = 'D:\18_建模项目\2026.07_湖北银行报规\3D\20260731test02.rvt'
foreach ($requiredPath in $hifcManifest,$hifcDll,$ifcFlux,$sourceRvt) {
  if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
    throw "required acceptance input missing: $requiredPath"
  }
}
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$acceptRunId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + $commit.Substring(0,12)
$acceptRoot = Join-Path $acceptBase $acceptRunId
if (Test-Path -LiteralPath $acceptRoot) { throw "acceptance run already exists: $acceptRoot" }
New-Item -ItemType Directory -Path $acceptRoot | Out-Null
$emptyRvt = Join-Path $acceptRoot 'BIMBaoGui-v0.4.3-empty.rvt'
$incompleteRvt = Join-Path $acceptRoot 'BIMBaoGui-v0.4.3-incomplete.rvt'
$goldenRvt = Join-Path $acceptRoot 'BIMBaoGui-v0.4.3-golden.rvt'
Copy-Item -LiteralPath $sourceRvt -Destination $incompleteRvt
Copy-Item -LiteralPath $sourceRvt -Destination $goldenRvt
Get-FileHash -LiteralPath $sourceRvt,$incompleteRvt,$goldenRvt -Algorithm SHA256

$addinRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin'
$context = [ordered]@{
  schema_version = '1.0.0'
  acceptance_run_id = $acceptRunId
  accept_root = [IO.Path]::GetFullPath($acceptRoot)
  repo_root = [IO.Path]::GetFullPath($repoRoot)
  commit_sha = $commit
  source_rvt_path = [IO.Path]::GetFullPath($sourceRvt)
  source_rvt_sha256 = (Get-FileHash -LiteralPath $sourceRvt -Algorithm SHA256).Hash.ToLowerInvariant()
  empty_rvt_path = [IO.Path]::GetFullPath($emptyRvt)
  incomplete_rvt_path = [IO.Path]::GetFullPath($incompleteRvt)
  golden_rvt_path = [IO.Path]::GetFullPath($goldenRvt)
  hifctool_manifest_path = [IO.Path]::GetFullPath($hifcManifest)
  hifctool_dll_path = [IO.Path]::GetFullPath($hifcDll)
  ifcflux_path = [IO.Path]::GetFullPath($ifcFlux)
  install_evidence_path = [IO.Path]::GetFullPath((Join-Path $addinRoot 'install-evidence.json'))
}
$contextPath = Join-Path $acceptRoot 'acceptance-context.json'
$context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextPath -Encoding utf8NoBOM
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
if (Test-Path -LiteralPath $activeContextPath) {
  throw "active acceptance pointer already exists; inspect it before starting another run: $activeContextPath"
}
[ordered]@{
  context_path = [IO.Path]::GetFullPath($contextPath)
  context_sha256 = (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()
  commit_sha = $commit
  acceptance_run_id = $acceptRunId
} | ConvertTo-Json | Set-Content -LiteralPath $activeContextPath -Encoding utf8NoBOM
"ACTIVE_ACCEPTANCE_CONTEXT=$activeContextPath"
```

Expected: 两份副本初始 SHA 与原件相同；原件只读不打开写入；固定 active pointer 与 run 内 context 的 SHA 一致。空模型在下一步由 Revit 2020 默认建筑样板新建并保存为 context 的 `empty_rvt_path`。同一 commit 若已有 active pointer，先检查并完成/归档旧验收，不能按“最新目录”猜测本次上下文。

- [ ] **Step 3: 关闭 Revit 后安装 CI artifact**

确认没有 `Revit.exe` 进程，再从 Task 13 artifact 根运行 `Install.cmd`；不直接复制 `bin`，不使用 `-Force` 掩盖已加载 DLL。安装后核对：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
$pointer = Get-Content -Raw -LiteralPath $activeContextPath | ConvertFrom-Json
$contextPath = (Resolve-Path -LiteralPath $pointer.context_path).Path
$contextHash = (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pointer.commit_sha -ne $commit -or $pointer.context_sha256 -ne $contextHash) {
  throw 'acceptance pointer identity/hash mismatch'
}
$context = Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json
$acceptRoot = (Resolve-Path -LiteralPath $context.accept_root).Path
if ($context.commit_sha -ne $commit -or
    $context.acceptance_run_id -ne (Split-Path -Leaf $acceptRoot) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
      (Split-Path -Parent $acceptRoot), (Resolve-Path -LiteralPath $acceptBase).Path)) {
  throw 'acceptance context/root mismatch'
}
$addinRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin'
$manifest = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin'
$installEvidencePath = $context.install_evidence_path
$installEvidence = Get-Content -Raw -LiteralPath $installEvidencePath | ConvertFrom-Json
$expectedCommit = $commit
if ($installEvidence.productVersion -ne '0.4.3') { throw 'installed product version mismatch' }
if ($installEvidence.assemblyVersion -ne '0.4.3.0') { throw 'installed assembly version mismatch' }
if ($installEvidence.mcpProductVersion -ne '0.4.3') { throw 'installed MCP version mismatch' }
if ($installEvidence.commitSha -ne $expectedCommit) { throw 'installed commit mismatch' }
$installedArtifacts = @(
  @($installEvidence.installedDll, $installEvidence.installedDllSha256),
  @($installEvidence.installedContractsDll, $installEvidence.contractsDllSha256),
  @($installEvidence.installedHifcCoreDll, $installEvidence.hifcCoreDllSha256),
  @($installEvidence.installedMcpServerExe, $installEvidence.mcpServerExeSha256)
)
foreach ($pair in $installedArtifacts) {
  $actual = (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $pair[1]) { throw "installed hash mismatch: $($pair[0])" }
}
Get-Item -LiteralPath $manifest,$installEvidencePath
```

Expected: 四个安装文件均与 install evidence 的 SHA 一致；工作台显示版本 `0.4.3`、同一 artifact commit 和对应 DLL 路径。

- [ ] **Step 4: 用空模型验证完整红清单和稳定性**

通过 Windows app 控制打开 Revit 2020，新建默认建筑项目，保存到 context 的 `empty_rvt_path`。先将 Stage03 输出目录设为 `$acceptRoot\empty-evidence`；该目录必须此前不存在，由本次最终 scan 创建，且只执行一次最终“执行全部检查”。打开工作台：

```text
01 选择“总平模型”并保存最小初始化
03 打开清单，确认初始灰色
点击“执行全部检查”
确认缺失登记/坐标/目标/构件/02B 数据均为红色
确认无“不适用”按钮、无崩溃、无伪造 ElementId
选择测试强制导出：空理由必须阻断；填写“空模型门禁测试”后仍不得绕过技术错误
```

确认目录中恰好一个 `*-stage03-scan-evidence.json`，再保存 Stage03 截图和测试强制导出报告到 `$acceptRoot\empty-evidence` 并记录 SHA；强制导出可以另生成 validation，但不能替代 scan evidence。

- [ ] **Step 5: 用不完整模型验证红绿混合、定位和部分成功**

打开 context 的 `incomplete_rvt_path`。交互调试阶段使用 `$acceptRoot\incomplete-working`，不要把多轮 scan 混入最终证据目录：

```text
01 只填写一部分登记/坐标/规划目标并写入回读
02A 分别测试“当前选择”和“交互点选”，接受一部分候选，保留一部分未确认
02A 对已有问题构件依次执行选中、缩放、隔离、恢复
02A 对缺失语义角色确认只出现“进入 02A 补充”，不出现定位按钮
02B 为五项填写合法值，给一个车位指标填写 1.5，保存全部
确认五项成功保留、失败项红色、重试按钮只带失败 propertyId
将失败车位改为整数后仅重试失败项
03 执行全部检查，确认绿色/红色/黄色混合及三阶段跳转
```

保存、关闭、重开；再次读取 01/02A/02B/03，验证 persistence 和 latest-attempt 规则。然后切换到此前不存在的 `$acceptRoot\incomplete-evidence`，只执行一次最终 scan，并确认恰好生成一个 `*-stage03-scan-evidence.json`。
保存截图、Stage01/02A/02B 结果和 Stage03 报告到 `$acceptRoot\incomplete-evidence`，记录每个文件 SHA。

- [ ] **Step 6: 准备 Golden 副本并验证时效性/门禁**

打开 context 的 `golden_rvt_path`，完成当前总平清单所有可提供的真实 Stage01、02A、02B 输入；不得以 0 或样例值补未知数据。工作过程使用 `$acceptRoot\golden-working`，验证：

```text
02A 全模型扫描和人工确认
02A 修改一个已确认构件名称/类型后旧 confirmation 变为 stale
重新确认并写入，其他成功构件保持
02B 修改一个指标后 LastAttemptRunId/LastSuccessfulRunId 更新
保存、关闭、重开后 Stage03 重新检查
Strict 遇任一红项必须阻断
ForcedTest 需要原因，导出后红项仍红，报告 is_test_export=true
```

如果官方 carrier 仍为 `PENDING_GOLDEN_RVT`，Strict 红项是正确结果，不得临时改绿。
保存、关闭、重开后切换到此前不存在的 `$acceptRoot\golden-evidence`，只执行一次最终 scan，并确认恰好一个 `*-stage03-scan-evidence.json`。保存本阶段截图和报告到该目录；后续官方 IFC 与 IFCFlux 原件分别留在 `official-hifc`、`ifcflux-evidence`，均纳入同一 golden scenario 引用。

- [ ] **Step 7: 用官方 HIFCTool 直接导出并锁定载体证据**

从同一 Golden RVT 使用官方 HIFCTool Ribbon 命令导出新 IFC；不得用 BIMBaoGui 标准导出或后处理文件替代。点击前先运行以下块，它从持久 context 重建全部路径，并冻结导出开始身份：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
$pointer = Get-Content -Raw -LiteralPath $activeContextPath | ConvertFrom-Json
$contextPath = (Resolve-Path -LiteralPath $pointer.context_path).Path
if ($pointer.context_sha256 -ne
    (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
  throw 'acceptance context hash mismatch'
}
$context = Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json
$acceptRoot = (Resolve-Path -LiteralPath $context.accept_root).Path
if ($pointer.commit_sha -ne $commit -or $context.commit_sha -ne $commit -or
    $context.acceptance_run_id -ne (Split-Path -Leaf $acceptRoot) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
      (Split-Path -Parent $acceptRoot), (Resolve-Path -LiteralPath $acceptBase).Path)) {
  throw 'acceptance context/root mismatch'
}
$acceptRunId = $context.acceptance_run_id
$goldenRvt = $context.golden_rvt_path
$hifcManifest = $context.hifctool_manifest_path
$hifcDll = $context.hifctool_dll_path
$officialDirectory = Join-Path $acceptRoot 'official-hifc'
if (Test-Path -LiteralPath $officialDirectory) {
  if (@(Get-ChildItem -LiteralPath $officialDirectory -Force).Count -ne 0) {
    throw "official export directory must start empty: $officialDirectory"
  }
} else {
  New-Item -ItemType Directory -Path $officialDirectory | Out-Null
}
$exportStartPath = Join-Path $officialDirectory 'official-export-start.json'
[ordered]@{
  acceptance_run_id = $acceptRunId
  export_started_utc = [DateTimeOffset]::UtcNow.ToString('O')
  golden_rvt_path = [IO.Path]::GetFullPath($goldenRvt)
  golden_rvt_sha256 = (Get-FileHash -LiteralPath $goldenRvt -Algorithm SHA256).Hash.ToLowerInvariant()
  hifctool_manifest_sha256 = (Get-FileHash -LiteralPath $hifcManifest -Algorithm SHA256).Hash.ToLowerInvariant()
  hifctool_dll_sha256 = (Get-FileHash -LiteralPath $hifcDll -Algorithm SHA256).Hash.ToLowerInvariant()
  expected_command_class = 'Hust.XAR.Shell.Commands.IfcExportRvtApi2022Cmd'
} | ConvertTo-Json | Set-Content -LiteralPath $exportStartPath -Encoding utf8NoBOM
```

立即用 Windows app 点击 HIFCTool 的“导出 HIFC”按钮，明确选择 `$officialDirectory`，等待文件写完；不要点击 Revit/BIMBaoGui 标准 IFC 导出。完成后关闭 Revit 以刷新 journal，期间不得保存新的 Golden RVT 修改；若 Golden 文件变化，回到 Step 6 重做。然后运行以下独立块，它重新读取 context/start marker，并要求 journal 中恰好存在一次官方 external-command Ribbon 事件：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
$pointer = Get-Content -Raw -LiteralPath $activeContextPath | ConvertFrom-Json
$contextPath = (Resolve-Path -LiteralPath $pointer.context_path).Path
if ($pointer.context_sha256 -ne
    (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
  throw 'acceptance context hash mismatch'
}
$context = Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json
$acceptRoot = (Resolve-Path -LiteralPath $context.accept_root).Path
if ($pointer.commit_sha -ne $commit -or $context.commit_sha -ne $commit -or
    $context.acceptance_run_id -ne (Split-Path -Leaf $acceptRoot) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
      (Split-Path -Parent $acceptRoot), (Resolve-Path -LiteralPath $acceptBase).Path)) {
  throw 'acceptance context/root mismatch'
}
$acceptRunId = $context.acceptance_run_id
$goldenRvt = $context.golden_rvt_path
$hifcManifest = $context.hifctool_manifest_path
$hifcDll = $context.hifctool_dll_path
$officialDirectory = Join-Path $acceptRoot 'official-hifc'
$exportStartPath = Join-Path $officialDirectory 'official-export-start.json'
$exportStart = Get-Content -Raw -LiteralPath $exportStartPath | ConvertFrom-Json
if ($exportStart.acceptance_run_id -ne $acceptRunId) { throw 'official export start marker mismatch' }
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
  throw 'close Revit before sealing official journal evidence'
}
$officialIfcFiles = @(Get-ChildItem -LiteralPath $officialDirectory -File -Filter '*.ifc')
if ($officialIfcFiles.Count -ne 1) { throw "expected exactly one official IFC, got $($officialIfcFiles.Count)" }
$officialIfcItem = $officialIfcFiles[0]
$officialIfc = $officialIfcItem.FullName
$started = [DateTimeOffset]::Parse($exportStart.export_started_utc)
$completed = [DateTimeOffset]::UtcNow
if ($officialIfcItem.LastWriteTimeUtc -lt $started.UtcDateTime -or
    $officialIfcItem.LastWriteTimeUtc -gt $completed.UtcDateTime.AddSeconds(2)) {
  throw 'official IFC timestamp is outside the recorded HIFCTool command window'
}
$journalRoot = Join-Path $env:LOCALAPPDATA 'Autodesk\Revit\Autodesk Revit 2020\Journals'
$journalCandidates = @(Get-ChildItem -LiteralPath $journalRoot -File -Filter 'journal*.txt' |
  Where-Object { $_.LastWriteTimeUtc -ge $started.UtcDateTime } |
  Where-Object {
    Select-String -LiteralPath $_.FullName `
      -Pattern 'Jrn\.RibbonEvent.*Hust\.XAR\.Shell\.Commands\.IfcExportRvtApi2022Cmd' `
      -Quiet
  })
if ($journalCandidates.Count -ne 1) {
  throw "expected one journal with the official HIFCTool command, got $($journalCandidates.Count)"
}
$commandMatches = @(Select-String -LiteralPath $journalCandidates[0].FullName `
  -Pattern 'Jrn\.RibbonEvent.*Hust\.XAR\.Shell\.Commands\.IfcExportRvtApi2022Cmd')
if ($commandMatches.Count -ne 1) { throw 'official HIFCTool command must occur exactly once' }
$objectCountMatches = @(Select-String -LiteralPath $journalCandidates[0].FullName `
  -Pattern 'Jrn\.Data\s+"IFCObjectCount"')
if ($objectCountMatches.Count -eq 0) { throw 'journal lacks IFC export object-count evidence' }
$journalEvidencePath = Join-Path $officialDirectory 'revit-hifctool-export-journal.txt'
Copy-Item -LiteralPath $journalCandidates[0].FullName -Destination $journalEvidencePath
$goldenSha = (Get-FileHash -LiteralPath $goldenRvt -Algorithm SHA256).Hash.ToLowerInvariant()
if ($goldenSha -ne $exportStart.golden_rvt_sha256) {
  throw 'Golden RVT changed during official export; restart from Step 6'
}
$officialExportResult = [ordered]@{
  acceptance_run_id = $acceptRunId
  golden_rvt_path = [IO.Path]::GetFullPath($goldenRvt)
  golden_rvt_sha256 = $goldenSha
  hifctool_manifest_path = [IO.Path]::GetFullPath($hifcManifest)
  hifctool_manifest_sha256 = (Get-FileHash -LiteralPath $hifcManifest -Algorithm SHA256).Hash.ToLowerInvariant()
  hifctool_dll_path = [IO.Path]::GetFullPath($hifcDll)
  hifctool_dll_sha256 = (Get-FileHash -LiteralPath $hifcDll -Algorithm SHA256).Hash.ToLowerInvariant()
  hifctool_product_version = (Get-Item -LiteralPath $hifcDll).VersionInfo.ProductVersion
  hifctool_command_class = $exportStart.expected_command_class
  revit_journal_path = [IO.Path]::GetFullPath($journalEvidencePath)
  revit_journal_sha256 = (Get-FileHash -LiteralPath $journalEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
  revit_journal_command_line = $commandMatches[0].Line.Trim()
  revit_journal_ifc_object_count_records = $objectCountMatches.Count
  official_ifc_path = [IO.Path]::GetFullPath($officialIfc)
  official_ifc_sha256 = (Get-FileHash -LiteralPath $officialIfc -Algorithm SHA256).Hash.ToLowerInvariant()
  official_ifc_last_write_utc = ([DateTimeOffset]$officialIfcItem.LastWriteTimeUtc).ToString('O')
  export_started_utc = $started.ToString('O')
  export_completed_utc = $completed.ToString('O')
  start_marker_sha256 = (Get-FileHash -LiteralPath $exportStartPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$officialExportResultPath = Join-Path $officialDirectory 'official-export-result.json'
$officialExportResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $officialExportResultPath -Encoding utf8NoBOM
```

后续若 Golden RVT、journal、官方 IFC、manifest 或 HIFCTool DLL 任一字节改变，当前 official export result 作废，必须重导。只出现插件启动注册行而没有上述 `Jrn.RibbonEvent` 命令行时，官方层保持 pending，不能以“DLL 已加载”代替“官方命令已执行”。

只有在 Steps 7–8 的官方 IFC/IFCFlux 中对**某个 exact propertyId** 精确发现 Entity/Pset/Property/type/unit/value，且能反查到确定的 Revit element selector、binding scope 和 parameter GUID 时，才允许把该 `stage02BMetrics[propertyId]` 的 `officialCarrierStatus/officialProjectionCarrierId/officialEvidenceRef/officialExportVerified` 改为 Verified。CarrierId 固定为 `OFFICIAL.<propertyId>.V1`；EvidenceId 固定为 `EVIDENCE.<propertyId>.<officialIfcSha256 前 12 位>`。在两个结构目录中分别新增一条同 propertyId 记录：carrier 必须填 selectorKind/role/category/class/INSTANCE/propertyId GUID，evidence 必须填 Golden RVT、manifest、官方 DLL、IFC、IFCFlux 报告的现场 SHA、两个工具版本和 observed UniqueId/GlobalId/scope/GUID；metric 只引用这两个 ID。未发现或 owner 冲突时该 propertyId 保持 `PENDING_GOLDEN_RVT`、两个 ID 为空，失败观察留在验收证据而不是猜 carrier。entity 级 `officialCarrierPolicies` 只有在证据明确覆盖该 entity 的全部受控属性时才可升级；单个 IfcSite 属性通过不能让另外两个 IfcSite 指标变绿。

若 Steps 7–8 首次证明某个 property carrier，实施顺序固定为：

1. 在 `test_hbr_rulepack_v043.py` 增加该 propertyId 的 exact identity、Verified 状态、两个外键及其结构记录完整性的 RED 测试；
2. 在 `NativeStage02BOwnerPolicyTests`、`NativeStage02BProjectionCarrierResolverTests` 与 Revit contract 中增加“只解析该 propertyId 的 exact target、按固定 GUID 写入并 GUID 回读、同 entity 其他 property 仍 pending”的 RED 测试；
3. 更新两个结构目录和该 metric 的外键；复用 Task 8 已实现的 `VerifiedElementParameter` 分支。carrier resolver 必须解析为唯一 live element；0 个或多个目标分别返回 `OFFICIAL_CARRIER_NOT_FOUND/OFFICIAL_CARRIER_AMBIGUOUS`，当前指标 transaction 回滚，其他指标继续；
4. 重跑 Tasks 1–2、7–13 的测试/构建/CI，下载新的同分支 artifact，重新执行 Step 3 安装，并用同一 `$goldenRvt` 重跑 Steps 6–8；
5. 只有实际参数 GUID 回读、官方 IFC exact value 和 IFCFlux 报告三者一致时，该 propertyId 的 Stage03 carrier 项才为绿。

证据 JSON 的 commit、rule-package SHA、安装 DLL SHA 必须取这次最终 artifact；不得把首次 pending artifact 的运行结果与更新后的规则身份拼接为一次通过。

- [ ] **Step 8: 在 IFCFlux 中核对 exact identity**

用 IFCFlux 0.1.0 打开 Step 7 的官方 IFC，逐项核对：

```text
IfcProject / Pset_登记信息属性集 / 总建筑面积
IfcSite / Pset_场地信息属性集 / 建筑密度
IfcSite / Pset_场地信息属性集 / 容积率
IfcSite / Pset_场地信息属性集 / 绿地率
IfcSpatialZone / Pset_停车场信息属性集 / 机动车位数量
IfcSpatialZone / Pset_停车场信息属性集 / 非机动车位数量
IfcBuildingElementProxy / Pset_绿地信息属性集 / 类型、投影面积、绿地类型、折算系数
```

保存 IFCFlux 原始报告为 `$acceptRoot\ifcflux-evidence\IFCFlux-report.pdf`（若工具只能生成其他格式，使用实际扩展名但保持 basename），截图可另存。用以下安全默认值生成观察表：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
$pointer = Get-Content -Raw -LiteralPath $activeContextPath | ConvertFrom-Json
$contextPath = (Resolve-Path -LiteralPath $pointer.context_path).Path
if ($pointer.context_sha256 -ne
    (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
  throw 'acceptance context hash mismatch'
}
$context = Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json
$acceptRoot = (Resolve-Path -LiteralPath $context.accept_root).Path
if ($pointer.commit_sha -ne $commit -or $context.commit_sha -ne $commit -or
    $context.acceptance_run_id -ne (Split-Path -Leaf $acceptRoot) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
      (Split-Path -Parent $acceptRoot), (Resolve-Path -LiteralPath $acceptBase).Path)) {
  throw 'acceptance context/root mismatch'
}
$acceptRunId = $context.acceptance_run_id
$goldenRvt = $context.golden_rvt_path
$hifcManifest = $context.hifctool_manifest_path
$hifcDll = $context.hifctool_dll_path
$ifcFlux = $context.ifcflux_path
$ifcFluxDirectory = Join-Path $acceptRoot 'ifcflux-evidence'
$officialExportResultPath = Join-Path $acceptRoot 'official-hifc\official-export-result.json'
$officialExportResult = Get-Content -Raw -LiteralPath $officialExportResultPath | ConvertFrom-Json
if ($officialExportResult.acceptance_run_id -ne $acceptRunId) {
  throw 'official export result belongs to another acceptance run'
}
$actualGoldenRvtSha = (Get-FileHash -LiteralPath $goldenRvt -Algorithm SHA256).Hash.ToLowerInvariant()
$actualHifcManifestSha = (Get-FileHash -LiteralPath $hifcManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$actualHifcDllSha = (Get-FileHash -LiteralPath $hifcDll -Algorithm SHA256).Hash.ToLowerInvariant()
$actualOfficialIfcSha = (Get-FileHash -LiteralPath $officialExportResult.official_ifc_path -Algorithm SHA256).Hash.ToLowerInvariant()
$actualJournalSha = (Get-FileHash -LiteralPath $officialExportResult.revit_journal_path -Algorithm SHA256).Hash.ToLowerInvariant()
if ($officialExportResult.golden_rvt_sha256 -ne $actualGoldenRvtSha -or
    $officialExportResult.hifctool_manifest_sha256 -ne $actualHifcManifestSha -or
    $officialExportResult.hifctool_dll_sha256 -ne $actualHifcDllSha -or
    $officialExportResult.official_ifc_sha256 -ne $actualOfficialIfcSha -or
    $officialExportResult.revit_journal_sha256 -ne $actualJournalSha -or
    -not (Select-String -LiteralPath $officialExportResult.revit_journal_path `
      -Pattern 'Jrn\.RibbonEvent.*Hust\.XAR\.Shell\.Commands\.IfcExportRvtApi2022Cmd' -Quiet)) {
  throw 'official export evidence no longer matches Golden RVT, official command, tools, or IFC bytes'
}
$ifcFluxReports = @(Get-ChildItem -LiteralPath $ifcFluxDirectory -File |
  Where-Object { $_.BaseName -eq 'IFCFlux-report' })
if ($ifcFluxReports.Count -ne 1) { throw "expected one IFCFlux report, got $($ifcFluxReports.Count)" }
$checkSpecs = @(
  @('ca21e324-046b-5bfd-84c8-0d3470082303','IfcProject|Pset_登记信息属性集|总建筑面积','IfcReal','m2'),
  @('93e51676-237e-56a8-8f28-2da845422e2e','IfcSite|Pset_场地信息属性集|建筑密度','IfcReal',''),
  @('201a00ac-3672-5ded-83d2-ed96f81bfabf','IfcSite|Pset_场地信息属性集|容积率','IfcReal',''),
  @('f630ad47-b006-5127-badd-b1660cf996c3','IfcSite|Pset_场地信息属性集|绿地率','IfcReal',''),
  @('c62cfd5f-2a50-5230-9c5d-4037c39061bf','IfcSpatialZone|Pset_停车场信息属性集|机动车位数量','IfcInteger','个'),
  @('84df74c2-a7e5-5a98-a5e0-4458e49a3973','IfcSpatialZone|Pset_停车场信息属性集|非机动车位数量','IfcInteger','个'),
  @('4d9d7775-e83c-5357-8f3e-1e6a6692e793','IfcBuildingElementProxy|Pset_绿地信息属性集|类型','IfcLabel',''),
  @('6cc053e3-891d-51b1-b861-af498733f73a','IfcBuildingElementProxy|Pset_绿地信息属性集|投影面积','IfcReal','m2'),
  @('3fd74b35-3164-5248-9fe9-c675992a4292','IfcBuildingElementProxy|Pset_绿地信息属性集|绿地类型','IfcLabel',''),
  @('a99a0961-05fe-56fd-b8a0-865410bfe72f','IfcBuildingElementProxy|Pset_绿地信息属性集|折算系数','IfcReal','')
)
$ifcFluxResult = [ordered]@{
  acceptance_run_id = $acceptRunId
  tool_path = [IO.Path]::GetFullPath($ifcFlux)
  tool_product_version = (Get-Item -LiteralPath $ifcFlux).VersionInfo.ProductVersion
  tool_sha256 = (Get-FileHash -LiteralPath $ifcFlux -Algorithm SHA256).Hash.ToLowerInvariant()
  input_ifc_path = $officialExportResult.official_ifc_path
  input_ifc_sha256 = $officialExportResult.official_ifc_sha256
  report_path = $ifcFluxReports[0].FullName
  report_sha256 = (Get-FileHash -LiteralPath $ifcFluxReports[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  checked_utc = [DateTimeOffset]::UtcNow.ToString('O')
  checks = @($checkSpecs | ForEach-Object {
    [ordered]@{
      property_id = $_[0]; identity = $_[1]
      expected_declared_type = $_[2]; expected_unit = $_[3]
      declared_type = ''; unit = ''; observed_value = ''
      owner_global_id = ''; passed = $false
    }
  })
}
$ifcFluxResultPath = Join-Path $ifcFluxDirectory 'ifcflux-result.json'
$ifcFluxResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ifcFluxResultPath -Encoding utf8NoBOM
```

检查人员逐项填写 `declared_type/unit/observed_value/owner_global_id`，并只在 exact identity/type/unit/value/owner 全部正确时把 `passed` 改为 JSON boolean `true`；不得删项或改 propertyId/identity/expected 字段。无量纲属性的正确 `unit` 是空字符串，不得为了“非空”伪造单位。最终脚本要求 exact 10 个 propertyId 唯一齐全，且 `input_ifc_sha256` 等于现场重算的 official IFC SHA。字段存在但类型、单位、owner 或值错误仍为失败。

- [ ] **Step 9: 写证据 JSON；若发现代码缺陷则回到 RED 测试**

从最终安装运行时报告和已捕获文件提取值，不读取本地 `obj` rulepack，不用 `Read-Host` 人工设置总状态：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git rev-parse HEAD).Trim().ToLowerInvariant()
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "invalid commit SHA: $commit" }
$acceptBase = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-acceptance\v0.4.3'
$activeContextPath = Join-Path $acceptBase ("active-$($commit.Substring(0,12)).json")
$pointer = Get-Content -Raw -LiteralPath $activeContextPath | ConvertFrom-Json
$contextPath = (Resolve-Path -LiteralPath $pointer.context_path).Path
$contextHash = (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pointer.commit_sha -ne $commit -or $pointer.context_sha256 -ne $contextHash) {
  throw 'acceptance pointer identity/hash mismatch'
}
$context = Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json
$acceptRoot = (Resolve-Path -LiteralPath $context.accept_root).Path
if ($context.commit_sha -ne $commit -or
    $context.acceptance_run_id -ne (Split-Path -Leaf $acceptRoot) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
      (Split-Path -Parent $acceptRoot), (Resolve-Path -LiteralPath $acceptBase).Path)) {
  throw 'acceptance context/root mismatch'
}
$acceptRunId = $context.acceptance_run_id
$emptyRvt = $context.empty_rvt_path
$incompleteRvt = $context.incomplete_rvt_path
$goldenRvt = $context.golden_rvt_path
$hifcManifest = $context.hifctool_manifest_path
$hifcDll = $context.hifctool_dll_path
$ifcFlux = $context.ifcflux_path
$installEvidencePath = $context.install_evidence_path
$expectedInstallEvidence = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\install-evidence.json'
if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($installEvidencePath),
    [IO.Path]::GetFullPath($expectedInstallEvidence))) {
  throw 'acceptance context install-evidence path mismatch'
}

function Get-OneFile([string]$directory, [string]$filter) {
  $items = @(Get-ChildItem -LiteralPath $directory -File -Filter $filter)
  if ($items.Count -ne 1) { throw "expected one $filter under $directory, got $($items.Count)" }
  return $items[0]
}
function File-Evidence([string]$path) {
  $item = Get-Item -LiteralPath $path
  [ordered]@{
    path = $item.FullName
    sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}
function Resolve-EvidenceFile([string]$path, [string]$expectedRoot) {
  $rootItem = Get-Item -LiteralPath $expectedRoot
  $fileItem = Get-Item -LiteralPath $path
  if (-not $rootItem.PSIsContainer -or $fileItem.PSIsContainer) {
    throw "invalid evidence root/file: $expectedRoot :: $path"
  }
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
      ($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "reparse-point evidence is forbidden: $path"
  }
  $rootFull = $rootItem.FullName.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
  $prefix = $rootFull + [IO.Path]::DirectorySeparatorChar
  if (-not $fileItem.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "evidence escapes expected root: $path"
  }
  $cursor = $fileItem.Directory
  while ($null -ne $cursor -and
         -not [StringComparer]::OrdinalIgnoreCase.Equals($cursor.FullName, $rootFull)) {
    if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "reparse-point parent is forbidden: $($cursor.FullName)"
    }
    $cursor = $cursor.Parent
  }
  if ($null -eq $cursor) { throw "evidence parent chain escaped root: $path" }
  return $fileItem.FullName
}
function Read-ScanEvidence(
  [string]$directory,
  [string]$expectedRvt,
  [string]$expectedCommit,
  [object]$installed) {
  $directoryFull = (Resolve-Path -LiteralPath $directory).Path
  $file = Get-OneFile $directoryFull '*-stage03-scan-evidence.json'
  Resolve-EvidenceFile $file.FullName $directoryFull | Out-Null
  $json = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
  if ($json.report_kind -ne 'STAGE03_SCAN' -or
      -not [StringComparer]::OrdinalIgnoreCase.Equals(
        (Resolve-Path -LiteralPath $json.document_path).Path,
        (Resolve-Path -LiteralPath $expectedRvt).Path) -or
      -not [StringComparer]::OrdinalIgnoreCase.Equals(
        (Resolve-Path -LiteralPath $json.normalized_output_directory).Path,
        $directoryFull)) {
    throw "scan evidence document/output mismatch: $($file.FullName)"
  }
  $runtime = $json.plugin_runtime
  $expectedInformation = '^0\.4\.3\+build\.\d+\.sha\.' + [regex]::Escape($expectedCommit) + '$'
  $actualRuntimeDllSha = (Get-FileHash -LiteralPath $runtime.addin_dll_path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($runtime.product_version -ne '0.4.3' -or
      $runtime.assembly_version -ne '0.4.3.0' -or
      $runtime.commit_sha -ne $expectedCommit -or
      $runtime.informational_version -notmatch $expectedInformation -or
      -not [StringComparer]::OrdinalIgnoreCase.Equals(
        [IO.Path]::GetFullPath($runtime.addin_dll_path),
        [IO.Path]::GetFullPath($installed.installedDll)) -or
      $runtime.addin_dll_sha256 -ne $actualRuntimeDllSha -or
      $runtime.addin_dll_sha256 -ne $installed.installedDllSha256) {
    throw "scan evidence runtime artifact mismatch: $($file.FullName)"
  }
  foreach ($stage in 'stage01','stage02a','stage02b') {
    $node = $json.workflow_results.$stage
    if ($node.run_id -notmatch '\S' -or
        $node.result_hash -notmatch '^[0-9a-f]{64}$' -or
        $node.input_snapshot_hash -notmatch '^[0-9a-f]{64}$') {
      throw "invalid $stage workflow identity in $($file.FullName)"
    }
  }
  if ($json.revit_version -ne '2020' -or
      $json.document_fingerprint -notmatch '\S' -or
      $json.rule_package.id -ne 'HBR-WUHAN-PLANNING' -or
      $json.rule_package.version -ne '1.0.0' -or
      $json.rule_package.sha256 -notmatch '^[0-9a-f]{64}$' -or
      $json.scan_hash -notmatch '^[0-9a-f]{64}$' -or
      $json.preflight_hash -notmatch '^[0-9a-f]{64}$') {
    throw "invalid scan evidence identity: $($file.FullName)"
  }
  [ordered]@{ report = File-Evidence $file.FullName; data = $json }
}

$installed = Get-Content -Raw -LiteralPath $installEvidencePath | ConvertFrom-Json
if ($installed.commitSha.ToLowerInvariant() -ne $commit) {
  throw 'installed commit differs from acceptance commit'
}
$emptyScan = Read-ScanEvidence (Join-Path $acceptRoot 'empty-evidence') $emptyRvt $commit $installed
$incompleteScan = Read-ScanEvidence (Join-Path $acceptRoot 'incomplete-evidence') $incompleteRvt $commit $installed
$goldenScan = Read-ScanEvidence (Join-Path $acceptRoot 'golden-evidence') $goldenRvt $commit $installed

$officialRoot = (Resolve-Path -LiteralPath (Join-Path $acceptRoot 'official-hifc')).Path
$ifcFluxRoot = (Resolve-Path -LiteralPath (Join-Path $acceptRoot 'ifcflux-evidence')).Path
$officialPath = Resolve-EvidenceFile (Join-Path $officialRoot 'official-export-result.json') $officialRoot
$ifcFluxPath = Resolve-EvidenceFile (Join-Path $ifcFluxRoot 'ifcflux-result.json') $ifcFluxRoot
$official = Get-Content -Raw -LiteralPath $officialPath | ConvertFrom-Json
$ifcFluxResult = Get-Content -Raw -LiteralPath $ifcFluxPath | ConvertFrom-Json
if ($official.acceptance_run_id -ne $acceptRunId -or
    $ifcFluxResult.acceptance_run_id -ne $acceptRunId) {
  throw 'acceptance runId linkage mismatch'
}
$resolvedGoldenRvt = Resolve-EvidenceFile $goldenRvt $acceptRoot
$resolvedOfficialIfc = Resolve-EvidenceFile $official.official_ifc_path $officialRoot
$resolvedJournal = Resolve-EvidenceFile $official.revit_journal_path $officialRoot
$resolvedStartMarker = Resolve-EvidenceFile (Join-Path $officialRoot 'official-export-start.json') $officialRoot
$resolvedIfcFluxReport = Resolve-EvidenceFile $ifcFluxResult.report_path $ifcFluxRoot
$pathsMatch =
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($official.golden_rvt_path), $resolvedGoldenRvt) -and
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($official.hifctool_manifest_path),
    (Resolve-Path -LiteralPath $hifcManifest).Path) -and
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($official.hifctool_dll_path),
    (Resolve-Path -LiteralPath $hifcDll).Path) -and
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($ifcFluxResult.tool_path),
    (Resolve-Path -LiteralPath $ifcFlux).Path) -and
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($ifcFluxResult.input_ifc_path), $resolvedOfficialIfc)

$actualGoldenRvtSha = (Get-FileHash -LiteralPath $resolvedGoldenRvt -Algorithm SHA256).Hash.ToLowerInvariant()
$actualHifcManifestSha = (Get-FileHash -LiteralPath $hifcManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$actualHifcDllSha = (Get-FileHash -LiteralPath $hifcDll -Algorithm SHA256).Hash.ToLowerInvariant()
$actualOfficialIfcSha = (Get-FileHash -LiteralPath $resolvedOfficialIfc -Algorithm SHA256).Hash.ToLowerInvariant()
$actualJournalSha = (Get-FileHash -LiteralPath $resolvedJournal -Algorithm SHA256).Hash.ToLowerInvariant()
$actualStartMarkerSha = (Get-FileHash -LiteralPath $resolvedStartMarker -Algorithm SHA256).Hash.ToLowerInvariant()
$actualIfcFluxToolSha = (Get-FileHash -LiteralPath $ifcFlux -Algorithm SHA256).Hash.ToLowerInvariant()
$actualIfcFluxReportSha = (Get-FileHash -LiteralPath $resolvedIfcFluxReport -Algorithm SHA256).Hash.ToLowerInvariant()
$startMarker = Get-Content -Raw -LiteralPath $resolvedStartMarker | ConvertFrom-Json
$journalCommands = @(Select-String -LiteralPath $resolvedJournal
  -Pattern 'Jrn\.RibbonEvent.*Hust\.XAR\.Shell\.Commands\.IfcExportRvtApi2022Cmd')
$journalObjectCounts = @(Select-String -LiteralPath $resolvedJournal
  -Pattern 'Jrn\.Data\s+"IFCObjectCount"')
$started = [DateTimeOffset]::Parse($official.export_started_utc)
$completed = [DateTimeOffset]::Parse($official.export_completed_utc)
$ifcLastWrite = [DateTimeOffset](Get-Item -LiteralPath $resolvedOfficialIfc).LastWriteTimeUtc
$startMarkerMatches =
  $startMarker.acceptance_run_id -eq $acceptRunId -and
  [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($startMarker.golden_rvt_path), $resolvedGoldenRvt) -and
  $startMarker.golden_rvt_sha256 -eq $actualGoldenRvtSha -and
  $startMarker.hifctool_manifest_sha256 -eq $actualHifcManifestSha -and
  $startMarker.hifctool_dll_sha256 -eq $actualHifcDllSha -and
  $startMarker.expected_command_class -eq
    'Hust.XAR.Shell.Commands.IfcExportRvtApi2022Cmd' -and
  $official.hifctool_command_class -eq $startMarker.expected_command_class -and
  [DateTimeOffset]::Parse($startMarker.export_started_utc).UtcDateTime -eq
    $started.UtcDateTime
$officialProvenanceMatches =
  $startMarkerMatches -and
  $official.hifctool_command_class -eq 'Hust.XAR.Shell.Commands.IfcExportRvtApi2022Cmd' -and
  $journalCommands.Count -eq 1 -and
  $official.revit_journal_command_line -eq $journalCommands[0].Line.Trim() -and
  $journalObjectCounts.Count -gt 0 -and
  $official.revit_journal_ifc_object_count_records -eq $journalObjectCounts.Count -and
  $official.revit_journal_sha256 -eq $actualJournalSha -and
  $official.start_marker_sha256 -eq $actualStartMarkerSha -and
  $started -lt $completed -and
  $ifcLastWrite -ge $started -and $ifcLastWrite -le $completed.AddSeconds(2) -and
  [DateTimeOffset]::Parse($official.official_ifc_last_write_utc).UtcDateTime -eq
    $ifcLastWrite.UtcDateTime

$checks = @($ifcFluxResult.checks)
$expectedChecks = [ordered]@{
  'ca21e324-046b-5bfd-84c8-0d3470082303' = [ordered]@{ identity='IfcProject|Pset_登记信息属性集|总建筑面积'; declared_type='IfcReal'; unit='m2' }
  '93e51676-237e-56a8-8f28-2da845422e2e' = [ordered]@{ identity='IfcSite|Pset_场地信息属性集|建筑密度'; declared_type='IfcReal'; unit='' }
  '201a00ac-3672-5ded-83d2-ed96f81bfabf' = [ordered]@{ identity='IfcSite|Pset_场地信息属性集|容积率'; declared_type='IfcReal'; unit='' }
  'f630ad47-b006-5127-badd-b1660cf996c3' = [ordered]@{ identity='IfcSite|Pset_场地信息属性集|绿地率'; declared_type='IfcReal'; unit='' }
  'c62cfd5f-2a50-5230-9c5d-4037c39061bf' = [ordered]@{ identity='IfcSpatialZone|Pset_停车场信息属性集|机动车位数量'; declared_type='IfcInteger'; unit='个' }
  '84df74c2-a7e5-5a98-a5e0-4458e49a3973' = [ordered]@{ identity='IfcSpatialZone|Pset_停车场信息属性集|非机动车位数量'; declared_type='IfcInteger'; unit='个' }
  '4d9d7775-e83c-5357-8f3e-1e6a6692e793' = [ordered]@{ identity='IfcBuildingElementProxy|Pset_绿地信息属性集|类型'; declared_type='IfcLabel'; unit='' }
  '6cc053e3-891d-51b1-b861-af498733f73a' = [ordered]@{ identity='IfcBuildingElementProxy|Pset_绿地信息属性集|投影面积'; declared_type='IfcReal'; unit='m2' }
  '3fd74b35-3164-5248-9fe9-c675992a4292' = [ordered]@{ identity='IfcBuildingElementProxy|Pset_绿地信息属性集|绿地类型'; declared_type='IfcLabel'; unit='' }
  'a99a0961-05fe-56fd-b8a0-865410bfe72f' = [ordered]@{ identity='IfcBuildingElementProxy|Pset_绿地信息属性集|折算系数'; declared_type='IfcReal'; unit='' }
}
$actualCheckIds = @($checks.property_id | Sort-Object -Unique)
$expectedCheckIds = @($expectedChecks.Keys | Sort-Object)
if ($checks.Count -ne 10 -or
    @(Compare-Object -ReferenceObject $expectedCheckIds -DifferenceObject $actualCheckIds).Count -ne 0) {
  throw 'IFCFlux checks do not contain the exact 10 expected propertyIds'
}
foreach ($check in $checks) {
  $spec = $expectedChecks[$check.property_id]
  if ($check.identity -cne $spec.identity -or
      $check.expected_declared_type -cne $spec.declared_type -or
      $check.expected_unit -cne $spec.unit) {
    throw "IFCFlux immutable contract mismatch: $($check.property_id)"
  }
}
$invalidPassedChecks = @($checks | Where-Object {
  $spec = $expectedChecks[$_.property_id]
  $_.passed -isnot [bool] -or $_.passed -ne $true -or
  $_.declared_type -cne $spec.declared_type -or $_.unit -cne $spec.unit -or
  $_.observed_value -notmatch '\S' -or $_.owner_global_id -notmatch '\S'
})

$ciRuns = foreach ($workflowName in @('Build BIMBaoGui Revit MCP','Build BIMBaoGui GHA')) {
  $candidate = @(gh run list --repo ArchitectureWorld/BIM-baogui `
    --workflow $workflowName --commit $commit --status success --limit 1 `
    --json databaseId,headSha,conclusion,url | ConvertFrom-Json) | Select-Object -First 1
  if ($null -eq $candidate -or $candidate.headSha -ne $commit -or $candidate.conclusion -ne 'success') {
    throw "missing successful workflow evidence: $workflowName"
  }
  [ordered]@{ name=$workflowName; runId=$candidate.databaseId; headSha=$candidate.headSha; url=$candidate.url }
}
$scanSet = @($emptyScan, $incompleteScan, $goldenScan)
$sameRuleIdentity = @($scanSet | ForEach-Object {
  "$($_.data.rule_package.id)|$($_.data.rule_package.version)|$($_.data.rule_package.sha256)"
} | Sort-Object -Unique).Count -eq 1
$installedArtifactPairs = @(
  @($installed.installedDll, $installed.installedDllSha256),
  @($installed.installedContractsDll, $installed.contractsDllSha256),
  @($installed.installedHifcCoreDll, $installed.hifcCoreDllSha256),
  @($installed.installedMcpServerExe, $installed.mcpServerExeSha256)
)
$installedFilesMatch = @($installedArtifactPairs | Where-Object {
  (Get-FileHash -LiteralPath $_[0] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $_[1]
}).Count -eq 0
$automatedVerified = $installed.productVersion -eq '0.4.3' -and
  $installed.assemblyVersion -eq '0.4.3.0' -and $installedFilesMatch -and
  $sameRuleIdentity -and
  $goldenScan.data.rule_package.id -eq 'HBR-WUHAN-PLANNING' -and
  $goldenScan.data.rule_package.version -eq '1.0.0' -and
  $goldenScan.data.rule_package.sha256 -match '^[0-9a-f]{64}$' -and
  @($ciRuns).Count -eq 2
$hostVerified = @($scanSet | Where-Object {
  $_.data.revit_version -eq '2020' -and $_.data.document_fingerprint -match '\S' -and
  @($_.data.technical_fatals).Count -eq 0 -and $_.data.checklist_counts.not_checked -eq 0
}).Count -eq 3 -and
  $emptyScan.data.checklist_counts.failed -gt 0 -and
  $incompleteScan.data.checklist_counts.passed -gt 0 -and
  $incompleteScan.data.checklist_counts.failed -gt 0
$officialVerified = $pathsMatch -and
  $official.golden_rvt_sha256 -eq $actualGoldenRvtSha -and
  $official.hifctool_manifest_sha256 -eq $actualHifcManifestSha -and
  $official.hifctool_dll_sha256 -eq $actualHifcDllSha -and
  $official.official_ifc_sha256 -eq $actualOfficialIfcSha -and
  $officialProvenanceMatches -and
  $official.hifctool_product_version -match '\S' -and
  $official.hifctool_product_version -eq (Get-Item -LiteralPath $hifcDll).VersionInfo.ProductVersion
$ifcFluxVerified = $pathsMatch -and $officialVerified -and
  $ifcFluxResult.input_ifc_sha256 -eq $actualOfficialIfcSha -and
  $ifcFluxResult.tool_sha256 -eq $actualIfcFluxToolSha -and
  $ifcFluxResult.tool_product_version -match '\S' -and
  $ifcFluxResult.tool_product_version -eq (Get-Item -LiteralPath $ifcFlux).VersionInfo.ProductVersion -and
  $ifcFluxResult.report_sha256 -eq $actualIfcFluxReportSha -and
  $invalidPassedChecks.Count -eq 0

$evidence = [ordered]@{
  schemaVersion = '2.1.0'
  acceptanceRunId = $acceptRunId
  productVersion = '0.4.3'
  build = [ordered]@{
    commit = $commit
    workflows = @($ciRuns)
    installEvidence = File-Evidence $installEvidencePath
    installedDll = File-Evidence $installed.installedDll
    installedContractsDll = File-Evidence $installed.installedContractsDll
    installedHifcCoreDll = File-Evidence $installed.installedHifcCoreDll
    installedMcpServerExe = File-Evidence $installed.installedMcpServerExe
  }
  rulePackage = [ordered]@{
    id = $goldenScan.data.rule_package.id
    version = $goldenScan.data.rule_package.version
    sha256 = $goldenScan.data.rule_package.sha256
  }
  scenarios = @(
    [ordered]@{ name='empty'; rvt=File-Evidence $emptyRvt; scanEvidence=$emptyScan },
    [ordered]@{ name='incomplete'; rvt=File-Evidence $incompleteRvt; scanEvidence=$incompleteScan },
    [ordered]@{ name='golden'; rvt=File-Evidence $goldenRvt; scanEvidence=$goldenScan }
  )
  officialChain = [ordered]@{
    exportResult = File-Evidence $officialPath
    goldenRvtSha256 = $actualGoldenRvtSha
    officialIfcSha256 = $official.official_ifc_sha256
    hifcToolVersion = $official.hifctool_product_version
    hifcToolManifestSha256 = $actualHifcManifestSha
    hifcToolDllSha256 = $actualHifcDllSha
    revitJournal = File-Evidence $resolvedJournal
    hifcToolCommandLine = $official.revit_journal_command_line
    ifcFluxResult = File-Evidence $ifcFluxPath
    ifcFluxVersion = $ifcFluxResult.tool_product_version
    ifcFluxToolSha256 = $actualIfcFluxToolSha
    ifcFluxReport = File-Evidence $ifcFluxResult.report_path
    ifcFluxReportSha256 = $ifcFluxResult.report_sha256
  }
  status = [ordered]@{
    AUTOMATED_VERIFIED = $automatedVerified
    REVIT2020_HOST_VERIFIED = $hostVerified
    OFFICIAL_HIFC_EXPORT_VERIFIED = $officialVerified
    IFCFLUX_CHECKER_VERIFIED = $ifcFluxVerified
  }
}
$evidencePath = Join-Path $repoRoot 'docs\revit-addin\acceptance\native-total-plan-phase1-v0.4.3-evidence.json'
$evidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding utf8NoBOM
$roundTrip = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
if ($roundTrip.acceptanceRunId -ne $acceptRunId) { throw 'evidence round-trip failed' }
```

最终 JSON 必须显式保留三个阶段各自的 runId/resultHash/inputSnapshotHash、文档指纹、最终安装文件 SHA、运行时规则 identity、工具版本和 RVT→官方 IFC→IFCFlux 报告的 SHA 关联。状态完全由这些结构化证据派生；缺文件、hash 不匹配或任一 IFCFlux check 未通过都为 false。若任一步暴露产品缺陷：先新增失败 regression test，修复，重跑 Tasks 12–14；若只是官方 carrier 未证实，则保持 pending，不把证据缺口改成代码通过。

- [ ] **Step 10: 提交可审计证据，不提交二进制原件**

```powershell
git add docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-checklist.md `
  docs/revit-addin/acceptance/native-total-plan-phase1-v0.4.3-evidence.json
git commit -m "docs(acceptance): record native v0.4.3 host evidence"
git diff --check HEAD~1 HEAD
```

只有四项 status 都为 true、且指向相同 Golden RVT 和同一官方 IFC 时，才可对用户报告“官方全链路通过”。否则准确报告已通过层级和仍为 pending/failed 的 exact identity。

---

## 自查要求

计划执行者在每个任务提交前执行对应定向测试；Task 13 前再执行以下一致性检查：

```powershell
rg -n "0\.[5]\.0|v0[5]0|build_hbr_rulepack_v0[5]0" docs/superpowers/plans/2026-08-14-native-revit-total-plan-reporting-phase1.md
rg -n "T[B]D|T[O]DO|i[m]plement later|f[i]ll in details|适[当]错误处理" docs/superpowers/plans/2026-08-14-native-revit-total-plan-reporting-phase1.md
rg -n "93e51676-237e-56a8-8f28-2da845422e2e|ca21e324-046b-5bfd-84c8-0d3470082303|201a00ac-3672-5ded-83d2-ed96f81bfabf|f630ad47-b006-5127-badd-b1660cf996c3|c62cfd5f-2a50-5230-9c5d-4037c39061bf|84df74c2-a7e5-5a98-a5e0-4458e49a3973" docs/superpowers/plans/2026-08-14-native-revit-total-plan-reporting-phase1.md
```

Expected: 第一条无结果；第二条无结果；第三条六个 propertyId 均出现且所有重复拼写完全一致。

需求覆盖映射：

| 需求 | 实施任务 |
|---|---|
| 唯一规则源、完整 identity、总平动态清单 | Tasks 1–2、9 |
| Stage01 登记/坐标/规划目标和回读 | Task 4 |
| 02A 全扫描/自主选择、候选确认、构件级部分成功 | Tasks 5–6 |
| 问题构件选中/缩放/隔离/恢复 | Tasks 6、10 |
| 独立 02B 手填、指标级局部回滚、失败重试 | Tasks 7–8 |
| 03 四色清单、三阶段检查、修复入口 | Tasks 9–10 |
| 强制导出理由、红项保留、技术错误不可绕过 | Task 11 |
| v0.4.3 构建/安装/CI | Tasks 12–13 |
| 空模型、不完整模型、Golden/官方插件/IFCFlux | Task 14 |

完成判定：Tasks 1–13 绿只证明自动化和可安装候选；Task 14 的 Revit 2020、官方 HIFCTool、IFCFlux 证据必须单独满足，不能由内部测试代替。
