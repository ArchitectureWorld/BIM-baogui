# HBR Planning Mapping Baseline v1.0.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 冻结 `HBR-WUHAN-PLANNING / 1.0.0` 的完整 359 条报规映射基线，使唯一规则源、嵌入式规则包、Stage01 identity、全映射 IFC fixture、manifest、CI 和发布 ZIP 使用同一份无空格 X/Y canonical identity，并明确保留 57 条未实现 Owner 能力与 359 条未分类 requirement 的事实。

**Architecture:** `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` 是唯一可编辑业务源；兼容基线只记录经批准的 raw-to-effective identity 例外，compiler 负责 schema/semantic/compatibility fail-closed 校验并生成确定性 `.hbrpack`。fixture generator 只消费 compiler 已验证的 `ifc` identity，validator 独立回读 IFC 和 manifest；GH 运行时只通过 `HbrRuleDatabase.Current` 读取唯一嵌入资源。Stage03 的 RAW/Candidate/Final、扫描/导出原子快照、Strict/Force 和 9 个现有 WIP 文件不在本计划修改范围内。

**Tech Stack:** Python 3.13+ 标准库与 pytest/jsonschema、C# 7.3 / .NET Framework 4.8、MSBuild/dotnet、IFC4 STEP、PowerShell、GitHub Actions Windows runner、Grasshopper `.gha` 嵌入资源。

---

## 文件职责与边界

### 新建

- `tools/hifc/generate_hifc_mapping_smoke.py`：从已验证规则源确定性生成 IFC、fixture manifest 和 rules manifest；不读取历史映射目录。
- `tests/test_hifc_mapping_smoke_fixture.py`：generator、validator、manifest、X/Y mutation 和确定性合同。
- `specs/hbr-rules/v1/manifest.sha256.json`：冻结交付文件与规则包的路径、大小、SHA-256。
- `docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json`：记录用户已完成的 IFCFlux B 人工验收及证据边界。
- `tools/build_hbr_baseline_archive.py`：构造固定时间戳、固定顺序、无压缩的确定性 baseline ZIP。
- `tests/test_hbr_baseline_archive.py`：ZIP 内容、哈希、路径安全和双构建一致性合同。

### 修改

- `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`：唯一规则源；raw X/Y 保留空格，effective identity 改为无空格；加入运行时支持策略。
- `specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json`：关闭并校验运行时支持策略结构。
- `specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json`：记录两条经 IFCFlux 批准的 raw-to-effective identity override。
- `tools/build_hbr_rulepack.py`：提供共享 effective identity、统一加载校验、确定性 pack bytes 和兼容 override 门禁。
- `src/BIMBaoGui.Stage01/Rules/HbrRulePackage.cs`：反序列化并公开运行时支持策略。
- `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs`：校验固定 package identity，并为 359 条规则导出有效运行时状态。
- `src/BIMBaoGui.Stage01/Core/Stage01Keys.cs`：`BaseX/BaseY` 改为无空格 fieldKey。
- `tools/hifc/validate_hifc_mapping_smoke.py`：以 effective identity 为正例，校验 fixture manifest 并可写确定性报告。
- `tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc`：由 generator 重建为 IFCFlux 已确认的无空格 B 字节版本。
- `tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json`：删除 Git commit/时间依赖，记录输入与生成器哈希。
- `tests/fixtures/hifc/README.md`、`docs/hifc/HBR_HIFC_mapping_authority_v1.md`：明确 raw evidence、effective identity、unsupported runtime 边界。
- `tests/test_hbr_rule_source_contract.py`、`tests/test_hbr_rule_source_semantics.py`、`tests/test_hbr_rulepack_compiler.py`、`tests/test_hbr_runtime_packaging_contract.py`：Python 合同门禁。
- `tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs`、`HbrRuleDatabaseTests.cs`、`HbrCatalogProjectionTests.cs`、`HbrRuntimePackagingTests.cs`：运行时加载、状态、identity、唯一资源门禁。
- `tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/stage01-registry.v1.json`、`official-hifc-mappings.v1.json`、`manifest.sha256.v1.json`：只更新由 X/Y fieldKey 变化导致的投影和对应哈希。
- `.github/workflows/build-stage01-gha.yml`：在临时目录重建、比较、打包，并把 baseline ZIP 加入现有单一 artifact 上传。

### 明确禁止进入本计划的现有 WIP

- `src/BIMBaoGui.Stage01/Stage03/Stage03ActivationStatePolicy.cs`
- `src/BIMBaoGui.Stage01/Stage03/Stage03ExportGatePolicy.cs`
- `src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs`
- `src/BIMBaoGui.Stage01/Stage03/Stage03ScannerFieldPolicy.cs`
- `src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03ActivationStatePolicyTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03ExportGatePolicyTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03WorkflowCoordinatorTests.cs`
- `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldStatusTests.cs`

这些文件留给下一份 GH/Stage03 计划；本计划不得移植其中的 identity 特例、UNCLASSIFIED 放行、activation 条件弱化或证据门禁变化。

---

### Task 1: 保护 14 个 WIP，并从已批准基点创建干净工作树

**Files:**
- Preserve only: `D:/18_建模项目/湖北BIM云平台/BIM-baogui-hardening-v090` 中现有 14 个 modified 文件
- Create outside Git: `D:/18_建模项目/湖北BIM云平台/wip-safeguards/2026-08-10-hbr-baseline/`
- Create external worktree: `C:/Users/2899/.config/superpowers/worktrees/BIM-baogui/hbr-planning-mapping-v1`

- [ ] **Step 1: 使用 worktree 技能核对当前 checkout 类型、远端和批准基点**

在执行本任务时先调用 `superpowers:using-git-worktrees`，然后运行：

```powershell
$sourceRoot = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-hardening-v090'
$designCommit = '7e1d39d5e1c45963e8645a020d2417f196236e27'
$approvedBase = (git -C $sourceRoot rev-parse HEAD).Trim()
$planPath = 'docs/superpowers/plans/2026-08-10-hbr-planning-mapping-baseline-v1.md'

git -C $sourceRoot rev-parse --show-toplevel
git -C $sourceRoot rev-parse --git-dir
git -C $sourceRoot rev-parse --git-common-dir
git -C $sourceRoot fetch origin --prune
git -C $sourceRoot cat-file -e "$approvedBase^{commit}"
git -C $sourceRoot merge-base --is-ancestor $designCommit $approvedBase
if ($LASTEXITCODE -ne 0) {
  throw '已批准设计提交不是当前实施基点的祖先。'
}
git -C $sourceRoot merge-base --is-ancestor origin/fix/official-hifc-hardening-v090 $approvedBase
if ($LASTEXITCODE -ne 0) {
  throw '远端 fix/official-hifc-hardening-v090 已超出批准基点；必须先审查远端新增提交。'
}
$baseDelta = @(git -C $sourceRoot diff --name-only "$designCommit..$approvedBase")
if ($baseDelta.Count -ne 1 -or $baseDelta[0] -ne $planPath) {
  throw "设计提交到实施基点之间不是唯一计划文件：$($baseDelta -join ', ')"
}
```

Expected: `cat-file` 和 `merge-base --is-ancestor` 均返回 0；批准设计提交包含当前远端分支历史。

- [ ] **Step 2: 为全部 WIP 和允许移植的 5 个文件各保存一份二进制 patch**

```powershell
$sourceRoot = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-hardening-v090'
$guardRoot = 'D:\18_建模项目\湖北BIM云平台\wip-safeguards\2026-08-10-hbr-baseline'
$fullPatch = Join-Path $guardRoot 'all-14-files.patch'
$baselinePatch = Join-Path $guardRoot 'baseline-5-files.patch'
$approvedBaseFile = Join-Path $guardRoot 'approved-base.txt'
New-Item -ItemType Directory -Force -Path $guardRoot | Out-Null
$approvedBase = (git -C $sourceRoot rev-parse HEAD).Trim()
[IO.File]::WriteAllText(
  $approvedBaseFile,
  $approvedBase + "`n",
  [Text.UTF8Encoding]::new($false))

git -C $sourceRoot diff --binary HEAD --output=$fullPatch
git -C $sourceRoot diff --binary HEAD --output=$baselinePatch -- `
  specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  tools/build_hbr_rulepack.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs

$modified = @(git -C $sourceRoot status --short | Where-Object { $_ -match '^ M ' })
if ($modified.Count -ne 14) { throw "WIP 文件数不是 14：$($modified.Count)" }
if ((Get-Item $fullPatch).Length -le (Get-Item $baselinePatch).Length) {
  throw '全量 WIP patch 必须大于 5 文件 baseline patch。'
}
Get-FileHash $fullPatch -Algorithm SHA256
Get-FileHash $baselinePatch -Algorithm SHA256
```

Expected: 14 个 modified 文件保持原位，两个 patch 非空且 SHA-256 可记录；不得 stash、reset 或 clean 原工作树。

- [ ] **Step 3: 使用现有全局 worktree 根创建隔离分支**

```powershell
$sourceRoot = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-hardening-v090'
$guardRoot = 'D:\18_建模项目\湖北BIM云平台\wip-safeguards\2026-08-10-hbr-baseline'
$approvedBase = (Get-Content -Raw (Join-Path $guardRoot 'approved-base.txt')).Trim()
$worktreeRoot = 'C:\Users\2899\.config\superpowers\worktrees\BIM-baogui'
$cleanRoot = Join-Path $worktreeRoot 'hbr-planning-mapping-v1'
if (-not (Test-Path -LiteralPath $worktreeRoot -PathType Container)) {
  throw "全局 worktree 根不存在：$worktreeRoot"
}
if (Test-Path -LiteralPath $cleanRoot) { throw "目标 worktree 已存在：$cleanRoot" }
git -C $sourceRoot show-ref --verify --quiet refs/heads/feat/hbr-planning-mapping-v1.0.0
if ($LASTEXITCODE -eq 0) { throw '目标 branch 已存在：feat/hbr-planning-mapping-v1.0.0' }

git -C $sourceRoot worktree add -b feat/hbr-planning-mapping-v1.0.0 $cleanRoot $approvedBase
git -C $cleanRoot status --short --branch
```

Expected: 新 worktree 位于现有全局 worktree 根下、分支为 `feat/hbr-planning-mapping-v1.0.0`、工作树干净；不在当前 linked worktree 内嵌套创建。

- [ ] **Step 4: 在未移植 WIP 前跑基线门禁**

```powershell
$cleanRoot = 'C:\Users\2899\.config\superpowers\worktrees\BIM-baogui\hbr-planning-mapping-v1'
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD = '1'
Push-Location $cleanRoot
try {
python -m pytest `
  tests/test_hbr_rule_source_contract.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/test_hbr_rulepack_compiler.py `
  tests/test_hbr_runtime_packaging_contract.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --nologo --logger 'console;verbosity=minimal'
} finally {
  Pop-Location
}
```

Expected: 两条命令均返回 0。若批准基点本身失败，保留输出并停止实施，不把既有失败归因于本计划。

---

### Task 2: 原子移植 X/Y 米制 Length 合同前置修正

**Files:**
- Modify: `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`
- Modify: `tools/build_hbr_rulepack.py`
- Modify: `tests/test_hbr_rule_source_semantics.py`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs`

- [ ] **Step 1: 只应用已审核的 5 文件 patch**

```powershell
$cleanRoot = 'C:\Users\2899\.config\superpowers\worktrees\BIM-baogui\hbr-planning-mapping-v1'
$baselinePatch = 'D:\18_建模项目\湖北BIM云平台\wip-safeguards\2026-08-10-hbr-baseline\baseline-5-files.patch'
git -C $cleanRoot apply --check $baselinePatch
git -C $cleanRoot apply $baselinePatch
$actual = @(git -C $cleanRoot diff --name-only)
$expected = @(
  'specs/hbr-rules/v1/source/hbr_rule_source.v1.json',
  'tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs',
  'tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs',
  'tests/test_hbr_rule_source_semantics.py',
  'tools/build_hbr_rulepack.py'
) | Sort-Object
if (Compare-Object ($actual | Sort-Object) $expected) {
  throw '移植后的文件集合不是批准的 5 个文件。'
}
```

Expected: 只出现上述 5 个路径，9 个 Stage03 WIP 为零变化。

- [ ] **Step 2: 验证 X/Y 数值合同，不在此提交改变 identity**

```powershell
python -m pytest `
  tests/test_hbr_rule_source_semantics.py::test_stage01_xy_keep_blank_workbook_evidence_but_use_meter_length_contract `
  tests/test_hbr_rulepack_compiler.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore --filter 'FullyQualifiedName~HbrCatalogProjectionTests|FullyQualifiedName~HbrRulePackageLoaderTests' `
  --nologo --logger 'console;verbosity=minimal'
```

Expected: PASS；此时 `source.rawProperty` 和 `ifc.property` 仍暂时同为带空格，下一任务用 RED/GREEN 迁移。

- [ ] **Step 3: 规范化这 5 个文本文件为 LF 并提交**

```powershell
git add --renormalize -- `
  specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  tools/build_hbr_rulepack.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs
git diff --cached --check
git commit -m 'fix(rules): freeze XY meter length contract'
```

Expected: commit 只含 5 个文件；X/Y 均为 `IfcReal`，runtime types 为 `IfcReal | IfcLengthMeasure`，canonical unit 为 `m`，Revit parameter type 为 `Length`。

---

### Task 3: 用数据驱动 override 分离 raw evidence 与 effective IFC identity

**Files:**
- Modify: `tests/test_hbr_rule_source_contract.py`
- Modify: `tests/test_hbr_rule_source_semantics.py`
- Modify: `tests/test_hbr_rulepack_compiler.py`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs`
- Modify: `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`
- Modify: `specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json`
- Modify: `tools/build_hbr_rulepack.py`
- Modify: `src/BIMBaoGui.Stage01/Core/Stage01Keys.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/stage01-registry.v1.json`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/official-hifc-mappings.v1.json`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/manifest.sha256.v1.json`

- [ ] **Step 1: 写 effective identity 与 166/166 的失败测试**

在 `tests/test_hbr_rulepack_compiler.py` 加入：

```python
def test_effective_ifc_identity_separates_raw_evidence_from_output_identity():
    from tools.build_hbr_rulepack import effective_ifc_identity

    source = _load_source()
    by_id = {item["propertyId"]: item for item in source["properties"]}
    expected = {
        "6b407894-09d4-529a-9f9f-a031219cdeaa": ("基点坐标 X", "基点坐标X"),
        "1a64ef8d-e97c-5fa1-b53f-52b969b6198a": ("基点坐标 Y", "基点坐标Y"),
    }
    for property_id, (raw_name, output_name) in expected.items():
        rule = by_id[property_id]
        assert rule["source"]["rawProperty"] == raw_name
        assert effective_ifc_identity(rule) == (
            "IfcProject",
            "Pset_申报信息属性集",
            output_name,
        )


def test_all_166_official_effective_identities_match_published_identity():
    from tools.build_hbr_rulepack import effective_ifc_identity

    source = _load_source()
    official = [
        rule for rule in source["properties"]
        if rule["officialPlugin"]["inExtracted166"]
    ]
    matches = sum(
        "|".join(effective_ifc_identity(rule))
        == rule["officialPlugin"]["originalIdentity"]
        for rule in official
    )
    assert matches == 166
```

在 `tests/test_hbr_rule_source_semantics.py` 把旧的 raw-equals-output 断言改为：

```python
assert rule["ifc"]["entity"] == raw["rawEntityId"]
assert rule["ifc"]["propertySet"] == raw["rawPropertySetId"]
if rule["ifc"]["property"] != raw["rawProperty"]:
    assert raw["rawProperty"] in rule["suggestion"]["aliases"]
```

Run:

```powershell
python -m pytest `
  tests/test_hbr_rulepack_compiler.py::test_effective_ifc_identity_separates_raw_evidence_from_output_identity `
  tests/test_hbr_rulepack_compiler.py::test_all_166_official_effective_identities_match_published_identity -q
```

Expected: RED；第一项因 API/无空格 output 尚未完成而失败，第二项显示 `164 != 166`。

- [ ] **Step 2: 在 compatibility baseline 中显式批准且只批准两条 override**

将 compatibility baseline 的 `schemaVersion` 和 `baselineVersion` 升为 `1.2.0`，新增精确结构：

```json
"approvedIdentityOverrides": [
  {
    "propertyId": "6b407894-09d4-529a-9f9f-a031219cdeaa",
    "sourceIdentity": "IfcProject|Pset_申报信息属性集|基点坐标 X",
    "effectiveIdentity": "IfcProject|Pset_申报信息属性集|基点坐标X",
    "reason": "IFCFlux B 单变量试件确认无空格属性名可识别",
    "evidenceSha256": "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632"
  },
  {
    "propertyId": "1a64ef8d-e97c-5fa1-b53f-52b969b6198a",
    "sourceIdentity": "IfcProject|Pset_申报信息属性集|基点坐标 Y",
    "effectiveIdentity": "IfcProject|Pset_申报信息属性集|基点坐标Y",
    "reason": "IFCFlux B 单变量试件确认无空格属性名可识别",
    "evidenceSha256": "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632"
  }
]
```

同时更新 `tests/test_hbr_rulepack_compiler.py` 中 baseline 顶层字段、版本和 override 的精确集合断言。除 `stage01FieldMetadata` 与 `spatialMappings` 两个 digest 外，不允许更新其他 legacy metadata digest。

- [ ] **Step 3: 实现共享 identity API 和 data-driven drift gate**

在 `tools/build_hbr_rulepack.py` 增加并由 compiler、generator、validator 共用：

```python
def _normalized_pset(value):
    return value if value.startswith("Pset_") else f"Pset_{value}"


def source_ifc_identity(rule):
    raw = rule["source"]
    return (
        raw["rawEntityId"],
        _normalized_pset(raw["rawPropertySetId"]),
        raw["rawProperty"],
    )


def effective_ifc_identity(rule):
    ifc = rule["ifc"]
    return (
        ifc["entity"],
        _normalized_pset(ifc["propertySet"]),
        ifc["property"],
    )


def canonical_source_sha256(source):
    return hashlib.sha256(canonical_bytes(source)).hexdigest()


def load_validated_rule_source(source_path, baseline_path):
    source = _load_json_without_duplicate_keys(Path(source_path), "HBR rule source")
    baseline = _load_json_without_duplicate_keys(
        Path(baseline_path), "compatibility baseline"
    )
    validate_semantics(source)
    validate_compatibility(source, baseline)
    return source
```

`validate_compatibility()` 必须构造 `(propertyId, sourceIdentity, effectiveIdentity)` 集合，并要求全部 raw/effective 差异与 `approvedIdentityOverrides` 精确相等；无差异规则不得出现在 override，override 不得重复。对 official 166 再要求 `"|".join(effective_ifc_identity(rule)) == originalIdentity`。

- [ ] **Step 4: 将唯一规则源及 Stage01 identity 改为无空格，保留 raw/alias**

对两个固定 propertyId 做以下数据变更：

```text
source.rawProperty               保持 基点坐标 X / 基点坐标 Y
ifc.property                     改为 基点坐标X / 基点坐标Y
revit.parameterName              改为 HBR｜申报信息属性集｜基点坐标X/Y
revit.legacyNames                保留 HIFC 有/无空格名，并加入旧 HBR 带空格名
suggestion.aliases               保留 rawProperty 与旧 HIFC/HBR 名称
stage01.fieldRefs[].fieldKey     改为 IfcProject|Pset_申报信息属性集|基点坐标X/Y
stage01.spatialMappings.fieldKey 改为 IfcProject|Pset_申报信息属性集|基点坐标X/Y
```

在 `src/BIMBaoGui.Stage01/Core/Stage01Keys.cs` 改为：

```csharp
public const string BaseX = "IfcProject|Pset_申报信息属性集|基点坐标X";
public const string BaseY = "IfcProject|Pset_申报信息属性集|基点坐标Y";
```

compiler 中 `revit.parameterName` 的期望值必须由 `effective_ifc_identity(rule)[2]` 构造；raw 名称只作为 alias 检查。`_EXPECTED_SPATIAL_MAPPINGS` 的 X/Y fieldKey 同步改成无空格。

- [ ] **Step 5: 添加带空格 output 的负例并更新最小快照差异**

在 `tests/test_hbr_rulepack_compiler.py` 增加：

```python
def test_compiler_rejects_spaced_xy_as_final_output(tmp_path):
    from tools.build_hbr_rulepack import compile_rulepack

    source = _load_source()
    x = next(
        rule for rule in source["properties"]
        if rule["propertyId"] == "6b407894-09d4-529a-9f9f-a031219cdeaa"
    )
    x["ifc"]["property"] = "基点坐标 X"
    mutated = tmp_path / "spaced-output.json"
    _write_json(mutated, source)
    output = tmp_path / "spaced-output.hbrpack"

    with pytest.raises(ValueError, match="effective identity"):
        compile_rulepack(mutated, output, BASELINE_PATH)
    assert not output.exists()
```

只更新两个 snapshot 中的 X/Y key/label/fieldKey 和 `manifest.sha256.v1.json` 对应 length/SHA；`mvd-ifc-normalization.v1.json` 与 shared parameter snapshot 已经使用无空格 identity，不应改动。

- [ ] **Step 6: 运行 GREEN 并提交**

```powershell
python -m pytest `
  tests/test_hbr_rule_source_contract.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/test_hbr_rulepack_compiler.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore `
  --filter 'FullyQualifiedName~HbrCatalogProjectionTests|FullyQualifiedName~HbrRulePackageLoaderTests' `
  --nologo --logger 'console;verbosity=minimal'
git diff --check
git add `
  specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  tools/build_hbr_rulepack.py `
  src/BIMBaoGui.Stage01/Core/Stage01Keys.cs `
  tests/test_hbr_rule_source_contract.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/test_hbr_rulepack_compiler.py `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/stage01-registry.v1.json `
  tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/official-hifc-mappings.v1.json `
  tests/BIMBaoGui.Stage01.Core.Tests/Snapshots/manifest.sha256.v1.json
git commit -m 'fix(rules): separate raw XY evidence from IFC identity'
```

Expected: Python/.NET 目标测试全绿，official identity `166/166`，最终 identity 中带空格 X/Y 为零。

---

### Task 4: 在规则包中导出 359 条明确运行时状态

**Files:**
- Modify: `tests/test_hbr_rule_source_contract.py`
- Modify: `tests/test_hbr_rule_source_semantics.py`
- Modify: `tests/test_hbr_rulepack_compiler.py`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleDatabaseTests.cs`
- Modify: `specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json`
- Modify: `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`
- Modify: `tools/build_hbr_rulepack.py`
- Modify: `src/BIMBaoGui.Stage01/Rules/HbrRulePackage.cs`
- Modify: `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs`

- [ ] **Step 1: 写运行时状态投影 RED 测试**

在 Python 测试中要求唯一源含以下闭合策略：

```python
EXPECTED_OWNER_STATUS = {
    "SINGLE_ENTITY_BY_TYPE": "SUPPORTED",
    "BY_EXPORT_GUID": "SUPPORTED",
    "CANONICAL_SPATIAL_ZONE_RECORD": "NOT_IMPLEMENTED",
    "USER_SELECTED_EXPORTABLE_GENERIC_MODEL": "NOT_IMPLEMENTED",
}
EXPECTED_REQUIREMENT_STATUS = {
    "REQUIRED": "SUPPORTED",
    "CONDITIONAL": "SUPPORTED",
    "OPTIONAL": "SUPPORTED",
    "NOT_APPLICABLE": "SUPPORTED",
    "UNCLASSIFIED": "UNCLASSIFIED_REQUIREMENT",
}


def test_runtime_support_policy_resolves_all_359_rules_without_fallback():
    from tools.build_hbr_rulepack import effective_runtime_status

    source = _load(SOURCE_PATH)
    statuses = [effective_runtime_status(source, rule) for rule in source["properties"]]
    assert statuses.count("NOT_IMPLEMENTED") == 57
    assert statuses.count("UNCLASSIFIED_REQUIREMENT") == 302
    assert len(statuses) == 359
```

在 `HbrRuleDatabaseTests.cs` 加入等价断言：

```csharp
[Fact]
public void Runtime_status_is_explicit_for_all_359_properties()
{
  HbrRuleDatabase database = HbrRuleDatabase.Current;
  var counts = database.Package.Properties
    .Select(database.GetEffectiveRuntimeStatus)
    .GroupBy(value => value, StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

  Assert.Equal(57, counts["NOT_IMPLEMENTED"]);
  Assert.Equal(302, counts["UNCLASSIFIED_REQUIREMENT"]);
  Assert.Equal(359, counts.Values.Sum());
}
```

Run:

```powershell
python -m pytest tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore --filter 'FullyQualifiedName~Runtime_status_is_explicit' --nologo
```

Expected: RED；源中尚无 `runtimeSupport`，C# 尚无投影 API。

- [ ] **Step 2: 在唯一源与 schema 中加入规范化策略而非复制 359 次状态**

在根级新增：

```json
"runtimeSupport": {
  "statusPrecedence": [
    "NOT_IMPLEMENTED",
    "UNCLASSIFIED_REQUIREMENT",
    "OFFICIAL_EVIDENCE_ONLY",
    "SUPPORTED"
  ],
  "ownerStrategies": [
    {"ownerStrategy": "BY_EXPORT_GUID", "status": "SUPPORTED"},
    {"ownerStrategy": "CANONICAL_SPATIAL_ZONE_RECORD", "status": "NOT_IMPLEMENTED"},
    {"ownerStrategy": "SINGLE_ENTITY_BY_TYPE", "status": "SUPPORTED"},
    {"ownerStrategy": "USER_SELECTED_EXPORTABLE_GENERIC_MODEL", "status": "NOT_IMPLEMENTED"}
  ],
  "requirementLevels": [
    {"requirementLevel": "CONDITIONAL", "status": "SUPPORTED"},
    {"requirementLevel": "NOT_APPLICABLE", "status": "SUPPORTED"},
    {"requirementLevel": "OPTIONAL", "status": "SUPPORTED"},
    {"requirementLevel": "REQUIRED", "status": "SUPPORTED"},
    {"requirementLevel": "UNCLASSIFIED", "status": "UNCLASSIFIED_REQUIREMENT"}
  ]
}
```

Schema 根级 `required` 增加 `runtimeSupport`，新增的每个 object 都设置 `additionalProperties: false`；状态 enum 精确为 `SUPPORTED / NOT_IMPLEMENTED / UNCLASSIFIED_REQUIREMENT / OFFICIAL_EVIDENCE_ONLY`。`ownerStrategies` 和 `requirementLevels` 使用数组，以便 JSON Schema 校验 shape，semantic validator 再校验 key 集合、顺序、唯一性和固定映射。

- [ ] **Step 3: 实现无 fallback 的 Python 状态解析**

在 `tools/build_hbr_rulepack.py` 增加：

```python
def effective_runtime_status(source, rule):
    policy = source["runtimeSupport"]
    owner_statuses = {
        item["ownerStrategy"]: item["status"]
        for item in policy["ownerStrategies"]
    }
    requirement_statuses = {
        item["requirementLevel"]: item["status"]
        for item in policy["requirementLevels"]
    }
    candidates = (
        owner_statuses[rule["ifcWrite"]["ownerStrategy"]],
        requirement_statuses[rule["requirement"]["level"]],
    )
    for status in policy["statusPrecedence"]:
        if status in candidates:
            return status
    raise ValueError(f"no runtime status for property {rule['propertyId']}")
```

semantic validator 必须断言 4 个 Owner strategy 和 5 个 requirement level 精确覆盖、没有未知状态、所有 359 条都能解析，并冻结 `57 NOT_IMPLEMENTED + 302 UNCLASSIFIED_REQUIREMENT`。不得把 UNCLASSIFIED 映射为 SUPPORTED。

- [ ] **Step 4: 在 .NET package/database 中公开相同数据和算法**

在 `HbrRulePackage.cs` 增加 DTO/domain types：

```csharp
public sealed class HbrRuntimeSupportPolicy
{
  internal HbrRuntimeSupportPolicy(HbrRuntimeSupportPolicyDto dto, string path)
  {
    dto = HbrDomain.Required(dto, path);
    StatusPrecedence = HbrDomain.FreezeStrings(dto.statusPrecedence, path + ".statusPrecedence");
    OwnerStrategies = HbrDomain.ConvertList(
      dto.ownerStrategies,
      path + ".ownerStrategies",
      (item, itemPath) => new HbrOwnerStrategySupport(item, itemPath));
    RequirementLevels = HbrDomain.ConvertList(
      dto.requirementLevels,
      path + ".requirementLevels",
      (item, itemPath) => new HbrRequirementLevelSupport(item, itemPath));
  }

  public IReadOnlyList<string> StatusPrecedence { get; }
  public IReadOnlyList<HbrOwnerStrategySupport> OwnerStrategies { get; }
  public IReadOnlyList<HbrRequirementLevelSupport> RequirementLevels { get; }
}

public sealed class HbrOwnerStrategySupport
{
  internal HbrOwnerStrategySupport(HbrOwnerStrategySupportDto dto, string path)
  {
    dto = HbrDomain.Required(dto, path);
    OwnerStrategy = HbrDomain.NonBlank(dto.ownerStrategy, path + ".ownerStrategy");
    Status = HbrDomain.NonBlank(dto.status, path + ".status");
  }

  public string OwnerStrategy { get; }
  public string Status { get; }
}

public sealed class HbrRequirementLevelSupport
{
  internal HbrRequirementLevelSupport(HbrRequirementLevelSupportDto dto, string path)
  {
    dto = HbrDomain.Required(dto, path);
    RequirementLevel = HbrDomain.NonBlank(
      dto.requirementLevel,
      path + ".requirementLevel");
    Status = HbrDomain.NonBlank(dto.status, path + ".status");
  }

  public string RequirementLevel { get; }
  public string Status { get; }
}

internal sealed class HbrRuntimeSupportPolicyDto
{
  public List<string> statusPrecedence { get; set; }
  public List<HbrOwnerStrategySupportDto> ownerStrategies { get; set; }
  public List<HbrRequirementLevelSupportDto> requirementLevels { get; set; }
}

internal sealed class HbrOwnerStrategySupportDto
{
  public string ownerStrategy { get; set; }
  public string status { get; set; }
}

internal sealed class HbrRequirementLevelSupportDto
{
  public string requirementLevel { get; set; }
  public string status { get; set; }
}
```

`HbrRulePackageDto` 增加 `public HbrRuntimeSupportPolicyDto runtimeSupport { get; set; }`；`HbrRulePackage` 构造器必须 materialize 公共属性 `RuntimeSupport`。`HbrRuleDatabase` 构造时把两组策略建成 `StringComparer.Ordinal` 的只读字典，并公开：

```csharp
public string GetEffectiveRuntimeStatus(HbrRuleProperty property)
{
  if (property == null) throw new ArgumentNullException(nameof(property));
  string owner = _ownerStrategyStatuses[property.IfcWrite.OwnerStrategy];
  string requirement = _requirementStatuses[property.Requirement.Level];
  foreach (string status in Package.RuntimeSupport.StatusPrecedence)
    if (StringComparer.Ordinal.Equals(status, owner)
      || StringComparer.Ordinal.Equals(status, requirement))
      return status;
  throw new InvalidDataException(
    "HBRP runtime support policy did not resolve property " + property.PropertyId + ".");
}
```

初始化时还要验证 `PackageId == HBR-WUHAN-PLANNING`、`PackageVersion == 1.0.0`，错误时抛 `InvalidDataException`，保持 runtime fail-closed。

- [ ] **Step 5: 运行 GREEN、构建嵌入 pack 并提交**

```powershell
python -m pytest `
  tests/test_hbr_rule_source_contract.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/test_hbr_rulepack_compiler.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore --nologo --logger 'console;verbosity=minimal'
git diff --check
git add `
  specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json `
  tools/build_hbr_rulepack.py `
  src/BIMBaoGui.Stage01/Rules/HbrRulePackage.cs `
  src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs `
  tests/test_hbr_rule_source_contract.py `
  tests/test_hbr_rule_source_semantics.py `
  tests/test_hbr_rulepack_compiler.py `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleDatabaseTests.cs
git commit -m 'feat(rules): expose explicit runtime support states'
```

Expected: 359 条均解析为一个有效状态；Owner 能力仍为 302 可实现、57 `NOT_IMPLEMENTED`；requirement 仍诚实保留 359 个 `UNCLASSIFIED`。

---

### Task 5: 恢复标准库确定性 full-mapping fixture generator

**Files:**
- Create: `tools/hifc/generate_hifc_mapping_smoke.py`
- Create: `tests/test_hifc_mapping_smoke_fixture.py`
- Modify: `tools/build_hbr_rulepack.py`

- [ ] **Step 1: 写 generator 不存在时的 RED 合同**

新测试文件先定义固定入口与验收锚点：

```python
ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "specs/hbr-rules/v1/source/hbr_rule_source.v1.json"
BASELINE = ROOT / "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json"
GENERATOR = ROOT / "tools/hifc/generate_hifc_mapping_smoke.py"
FIXTURE = ROOT / "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc"
FIXTURE_MANIFEST = FIXTURE.with_suffix(".manifest.json")
IFCFLUX_B_SHA256 = "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632"


def test_generator_entrypoint_exists():
    assert GENERATOR.is_file()
```

Run:

```powershell
python -m pytest tests/test_hifc_mapping_smoke_fixture.py::test_generator_entrypoint_exists -q
```

Expected: RED，明确报告 generator 文件缺失。

- [ ] **Step 2: 提取确定性 pack bytes，避免 generator 重复 HBRP 逻辑**

在 compiler 中增加并由 `compile_rulepack()` 调用：

```python
def build_rulepack_bytes(source):
    payload = canonical_bytes(source)
    return (
        MAGIC
        + struct.pack(">I", FORMAT_VERSION)
        + struct.pack(">Q", len(payload))
        + hashlib.sha256(payload).digest()
        + payload
    )
```

`compile_rulepack()` 仍负责 atomic replace；同一 source 必须得到相同 pack bytes。

- [ ] **Step 3: 实现 generator 的稳定公开 API**

generator 顶层 API 固定为：

```python
GENERATOR_VERSION = "1.0.0"
FIXED_FILE_TIMESTAMP = "2026-08-07T18:00:00+08:00"


@dataclass(frozen=True)
class FixtureSummary:
    step_entities: int
    properties: int
    property_sets: int
    attachments: int
    owner_types: Sequence[str]
    extruded_solids: int


def build_ifc_bytes(source: Mapping[str, object]) -> tuple[bytes, FixtureSummary]:
    allocator = StepIdAllocator()
    document = IfcFixtureDocument(allocator, FIXED_FILE_TIMESTAMP)
    owners = document.add_owner_scaffold(source["guidNamespace"])
    document.add_spatial_relationships(owners)
    document.add_visible_geometry(owners)
    document.add_rule_properties(source["properties"], owners)
    payload = document.to_bytes()
    return payload, summarize_fixture(payload)


def build_fixture_manifest(
    root: Path,
    source_path: Path,
    baseline_path: Path,
    generator_path: Path,
    ifc_path: Path,
    ifc_bytes: bytes,
    source: Mapping[str, object],
    summary: FixtureSummary,
) -> bytes:
    document = fixture_manifest_document(
        root=root,
        source_path=source_path,
        baseline_path=baseline_path,
        generator_path=generator_path,
        ifc_path=ifc_path,
        ifc_bytes=ifc_bytes,
        source=source,
        summary=summary,
    )
    return canonical_json_bytes(document)


def generate_fixture(
    source_path: Path,
    baseline_path: Path,
    output_path: Path,
    fixture_manifest_path: Path,
) -> FixtureSummary:
    source = load_validated_rule_source(source_path, baseline_path)
    ifc_bytes, summary = build_ifc_bytes(source)
    manifest_bytes = build_fixture_manifest(
        repository_root(source_path),
        source_path,
        baseline_path,
        Path(__file__),
        output_path,
        ifc_bytes,
        source,
        summary,
    )
    atomic_replace_bytes(output_path, ifc_bytes)
    atomic_replace_bytes(fixture_manifest_path, manifest_bytes)
    return summary
```

同一文件中必须定义上述调用的私有构件，接口和职责固定如下：

```python
@dataclass
class StepIdAllocator:
    next_id: int = 1

    def allocate(self) -> int:
        value = self.next_id
        self.next_id += 1
        return value


def canonical_json_bytes(value: Mapping[str, object]) -> bytes:
    return (
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=False)
        + "\n"
    ).encode("utf-8")


def repository_root(source_path: Path) -> Path:
    resolved = source_path.resolve()
    for parent in (resolved.parent, *resolved.parents):
        if (parent / ".git").exists() or (parent / ".git").is_file():
            return parent
    raise ValueError(f"source is not inside a Git worktree: {resolved}")


def atomic_replace_bytes(path: Path, payload: bytes) -> None:
    path = path.resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(
        dir=str(path.parent), prefix=f".{path.name}.", suffix=".tmp"
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    except BaseException:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise
```

`IfcFixtureDocument` 也在该文件内定义，且必须完整实现 `add_owner_scaffold()`、`add_spatial_relationships()`、`add_visible_geometry()`、`add_rule_properties()`、`to_bytes()`；`summarize_fixture()` 独立解析刚生成的 bytes 后计数，不能直接回报写入循环计数。`fixture_manifest_document()` 使用下面 Task 6 的精确字段构造 manifest，不添加额外字段。

上面两个函数体按以下完整约束实现，不接受环境输入：

1. 先调用 `load_validated_rule_source()`；identity 只调用 `effective_ifc_identity(rule)`，不得读取 rawProperty、alias 或 `originalIdentity` 猜最终名称。
2. 固定 IFC4 / `ReferenceView_V1.2` header、`FIXED_FILE_TIMESTAMP`、author/application 文本和 14 类 Owner scaffold。
3. 以 source 中 properties 的稳定顺序生成 property，按 `(owner type, propertySet)` 的 UTF-8 bytes 排序生成 52 个 Pset/attachment；STEP id 由单调 allocator 唯一分配。
4. GlobalId 使用现有 `guidNamespace`、语义 seed、UUIDv5 和 IFC GUID 压缩；不得使用随机数。
5. 非 ASCII 字符统一编码为 UTF-16BE；例如 `基点坐标X` 固定编码为 `\X2\57FA70B9575068070058\X0\`。单引号按 STEP 规则双写；locale 不参与数值格式化。
6. X propertyId `6b407894-09d4-529a-9f9f-a031219cdeaa` 固定 `IFCREAL(3353559.52)`，Y propertyId `1a64ef8d-e97c-5fa1-b53f-52b969b6198a` 固定 `IFCREAL(38345264.397)`；Boolean sample 固定 `.T.`，但 manifest 标为 structural smoke policy。
7. 维持 Project→Site→Building→Storey→Space、一个真实 SpatialZone、IfcActor→IfcOrganization、一个楼层 containment、一个 zone reference、9 个拉伸体。
8. 输出严格为 UTF-8 无 BOM、LF、末尾单一 LF；先写同目录临时文件并 `flush/fsync`，IFC replace 成功后最后 replace manifest；异常清理临时文件。
9. source/baseline/output/manifest 必须是四个不同解析路径；不写绝对路径、当前时间、Git commit、用户名或临时目录。

- [ ] **Step 4: 补齐确定性、路径和失败原子性测试**

测试名固定为 `test_generator_is_deterministic_for_identical_validated_source`、`test_generator_emits_every_effective_identity_exactly_once`、`test_generator_emits_only_unspaced_xy_output_identities`、`test_generator_writes_utf8_without_bom_lf_and_no_environment_metadata`、`test_generator_rejects_invalid_source_without_outputs_or_temp_files`、`test_generator_cli_supports_paths_with_spaces`。

关键断言：两次 `build_ifc_bytes(source)` bytes 相等；实际 identities 精确等于 359 条 effective identity；带空格 X/Y 出现 0 次；`len(entities)=616`、property/Pset/attachment=`359/52/52`、Owner=14、extruded solids=9。

- [ ] **Step 5: 运行 GREEN 并提交 generator，不覆盖正式 fixture**

```powershell
python -m pytest `
  tests/test_hbr_rulepack_compiler.py `
  tests/test_hifc_mapping_smoke_fixture.py -q
git diff --check
git add tools/build_hbr_rulepack.py tools/hifc/generate_hifc_mapping_smoke.py tests/test_hifc_mapping_smoke_fixture.py
git commit -m 'feat(hifc): restore deterministic full-mapping generator'
```

Expected: generator 在 `tmp_path` 中满足结构与确定性合同；正式 fixture 留到下一任务和 IFCFlux B 字节锚点一起冻结。

---

### Task 6: 冻结 IFCFlux B fixture、独立 validator 和人工验收证据

**Files:**
- Modify: `tools/hifc/validate_hifc_mapping_smoke.py`
- Modify: `tests/test_hifc_mapping_smoke_fixture.py`
- Modify: `tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc`
- Modify: `tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json`
- Modify: `tests/fixtures/hifc/README.md`
- Modify: `docs/hifc/HBR_HIFC_mapping_authority_v1.md`
- Create: `docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json`

- [ ] **Step 1: 写 committed fixture 与 manifest 的 RED 测试**

```python
def test_generator_rebuilds_committed_fixture_and_manifest_byte_for_byte(tmp_path):
    generated = tmp_path / FIXTURE.name
    manifest = tmp_path / FIXTURE_MANIFEST.name
    generate_fixture(SOURCE, BASELINE, generated, manifest)
    assert generated.read_bytes() == FIXTURE.read_bytes()
    assert manifest.read_bytes() == FIXTURE_MANIFEST.read_bytes()
    assert hashlib.sha256(generated.read_bytes()).hexdigest() == IFCFLUX_B_SHA256


def test_fixture_keeps_616_359_52_52_14_9_release_contract():
    result = validate(SOURCE, BASELINE, FIXTURE, FIXTURE_MANIFEST)
    assert result["stepEntities"] == 616
    assert result["properties"] == 359
    assert result["propertySets"] == 52
    assert result["attachments"] == 52
    assert len(result["ownerTypes"]) == 14
    assert result["extrudedSolids"] == 9
```

Run:

```powershell
python -m pytest `
  tests/test_hifc_mapping_smoke_fixture.py::test_generator_rebuilds_committed_fixture_and_manifest_byte_for_byte `
  tests/test_hifc_mapping_smoke_fixture.py::test_fixture_keeps_616_359_52_52_14_9_release_contract -q
```

Expected: RED；已提交 fixture 仍是带空格 SHA `6293fb5a198716857292549297abed2f9a6affcc5f0552b3f60f2eb858742bf2`，manifest 仍含 stale `sourceCommit`。

- [ ] **Step 2: 让 validator 独立校验 source、baseline、IFC 和 manifest**

CLI 固定支持：

```text
--source PATH
--mapping PATH    # --source 的兼容 alias，两者同时出现时必须报错
--baseline PATH
--ifc PATH
--manifest PATH
--report PATH     # 可选，写确定性 JSON 报告
```

fixture manifest 顶层由以下函数精确构造；所有 hash/bytes 都从已读取的真实 bytes 计算：

```python
EXPECTED_OWNER_TYPES = (
    "IfcActor",
    "IfcBuilding",
    "IfcBuildingStorey",
    "IfcDoor",
    "IfcDuctSegment",
    "IfcProject",
    "IfcRoof",
    "IfcSite",
    "IfcSlab",
    "IfcSpace",
    "IfcSpatialZone",
    "IfcStairFlight",
    "IfcWall",
    "IfcWindow",
)


def fixture_manifest_document(
    root, source_path, baseline_path, generator_path, ifc_path,
    ifc_bytes, source, summary,
):
    source_bytes = source_path.read_bytes()
    baseline_bytes = baseline_path.read_bytes()
    generator_bytes = generator_path.read_bytes()
    return {
        "schemaVersion": "1.0.0",
        "fixtureId": "HBR-HIFC-FULL-MAPPING-V1",
        "generator": {
            "path": "tools/hifc/generate_hifc_mapping_smoke.py",
            "version": GENERATOR_VERSION,
            "sha256": hashlib.sha256(generator_bytes).hexdigest(),
        },
        "source": {
            "path": "specs/hbr-rules/v1/source/hbr_rule_source.v1.json",
            "sha256": hashlib.sha256(source_bytes).hexdigest(),
            "canonicalSha256": canonical_source_sha256(source),
            "compatibilityBaselinePath": "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json",
            "compatibilityBaselineSha256": hashlib.sha256(baseline_bytes).hexdigest(),
            "packageId": source["packageId"],
            "packageVersion": source["packageVersion"],
        },
        "fixture": {
            "path": "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc",
            "sha256": hashlib.sha256(ifc_bytes).hexdigest(),
            "bytes": len(ifc_bytes),
            "encoding": "UTF-8",
            "lineEnding": "LF",
            "schema": "IFC4",
            "viewDefinition": "ReferenceView_V1.2",
        },
        "summary": {
            "stepEntities": summary.step_entities,
            "properties": summary.properties,
            "propertySets": summary.property_sets,
            "attachments": summary.attachments,
            "ownerTypes": list(summary.owner_types),
            "extrudedSolids": summary.extruded_solids,
        },
        "policies": {
            "valueProfile": "STRUCTURAL_SMOKE_V1",
            "booleanSample": "ALWAYS_TRUE_FOR_IFCFLUX_SMOKE",
        },
    }
```

`summary.owner_types` 必须精确等于 `EXPECTED_OWNER_TYPES`。validator 自己解析 IFC 并比较 manifest；manifest 不能驱动实际计数。validator 的新公开入口固定为 `validate(source_path: Path, baseline_path: Path, ifc_path: Path, manifest_path: Path) -> dict`。报告只含相对逻辑路径、输入/IFC SHA 和 summary，不含当前时间或绝对路径。

- [ ] **Step 3: 将 X/Y 正负例反转为批准语义**

validator 的坐标检查改为：

```python
x_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标X")
y_key = ("IFCPROJECT", "Pset_申报信息属性集", "基点坐标Y")
if actual[x_key]["typed_token"] != "IFCREAL(3353559.52)":
    raise AssertionError("X/南北坐标值错误")
if actual[y_key]["typed_token"] != "IFCREAL(38345264.397)":
    raise AssertionError("Y/东西坐标值错误")
for forbidden in ("基点坐标 X", "基点坐标 Y"):
    if ("IFCPROJECT", "Pset_申报信息属性集", forbidden) in actual:
        raise AssertionError("最终 IFC 含带空格坐标 identity")
```

增加 mutation 测试：把临时 IFC 的无空格 X 或 Y 改回带空格，validator 必须非零退出且报告 `映射路径不一致` 或 `带空格坐标 identity`。

- [ ] **Step 4: 生成并冻结与 IFCFlux B 完全相同的正式 fixture**

```powershell
python tools/hifc/generate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --output tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json

$fixture = 'tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc'
$actualHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne '570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632') {
  throw "生成 fixture 未复现 IFCFlux B：$actualHash"
}
```

Expected: `56964` bytes、SHA-256 精确为 B hash；这既是 source-derived fixture，也是用户已确认可识别的字节文件。

- [ ] **Step 5: 写诚实的 IFCFlux 验收记录**

新 JSON 固定记录：

```json
{
  "schemaVersion": "1.0.0",
  "evidenceId": "HBR-HIFC-FULL-MAPPING-V1-IFCFLUX-B",
  "tool": "IFCFlux",
  "toolVersionStatus": "NOT_RECORDED",
  "testedOn": "2026-08-10",
  "evidenceType": "USER_CONFIRMED_MANUAL_RUN",
  "fixtureSha256": "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632",
  "result": "PASS",
  "assertions": {
    "baseCoordinateXRecognized": true,
    "baseCoordinateYRecognized": true,
    "xAxis": "Northing",
    "yAxis": "Easting",
    "axisSwapObserved": false
  },
  "limitations": [
    "IFCFlux 版本号未记录",
    "本次确认没有独立机器导出报告或截图"
  ]
}
```

README/authority 文档同步把 canonical 改为 `基点坐标X/Y`，明确结构 fixture 包含尚未由 GH 实现的 SpatialZone/Organization Owner，只证明目标 IFC 结构，不证明 Stage03 已完整生产支持。

- [ ] **Step 6: 运行 GREEN 并提交**

```powershell
python tools/hifc/validate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --ifc tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json
python -m pytest tests/test_hifc_mapping_smoke_fixture.py -q
git diff --check
git add tools/hifc/validate_hifc_mapping_smoke.py tests/test_hifc_mapping_smoke_fixture.py `
  tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  tests/fixtures/hifc/README.md docs/hifc/HBR_HIFC_mapping_authority_v1.md `
  docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json
git commit -m 'test(hifc): freeze IFCFlux accepted full-mapping fixture'
```

Expected: validator 输出 `PASS` 及 616/359/52/52/14/9；临时重建与 committed IFC/manifest 逐字节相同。

---

### Task 7: 生成 rules manifest 与确定性 baseline ZIP

**Files:**
- Create: `specs/hbr-rules/v1/manifest.sha256.json`
- Create: `tools/build_hbr_baseline_archive.py`
- Create: `tests/test_hbr_baseline_archive.py`
- Modify: `tools/hifc/generate_hifc_mapping_smoke.py`
- Modify: `tests/test_hifc_mapping_smoke_fixture.py`
- Modify: `tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json`

- [ ] **Step 1: 写 rules manifest RED 合同**

```python
FROZEN_PATHS = (
    "docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json",
    "specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json",
    "specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json",
    "specs/hbr-rules/v1/source/hbr_rule_source.v1.json",
    "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc",
    "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json",
    "tools/build_hbr_baseline_archive.py",
    "tools/build_hbr_rulepack.py",
    "tools/hifc/generate_hifc_mapping_smoke.py",
    "tools/hifc/validate_hifc_mapping_smoke.py",
)


def test_rules_manifest_paths_are_relative_sorted_unique_and_exact():
    manifest = json.loads(RULES_MANIFEST.read_text(encoding="utf-8"))
    paths = [item["path"] for item in manifest["files"]]
    assert tuple(paths) == FROZEN_PATHS
    assert all("\\" not in path and not Path(path).is_absolute() for path in paths)
```

Run:

```powershell
python -m pytest tests/test_hifc_mapping_smoke_fixture.py::test_rules_manifest_paths_are_relative_sorted_unique_and_exact -q
```

Expected: RED，rules manifest 尚不存在。

- [ ] **Step 2: 生成闭合、非自引用的 rules manifest**

顶层结构由下列函数固定构造：

```python
def build_rules_manifest_document(
    root: Path,
    source: Mapping[str, object],
    fixture_bytes: bytes,
    fixture_manifest_bytes: bytes,
):
    pack = build_rulepack_bytes(source)
    payload = canonical_bytes(source)
    generated = {
        "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc": fixture_bytes,
        "tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json": fixture_manifest_bytes,
    }
    files = []
    for relative in FROZEN_PATHS:
        data = generated.get(relative)
        if data is None:
            data = (root / Path(relative)).read_bytes()
        files.append({
            "path": relative,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
        })
    generator_bytes = (
        root / "tools/hifc/generate_hifc_mapping_smoke.py"
    ).read_bytes()
    statuses = [effective_runtime_status(source, rule) for rule in source["properties"]]
    return {
        "schemaVersion": "1.0.0",
        "manifestId": "HBR-WUHAN-PLANNING-1.0.0-BASELINE",
        "packageId": source["packageId"],
        "packageVersion": source["packageVersion"],
        "generator": {
            "path": "tools/hifc/generate_hifc_mapping_smoke.py",
            "version": GENERATOR_VERSION,
            "sha256": hashlib.sha256(generator_bytes).hexdigest(),
        },
        "rulePack": {
            "logicalPath": "src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack",
            "bytes": len(pack),
            "sha256": hashlib.sha256(pack).hexdigest(),
            "payloadSha256": hashlib.sha256(payload).hexdigest(),
        },
        "runtimeStatusCounts": {
            "NOT_IMPLEMENTED": statuses.count("NOT_IMPLEMENTED"),
            "UNCLASSIFIED_REQUIREMENT": statuses.count("UNCLASSIFIED_REQUIREMENT"),
        },
        "files": files,
    }
```

`generate_fixture()` 在本任务扩展为以下签名，保持 Task 5 的四参数调用兼容：

```python
def generate_fixture(
    source_path: Path,
    baseline_path: Path,
    output_path: Path,
    fixture_manifest_path: Path,
    rules_manifest_path: Path | None = None,
) -> FixtureSummary:
    ...
```

`FROZEN_PATHS` 在 generator 中作为公开常量定义，测试直接导入它，避免复制出第二份路径清单。`files` 精确为 `FROZEN_PATHS`、Ordinal 排序、唯一，不包含 manifest 自身。fixture 与 fixture manifest 的哈希必须来自本次生成的内存 bytes，不得回读仓库内已提交的同名文件；这使 `tmp_path` 重建能证明实际生成物。CLI 增加 `--rules-manifest`；所有三份输出 bytes 均在任何 replace 前构造完成，写入顺序为 IFC、可选 rules manifest、最后 fixture manifest，使 fixture manifest 继续作为完成标记。因 generator 文件在本任务发生变化，必须同步重生成并提交 fixture manifest 中的 `generator.sha256`。

- [ ] **Step 3: 写 baseline ZIP 的 RED 测试**

ZIP entry 精确为：

```python
EXPECTED_ARCHIVE_ENTRIES = (
    "HBR_HIFC_全映射结构验证_v1.0.ifc",
    "HBR_HIFC_全映射结构验证_v1.0.ifcflux.json",
    "HBR_HIFC_全映射结构验证_v1.0.manifest.json",
    "HBR_HIFC_全映射结构验证_v1.0.validation.json",
    "HBR_RulePack.hbrpack",
    "manifest.sha256.json",
    "release-manifest.json",
)
```

测试两次调用 archive builder 到不同临时目录，断言 ZIP bytes 相同；所有 `ZipInfo.date_time == (1980, 1, 1, 0, 0, 0)`、`compress_type == ZIP_STORED`、无目录穿越、release manifest 中 pack payload SHA 等于 rules manifest。release manifest 的 entry 表只记录其余 6 个 archive entry，明确排除 `release-manifest.json` 自身，避免自引用哈希。

- [ ] **Step 4: 实现确定性 archive builder**

公开 API：

```python
ARCHIVE_NAME = "HBR-WUHAN-PLANNING-v1.0.0-baseline.zip"
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)


def build_baseline_archive(
    root: Path,
    rule_pack: Path,
    rules_manifest: Path,
    fixture: Path,
    fixture_manifest: Path,
    validation_report: Path,
    ifcflux_evidence: Path,
    output: Path,
) -> dict:
    verified = verify_release_inputs(
        root,
        rule_pack,
        rules_manifest,
        fixture,
        fixture_manifest,
        validation_report,
        ifcflux_evidence,
    )
    release_manifest = canonical_json_bytes(release_manifest_document(verified))
    entries = archive_entries(verified, release_manifest)
    write_zip_atomic(output, entries, ZIP_TIMESTAMP)
    return release_manifest_document(verified)
```

同一文件定义的私有入口不得改名：`verify_release_inputs()` 逐项校验 rules manifest、fixture manifest、pack header、acceptance fixture hash 和 validation report；`release_manifest_document()` 返回 packageId/version、pack whole/payload SHA、fixture SHA，以及除 `release-manifest.json` 自身外其余 6 个 entry 的 bytes/SHA；`archive_entries()` 只返回 `EXPECTED_ARCHIVE_ENTRIES` 的固定映射；`write_zip_atomic()` 拒绝 absolute/`..` entry，并使用固定 timestamp、权限和 `ZIP_STORED` 后 atomic replace。所有私有入口都有直接单元测试。`release-manifest.json` 不记录自身 SHA；它由整个 baseline ZIP 的外层 SHA 保护。

实现先校验 rules/fixture manifest、pack header magic/version/length/payload hash、fixture/acceptance hash，再以 UTF-8 名称、固定 timestamp、`ZIP_STORED`、`external_attr = 0o100644 << 16`、上面的固定 entry 顺序写临时 ZIP并 atomic replace。`release-manifest.json` 由输入哈希和大小确定性生成，不含 commit、时间或绝对路径。

- [ ] **Step 5: 生成正式 rules manifest、验证报告和本地 ZIP**

```powershell
python tools/hifc/generate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --output tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  --rules-manifest specs/hbr-rules/v1/manifest.sha256.json

python tools/build_hbr_rulepack.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --output src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack

New-Item -ItemType Directory -Force artifacts | Out-Null
python tools/hifc/validate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --ifc tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  --report artifacts/HBR_HIFC_全映射结构验证_v1.0.validation.json

python tools/build_hbr_baseline_archive.py `
  --root . `
  --rule-pack src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack `
  --rules-manifest specs/hbr-rules/v1/manifest.sha256.json `
  --fixture tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --fixture-manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  --validation-report artifacts/HBR_HIFC_全映射结构验证_v1.0.validation.json `
  --ifcflux-evidence docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json `
  --output artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip
```

Expected: rules manifest 全量匹配；ZIP 存在于 gitignored `artifacts/`，entry 集合精确，无第二份规则源。

- [ ] **Step 6: 运行 GREEN 并提交可重建发布链**

```powershell
python -m pytest tests/test_hifc_mapping_smoke_fixture.py tests/test_hbr_baseline_archive.py -q
git diff --check
git add `
  specs/hbr-rules/v1/manifest.sha256.json `
  tools/hifc/generate_hifc_mapping_smoke.py `
  tools/build_hbr_baseline_archive.py `
  tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  tests/test_hifc_mapping_smoke_fixture.py `
  tests/test_hbr_baseline_archive.py
git commit -m 'build(rules): add deterministic baseline manifest and archive'
```

Expected: 两次 ZIP build 字节相同；`artifacts/` 保持未跟踪且不进入 commit。

---

### Task 8: 强化 GH 唯一规则包边界和 GHA identity 一致性

**Files:**
- Modify: `tests/test_hbr_runtime_packaging_contract.py`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimePackagingTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleIdentityPropagationTests.cs`
- Modify only if a failing test exposes a gap: `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs`

- [ ] **Step 1: 写 runtime 只消费一个 pack 的合同测试**

Python 测试递归读取 `src/BIMBaoGui.Stage01/**/*.cs`，允许历史文件名只出现在明确的否定测试/diagnostic 列表，不允许生产代码包含以下读取入口：

```python
FORBIDDEN_RUNTIME_INPUTS = (
    "wuhan_planning_rules.v1.json",
    "GH_HIFC_ParameterBindings.json",
    "GH_HIFC_SharedParameters.txt",
    "official_plugin_compatibility_status.v1.json",
    "stage01_file_initialization_registry_v0.1.json",
)
```

C# 测试断言 production assembly：

```csharp
string[] hbrResources = productionAssembly.GetManifestResourceNames()
  .Where(name => name.EndsWith(".hbrpack", StringComparison.Ordinal))
  .ToArray();
Assert.Equal(new[] { HbrRuleDatabase.ResourceName }, hbrResources);
Assert.Equal("HBR-WUHAN-PLANNING", HbrRuleDatabase.Current.Package.PackageId);
Assert.Equal("1.0.0", HbrRuleDatabase.Current.Package.PackageVersion);
Assert.Matches("^[0-9a-f]{64}$", HbrRuleDatabase.Current.Package.RulePackageSha256);
```

- [ ] **Step 2: 校验 FileContext、TaskPlan 和 Stage03 identity 仍来自同一 package**

扩充现有 `HbrRuleIdentityPropagationTests`，从 `HbrRuleDatabase.Current.Package` 取三元组 `(PackageId, PackageVersion, RulePackageSha256)`，依次验证真实生产传播路径，禁止手工构造一个恰好含相同值的 report DTO：

1. 用 `HBRFileContextFactory.Create()` 构造 FileContext，再用 `TaskPlanCompiler.Compile()` 构造 TaskPlan，断言两者三元组均来自当前 package。
2. 用该 FileContext 构造真实 `Stage03RevitScanRequest`，断言其 `RulePackageId/Version/Sha256` 与 package 一致。
3. 通过可注入的 `Stage03WorkflowServices.WriteFieldReport` 捕获 `Stage03WorkflowCoordinator` 实际生成的 `Stage03FieldReportContext`，让 scan result 携带同一三元组，断言捕获到的 report context 与 package 完全一致。

本步只允许修改 identity 测试和必要的 `HbrRuleDatabase.cs`；不修改任何 Stage03 生产文件。如果现有 Stage03 生产路径无法通过此传播测试，立即停止并把缺口转入后续 GH/Stage03 计划，不得为通过基线而移植 9 个 WIP。

- [ ] **Step 3: 运行 GREEN 并提交**

```powershell
python -m pytest tests/test_hbr_runtime_packaging_contract.py -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore `
  --filter 'FullyQualifiedName~HbrRuntimePackagingTests|FullyQualifiedName~HbrRuleIdentityPropagationTests' `
  --nologo --logger 'console;verbosity=minimal'
git diff --check
git add tests/test_hbr_runtime_packaging_contract.py `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimePackagingTests.cs `
  tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleIdentityPropagationTests.cs `
  src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs
git commit -m 'test(rules): enforce the single embedded package boundary'
```

Expected: production GHA/DLL 只有一个 `.hbrpack` 资源；所有阶段报告同一 package identity；历史目录只作为证据保留。

---

### Task 9: 把重建、验证和 baseline ZIP 接入现有单一 CI artifact

**Files:**
- Modify: `.github/workflows/build-stage01-gha.yml`
- Modify: `tests/test_v090_release_contract.py`
- Modify: `tests/test_hifc_mapping_smoke_fixture.py`
- Modify: `tests/test_hbr_baseline_archive.py`

- [ ] **Step 1: 写 workflow RED 合同**

在 release contract 中要求：

```python
assert '"tools/hifc/**"' in workflow
assert "Regenerate and verify deterministic H-IFC fixture" in workflow
assert "Build HBR baseline archive" in workflow
assert "HBR-WUHAN-PLANNING-v1.0.0-baseline.zip" in workflow
assert workflow.count("actions/upload-artifact@") == 1
```

Run:

```powershell
python -m pytest tests/test_v090_release_contract.py -k 'workflow or artifact' -q
```

Expected: RED；当前 paths 不覆盖单独的 `tools/hifc/**`，也没有 baseline 重建/ZIP 步骤。

- [ ] **Step 2: 增加临时目录逐字节重建门禁**

在现有 compiler contract step 后加入：

```yaml
- name: Regenerate and verify deterministic H-IFC fixture
  shell: pwsh
  run: python -m pytest tests/test_hifc_mapping_smoke_fixture.py tests/test_hbr_baseline_archive.py -q
```

该 pytest 必须在 `tmp_path` 中生成并与 committed fixture、fixture manifest、rules manifest 比较，不得覆盖 CI checkout。push/pull_request `paths` 同时增加：

```yaml
- "tools/hifc/**"
- "tools/build_hbr_baseline_archive.py"
- "docs/hifc/acceptance/**"
```

- [ ] **Step 3: 在 Release build 后生成 baseline ZIP，并复用现有 upload**

新增一个 `Build HBR baseline archive` PowerShell step，使用 Task 7 的 validator/archive 命令，输出：

```text
artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip
```

扩充现有 `artifact-manifest.json`：

```powershell
$rules = Get-Content -Raw 'specs/hbr-rules/v1/manifest.sha256.json' | ConvertFrom-Json
$baselineZip = 'artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip'
$manifest = [ordered]@{
  artifactName = 'BIMBaoGui.Stage01.gha'
  assemblyVersion = '0.9.0.0'
  sha256 = '${{ steps.verify.outputs.sha256 }}'
  sizeBytes = [int64]'${{ steps.verify.outputs.size }}'
  commitSha = '${{ github.sha }}'
  rulePackageId = $rules.packageId
  rulePackageVersion = $rules.packageVersion
  rulePackagePayloadSha256 = $rules.rulePack.payloadSha256
  baselineArchiveName = [IO.Path]::GetFileName($baselineZip)
  baselineArchiveSha256 = (Get-FileHash $baselineZip -Algorithm SHA256).Hash.ToLowerInvariant()
  baselineArchiveBytes = (Get-Item $baselineZip).Length
}
```

现有唯一 `actions/upload-artifact@v4` 的 path 增加 ZIP；不创建第二个 upload action。

- [ ] **Step 4: 运行 workflow 合同、LF 和 YAML 相关门禁并提交**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD = '1'
python -m pytest `
  tests/test_v090_release_contract.py `
  tests/test_hifc_mapping_smoke_fixture.py `
  tests/test_hbr_baseline_archive.py -q
git diff --check
git add .github/workflows/build-stage01-gha.yml tests/test_v090_release_contract.py `
  tests/test_hifc_mapping_smoke_fixture.py tests/test_hbr_baseline_archive.py
git commit -m 'ci(rules): gate HBR v1 baseline regeneration'
```

Expected: CI contract 全绿，所有受控文本 LF-only，workflow 仍只有一个上传链。

---

### Task 10: 全量验收、远端 CI、发布 ZIP 和不可变 tag

**Files:**
- Verify generated: `src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack`
- Verify generated: `src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha`
- Verify generated: `artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip`
- Verify committed: `specs/hbr-rules/v1/manifest.sha256.json`
- Verify preserved: `D:/18_建模项目/湖北BIM云平台/wip-safeguards/2026-08-10-hbr-baseline/all-14-files.patch`

- [ ] **Step 1: 运行全部 Python、.NET、Release、fixture 和 EOL 门禁**

```powershell
$guardRoot = 'D:\18_建模项目\湖北BIM云平台\wip-safeguards\2026-08-10-hbr-baseline'
$approvedBase = (Get-Content -Raw (Join-Path $guardRoot 'approved-base.txt')).Trim()
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD = '1'
python -m pytest tests -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj `
  -c Release --no-restore --nologo --logger 'console;verbosity=normal'
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj `
  -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true
python tools/hifc/validate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --ifc tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json
git diff --check "${approvedBase}..HEAD"
```

Expected: Python 0 failed；.NET 0 failed；Release 0 warning/0 error；validator PASS 616/359/52/52/14/9；diff whitespace clean。

- [ ] **Step 2: 重新生成最终 ZIP 并核对 pack/GHA/manifest identity**

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
python tools/hifc/validate_hifc_mapping_smoke.py `
  --source specs/hbr-rules/v1/source/hbr_rule_source.v1.json `
  --baseline specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json `
  --ifc tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  --report artifacts/HBR_HIFC_全映射结构验证_v1.0.validation.json
python tools/build_hbr_baseline_archive.py `
  --root . `
  --rule-pack src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack `
  --rules-manifest specs/hbr-rules/v1/manifest.sha256.json `
  --fixture tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc `
  --fixture-manifest tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.manifest.json `
  --validation-report artifacts/HBR_HIFC_全映射结构验证_v1.0.validation.json `
  --ifcflux-evidence docs/hifc/acceptance/HBR_HIFC_全映射结构验证_v1.0.ifcflux.json `
  --output artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip

$rules = Get-Content -Raw specs/hbr-rules/v1/manifest.sha256.json | ConvertFrom-Json
$pack = [IO.File]::ReadAllBytes('src/BIMBaoGui.Stage01/obj/Release/net48/HBR_RulePack.hbrpack')
$payloadHash = -join ($pack[16..47] | ForEach-Object { $_.ToString('x2') })
if ($payloadHash -ne $rules.rulePack.payloadSha256) { throw 'pack payload SHA 与 rules manifest 不一致。' }
$zipHash = (Get-FileHash artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip -Algorithm SHA256).Hash.ToLowerInvariant()
$ghaHash = (Get-FileHash src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha -Algorithm SHA256).Hash.ToLowerInvariant()
"RULE_PAYLOAD_SHA256=$payloadHash"
"BASELINE_ZIP_SHA256=$zipHash"
"GHA_SHA256=$ghaHash"
```

Expected: 三个 hash 均为 64 位小写十六进制；pack payload 与 rules manifest 精确一致；GHA 的 embedded resource 测试已证明相同 payload。

- [ ] **Step 3: 确认只改基线范围，并验证原工作树 WIP 未变**

```powershell
$sourceRoot = 'D:\18_建模项目\湖北BIM云平台\BIM-baogui-hardening-v090'
$guardRoot = 'D:\18_建模项目\湖北BIM云平台\wip-safeguards\2026-08-10-hbr-baseline'
$approvedBase = (Get-Content -Raw (Join-Path $guardRoot 'approved-base.txt')).Trim()
$fullPatch = Join-Path $guardRoot 'all-14-files.patch'
$forbidden = @(git diff --name-only "${approvedBase}..HEAD" | Where-Object {
  $_ -match '(^src/BIMBaoGui\.Stage01/(Revit/Stage03ModelScanService|Stage03/Stage03)|^tests/BIMBaoGui\.Stage01\.Core\.Tests/Stage03)'
})
if ($forbidden.Count -ne 0) { throw "基线分支误含 Stage03 文件：$($forbidden -join ', ')" }

$postPatch = Join-Path $guardRoot 'all-14-files-after-baseline.patch'
git -C $sourceRoot diff --binary HEAD --output=$postPatch
$beforeHash = (Get-FileHash $fullPatch -Algorithm SHA256).Hash
$afterHash = (Get-FileHash $postPatch -Algorithm SHA256).Hash
if ($beforeHash -ne $afterHash) { throw '原工作树 14 文件 WIP 在基线实施期间发生变化。' }

git status --short --branch
```

Expected: forbidden 集合为空；原工作树 patch 前后 hash 相同；clean worktree 只允许 gitignored `obj/bin/artifacts` 产物，tracked 状态干净。

- [ ] **Step 4: 推送实施分支并等待对应 head SHA 的远端 CI**

```powershell
$headSha = git rev-parse HEAD
git push -u origin feat/hbr-planning-mapping-v1.0.0
$run = $null
for ($attempt = 0; $attempt -lt 12 -and $null -eq $run; $attempt++) {
  $run = gh run list `
    --workflow 'Build BIMBaoGui GHA' `
    --branch feat/hbr-planning-mapping-v1.0.0 `
    --limit 20 `
    --json databaseId,headSha,status,conclusion `
    | ConvertFrom-Json `
    | Where-Object { $_.headSha -eq $headSha } `
    | Select-Object -First 1
  if ($null -eq $run) { Start-Sleep -Seconds 5 }
}
if ($null -eq $run) { throw "没有找到 head SHA $headSha 对应的 CI run。" }
gh run watch $run.databaseId --exit-status
```

Expected: 远端分支指向本地 `HEAD`，对应 CI conclusion 为 `success`，上传 artifact 同时含 GHA、artifact manifest 和 baseline ZIP。

- [ ] **Step 5: 只在远端 CI 成功后创建并推送 annotated tag**

```powershell
$tag = 'hbr-planning-mapping-v1.0.0'
$headSha = git rev-parse HEAD
if (git tag --list $tag) { throw "本地 tag 已存在：$tag" }
git ls-remote --exit-code --tags origin "refs/tags/$tag" | Out-Null
if ($LASTEXITCODE -eq 0) { throw "远端 tag 已存在：$tag" }

$rules = Get-Content -Raw specs/hbr-rules/v1/manifest.sha256.json | ConvertFrom-Json
$fixture = Get-FileHash tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc -Algorithm SHA256
$zip = Get-FileHash artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip -Algorithm SHA256
$message = @(
  'Freeze HBR-WUHAN-PLANNING mapping baseline 1.0.0',
  "Rule payload SHA256: $($rules.rulePack.payloadSha256)",
  "Fixture SHA256: $($fixture.Hash.ToLowerInvariant())",
  "Baseline ZIP SHA256: $($zip.Hash.ToLowerInvariant())"
) -join "`n"
git tag -a $tag -m $message $headSha
git push origin "refs/tags/$tag"
git ls-remote --exit-code origin "refs/tags/$tag"
```

Expected: 远端 tag 存在并指向已通过 CI 的 `$headSha`；tag message、ZIP release manifest 和 GHA runtime 使用同一个 `rulePackagePayloadSha256`。

- [ ] **Step 6: 输出冻结交付清单并停止，不进入 Stage03 实施**

最终交付记录必须列出：

```text
branch = feat/hbr-planning-mapping-v1.0.0
tag = hbr-planning-mapping-v1.0.0
rule source = specs/hbr-rules/v1/source/hbr_rule_source.v1.json
rules manifest = specs/hbr-rules/v1/manifest.sha256.json
fixture = tests/fixtures/hifc/HBR_HIFC_全映射结构验证_v1.0.ifc
fixture SHA256 = 570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632
local archive = artifacts/HBR-WUHAN-PLANNING-v1.0.0-baseline.zip
runtime status = 57 NOT_IMPLEMENTED + 302 UNCLASSIFIED_REQUIREMENT
Stage03 production stability = not claimed
```

本任务到此停止。下一份计划从 tag 创建新的 GH/Stage03 分支，按“证据语义 → 扫描/导出原子快照 → Owner 策略 → requirement/cardinality → Strict/Force → Revit/IFCFlux 三件套”顺序实施。

---

## 自审门禁

实施前和最终提交前各执行一次：

```powershell
$plan = 'docs/superpowers/plans/2026-08-10-hbr-planning-mapping-baseline-v1.md'
$patterns = @(
  ('T' + 'BD'),
  ('T' + 'ODO'),
  ('implement' + ' later'),
  ('fill in' + ' details'),
  ('add appro' + 'priate'),
  ('write tests for' + ' the above'),
  ('similar to' + ' task')
)
$redFlags = Select-String -Path $plan -Pattern $patterns -CaseSensitive:$false
if ($redFlags) { throw "计划仍含占位表达：`n$redFlags" }
```

人工规格覆盖必须逐项确认：

1. 唯一源、schema、compatibility baseline 的职责没有互换。
2. raw X/Y 保持带空格；canonicalKey、ifc.property、Stage01 fieldKey、spatial mapping、fixture、validator 和 runtime index 全部无空格。
3. X=Northing、Y=Easting，单位/类型为 m + IfcReal/IfcLengthMeasure + Revit Length。
4. 359 propertyId/canonicalKey/effective IFC identity 唯一，official `166/166`。
5. 57 未实现 Owner 明确 `NOT_IMPLEMENTED`；302 条虽 Owner 可实现，因 requirement 未分类输出 `UNCLASSIFIED_REQUIREMENT`。
6. generator/pack/fixture/manifest/ZIP 同输入同字节；CI 在临时目录比较，不自改 checkout。
7. fixture 维持 616/359/52/52/14/9，带空格 mutation 必败。
8. GHA 只有一个 embedded `.hbrpack`，所有阶段传播同一 package identity。
9. IFCFlux B 证据诚实记录版本/截图未留存的限制，不升级为官方认证。
10. 9 个 Stage03 WIP 文件没有进入 baseline diff，原 14 文件 WIP patch 前后 hash 不变。
11. Python、.NET、Release、EOL、远端 CI 全绿后才打 tag。
12. tag 后停止；Stage03 生产稳定性仍不声明完成。
