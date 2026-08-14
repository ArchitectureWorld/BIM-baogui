# Revit Stage02 手动语义类型分配：详细实施计划

> **执行约束：**整个开发过程在独立子分支和独立 worktree 中进行；`feat/revit-native-addin-mcp-v0.3` 在所有测试、编译、安装包和实机证据完成前保持不变。禁止把临时 Workflow、构建产物、日志、ZIP、诊断脚本或半成品提交到目标支线。

- 日期：2026-08-13
- 上游基线分支：`feat/revit-native-addin-mcp-v0.3`
- 开发子分支：`feat/revit-stage02-manual-semantic-v0.4.2`
- 目标产品版本：`0.4.2`
- Stage01 Payload：保持 `0.9.1`
- 新增 Stage02 Assignment Schema：`1.0.0`
- 目标环境：Autodesk Revit 2020
- 设计规范：`docs/superpowers/specs/2026-08-13-stage02-manual-semantic-role-assignment-design.md`

---

# 一、支线洁净策略

## 1.1 开发隔离

执行前记录上游精确提交：

```powershell
git fetch origin
git switch feat/revit-native-addin-mcp-v0.3
git pull --ff-only
git status --short
git rev-parse HEAD
```

要求：

```text
git status --short 为空
不存在未追踪文件
不存在未推送本地提交
记录 BASE_SHA
```

建立独立 worktree：

```powershell
git worktree add `
  ..\BIM-baogui-revit-stage02-v042 `
  -b feat/revit-stage02-manual-semantic-v0.4.2 `
  BASE_SHA
```

后续所有修改只在该 worktree 中进行。

## 1.2 禁止进入仓库的内容

禁止提交：

```text
artifacts/
bin/
obj/
TestResults/
*.zip
*.log
*.tmp
*.bak
install-evidence.json
stage03-output-directories.json
本机 Revit RVT/IFC 测试文件
临时 GitHub Actions Workflow
一次性 Python/PowerShell 修补脚本
IDE 用户配置
```

不得使用：

```powershell
git add -A
git add .
```

每次只允许按明确路径暂存：

```powershell
git add src/... tests/... specs/... docs/...
```

## 1.3 Workflow 白名单

本功能不新增一次性 Workflow。开发和发布只复用仓库长期维护的正式 Workflow。

最终合并前检查：

```powershell
git ls-files .github/workflows
```

若出现为本次开发临时创建的 Workflow，必须在合并前删除，且不能进入上游支线历史。

## 1.4 提交历史整理

开发子分支允许本地 TDD 红绿循环，但推送和合并前必须整理成可审阅的原子提交。建议最终提交序列：

```text
docs: define Stage02 manual semantic assignment
feat(rules): add manual carrier contracts and green object role
feat(stage02): separate selection inventory from role matching
feat(stage02): persist semantic assignments in RVT
feat(stage02): add batch role assignment and per-element overrides
feat(mcp): expose controlled Stage02 semantic assignments
feat(stage03): consume assigned green object owners
chore: release Revit native product v0.4.2
test: refresh Revit v0.4.2 functional baseline
```

不得把“修 CI”“临时生成 baseline”“一次性 finalizer”之类提交带入最终历史。

---

# 二、总体实现顺序

```text
规则与领域模型
→ 选择范围解耦
→ 手动载体许可
→ 语义角色持久化
→ 预览与写入
→ UI
→ MCP
→ Stage03 Owner
→ 版本、安装包与全量验证
```

必须遵循测试驱动：

1. 先增加最小失败测试；
2. 确认测试因目标能力缺失而失败；
3. 只实现使该测试通过的最小代码；
4. 跑相邻回归；
5. 通过后才形成提交。

共享远端不得保留故意失败的提交。

---

# Task 0：保存已确认设计与实施计划

## 文件

新增：

```text
docs/superpowers/specs/
  2026-08-13-stage02-manual-semantic-role-assignment-design.md

docs/superpowers/plans/
  2026-08-13-stage02-manual-semantic-role-assignment-implementation.md
```

## 检查

```powershell
rg -n "TBD|TODO|待定|以后再说" docs/superpowers/specs docs/superpowers/plans
git diff --check
```

要求：

- 无未定义占位符；
- `SITE` 与对象级角色边界一致；
- 产品版本、Schema 版本和分支名称一致；
- 不包含本机绝对路径；
- 不包含凭证或私有下载地址。

## 提交

```text
docs: define Stage02 manual semantic assignment
```

---

# Task 1：建立手动语义分配领域模型

## 新增文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02SemanticAssignmentModels.cs
  NativeStage02RoleAssignmentPolicy.cs
```

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02Inventory.cs
  NativeStage02PreviewModels.cs
```

## 领域模型

新增：

```csharp
internal enum NativeStage02IdentificationMode
{
  Automatic,
  Manual
}

internal enum NativeStage02AssignmentMode
{
  Auto,
  Manual
}

internal sealed class NativeStage02RoleOverride
{
  string ElementUniqueId;
  string RoleId;
}

internal sealed class NativeStage02ResolvedAssignment
{
  string ElementUniqueId;
  string RoleId;
  NativeStage02AssignmentMode AssignmentMode;
  string Source;
}
```

扩展 `NativeStage02PreviewRequest`：

```csharp
IdentificationMode
BulkRoleId
RoleOverrides
```

要求：

- `Clone()` 深复制；
- UniqueId 去空、去重、稳定排序；
- override 重复且角色不同时明确报冲突；
- 全模型模式禁止携带当前选择专用的批量角色或 override；
- 自动模式不得携带手动角色；
- 手动模式必须有批量角色或逐项 override；
- 所有错误使用稳定错误码。

## 测试

新增：

```text
tests/BIMBaoGui.RevitAddin.Tests/
  NativeStage02RoleAssignmentPolicyTests.cs
```

覆盖：

1. 自动模式空角色有效；
2. 自动模式携带手动角色阻断；
3. 手动模式批量角色有效；
4. 批量角色被单项 override 覆盖；
5. 重复 override 相同角色去重；
6. 重复 override 不同角色阻断；
7. override 指向未选择元素阻断；
8. canonical 顺序不受输入顺序影响。

## 验证

```powershell
dotnet test `
  tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~NativeStage02RoleAssignmentPolicyTests"
```

---

# Task 2：将“选择范围资格”与“自动角色资格”彻底解耦

## 新增文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02SelectionInventoryPolicy.cs
```

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02Inventory.cs
  NativeStage02RevitService.cs
```

## 新逻辑

### 当前选择范围

只判断：

```text
属于当前活动文档
UniqueId 可回读
不是 ElementType
不是 ViewSpecific
不是 ImportInstance
不是 RevitLinkInstance
属于可持久化模型元素
```

不得在这一层使用 HBR 角色类别白名单。

### 全模型范围

全模型自动扫描继续使用 HBR 自动识别可用类别，避免把所有 Revit 构件塞入预览。

### 错误码

新增并替代含义模糊的 `CUSTOM_ELEMENT_UNAVAILABLE`：

```text
SELECTION_EMPTY
SELECTION_ELEMENT_MISSING
SELECTION_ELEMENT_NOT_ELIGIBLE
AUTO_ROLE_UNSUPPORTED
```

错误证据至少包含：

```text
ElementId
UniqueId
CategoryKey
CategoryName
CLR Type
ElementKind
IsViewSpecific
IsImported
IsLinked
```

## Revit 2020 建筑地坪诊断门

在修改规则前，必须用用户测试模型读取截图中构件的真实：

```text
BuiltInCategory
Category.Id
CLR Type.FullName
ElementKind
GetTypeId()
面积参数来源
IFC Export GUID
```

不得仅根据中文界面名称猜测 `OST_BuildingPad` 或类名。

该诊断结果写入人工验收记录，不提交 RVT 文件。

## 测试

新增：

```text
tests/BIMBaoGui.RevitAddin.Tests/
  NativeStage02SelectionInventoryPolicyTests.cs
```

覆盖：

- 建筑地坪类模型元素在当前选择范围中保留；
- 同一元素在自动角色阶段仍可返回 `AUTO_ROLE_UNSUPPORTED`；
- 链接、导入、视图专用和 ElementType 被准确分类；
- 缺失 UniqueId 与不合格元素使用不同错误码；
- 当前选择顺序不影响结果。

更新：

```text
NativeStage02InventoryPolicyTests.cs
tests/test_revit_addin_stage02_revit_contract.py
```

## 验证

```powershell
dotnet test `
  tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~NativeStage02SelectionInventoryPolicyTests|FullyQualifiedName~NativeStage02InventoryPolicyTests"

python -m pytest `
  tests/test_revit_addin_stage02_revit_contract.py `
  -q
```

## 提交

```text
feat(stage02): separate selection inventory from role matching
```

---

# Task 3：扩展规则包的手动载体合同

## 修改文件

```text
specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json
specs/hbr-rules/v1/manifest.sha256.json
tools/build_hbr_rulepack.py

src/BIMBaoGui.RevitAddin/Rules/
  NativeStage02RuleCatalog.cs
```

## 规则结构

保持现有字段作为自动识别合同：

```json
"revitCategories": [],
"allowedElementKinds": []
```

新增可选字段：

```json
"manualCarriers": [
  {
    "category": "经 Revit 2020 实机确认的 CategoryKey",
    "elementKinds": ["经实机确认的 ElementKind"]
  }
]
```

第一版不要求重写全部既有角色为新结构，避免无必要迁移。

运行时新增：

```csharp
NativeManualCarrierDefinition
NativeCarrierRoleDefinition.ManualCarriers
```

## 新增策略

新增：

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02ManualCarrierPolicy.cs
```

校验：

```text
角色存在
模型类型允许
Stage01 条件允许
CategoryKey + ElementKind 在 manualCarriers 中
角色具有属性模板
Owner 策略已声明
```

## 编译器调整

`tools/build_hbr_rulepack.py` 必须：

- 接受、规范化并校验 `manualCarriers`；
- 校验类别和 ElementKind 非空；
- 校验同一角色内载体组合不重复；
- 校验 `manualCarriers` 稳定排序；
- 更新 carrier role 数量与引用完整性合同；
- 保持旧角色兼容；
- 将新增字段写入二进制规则包；
- 更新 Manifest SHA。

禁止手工编辑生成哈希。

## 测试

更新：

```text
tests/test_hbr_rulepack_compiler.py
tests/test_hbr_rules_manifest.py
tests/BIMBaoGui.RevitAddin.Tests/NativeStage02RuleCatalogTests.cs
```

新增：

```text
tests/BIMBaoGui.RevitAddin.Tests/
  NativeStage02ManualCarrierPolicyTests.cs
```

覆盖：

- 合法 BuildingPad → Green role；
- 同类别但错误 ElementKind 阻断；
- 条件未启用阻断；
- 模型类型不匹配阻断；
- 未知角色阻断；
- 自动载体不会自动变成手动载体；
- JSON 输入顺序不影响编译结果。

## 验证

```powershell
python -m pytest `
  tests/test_hbr_rulepack_compiler.py `
  tests/test_hbr_rules_manifest.py `
  -q

dotnet test `
  tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~NativeStage02RuleCatalogTests|FullyQualifiedName~NativeStage02ManualCarrierPolicyTests"
```

## 提交

```text
feat(rules): add controlled Stage02 manual carrier contracts
```

---

# Task 4：建立首个对象级角色 `SITE_GREEN_OBJECT`

## 证据门

在规则包中落地前必须完成：

1. 官方示例 H-IFC 中绿地对象 Owner、实体、Pset 和属性的 exact 提取；
2. 用户 Revit 2020 中建筑地坪 RAW IFC 导出实体检查；
3. 根据 Export GUID 确认该构件在 RAW IFC 中的实际实体；
4. 确认 Stage03 可以稳定定位 Owner。

若 RAW IFC 不是预期实体，不得通过修改字符串“伪装通过”。必须选择以下受控方案之一：

```text
A. 调整 Revit IFC 导出映射，使载体导出为批准实体；
B. 改用能够稳定导出为批准实体的 Revit 载体；
C. 将该载体标记为 Stage02 可准备、Stage03 暂不发布。
```

## 新角色

在规则包中新增：

```text
roleId：SITE_GREEN_OBJECT
displayName：绿地
modelFileTypes：总平模型
selectionPolicy：MANUAL_SEMANTIC_ASSIGNMENT
ifcOwnerStrategy：BY_EXPORT_GUID
```

`manualCarriers` 第一版仅加入经过实机确认的构件组合。未验证的楼板、面域、通用模型不能提前全部开放。

## 对象级属性

不要破坏现有 `SITE` 汇总兼容字段。新增对象级固定 GUID 属性，使用新的 canonical identity 和新的 Revit 参数名称，避免同名不同 GUID 冲突：

```text
HBR｜绿地对象属性集｜分类名称
HBR｜绿地对象属性集｜投影面积
HBR｜绿地对象属性集｜绿地类型
HBR｜绿地对象属性集｜折算系数
```

IFC 输出仍对应：

```text
Pset_绿地信息属性集
```

建议：

| 属性 | 规则 |
|---|---|
| 分类名称 | 系统固定“绿地” |
| 投影面积 | 经批准的载体提取器；无法可靠取得则 PendingInput |
| 绿地类型 | 类型名精确命中批准枚举时作为建议值 |
| 折算系数 | 不伪造；人工填写或后续规则建议 |

要求级别：

```text
CONDITIONAL
conditionId = site.green
```

## 新增建议值策略

新增：

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02SemanticValueSuggestionPolicy.cs
```

只允许：

- 系统固定值；
- 精确枚举命中；
- 明确批准的 Revit 内置参数；
- 已批准别名。

禁止模糊猜测。

## 测试

新增：

```text
NativeStage02GreenObjectRuleTests.cs
NativeStage02SemanticValueSuggestionPolicyTests.cs
```

覆盖：

- 条件启用后出现 4 个字段；
- 条件关闭后全部 `NotApplicable` 或角色选择被阻断；
- 分类名称固定为“绿地”；
- “集中绿地”类型名精确建议；
- 未知类型名不写入；
- 面积单位转换正确；
- 面积提取失败时不伪造；
- 折算系数保持 PendingInput。

## 提交

```text
feat(rules): add the first green object semantic role
```

---

# Task 5：实现 RVT 内语义角色持久化

## 新增文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02SemanticAssignmentCanonicalizer.cs
  NativeStage02SemanticAssignmentStorage.cs
  NativeStage02SemanticAssignmentStoragePolicy.cs
  NativeStage02SemanticAssignmentRevitService.cs
```

## 存储方案

使用唯一 `DataStorage + Extensible Storage`。

字段：

```text
SchemaVersion
RulePackageId
RulePackageVersion
CanonicalJson
PayloadSha256
UpdatedUtc
```

Assignment：

```text
ElementUniqueId
RoleId
AssignmentMode
CarrierCategory
CarrierElementKind
```

不保存用户姓名，不保存本机路径。

## 状态

```text
NoRecord
Current
Corrupt
UnsupportedFuture
```

读取行为：

- 纯读取，不修改 RVT；
- 哈希不一致为 `Corrupt`；
- 未知未来 Schema 为 `UnsupportedFuture`；
- stale UniqueId 单独报告，不把记录转移到其他构件；
- canonical JSON 按 UniqueId 排序。

## 写入语义

每个构件的角色记录与该构件的 Stage02 值写入处于同一构件事务中。

如果：

- 参数绑定失败；
- 构件写入失败；
- Assignment 回读失败；

则该构件事务回滚。

其他构件仍可保持现有 Stage02 部分成功语义。

即使该构件没有可靠值可写，只要参数绑定已准备且角色有效，也必须允许保存角色分配。

## 清除语义

用户显式切回自动识别时，预览显示：

```text
AssignmentAction = RemoveManualAssignment
```

确认写入后删除该构件的手动记录。

## 测试

新增：

```text
NativeStage02SemanticAssignmentCanonicalizerTests.cs
NativeStage02SemanticAssignmentStoragePolicyTests.cs
```

覆盖：

- 确定性 JSON；
- 哈希检测；
- 空记录；
- 重复 UniqueId；
- 未知 Schema；
- stale record；
- 新增、更新、删除 assignment；
- 一个构件失败不污染其他 assignment；
- 读取不会修改模型。

更新 Python 合同验证固定 Schema GUID 和字段名称。

## 验证

```powershell
dotnet test `
  tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~NativeStage02SemanticAssignment"
```

## 提交

```text
feat(stage02): persist semantic assignments in RVT
```

---

# Task 6：重构角色匹配与预览编译

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02RoleMatcher.cs
  NativeStage02RevitService.cs
  NativeStage02PreviewCompiler.cs
  NativeStage02PreviewModels.cs
```

## 匹配接口

拆分：

```csharp
MatchAutomatic(...)
MatchManual(...)
```

不得继续采用：

```text
先按自动类别过滤 compatible
→ 再检查 AssignedRoleId
```

正确流程：

```text
Automatic：
  自动类别 + ElementKind + 精确别名

Manual：
  指定 RoleId
  → ManualCarrierPolicy
  → 条件校验
  → 属性模板
```

## 持久化角色优先级

当前选择预览：

```text
本次逐项 override
> 本次批量角色
> RVT 中已保存手动角色
> 自动识别
```

但只有用户明确选择 `Manual` 模式时，本次批量角色和 override 才生效。

全模型预览：

```text
RVT 中已保存手动角色
> 自动识别
```

持久化手动角色不得被自动识别静默覆盖。

## Preview Schema

升级：

```text
HBR_NATIVE_STAGE02_PREVIEW_V1
→ HBR_NATIVE_STAGE02_PREVIEW_V2
```

canonical JSON 增加：

```text
identificationMode
bulkRoleId
roleOverrides
assignmentMode
assignedRoleId
assignmentSource
assignmentAction
manualCarrierEvidence
```

preview hash 必须包含全部语义选择。修改下拉框或逐项 override 后，旧 hash 自动失效。

## 属性计划

`NativeStage02ElementPlan` 增加：

```text
AutomaticRoleStatus
AutomaticRoleId
EffectiveRoleId
AssignmentMode
AssignmentSource
AssignmentAction
ManualCarrierStatus
```

## 测试

更新：

```text
NativeStage02RoleMatcherTests.cs
NativeStage02PreviewCompilerTests.cs
NativeStage02ProjectInformationTests.cs
```

新增：

```text
NativeStage02ManualPreviewCompilerTests.cs
```

重点覆盖：

- 建筑地坪自动识别失败，但手动绿地成功；
- 已保存手动角色在全模型预览中生效；
- 批量角色被逐项 override；
- 角色更改改变 preview hash；
- 元素名称变化不覆盖已保存手动角色；
- ProjectInformation 的 PROJECT/SITE/BUILDING 单实体逻辑不回归；
- 自动模式行为与 0.4.1 一致。

## 提交

```text
feat(stage02): compile batch and per-element semantic assignments
```

---

# Task 7：将角色分配纳入 Stage02 写入和回读

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02RevitWriteService.cs
  NativeStage02RevitService.cs
```

## 写入前

继续执行：

```text
重建预览
→ 比较 preview_hash
→ 不一致则 RequiresNewPreview
```

重建请求必须完整携带：

```text
IdentificationMode
BulkRoleId
RoleOverrides
```

## 参数准备

按 `EffectiveRoleId` 收集字段和类别，不能再只依赖自动角色。

## 构件事务

现有逻辑在 `writes.Length == 0` 时直接跳过。必须改为：

```text
有 ValueAction.Set
OR 有 AssignmentAction
→ 开启构件事务
```

事务内：

1. 回读构件；
2. 写入所有可靠值；
3. 更新或删除 assignment；
4. `Regenerate()`；
5. 回读参数；
6. 回读 assignment；
7. 一致后提交。

## 结果统计

新增：

```text
AssignedElementCount
RemovedAssignmentCount
FailedAssignmentCount
```

`PartialSuccess` 同时考虑参数和 assignment。

## 测试

新增或更新：

```text
NativeStage02WritePlanningTests.cs
tests/test_revit_addin_stage02_revit_contract.py
```

覆盖：

- 只有 assignment、没有值时仍写入；
- 参数绑定失败时不保存 assignment；
- assignment 保存失败时构件事务回滚；
- 一个构件失败，其他构件可成功；
- 写入前角色变化导致旧预览失效；
- 删除手动 assignment；
- 写入后预览读取 `Current` assignment。

## 提交

```text
feat(stage02): write and verify semantic assignments atomically
```

---

# Task 8：实现人工工作台 UI

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage02/
  NativeStage02View.cs
```

如文件继续膨胀，允许新增聚焦组件：

```text
NativeStage02AssignmentControls.cs
NativeStage02ElementAssignmentItem.cs
```

禁止无关 UI 重构。

## 顶部区

仅在“当前 Revit 选择”下启用：

```text
识别方式：
○ 自动识别
● 手动指定

批量语义类型：
[ 绿地 ▼ ]
```

角色列表来源于规则目录，并按以下条件过滤：

```text
当前模型类型
Stage01 项目条件
至少一个当前选择构件存在合法 manualCarrier
属性模板可用
```

不能硬编码中文列表。

## 逐项 override

元素列表每行显示：

```text
ElementId
类别
类型
自动角色
最终角色
分配模式
状态
```

右侧详情允许：

```text
继承批量选择
选择其他合法角色
恢复自动识别
```

每次变更都必须：

```text
清空当前 preview
禁用“确认写入”
要求重新生成预览
```

## 状态信息

错误必须显示构件证据，不再只显示 GUID：

```text
构件：集中绿地
ElementId：7161...
Revit 类别：建筑地坪
ElementKind：BuildingPad
自动识别：不支持
手动绿地：允许/不允许
原因：...
```

## UI 合同测试

更新：

```text
tests/test_revit_addin_stage02_ui_contract.py
```

若当前仓库没有该文件，则新增；同时更新正式 Revit Workflow 的测试列表。

断言：

- “自动识别”；
- “手动指定”；
- “批量语义类型”；
- per-element override 控件；
- 模式切换使 preview 失效；
- 不再把类别不支持显示为 `CUSTOM_ELEMENT_UNAVAILABLE`；
- 右侧连续滚动，不新增分页；
- “确认写入”只有有效预览时启用。

## 提交

```text
feat(stage02): add batch semantic assignment and row overrides
```

---

# Task 9：同步 MCP 受控入口

## 修改文件

```text
src/BIMBaoGui.RevitAddin/McpBridge/
  McpStage02Adapter.cs
  McpBridgeCommandRouter.cs
  McpRevitCommandGateway.cs

src/BIMBaoGui.McpServer/
  BimBaoGuiTools.cs

src/BIMBaoGui.McpContracts/
  ToolContracts.cs
```

## 工具数量

不新增任意执行工具，仍维持现有批准工具数量。只扩展：

```text
bimbaogui_stage02_preview
```

## 输入

建议 MCP 外部输入：

```json
{
  "scope": "current_selection",
  "identification_mode": "manual",
  "bulk_role_id": "SITE_GREEN_OBJECT",
  "role_overrides": [
    {
      "element_unique_id": "...",
      "role_id": "SITE_FIRE_FIELD"
    }
  ]
}
```

若 SDK 对复杂数组工具参数支持不稳定，则使用单个严格 Schema JSON 参数：

```text
semantic_assignment_json
```

但内部必须立即反序列化为强类型 DTO，禁止字符串拼接。

## 输出

预览返回：

```text
available_roles
automatic_role
effective_role
assignment_mode
assignment_source
manual_carrier_status
assignment_action
```

## 安全边界

MCP 不得：

- 指定规则包不存在的 RoleId；
- 绕过 Stage01 条件；
- 绕过 manualCarrier；
- 绕过 preview hash；
- 直接写任意共享参数；
- 传入任意 IFC Entity 或 Pset 名称。

## 测试

更新：

```text
tests/test_revit_addin_mcp_contract.py
tests/test_revit_addin_mcp_stage02_contract.py
tests/BIMBaoGui.RevitAddin.Tests/McpLeaseStoreTests.cs
```

若 `test_revit_addin_mcp_stage02_contract.py` 不存在则新增。

验证：

```powershell
python -m pytest `
  tests/test_revit_addin_mcp_contract.py `
  tests/test_revit_addin_mcp_stage02_contract.py `
  -q
```

## 提交

```text
feat(mcp): expose controlled Stage02 semantic assignments
```

---

# Task 10：让 Stage03 消费保存的对象级角色

## 修改文件

```text
src/BIMBaoGui.RevitAddin/Stage03/
  NativeStage03Scanner.cs
  NativeStage03Models.cs
  NativeStage03ReportWriter.cs

src/BIMBaoGui.HifcCore/
  HifcCoreService.cs
  LegacyCore/HbrIfcEnricher.cs
```

是否修改 HifcCore 取决于 RAW IFC 实体证据；没有证据不得预先改写。

## 扫描

Stage03 通过 Stage02 全模型预览自动读取保存的 manual assignments。

要求：

- `SITE_GREEN_OBJECT` 的字段只出现在被分配的实例；
- 同类别未分配构件不进入绿地导出；
- Owner 使用选中实例的 `ExportUtils.GetExportId`；
- 报告记录 Revit UniqueId、ElementId、RoleId、Export GUID 和 RAW IFC 实体。

## RAW IFC 实体门

新增诊断状态：

```text
OWNER_ENTITY_MATCH
OWNER_ENTITY_MISMATCH
OWNER_GUID_NOT_FOUND
```

如果 Export GUID 在 RAW IFC 中存在但实体类型与规则不一致：

- 不猜测；
- 不挂到第一个同类实体；
- 不退化为唯一 IfcSite；
- Stage03 阻断并输出证据。

## H-IFC fixture

新增或扩展固定 fixture：

```text
tests/fixtures/hifc/
  green-object-owner-*.ifc
  green-object-owner-*.manifest.json
```

Fixture 必须脱敏、最小化且可提交，不提交用户完整模型。

exact 验证：

```text
Owner GlobalId
Owner Entity
Pset_绿地信息属性集
分类名称
投影面积
绿地类型
折算系数
Ifc 类型
值
```

## 测试

新增：

```text
tests/BIMBaoGui.RevitAddin.Tests/
  NativeStage03SemanticAssignmentTests.cs

tests/BIMBaoGui.HifcCore.Tests/
  GreenObjectEnrichmentTests.cs
```

更新：

```text
tests/test_revit_addin_stage03_revit_contract.py
tests/test_official_hifc_write_contract.py
tests/test_hifc_mapping_smoke_fixture.py
```

## 人工门

自动化通过后仍必须：

1. 用户 Revit 2020 实际导出；
2. 检查最终 IFC；
3. 在 IFCFlux 打开；
4. 记录识别对象、Pset、属性和值；
5. 状态保持 `IFCFLUX_MANUAL_PENDING`，直至真实检查完成。

## 提交

```text
feat(stage03): consume assigned green object owners
```

---

# Task 11：产品版本升级至 0.4.2

## 修改文件

四个项目：

```text
src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj
src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj
src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj
src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj
```

统一：

```xml
<Version>0.4.2</Version>
<FileVersion>0.4.2.0</FileVersion>
<AssemblyVersion>0.4.2.0</AssemblyVersion>
```

更新：

```text
installer/Install-Revit2020.ps1
installer/McpProbe.cmd
installer/mcp-server-config.example.json
docs/revit-addin/README.md
.github/workflows/build-revit-mcp.yml
specs/revit-addin/v0.4.2-functional-baseline.json
```

新 MCP Server 目录：

```text
%LOCALAPPDATA%\BIMBaoGui\McpServer\0.4.2
```

安装器保留：

- Revit 运行检测；
- staging；
- SHA-256；
- install evidence；
- 清理旧语义版本；
- uninstall smoke。

## Functional baseline

仅在生产源码和测试最终冻结后生成一次：

```text
specs/revit-addin/v0.4.2-functional-baseline.json
```

禁止为更新 baseline 临时提交 Workflow。

使用仓库正式脚本或本地确定性命令生成，随后运行非回归测试。

## 提交

```text
chore: release Revit native product v0.4.2
test: freeze Revit v0.4.2 functional baseline
```

---

# Task 12：完整验证与洁净合并

## 12.1 静态和规则合同

```powershell
git diff --check

python -m pytest `
  tests/test_hbr_rulepack_compiler.py `
  tests/test_hbr_rules_manifest.py `
  tests/test_revit_addin_stage02_revit_contract.py `
  tests/test_revit_addin_stage02_ui_contract.py `
  tests/test_revit_addin_mcp_contract.py `
  tests/test_revit_addin_mcp_stage02_contract.py `
  tests/test_revit_addin_stage03_revit_contract.py `
  tests/test_revit_addin_v042_contract.py `
  -q
```

## 12.2 .NET 测试

```powershell
dotnet test `
  tests/BIMBaoGui.HifcCore.Tests/BIMBaoGui.HifcCore.Tests.csproj `
  -c Release

dotnet test `
  tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj `
  -c Release

dotnet test `
  tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj `
  -c Release
```

## 12.3 Release 编译

```powershell
dotnet build `
  src/BIMBaoGui.HifcCore/BIMBaoGui.HifcCore.csproj `
  -c Release `
  -p:TreatWarningsAsErrors=true

dotnet build `
  src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj `
  -c Release `
  -p:TreatWarningsAsErrors=true `
  -p:HbrBuildNumber=local `
  -p:HbrCommitSha=$(git rev-parse HEAD)

dotnet build `
  src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj `
  -c Release `
  -p:TreatWarningsAsErrors=true
```

## 12.4 GitHub Actions

推送开发子分支后，完整运行：

```text
Build BIMBaoGui Revit MCP
Build BIMBaoGui GHA
```

两条都必须 `success`。Revit 修改不得破坏 GHA 共用规则包。

## 12.5 安装包 Smoke

验证：

```text
安装 0.4.2
清理 0.4.1 MCP 目录
.addin 绝对路径
四程序集版本统一
MCP Server 单文件发布
安装证据哈希
MCP Probe
卸载无残留
```

## 12.6 Revit 2020 实机验收

使用用户截图中的模型：

1. 选择“建筑地坪｜集中绿地”；
2. 当前选择模式；
3. 手动指定“绿地”；
4. 生成预览；
5. 不再出现 `CUSTOM_ELEMENT_UNAVAILABLE`；
6. 显示 4 个绿地对象字段；
7. 确认写入；
8. 重开 Revit 后角色仍存在；
9. 同类未分配构件不被误识别；
10. 多选统一指定；
11. 逐项改成消防场地；
12. Stage01 关闭绿地条件时明确阻断；
13. Stage03 导出并记录 Owner 实体；
14. IFCFlux 人工检查。

## 12.7 最终支线清洁检查

```powershell
git status --short
git diff --check
git clean -nd
git clean -ndX
git ls-files .github/workflows
git ls-files | Select-String `
  -Pattern '(^|/)(artifacts|bin|obj|TestResults|logs|tmp)/|\.zip$|\.log$|\.tmp$|\.bak$'
```

要求：

```text
工作区为空
无未追踪文件
无构建产物
无临时 Workflow
无安装包 ZIP
无本机路径设置
无用户 RVT/IFC
无一次性补丁脚本
```

## 12.8 历史整理与合并

```powershell
git fetch origin
git rebase -i BASE_SHA
```

将试验提交压缩为前述原子提交序列。

再次执行全量测试后：

```powershell
git switch feat/revit-native-addin-mcp-v0.3
git pull --ff-only
```

如果上游 HEAD 已变化：

```text
停止合并
将开发子分支 rebase 到新的上游 HEAD
重新运行完整验证
```

禁止强推上游 Revit 支线。

最终采用经过审阅的合并或逐提交 cherry-pick，将干净提交引入：

```text
feat/revit-native-addin-mcp-v0.3
```

合并后最后一次检查：

```powershell
git status --short
git log --oneline --decorate -15
```

---

# 三、完成定义

只有同时满足以下条件，才能称为本功能完成：

| 验收项 | 必须状态 |
|---|---|
| 手动语义类型领域模型 | 通过 |
| 当前选择范围解耦 | 通过 |
| BuildingPad 真实类别证据 | 通过 |
| manualCarrier 合同 | 通过 |
| `SITE_GREEN_OBJECT` | 通过 |
| RVT 内角色持久化 | 通过 |
| 批量指定 | 通过 |
| 逐项改写 | 通过 |
| Preview Hash 防过期 | 通过 |
| 参数写入与 assignment 原子回读 | 通过 |
| MCP 同步 | 通过 |
| Stage03 Owner 定位 | 通过 |
| H-IFC exact 回读 | 通过 |
| Revit 2020 实机 | 通过 |
| IFCFlux 人工检查 | 有真实证据 |
| Revit MCP CI | 绿色 |
| GHA CI | 绿色 |
| 安装/卸载 Smoke | 通过 |
| 上游支线无临时文件和临时 Workflow | 通过 |

---

# 四、明确不做

本轮不包含：

- 任意 Pset 自由选择；
- 任意 IFC Entity 输入；
- 任意 Revit 类别绕过；
- 链接模型写入；
- 导入 CAD 写入；
- 模糊语义匹配；
- 自动更新功能；
- 未经 RAW IFC 证据的实体强制转换；
- 为通过 IFCFlux 而静默把多块绿地合并到唯一 `IfcSite`；
- 与本功能无关的 Stage01/Stage03 UI 重构。
