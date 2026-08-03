# HBR Unified Rule Database and Three-Stage Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有多源、官方插件黑箱驱动的四组件原型升级为单规则包驱动的 Stage01 初始化、Stage02 Revit 可见属性准备、Stage03 检测/标准 IFC4 导出/H-IFC 转译三阶段单 GHA。

**Architecture:** Git 中一个 JSON 是唯一可编辑业务规则源，构建期编译成带 SHA-256 的只读 `.hbrpack` 并嵌入 GHA。Stage02 使用文档指纹、Element.UniqueId、旧值快照和规则哈希生成可消费一次的写入预览；Stage03 在 Revit API 线程扫描并导出 RAW IFC4，随后在纯后台阶段创建/校正 IFC Pset 和属性，最后写字段级报告。

**Tech Stack:** C# / .NET Framework 4.8、Revit 2020 API、Grasshopper 8 API、System.Web.Script.Serialization、Python 3 标准库、xUnit、pytest、IFC4 STEP。

**Authoritative design:** `docs/superpowers/specs/2026-08-02-hbr-three-stage-rule-database-design.md`

---

## 文件结构

新增或重构后的职责边界：

```text
specs/hbr-rules/v1/
  schemas/hbr_rule_source.schema.json       # 结构契约
  source/hbr_rule_source.v1.json             # 唯一可编辑业务规则源
tools/build_hbr_rulepack.py                  # 语义校验和确定性 pack 编译
src/BIMBaoGui.Stage01/Rules/                 # 单一运行时规则入口
src/BIMBaoGui.Stage01/Stage02/               # 纯预览/确认领域逻辑
src/BIMBaoGui.Stage01/Revit/Stage02*.cs       # Revit 选择、预览和写入适配
src/BIMBaoGui.Stage01/Stage03/               # 纯检测、门禁、路径和报告模型
src/BIMBaoGui.Stage01/Revit/Stage03*.cs       # Revit 扫描和 IFC 导出
src/BIMBaoGui.Stage01/Mvd/                    # IFC4 STEP 转译和回读
src/BIMBaoGui.Stage01/Diagnostics/            # 原子 JSON 与失败报告
```

旧 `specs/hifc-mapping/v1`、Stage01 registry、官方 mapping catalog 和硬编码 TaskRuleCatalog 只在迁移期间用于交叉核对；完成 Task 3 后运行时不得再加载它们。

### Task 1: 建立 356+3 单一规则源与结构/语义合同

**Files:**
- Create: `specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json`
- Create: `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`
- Create: `tests/test_hbr_rule_source_contract.py`
- Create: `tests/test_hbr_rule_source_semantics.py`
- Reference: `src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json`
- Reference: `specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json`

- [ ] **Step 1: 写集合关系失败测试**

```python
def test_rule_source_preserves_verified_set_relationships(source):
    mvd = [p for p in source["properties"] if p["contractKind"] == "MVD"]
    extension = [p for p in source["properties"] if p["contractKind"] == "HIFC_EXTENSION"]
    official = [p for p in source["properties"] if p["officialPlugin"]["inExtracted166"]]
    stage01 = source["stage01"]["fieldRefs"]
    assert len(mvd) == 356
    assert len(extension) == 3
    assert len(official) == 166
    assert sum(p["contractKind"] == "MVD" for p in official) == 163
    assert len(stage01) == 102
    assert sum(ref["propertyId"] in {p["propertyId"] for p in official} for ref in stage01) == 89
```

- [ ] **Step 2: 运行并确认 RED**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rule_source_contract.py -q`  
Expected: FAIL，原因是 `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` 尚不存在。

- [ ] **Step 3: 写 schema 与唯一源**

源文件每条 property 必须落成以下实际形状；356 条从已核实工作簿 `Sheet1!A2:I357` 导入，空白样式号 `14` 规范化为 `null`，同时保留 `source.raw*` 证据。163 条匹配旧 166 时复用已发布 GUID，3 条扩展带 `extensionReason`，其余 MVD 字段按固定 namespace 生成后写死 GUID。

```json
{
  "propertyId": "3b51a805-d2d0-5c8c-b4ef-37447db54f55",
  "canonicalKey": "WH-DB42T1001-2025|HIFC规划报建|IfcBuilding|建筑技术信息属性集|耐火等级",
  "contractKind": "MVD",
  "source": {"artifact": "《MVD》规划报建.xlsx", "sheet": "Sheet1", "row": 76},
  "ifc": {
    "entity": "IfcBuilding",
    "propertySet": "Pset_建筑技术信息属性集",
    "property": "耐火等级",
    "declaredType": "IfcLabel",
    "sourceUnit": null,
    "canonicalUnit": null,
    "allowedRuntimeTypes": ["IfcLabel"]
  },
  "revit": {
    "parameterGuid": "3b51a805-d2d0-5c8c-b4ef-37447db54f55",
    "parameterName": "HBR｜建筑技术信息属性集｜耐火等级",
    "legacyNames": ["HIFC.建筑技术信息属性集.耐火等级"],
    "bindingScope": "INSTANCE",
    "storageType": "String",
    "parameterType": "Text",
    "visible": true,
    "userModifiable": true
  },
  "officialPlugin": {"inExtracted166": true, "evidenceStatus": "OFFICIAL_EXTRACTED"},
  "carrierRoleIds": ["BUILDING"],
  "stageOwnership": ["STAGE02", "STAGE03"],
  "requirement": {"level": "UNCLASSIFIED", "conditionId": null},
  "suggestion": {"kind": "EXISTING_OR_ALIAS", "aliases": ["耐火等级"]},
  "ifcWrite": {"ownerStrategy": "SINGLE_ENTITY_BY_TYPE", "writeStrategy": "CREATE_OR_UPDATE_PSET"}
}
```

顶层同时写入 `carrierRoles`、三种 `modelProfiles`、Stage01 102 个 `fieldRefs`、任务与条件。没有权威必填证据的记录保留 `UNCLASSIFIED`；不得伪造必填性。

- [ ] **Step 4: 写语义失败测试并校验真实缺陷**

```python
def test_no_style_ids_are_promoted_to_units_or_value_kinds(source):
    for rule in source["properties"]:
        assert rule["ifc"]["sourceUnit"] != "14"
        assert rule["ifc"]["declaredType"] != "14"

def test_all_ids_guids_and_references_are_unique(source):
    ids = [p["propertyId"] for p in source["properties"]]
    guids = [p["revit"]["parameterGuid"] for p in source["properties"]]
    assert len(ids) == len(set(ids))
    assert len(guids) == len(set(guids))
    assert {r["propertyId"] for r in source["stage01"]["fieldRefs"]} <= set(ids)
```

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py -q`  
Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add specs/hbr-rules/v1 tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py
git commit -m "feat: establish unified HBR rule source"
```

### Task 2: 确定性 `.hbrpack` 编译器与构建集成

**Files:**
- Create: `tools/build_hbr_rulepack.py`
- Create: `tests/test_hbr_rulepack_compiler.py`
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`

- [ ] **Step 1: 写 RED 测试锁定包格式和确定性**

```python
def test_compile_is_byte_for_byte_deterministic(tmp_path, source_path):
    a = tmp_path / "a.hbrpack"
    b = tmp_path / "b.hbrpack"
    compile_rulepack(source_path, a)
    compile_rulepack(source_path, b)
    assert a.read_bytes() == b.read_bytes()
    raw = a.read_bytes()
    assert raw[:4] == b"HBRP"
    version, length = struct.unpack(">IQ", raw[4:16])
    assert version == 1
    assert length == len(raw) - 48
    assert hashlib.sha256(raw[48:]).digest() == raw[16:48]
```

- [ ] **Step 2: 运行并确认 RED**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rulepack_compiler.py -q`  
Expected: FAIL，`tools.build_hbr_rulepack` 不存在。

- [ ] **Step 3: 实现最小编译器**

```python
MAGIC = b"HBRP"
FORMAT_VERSION = 1

def canonical_bytes(source: dict) -> bytes:
    return json.dumps(
        source, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")

def compile_rulepack(source_path: Path, output_path: Path) -> None:
    source = json.loads(source_path.read_text(encoding="utf-8"))
    validate_semantics(source)
    payload = canonical_bytes(source)
    header = MAGIC + struct.pack(">IQ", FORMAT_VERSION, len(payload)) + hashlib.sha256(payload).digest()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(header + payload)
```

`validate_semantics` 必须检查 356+3、166、102、所有唯一键、引用、固定 GUID、允许值枚举以及 `visible/userModifiable=true`；错误时返回非零退出码。

- [ ] **Step 4: 将 pack 生成到 obj 并只嵌入一个规则资源**

```xml
<PropertyGroup>
  <HbrRuleSource>$(MSBuildProjectDirectory)\..\..\specs\hbr-rules\v1\source\hbr_rule_source.v1.json</HbrRuleSource>
  <HbrRulePack>$(IntermediateOutputPath)HBR_RulePack.hbrpack</HbrRulePack>
</PropertyGroup>
<Target Name="CompileHbrRulePack" BeforeTargets="PrepareResources">
  <Exec Command="python &quot;$(MSBuildProjectDirectory)\..\..\tools\build_hbr_rulepack.py&quot; --source &quot;$(HbrRuleSource)&quot; --output &quot;$(HbrRulePack)&quot;" />
</Target>
<ItemGroup>
  <EmbeddedResource Include="$(HbrRulePack)" LogicalName="BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack" />
</ItemGroup>
```

此时先保留旧资源，直到 Task 3 完成运行时切换；新增 Python 测试只断言 `HBR_RulePack.hbrpack` 唯一，不把旧资源误算为新 pack。

- [ ] **Step 5: 运行 GREEN**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rulepack_compiler.py -q`  
Expected: PASS。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors，GHA 内含 `BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack`。

- [ ] **Step 6: 提交**

```powershell
git add tools/build_hbr_rulepack.py tests/test_hbr_rulepack_compiler.py src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj
git commit -m "build: compile deterministic HBR rule pack"
```

### Task 3: 单一运行时规则数据库和跨阶段哈希传播

> 细化执行计划：[HBR Task 3 Runtime Database Migration Implementation Plan](2026-08-02-hbr-task3-runtime-database-migration.md)

**Files:**
- Create: `src/BIMBaoGui.Stage01/Rules/HbrRulePackage.cs`
- Create: `src/BIMBaoGui.Stage01/Rules/HbrRulePackageLoader.cs`
- Create: `src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContext.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs`
- Modify: `src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs`
- Modify: `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs`
- Modify: `src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlanCanonicalizer.cs`
- Modify: `src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs`
- Modify: `src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj`

- [ ] **Step 1: 写损坏包和只读索引 RED 测试**

```csharp
[Fact]
public void Load_rejects_payload_hash_mismatch()
{
  byte[] bytes = Fixture.ValidPack();
  bytes[bytes.Length - 1] ^= 0x01;
  Assert.Throws<InvalidDataException>(() => HbrRulePackageLoader.Load(bytes));
}

[Fact]
public void Database_exposes_verified_counts_and_hash()
{
  HbrRuleDatabase db = HbrRuleDatabase.Create(HbrRulePackageLoader.Load(Fixture.ValidPack()));
  Assert.Equal(356, db.Properties.Count(x => x.ContractKind == "MVD"));
  Assert.Equal(3, db.Properties.Count(x => x.ContractKind == "HIFC_EXTENSION"));
  Assert.Equal(64, db.RulePackageSha256.Length);
}
```

- [ ] **Step 2: 运行并确认 RED**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter HbrRulePackageLoaderTests`  
Expected: FAIL，类型尚不存在。

- [ ] **Step 3: 实现 loader 和索引**

```csharp
internal static HbrRulePackage Load(byte[] bytes)
{
  if (bytes == null || bytes.Length < 48 || Encoding.ASCII.GetString(bytes, 0, 4) != "HBRP")
    throw new InvalidDataException("HBR 规则包头无效。");
  int version = ReadInt32BigEndian(bytes, 4);
  long length = ReadInt64BigEndian(bytes, 8);
  byte[] expected = bytes.Skip(16).Take(32).ToArray();
  byte[] payload = bytes.Skip(48).ToArray();
  if (version != 1 || length != payload.LongLength || !Hash(payload).SequenceEqual(expected))
    throw new InvalidDataException("HBR 规则包长度或 SHA-256 无效。");
  return Deserialize(payload, ToHex(expected));
}
```

`HbrRuleDatabase` 构造 `propertyId / IFC identity / parameterGuid / roleId / modelFileType / taskId` 的 `ReadOnlyDictionary`；发现重复立即失败。

- [ ] **Step 4: 将规则身份加入 Context 和 TaskPlan 哈希**

```csharp
public string RulePackageId { get; }
public string RulePackageVersion { get; }
public string RulePackageSha256 { get; }
```

`HBRFileContextFactory.Create` 从 `HbrRuleDatabase.Current` 写入三字段；canonicalizer 和 TaskPlan canonicalizer 都把它们写入确定性 JSON。`TaskPlanCompiler.ValidateContext` 对当前 pack hash 不一致返回阻断。

- [ ] **Step 5: 切换旧 catalog 数据入口并删除旧 EmbeddedResource**

`Stage01RegistryProvider`、`OfficialHifcMappingCatalog`、`MvdIfcNormalizationCatalog`、`TaskRuleCatalog` 和 `RuleActivationCatalog` 全部从 `HbrRuleDatabase.Current` 投影兼容模型。完成后从生产 csproj 和测试 csproj 删除 registry/bindings/rules/status 四个旧 EmbeddedResource，仅保留 `.hbrpack`。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release`  
Expected: 所有测试通过。  
Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests -q`  
Expected: 所有契约测试通过，且断言单一规则资源。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。

- [ ] **Step 7: 提交**

```powershell
git add src/BIMBaoGui.Stage01/Rules src/BIMBaoGui.Stage01/Context src/BIMBaoGui.Stage01/TaskPlanning src/BIMBaoGui.Stage01/Infrastructure src/BIMBaoGui.Stage01/Hifc src/BIMBaoGui.Stage01/Mvd tests
git commit -m "refactor: use one verified HBR rule database"
```

### Task 4: Stage02 纯匹配、预览和一次性确认合同

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs`
- Create: `src/BIMBaoGui.Stage01/Stage02/Stage02MatchEngine.cs`
- Create: `src/BIMBaoGui.Stage01/Stage02/Stage02PreviewCompiler.cs`
- Create: `src/BIMBaoGui.Stage01/Stage02/Stage02ConfirmationPolicy.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02MatchEngineTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreviewCompilerTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02ConfirmationPolicyTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: 写角色歧义和跨文档 RED 测试**

```csharp
[Fact]
public void Match_requires_role_hint_when_category_has_multiple_active_roles()
{
  var element = ElementSnapshot.Floor("uid-1", "阳台板");
  Stage02MatchResult result = Stage02MatchEngine.Match(Profile.AboveGround, element, roleHint: null);
  Assert.False(result.Success);
  Assert.Contains("AMBIGUOUS_CARRIER", result.Blockers);
}

[Fact]
public void Confirmation_rejects_document_or_old_value_change()
{
  Stage02WritePreview preview = PreviewFixture.Valid();
  Assert.False(Stage02ConfirmationPolicy.Validate(preview, PreviewFixture.CurrentWithDocument("other")).Success);
  Assert.False(Stage02ConfirmationPolicy.Validate(preview, PreviewFixture.CurrentWithOldValueHash("changed")).Success);
}
```

- [ ] **Step 2: 运行并确认 RED**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage02*Tests"`  
Expected: FAIL，Stage02 领域类型尚不存在。

- [ ] **Step 3: 实现确定性匹配与预览**

```csharp
public sealed class Stage02ElementReference
{
  public string DocumentFingerprint { get; set; }
  public int ElementId { get; set; }
  public string UniqueId { get; set; }
  public string Category { get; set; }
  public string FamilyName { get; set; }
  public string TypeName { get; set; }
  public string ElementName { get; set; }
}

public sealed class Stage02WriteOperation
{
  public string PropertyId { get; set; }
  public string ParameterGuid { get; set; }
  public string ParameterName { get; set; }
  public string OldValue { get; set; }
  public string SuggestedValue { get; set; }
  public string ValueSource { get; set; }
  public string Action { get; set; }
}
```

匹配顺序固定为显式提示、已有角色元数据、类别、名称别名、唯一候选。预览按 `UniqueId + propertyId` 排序，并把 `FileGuid + DocumentFingerprint + FileContextHash + RulePackageSha256 + element/value snapshots + nonce` canonicalize 后计算 `PreviewHash`。

- [ ] **Step 4: 实现 nonce 一次消费**

```csharp
public bool TryConsume(string previewHash, string nonce)
{
  string key = previewHash + "|" + nonce;
  lock (_sync) return _consumed.Add(key);
}
```

确认策略要求当前文档、上下文、规则、UniqueId 集合、旧值哈希都相同，且 nonce 未消费。

- [ ] **Step 5: 运行 GREEN 并提交**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage02*Tests"`  
Expected: PASS。

```powershell
git add src/BIMBaoGui.Stage01/Stage02 tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add Stage02 preview and confirmation domain"
```

### Task 5: Stage02 Revit 选择、可见共享参数和原子写入

**Files:**
- Create: `src/BIMBaoGui.Stage01/Revit/RevitDocumentIdentityService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage02MetadataStorage.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Parameters/HbrSharedParameterInstaller.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterValueConverter.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Parameters/HbrParameterReadbackVerifier.cs`
- Create: `src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs`
- Create: `tests/test_stage02_revit_contract.py`
- Modify: `src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs`

- [ ] **Step 1: 写参数可见性、文档身份和无业务隐藏值 RED 合同**

```python
def test_stage02_request_uses_unique_id_and_document_fingerprint():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02RevitWriteService.cs")
    assert "UniqueId" in text
    assert "DocumentFingerprint" in text
    assert "GetElement(request.UniqueId)" in text

def test_stage02_metadata_excludes_business_values():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage02MetadataStorage.cs")
    for forbidden in ("SuggestedValue", "OldValue", "BusinessValues", "RawValue"):
        assert forbidden not in text
```

- [ ] **Step 2: 运行并确认 RED**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage02_revit_contract.py -q`  
Expected: FAIL，文件尚不存在。

- [ ] **Step 3: 实现只读选择与预览服务**

`Stage02RevitSelectionService` 在 host context 中支持当前选择 `Selection.GetElementIds()` 和显式 `PickObjects(ObjectType.Element)`；捕获 `OperationCanceledException` 为取消结果，不当异常。所有结果同时保存 ElementId、UniqueId 和文档指纹。`Stage02RevitPreviewService` 只读参数，不启动事务。

- [ ] **Step 4: 实现共享参数安装器**

安装器从 `HbrRuleDatabase` 动态生成临时共享参数文本：

```text
*PARAM	<GUID>	HBR｜<Pset>｜<Property>	<TYPE>		1	1	HBR
```

`VISIBLE=1`、`USERMODIFIABLE=1`；相同定义已经绑定其他类别时，读取现有 `ElementBinding.Categories` 并与新类别做并集后 `ReInsert`，不得缩窄绑定。`Application.SharedParametersFilename` 始终在 `finally` 恢复，临时文件始终删除。

- [ ] **Step 5: 实现确认写入事务**

```csharp
using (var group = new TransactionGroup(document, "湖北BIM报规｜HBR属性准备"))
{
  group.Start();
  using (var tx = new Transaction(document, "安装并填写HBR可见参数"))
  {
    tx.Start();
    RevalidateDocumentAndPreview(document, request);
    installer.EnsureBindings(document, request.Operations);
    writer.WriteNonBlankSuggestions(document, request.Operations);
    document.Regenerate();
    verifier.Verify(document, request.Operations);
    Stage02MetadataStorage.WriteAuditOnly(document, request.Metadata);
    tx.Commit();
  }
  group.Assimilate();
}
```

任一失败显式 `RollBack`，并用 `Stage02FailureReportWriter` 在活动 GHA 同目录原子写 `BIMBaoGui.Stage02.failure-*.json`。

- [ ] **Step 6: 运行 GREEN 并提交**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage02_revit_contract.py -q`  
Expected: PASS。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。

```powershell
git add src/BIMBaoGui.Stage01/Revit src/BIMBaoGui.Stage01/Diagnostics tests/test_stage02_revit_contract.py
git commit -m "feat: write visible HBR parameters from Stage02"
```

### Task 6: Stage02 新公开 Grasshopper 组件和 UI

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs`
- Create: `src/BIMBaoGui.Stage01/UI/Stage02PreparationAttributes.cs`
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewGoo.cs`
- Create: `src/BIMBaoGui.Stage01/GrasshopperTypes/HBRStage02PreviewParam.cs`
- Create: `src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage02/Stage02Models.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs`
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs`
- Modify: `src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreparationInputPolicyTests.cs`
- Create: `tests/test_stage02_component_contract.py`

- [ ] **Step 1: 写选择模式、输入签名和端口 RED 测试**

`Stage02PreparationInputPolicyTests` 必须先验证：

- `项目信息` 与 ElementId/交互点选冲突时阻断；
- ElementId 与交互点选冲突时阻断；
- 四种合法入口分别解析为 `ProjectInformation/ExplicitIds/ExplicitPick/CurrentSelection`；
- `Stage02SelectionModes.ExplicitIds` 与 `ExplicitPick` 保持独立身份，并在确认时按冻结的 UniqueId 证据重建；
- ElementId 顺序和重复不影响确定性输入签名；
- context hash、选择模式、ElementId、角色提示任一变化都会改变签名，使旧预览失效。

```python
def test_new_stage02_has_real_ports_and_legacy_is_hidden():
    new = read("src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs")
    old = read("src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs")
    for label in (
        "文件上下文", "元素Id", "角色提示", "交互点选", "项目信息",
        "生成预览", "确认写入", "预览", "匹配载体", "字段明细",
        "阻断信息", "写入状态", "规则哈希", "报告路径"
    ):
        assert label in new
    assert "GH_Exposure.primary" in new
    assert "GH_Exposure.hidden" in old
```

- [ ] **Step 2: 运行 RED，随后实现选择适配与纯输入策略**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage02_component_contract.py -q`  
Expected: FAIL。
Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage02PreparationInputPolicyTests`
Expected: FAIL。

`Stage02PreparationInputPolicy` 只做确定性选择模式、冲突和输入签名，不引用 Revit/GH。领域 `Stage02SelectionModes` 新增独立的 `ExplicitIds`，不能把 ElementId 来源伪装成 `ExplicitPick`；`Stage02RevitPreviewService` 的确认白名单把它作为使用冻结 UniqueId 独立证据重建的模式。`Stage02RevitSelectionService` 增加按当前文档 ElementId 解析入口，并让当前选择、显式点选和 ElementId 入口都能携带一个可选角色提示；所有请求在预览时立即固化 `DocumentFingerprint + Element.UniqueId`。空 `ProjectInformation` 角色默认 `PROJECT`。

- [ ] **Step 3: 实现可消费一次的 GH 状态机与完整输出**

新组件内部使用两个 `ExplicitExecutionGate`，只允许“生成预览”和“确认写入”的 `false -> true` 边沿执行。选择入口不允许静默优先级：冲突直接输出阻断；否则按 `ProjectInformation -> ExplicitIds -> ExplicitPick -> CurrentSelection` 的互斥决策执行。`PickObjects` 只能由生成预览边沿触发并通过 Revit host context 调用，不能在普通 `SolveInstance()` 中调用。

预览和写入回调均通过 `ScheduleSolution` 回到 GH；共享状态由锁保护。回调完成时必须再次比较输入签名，过期结果不得发布。context、选择模式、ElementId、角色提示变化，以及写入成功或 `RequiresNewPreview`，都会清空旧预览/nonce/确认资格。确认写入使用预览时保存的独立选择证据构造 `Stage02RevitWriteRequest`，不能从输出 Goo 或 ElementId 重新猜测。

输出至少包括：强类型预览、匹配载体、按稳定元素顺序分支的完整字段 Data Tree、全部阻断、写入状态、待安装/已安装数、待写入/已写入数、规则哈希、失败报告路径和总状态。字段行同时包含 ElementId/UniqueId、角色、作用域、propertyId、参数 GUID/名称、旧值、建议值、来源、必填性、适用性、binding/value action 与阻断；卡片摘要不能代替这些输出。

- [ ] **Step 4: 实现不遮挡端口的卡片 UI**

`LayoutInputParams`、`LayoutOutputParams` 必须在扩展后的 component box 上调用；卡片只在端口之间绘制。UI 明确显示当前 RVT、已选元素、匹配角色、预览 hash、安装/写入数和第一条阻断。

状态文字至少区分：等待上下文、等待预览、选择取消、预览阻断、预览就绪、确认中、写入成功、写入失败、结果过期。正常/等待/阻断/失败不能只靠颜色区别。

- [ ] **Step 5: 运行 GREEN、全量回归并提交**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage02_component_contract.py -q`  
Expected: PASS。  
Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage02PreparationInputPolicyTests`
Expected: PASS。
Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --no-restore --nologo`
Expected: PASS。
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。

```powershell
git add src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs src/BIMBaoGui.Stage01/UI src/BIMBaoGui.Stage01/GrasshopperTypes src/BIMBaoGui.Stage01/Stage02 src/BIMBaoGui.Stage01/Revit/Stage02RevitSelectionService.cs src/BIMBaoGui.Stage01/Revit/Stage02RevitPreviewService.cs src/BIMBaoGui.Stage01/Stage02TaskPlanComponent.cs tests/BIMBaoGui.Stage01.Core.Tests tests/test_stage02_component_contract.py
git commit -m "feat: expose Stage02 element preparation workflow"
```

### Task 7: Stage03 字段状态、Strict/Force 门禁、路径和报告

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage03/Stage03ValidationModels.cs`
- Create: `src/BIMBaoGui.Stage01/Stage03/Stage03ExportGatePolicy.cs`
- Create: `src/BIMBaoGui.Stage01/Stage03/Stage03OutputPathPolicy.cs`
- Create: `src/BIMBaoGui.Stage01/Diagnostics/AtomicJsonReportWriter.cs`
- Create: `src/BIMBaoGui.Stage01/Diagnostics/Stage03FieldReportWriter.cs`
- Create: `src/BIMBaoGui.Stage01/Diagnostics/Stage03FailureReportWriter.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03ExportGatePolicyTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03OutputPathPolicyTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03FieldReportWriterTests.cs`

- [ ] **Step 1: 写 Strict/Force RED 测试**

```csharp
[Fact]
public void Strict_blocks_any_active_business_blocker()
{
  Stage03GateDecision result = Stage03ExportGatePolicy.Decide(
    Stage03GateMode.Strict, "", new[] { FieldResult.MissingRequired("p1") }, Array.Empty<string>());
  Assert.False(result.AllowExport);
}

[Fact]
public void Force_requires_reason_and_never_bypasses_technical_fatal()
{
  Assert.False(Stage03ExportGatePolicy.Decide(Stage03GateMode.Force, "", Business.One, Array.Empty<string>()).AllowExport);
  Assert.False(Stage03ExportGatePolicy.Decide(Stage03GateMode.Force, "测试", Business.One, new[] { "WRONG_DOCUMENT" }).AllowExport);
}
```

- [ ] **Step 2: 写三件套和不覆盖 RED 测试**

```csharp
[Fact]
public void Paths_share_run_id_and_reject_existing_target()
{
  Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(dir, "model", "20260802-210101-123");
  Assert.EndsWith("-RAW.ifc", paths.RawIfc);
  Assert.EndsWith("-HIFC-MVD.ifc", paths.FinalIfc);
  Assert.EndsWith("-fields.json", paths.FieldReport);
  File.WriteAllText(paths.RawIfc, "occupied");
  Assert.Throws<IOException>(() => Stage03OutputPathPolicy.ValidateUnused(paths));
}
```

- [ ] **Step 3: 运行 RED 并实现最小纯 Core**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage03*"`  
Expected: FAIL。

字段结果使用设计规格中的状态枚举；门禁把 `WRONG_DOCUMENT/UNSUPPORTED_REVIT/OUTPUT_EXISTS/EXPORT_FAILED/INVALID_IFC/REPORT_FAILED` 视为技术致命错误。字段报告按 `entity + ownerUniqueId + propertyId` 稳定排序，通过 `AtomicJsonReportWriter` 写临时文件再 `File.Move`。

- [ ] **Step 4: 运行 GREEN 并提交**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "Stage03*"`  
Expected: PASS。

```powershell
git add src/BIMBaoGui.Stage01/Stage03 src/BIMBaoGui.Stage01/Diagnostics tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add Stage03 validation gate and reports"
```

### Task 8: IFC4 STEP 新实体插入和缺失 Pset/属性转译

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Mvd/IfcStepDocument.cs`
- Modify: `src/BIMBaoGui.Stage01/Mvd/IfcStepEntity.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/IfcGuidCodec.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/HbrIfcEnrichmentModels.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/HbrIfcEnricher.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/HbrIfcFieldInspector.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/IfcStepDocumentMutationTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/HbrIfcEnricherTests.cs`

- [ ] **Step 1: 写 AddEntity RED 测试**

```csharp
[Fact]
public void AddEntity_inserts_before_data_endsec_and_roundtrips()
{
  IfcStepDocument doc = IfcStepDocument.Parse(Fixture.MinimalIfc4);
  IfcStepEntity added = doc.AddEntity("IFCPROPERTYSINGLEVALUE", new[] { "'X'", "$", "IFCREAL(1.5)", "$" });
  string text = doc.Serialize();
  Assert.Contains("#" + added.Id + "=IFCPROPERTYSINGLEVALUE", text);
  Assert.Single(IfcStepDocument.Parse(text).OfType("IFCPROPERTYSINGLEVALUE"));
}
```

- [ ] **Step 2: 写缺失 Pset 创建 RED 测试**

```csharp
[Fact]
public void Enricher_creates_property_pset_and_relationship_for_existing_owner()
{
  IfcStepDocument doc = IfcStepDocument.Parse(Fixture.ProjectWithoutPsets);
  var value = Fixture.Value("IfcProject", "owner-guid", "Pset_申报信息属性集", "原点坐标X", "IfcReal", "3353559.52");
  HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(doc, new[] { value });
  Assert.True(result.Success);
  Assert.Equal(1, result.CreatedProperties);
  Assert.Equal(1, result.CreatedPropertySets);
  Assert.Equal(1, result.CreatedRelationships);
  Assert.True(new HbrIfcFieldInspector().Inspect(doc, value).Success);
}
```

- [ ] **Step 3: 运行 RED 并实现可变 Statement 列表**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "IfcStepDocumentMutationTests|HbrIfcEnricherTests"`  
Expected: FAIL。

`IfcStepDocument` 保存可变 `List<Statement>`，`AddEntity` 使用当前最大 ID + 1，并在 DATA 的终止 `ENDSEC;` statement 前插入。新 `IfcStepEntity` 直接按 canonical STEP 格式序列化。

- [ ] **Step 4: 实现 IFC GUID 和 enrichment**

`IfcGuidCodec` 把确定性 UUIDv5 压缩为 22 字符 IFC GlobalId。Enricher：

```text
resolve owner by GlobalId or single spatial entity
-> resolve/create IfcPropertySingleValue with exact typed token
-> resolve/create IfcPropertySet
-> merge property reference list
-> resolve/create IfcRelDefinesByProperties
-> inspect exact owner/Pset/property/type/value
```

已有属性只更新目标类型和值；同 owner+Pset+property 多份冲突时失败。Organization/尚未支持 owner 不转挂 IfcProject，而返回 `IFC_OWNER_NOT_FOUND`。

- [ ] **Step 5: 运行 GREEN、真实 fixture 回归并提交**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "IfcStepDocumentMutationTests|HbrIfcEnricherTests"`  
Expected: PASS。  
Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "MvdIfc"`  
Expected: 旧 normalizer 测试仍全部 PASS。

```powershell
git add src/BIMBaoGui.Stage01/Mvd tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: create missing HBR properties in IFC4"
```

### Task 9: Revit 2020 全模型扫描和 Autodesk IFC4 导出

**Files:**
- Create: `src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs`
- Create: `src/BIMBaoGui.Stage01/Revit/Stage03RevitPhaseService.cs`
- Create: `tests/test_stage03_revit_export_contract.py`

- [ ] **Step 1: 写 API 线程、IFC4、事务和输出验证 RED 合同**

```python
def test_export_is_explicit_ifc4_inside_transaction():
    text = read("src/BIMBaoGui.Stage01/Revit/AutodeskIfcExportService.cs")
    assert "new IFCExportOptions" in text
    assert "FileVersion = IFCVersion.IFC4" in text
    assert "new Transaction" in text
    assert "document.Export" in text
    assert "File.Exists" in text
    assert "new FileInfo" in text

def test_scanner_uses_export_id_and_visible_parameter_guid():
    text = read("src/BIMBaoGui.Stage01/Revit/Stage03ModelScanService.cs")
    assert "ExportUtils.GetExportId" in text
    assert "SharedParameterElement.Lookup" in text
    assert "RulePackageSha256" in text
```

- [ ] **Step 2: 运行 RED 并实现 scanner**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage03_revit_export_contract.py -q`  
Expected: FAIL。

Scanner 在 Revit host context 中按活动 profile 枚举 ProjectInformation、Level、Room、Area 及规则类别元素。每条值读取固定 GUID 参数，转换为 canonical 外部单位；元素 IFC owner id 使用 `ExportUtils.GetExportId(document, element.Id)` 后交给 `IfcGuidCodec`。Project/Building/Site 允许使用唯一实体类型 owner strategy。

- [ ] **Step 3: 实现标准 IFC 导出**

```csharp
using (var tx = new Transaction(document, "湖北BIM报规｜导出标准IFC4"))
{
  if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("无法启动IFC导出事务。");
  var options = new IFCExportOptions { FileVersion = IFCVersion.IFC4 };
  options.AddOption("ExportInternalRevitPropertySets", "true");
  bool exported = document.Export(directory, fileNameWithoutExtension, options);
  tx.RollBack();
  if (!exported || !File.Exists(path) || new FileInfo(path).Length == 0)
    throw new IOException("Autodesk IFC4 导出失败或输出为空。");
}
```

调用前验证目标不存在；导出后报告事务实际为 RollBack。若实机证明 Revit 2020 必须 Commit，先增加回归测试和设计记录再更改。

- [ ] **Step 4: 运行 GREEN、构建并提交**

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage03_revit_export_contract.py -q`  
Expected: PASS。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。

```powershell
git add src/BIMBaoGui.Stage01/Revit tests/test_stage03_revit_export_contract.py
git commit -m "feat: scan Revit and export Autodesk IFC4"
```

### Task 10: Stage03 协调器、新公开组件和 legacy 隐藏

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage03/Stage03WorkflowCoordinator.cs`
- Create: `src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs`
- Create: `src/BIMBaoGui.Stage01/UI/Stage03ComponentAttributes.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs`
- Modify: `src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage03WorkflowCoordinatorTests.cs`
- Create: `tests/test_stage03_component_contract.py`

- [ ] **Step 1: 写协调状态机 RED 测试**

```csharp
[Fact]
public async Task Translation_failure_keeps_raw_and_writes_failure_report()
{
  var fixture = CoordinatorFixture.WithTranslatorFailure();
  Stage03RunResult result = await fixture.RunAsync(Stage03GateMode.Force, "验证转译失败路径");
  Assert.False(result.Success);
  Assert.True(File.Exists(result.RawIfcPath));
  Assert.False(File.Exists(result.FinalIfcPath));
  Assert.True(File.Exists(result.FailureReportPath));
}
```

- [ ] **Step 2: 运行 RED 并实现两阶段协调**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage03WorkflowCoordinatorTests`  
Expected: FAIL。

协调器顺序固定：

```text
preflight identity/paths
-> Revit host: scan and gate
-> strict blocked: fields JSON only
-> Revit host: export RAW IFC4
-> background: parse/enrich/inspect/write final
-> write final fields JSON and hashes
```

Force 只影响业务门禁。任何异常调用 `Stage03FailureReportWriter`；RAW 已成功时不删除。

- [ ] **Step 3: 写新组件端口合同并实现**

```python
def test_public_menu_is_exactly_three_stage_components():
    new = read("src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs")
    legacy3 = read("src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs")
    legacy4 = read("src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs")
    for label in ("文件上下文", "执行", "输出目录", "全部通过才导出", "强制原因"):
        assert label in new
    assert "DefaultStrictMode = true" in new
    assert "GH_Exposure.primary" in new
    assert "GH_Exposure.hidden" in legacy3
    assert "GH_Exposure.hidden" in legacy4
```

新组件定义 `private const bool DefaultStrictMode = true;`，并把 `全部通过才导出` 实现为使用该常量作为默认值的布尔开关。`true` 映射 `Strict`，`false` 映射 `Force`；`Force` 必须有非空 `强制原因`。模式开关与独立的 `执行` 上升沿分离，模式或原因变化会使旧结果失效。输出：允许导出、字段通过、全部阻断、RAW IFC、HIFC-MVD IFC、fields JSON、规则哈希和状态。卡片 UI 同时用文字和计数显示 Strict/Force、字段计数、运行状态和三个路径；完整字段通过稳定 Data Tree 输出，不把卡片摘要作为唯一结果。

- [ ] **Step 4: 运行 GREEN 并提交**

Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage03WorkflowCoordinatorTests`  
Expected: PASS。  
Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_stage03_component_contract.py -q`  
Expected: PASS。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。

```powershell
git add src/BIMBaoGui.Stage01/Stage03 src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs src/BIMBaoGui.Stage01/UI src/BIMBaoGui.Stage01/Stage03OfficialHifcWriteComponent.cs src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs tests
git commit -m "feat: complete Stage03 validation export workflow"
```

### Task 11: 文档、CI、完整自动化和单 GHA 部署

**Files:**
- Modify: `README.md`
- Modify: `docs/revit2020-v090-acceptance-checklist.md`
- Modify: `.github/workflows/build-stage01-gha.yml`
- Modify: `tests/test_v090_release_contract.py`
- Modify: `tests/test_official_export_contract_review.py`

- [ ] **Step 1: 先把旧“官方插件唯一/禁止后处理”测试改成 RED 的新合同**

```python
def test_readme_declares_three_stage_standard_export_workflow():
    readme = Path("README.md").read_text(encoding="utf-8")
    assert "03 检测、导出与 H-IFC 转译" in readme
    assert "Autodesk Revit 标准 IFC4" in readme
    assert "官方 H-IFC 插件重新导出" not in readme
```

Run: `C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_official_export_contract_review.py tests/test_v090_release_contract.py -q`  
Expected: FAIL，README 和旧测试仍描述旧路线。

- [ ] **Step 2: 更新产品文档和 CI**

README 只展示三个公开组件、单一规则源、Stage02 预览确认、Stage03 两个 IFC + JSON、Strict/Force、失败报告同 GHA 和无备份。CI push 分支加入当前修复分支或取消功能分支白名单，并显式执行 rulepack compiler tests。

- [ ] **Step 3: 运行完整自动化验证**

Run: `$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'; C:\ProgramData\Anaconda3\python.exe -m pytest -q`  
Expected: 0 failed。  
Run: `dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release`  
Expected: 0 failed。  
Run: `dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo`  
Expected: 0 warnings, 0 errors。  
Run: `git diff --check`  
Expected: no output。  
Run: `dotnet list src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj package --vulnerable --include-transitive`  
Expected: no known vulnerable packages。

- [ ] **Step 4: 请求整体验收代码审查，修复 Critical/Important 后提交**

```powershell
git add README.md docs/revit2020-v090-acceptance-checklist.md .github/workflows/build-stage01-gha.yml tests
git commit -m "docs: document three-stage HBR workflow"
```

- [ ] **Step 5: 无备份部署**

确认 Revit、Rhino.Inside、Grasshopper 进程均已关闭后，直接覆盖：

```powershell
Copy-Item -LiteralPath 'src\BIMBaoGui.Stage01\bin\Release\net48\BIMBaoGui.Stage01.gha' -Destination 'C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui\BIMBaoGui.Stage01.gha' -Force
```

随后验证插件目录：GHA 数量恰为 1，`.bak/.backup` 数量为 0，源/部署 SHA-256 一致。

### Task 12: Revit 2020 真实全流程验收

**Files:**
- Create after run: GHA 同目录 `BIMBaoGui.Stage02.failure-*.json` / `BIMBaoGui.Stage03.failure-*.json` only on failure
- Create after successful Stage03: RVT 同目录或用户输出目录的 `*-RAW.ifc`, `*-HIFC-MVD.ifc`, `*-fields.json`
- Update: `docs/revit2020-v090-acceptance-checklist.md` with actual observed hashes and counts

- [ ] **Step 1: 启动并验证只有三个公开组件**

启动顺序：Revit 2020 → Rhino.Inside.Revit → Grasshopper。确认菜单只有 01 初始化、02 构件与属性准备、03 检测导出转译；所有端口可见。

- [ ] **Step 2: 在 `20260731test02.rvt` 运行 Stage01**

确认现有项目身份、X/Y、高程和真北效果未回归，并记录 `FileContextHash + RulePackageSha256`。

- [ ] **Step 3: Stage02 项目信息与实例验收**

先生成预览，确认后写入。必须在 Revit 项目信息和至少一个实例/类型属性面板中看到例如 `HBR｜申报信息属性集｜原点坐标X` 的参数或同 GUID legacy 名，直接编辑一个哨兵值；切换 RVT 后旧预览必须失效。

- [ ] **Step 4: Stage03 Strict 验收**

当前规则仍有 `UNCLASSIFIED` 或缺失字段时，Strict 必须阻断且只生成 fields JSON，报告准确区分缺构件、名称不匹配、缺参数、空值和未分类。

- [ ] **Step 5: Stage03 Force 双 IFC 验收**

提供非空强制原因。验证同一 runId 的 RAW、HIFC-MVD、fields JSON 均生成；RAW 来自 Revit 2020 IFC4，final 中至少 X、Y、高程字段与 Revit 可见值逐字/数值一致；源 RVT 和 RAW 哈希在转译阶段不变。

- [ ] **Step 6: 重启持久性验收**

保存、关闭并重新打开 RVT，确认 HBR 参数和值仍在 Revit UI 可见可编辑。插件目录仍只有一个 GHA，无插件备份文件。

- [ ] **Step 7: 保存实测证据并停止**

把 GHA SHA-256、规则包 SHA-256、三件套路径/哈希、字段状态计数和仍未实施的 owner 策略写入验收清单。未通过的字段保持明确失败状态，不把 Force 输出描述为 Strict 全通过。
