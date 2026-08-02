# HBR Task 3 Runtime Database Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 HBR 规则包迁移为 net48 唯一运行时数据库，保持所有旧 catalog 行为等价，并让 FileContext、TaskPlan 和交付物携带同一规则包身份。

**Architecture:** 先把旧资源中的兼容元数据逐项复制进唯一业务源，并用冻结 baseline 阻止身份漂移；随后只从经 SHA-256 验证的 HBRP payload 构造深只读单例。六个 catalog 和共享参数投影均从该数据库确定性生成，最后移除五个旧 EmbeddedResource，并以跨阶段哈希门禁拒绝旧结果静默升级。

**Tech Stack:** C# / .NET Framework 4.8、System.Web.Script.Serialization、LazyThreadSafetyMode.ExecutionAndPublication、xUnit、Python 3 / pytest、MSBuild、SHA-256。

---

## 不可协商的迁移约束

- 唯一可编辑业务源是 specs/hbr-rules/v1/source/hbr_rule_source.v1.json。
- 运行时只允许读取 HBR_RulePack.hbrpack，禁止读取旧 JSON、TXT、硬编码 catalog 或 fallback。
- compatibility baseline 继续冻结并作为编译输入。
- baseline 的 propertyId、canonicalKey、parameterGuid、originalIdentity 四字段在新业务源变更后仍须逐项不变。
- 五个任务必须按 A、B、C、D、E 顺序执行，每项先 RED、后最小实现、再 GREEN、最后独立提交。

### Task A: 将旧兼容元数据逐项迁入唯一源

**Files:**

- Modify: specs/hbr-rules/v1/schemas/hbr_rule_source.schema.json
- Modify: specs/hbr-rules/v1/source/hbr_rule_source.v1.json
- Reference: specs/hbr-rules/v1/compatibility/hbr_rule_compatibility_baseline.v1.json
- Modify: tools/build_hbr_rulepack.py
- Modify: tests/test_hbr_rule_source_contract.py
- Modify: tests/test_hbr_rule_source_semantics.py
- Modify: tests/test_hbr_rulepack_compiler.py
- Reference: src/BIMBaoGui.Stage01/Resources/stage01_file_initialization_registry_v0.1.json
- Reference: src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs
- Reference: src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs
- Reference: src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs
- Reference: src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs

- [ ] **Step 1: 写迁移字段完整性 RED 测试**

在 tests/test_hbr_rule_source_contract.py 增加以下具体测试：

~~~python
def test_source_contains_complete_legacy_compatibility_metadata(source):
    stage01 = source["stage01"]
    assert len(stage01["internalWorkflowFields"]) == 12
    assert len(stage01["fieldRefs"]) == 102
    assert all({"uiGroup", "sourceKind", "writeInStage01"} <= set(x) for x in stage01["fieldRefs"])

    official = [p for p in source["properties"] if p["officialPlugin"]["inExtracted166"]]
    frozen = {"category", "carrier", "persistenceMode", "sharedParameterType",
              "officialSourceParameterGroup", "sourceParameterOverride"}
    assert len(official) == 166
    assert all(frozen <= set(p["officialPlugin"]["legacyProjection"]) for p in official)

    compatibility = stage01["officialPluginCompatibility"]
    assert len(compatibility["entityPolicies"]) == 9
    assert len(compatibility["exceptions"]) == 13
    assert all(x["reason"].strip() for x in compatibility["exceptions"])
    assert len(source["modelProfiles"]) == 3
    assert all("activationRuleIds" in p for p in source["modelProfiles"])
~~~

同时在 tests/test_hbr_rule_source_semantics.py 增加
test_migrated_metadata_is_exactly_equivalent_to_legacy_resources：
按稳定键排序后，逐项比较 12 个 internalWorkflowFields、102 个 fieldRefs 的三个新增字段、
166 个 legacyProjection 六字段、9 个 entity policies、13 个 exception/reason，以及三个
profile 的 activationRuleIds；测试不得使用 get(..., default)、空字符串补齐或按名称猜值。

- [ ] **Step 2: 写迁移元数据语义闭合 RED 测试**

~~~python
@pytest.mark.parametrize("mutate", [
    lambda d: d["stage01"].pop("internalWorkflowFields"), lambda d: d["stage01"]["fieldRefs"][0].pop("uiGroup"),
    lambda d: first_official(d)["officialPlugin"].pop("legacyProjection"),
    lambda d: d["stage01"]["officialPluginCompatibility"]["exceptions"].pop(), lambda d: d["modelProfiles"][0].pop("activationRuleIds"),
])
def test_validate_semantics_rejects_missing_or_truncated_migrated_metadata(source, baseline, mutate):
    mutate(source)
    with pytest.raises(ValueError, match="migrated metadata"):
        validate_semantics(source, baseline)
@pytest.mark.parametrize("mutate", [
    duplicate_first_internal_workflow_field, point_first_field_ref_to_missing_property,
    duplicate_first_entity_policy, point_first_profile_to_missing_activation_rule,
])
def test_validate_semantics_rejects_duplicate_or_dangling_migrated_references(source, baseline, mutate):
    mutate(source)
    with pytest.raises(ValueError, match="duplicate|unknown reference"):
        validate_semantics(source, baseline)
~~~
Run: C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py tests/test_hbr_rulepack_compiler.py -q

Expected: FAIL；旧 validate_semantics 不识别这些迁移节点，会错误放行缺失、删减、重复或悬空数据。现有 baseline 必需参数与四字段冻结测试继续 PASS，不属于本步骤 RED。

- [ ] **Step 3: 最小扩展 schema 与唯一源**

- 在 stage01 下声明 12 项 internalWorkflowFields 和 102 项 fieldRefs；每个 fieldRef 必须显式含
  uiGroup、sourceKind、writeInStage01。
- 在 166 条 officialPlugin 记录下声明 legacyProjection，并逐条复制 category、carrier、
  persistenceMode、sharedParameterType、officialSourceParameterGroup、sourceParameterOverride。
- 在 stage01.officialPluginCompatibility 中逐条复制 9 个 entityPolicies 和 13 个 exceptions；
  每个 exception 保留旧 reason，不把异常压缩成实体默认策略。
- 给三个 modelProfiles 逐条复制 activationRuleIds。值只来自现有旧资源；缺项立即失败，
  禁止 runtime fallback、编译器 fallback 或测试 fixture fallback。

- [ ] **Step 4: 扩展编译器迁移语义校验**

扩展 validate_semantics 的闭合 shape、固定计数、唯一键、非空 reason 和引用存在性校验：
12 个 internalWorkflowFields、102 个含三个新增字段的 fieldRefs、166 个完整六字段
legacyProjection、9 个 entity policies、13 个 exception/reason、三个 profile 的
activationRuleIds；再调用 legacy 等价性投影检查。复用现有 --compatibility-baseline 必需参数
及 propertyId/canonicalKey/parameterGuid/originalIdentity 四字段冻结实现，不重复实现该门禁，
也不从旧资源 fallback。

- [ ] **Step 5: 运行 GREEN**

Run: C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py tests/test_hbr_rulepack_compiler.py -q

Expected: PASS；所有损坏样例均被 validate_semantics 精确拒绝，合法源计数为 12、102、166、9、13、3；既有 baseline 必需参数和四字段冻结回归继续通过。

- [ ] **Step 6: 提交**

~~~powershell
git add specs/hbr-rules/v1 tools/build_hbr_rulepack.py tests/test_hbr_rule_source_contract.py tests/test_hbr_rule_source_semantics.py tests/test_hbr_rulepack_compiler.py
git commit -m "feat: migrate legacy metadata into HBR source"
~~~

### Task B: 建立 net48 HBRP loader 与深只读数据库

**Files:**

- Create: src/BIMBaoGui.Stage01/Rules/HbrRulePackage.cs
- Create: src/BIMBaoGui.Stage01/Rules/HbrRulePackageLoader.cs
- Create: src/BIMBaoGui.Stage01/Rules/HbrRuleDatabase.cs
- Create: tests/BIMBaoGui.Stage01.Core.Tests/HbrRulePackageLoaderTests.cs
- Create: tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleDatabaseTests.cs
- Modify: src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj
- Modify: tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj

- [ ] **Step 1: 写 loader 与 raw payload SHA RED 测试**

在 HbrRulePackageLoaderTests.cs 增加以下具体测试：

~~~csharp
[Fact]
public void Load_rejects_payload_hash_mismatch_before_json_deserialization()
{
    byte[] bytes = HbrPackFixture.ValidBytes();
    bytes[bytes.Length - 1] ^= 0x01;
    var ex = Assert.Throws<InvalidDataException>(() => HbrRulePackageLoader.Load(bytes));
    Assert.Contains("payload SHA-256", ex.Message);
}

[Fact]
public void Load_exposes_sha_of_raw_payload_not_reserialized_dto()
{
    byte[] bytes = HbrPackFixture.ValidBytesWithNonAsciiPayload();
    HbrRulePackage package = HbrRulePackageLoader.Load(bytes);
    Assert.Equal(HbrPackFixture.PayloadSha256(bytes), package.RulePackageSha256);
}
~~~

- [ ] **Step 2: 写深只读六索引与 Lazy RED 测试**

HbrRuleDatabaseTests.cs 必须包含
Database_builds_property_ifc_guid_role_profile_task_indexes、
Database_rejects_duplicate_key_in_each_index、
Database_does_not_expose_mutable_dto_collections，以及
Current_uses_execution_and_publication_and_loads_once。

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "FullyQualifiedName~HbrRulePackageLoaderTests|FullyQualifiedName~HbrRuleDatabaseTests"

Expected: FAIL；HbrRulePackageLoader、HbrRuleDatabase 和 concrete DTO 尚不存在。

- [ ] **Step 3: 最小实现 net48 HBRP loader**

固定按 4-byte magic、big-endian formatVersion、big-endian payloadLength、32-byte SHA、
raw payload 的顺序解析。先验证边界、长度和 SHA，再把 UTF-8 payload 交给 serializer：

~~~csharp
var serializer = new JavaScriptSerializer {
    MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
HbrRulePackageDto dto = serializer.Deserialize<HbrRulePackageDto>(
    StrictUtf8.GetString(payload));
~~~

HbrRulePackage.cs 定义 sealed concrete DTO，字段类型必须具体到
HbrRulePackageDto、HbrPropertyDto、HbrCarrierRoleDto、HbrModelProfileDto、
HbrTaskDto 和各嵌套 DTO；禁止 dynamic、object 树或
Dictionary<string, object>。RulePackageSha256 直接来自已验证 raw payload，
不得重新序列化 DTO 后再计算。

- [ ] **Step 4: 最小实现深只读 HbrRuleDatabase**

- 从 DTO 构造不可变领域记录，所有数组先复制，再包装为 ReadOnlyCollection。
- 构建 PropertiesById、PropertiesByIfcIdentity、PropertiesByParameterGuid、
  CarrierRolesById、ProfilesByModelFileType、TasksById 六索引。
- IFC identity 使用 entity/propertySet/property 三元值对象与 Ordinal 比较；
  parameterGuid 规范成 Guid，不以大小写不同的字符串制造重复。
- 每个索引用显式 Add；遇到重复抛 InvalidDataException 并指出索引名和冲突键。
- 对外仅返回 IReadOnlyList 和 ReadOnlyDictionary，绝不暴露 DTO 或内部数组。

~~~csharp
private static readonly Lazy<HbrRuleDatabase> LazyCurrent =
    new Lazy<HbrRuleDatabase>(LoadEmbeddedDatabase,
        LazyThreadSafetyMode.ExecutionAndPublication);
public static HbrRuleDatabase Current { get { return LazyCurrent.Value; } }
~~~

LoadEmbeddedDatabase 只读取逻辑名
BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack；资源缺失立即失败，不尝试旧 JSON。

- [ ] **Step 5: 运行 GREEN**

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "FullyQualifiedName~HbrRulePackageLoaderTests|FullyQualifiedName~HbrRuleDatabaseTests"

Expected: PASS；损坏包在反序列化前被拒绝，六索引可查且不可变，Current 只发布一个成功实例。

- [ ] **Step 6: 提交**

~~~powershell
git add src/BIMBaoGui.Stage01/Rules src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add verified HBR runtime database"
~~~

### Task C: 从数据库投影六个 catalog 与共享参数文本

**Files:**

- Modify: src/BIMBaoGui.Stage01/Infrastructure/Stage01RegistryProvider.cs
- Modify: src/BIMBaoGui.Stage01/Hifc/OfficialHifcMappingCatalog.cs
- Modify: src/BIMBaoGui.Stage01/Hifc/OfficialPluginCompatibilityCatalog.cs
- Modify: src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizationCatalog.cs
- Modify: src/BIMBaoGui.Stage01/TaskPlanning/TaskRuleCatalog.cs
- Modify: src/BIMBaoGui.Stage01/Context/RuleActivationCatalog.cs
- Modify: src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs
- Create: tests/BIMBaoGui.Stage01.Core.Tests/HbrCatalogProjectionTests.cs
- Create: tests/BIMBaoGui.Stage01.Core.Tests/OfficialParameterProjectionServiceTests.cs
- Modify: tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj

- [ ] **Step 1: 写六个 catalog 等价性 RED 测试**

HbrCatalogProjectionTests.cs 至少加入以下具体测试：

~~~csharp
[Fact]
public void Stage01_registry_projects_12_internal_and_102_refs_with_legacy_behavior()
{
    var registry = Stage01RegistryProvider.FromDatabase(Fixture.Database);
    Assert.Equal(12, registry.InternalWorkflowFields.Count);
    Assert.Equal(102, registry.FieldReferences.Count);
    Assert.Equal(Fixture.LegacyStage01Snapshot, registry.ToCompatibilitySnapshot());
}

[Fact]
public void Other_catalogs_preserve_166_9_plus_13_179_28_and_activation()
{
    Assert.Equal(166, OfficialHifcMappingCatalog.FromDatabase(Fixture.Database).Count);
    Assert.Equal(9, OfficialPluginCompatibilityCatalog.FromDatabase(Fixture.Database).EntityPolicies.Count);
    Assert.Equal(13, OfficialPluginCompatibilityCatalog.FromDatabase(Fixture.Database).Exceptions.Count);
    Assert.Equal(179, MvdIfcNormalizationCatalog.FromDatabase(Fixture.Database).Count);
    Assert.Equal(28, TaskRuleCatalog.FromDatabase(Fixture.Database).Count);
    Assert.Equal(Fixture.LegacyActivationSnapshot,
        RuleActivationCatalog.FromDatabase(Fixture.Database).ToCompatibilitySnapshot());
}
~~~

Fixture.LegacyStage01Snapshot、LegacyActivationSnapshot 和其余 catalog snapshot
在测试迁移时一次性从旧资源生成并签入为显式期望值；运行测试不得再读取旧资源。

- [ ] **Step 2: 写共享参数与坐标语义 RED 测试**

OfficialParameterProjectionServiceTests.cs 加入
Shared_parameter_text_is_deterministic_without_txt_resource 和
Axis_defaults_and_ui_groups_are_legacy_equivalent。前者在无任何 TXT 文件的临时目录中调用两次，
断言字节完全相同；后者逐条比较 defaultValue、uiGroup、writeInStage01，并明确断言：

~~~csharp
Assert.Equal("NorthSouth", projection.BySourceName["X"].TargetName);
Assert.Equal("EastWest", projection.BySourceName["Y"].TargetName);
Assert.Equal(Fixture.LegacyDefaultsAndUiGroups, projection.ToDefaultsAndUiGroupsSnapshot());
~~~

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "FullyQualifiedName~HbrCatalogProjectionTests|FullyQualifiedName~OfficialParameterProjectionServiceTests"

Expected: FAIL；六个 catalog 仍从旧资源或硬编码构造，parameter service 仍依赖 TXT/旧入口。

- [ ] **Step 3: 最小切换六个 catalog**

每个 catalog 增加 internal FromDatabase(HbrRuleDatabase database) 纯投影入口，
现有 public/static 入口只委托 HbrRuleDatabase.Current，以保持调用方 API 不变：

- Stage01RegistryProvider 投影 12 个 internalWorkflowFields 和 102 个 fieldRefs，
  保留 uiGroup、sourceKind、writeInStage01、默认值及原排序行为。
- OfficialHifcMappingCatalog 只投影 inExtracted166=true 的 166 条及 legacyProjection 六字段。
- OfficialPluginCompatibilityCatalog 投影 9 个 entity policies 和 13 个 exception/reason。
- MvdIfcNormalizationCatalog 投影旧行为对应的 179 条规范化记录。
- TaskRuleCatalog 投影 28 个任务规则；RuleActivationCatalog 从三个 profile 的
  activationRuleIds 投影，不再持有第二份激活表。

删除这些类中的 manifest stream、旧 JSON 解析、TXT 读取和 catch-then-default 分支；
缺字段或未知引用必须抛 InvalidDataException。

- [ ] **Step 4: 最小实现确定性共享参数投影**

OfficialParameterProjectionService 只接收 HbrRuleDatabase，按 parameterGuid 稳定排序，
从 revit 与 legacyProjection 生成完整共享参数文本。固定 UTF-8、CRLF、固定组声明，
不写时间戳；同一数据库两次输出必须 byte-for-byte 相同。X/Y 映射固定为
X=NorthSouth、Y=EastWest；默认值和 UI 分组直接取 Task A 已迁移字段，不设置通用默认值。

- [ ] **Step 5: 运行 GREEN**

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter "FullyQualifiedName~HbrCatalogProjectionTests|FullyQualifiedName~OfficialParameterProjectionServiceTests"

Expected: PASS；六类计数、旧快照、坐标语义、默认值/UI 分组均等价，测试环境无需 TXT。

- [ ] **Step 6: 提交**

~~~powershell
git add src/BIMBaoGui.Stage01/Infrastructure src/BIMBaoGui.Stage01/Hifc src/BIMBaoGui.Stage01/Mvd src/BIMBaoGui.Stage01/TaskPlanning src/BIMBaoGui.Stage01/Context src/BIMBaoGui.Stage01/Revit/OfficialParameterProjectionService.cs tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "refactor: project runtime catalogs from HBR database"
~~~

### Task D: 传播规则包身份并拒绝旧 Goo 静默升级

**Files:**

- Modify: src/BIMBaoGui.Stage01/Context/HBRFileContext.cs
- Modify: src/BIMBaoGui.Stage01/Context/HBRFileContextFactory.cs
- Modify: src/BIMBaoGui.Stage01/Context/HBRFileContextCanonicalizer.cs
- Modify: src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlan.cs
- Modify: src/BIMBaoGui.Stage01/TaskPlanning/HBRTaskPlanCanonicalizer.cs
- Modify: src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs
- Modify: src/BIMBaoGui.Stage01/GrasshopperTypes/HBRFileContextGoo.cs
- Modify: src/BIMBaoGui.Stage01/GrasshopperTypes/HBRTaskPlanGoo.cs
- Create: tests/BIMBaoGui.Stage01.Core.Tests/HbrRuleIdentityPropagationTests.cs

- [ ] **Step 1: 写四个具体 RED 测试**

在 HbrRuleIdentityPropagationTests.cs 增加
File_context_and_task_plan_hashes_include_package_identity、
ValidateContext_rejects_rule_package_hash_mismatch、
Legacy_goo_with_valid_old_hash_requires_rerun、
Legacy_goo_with_invalid_old_hash_is_rejected。

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter FullyQualifiedName~HbrRuleIdentityPropagationTests

Expected: FAIL；FileContext/TaskPlan 尚未传播 RulePackageId、RulePackageVersion、RulePackageSha256，mismatch 和旧 Goo 也未阻断。

- [ ] **Step 2: 最小实现并运行 GREEN**

Factory 从 HbrRuleDatabase.Current 写入三字段；两个 canonicalizer 按 packageId、version、SHA 固定顺序参与 hash。TaskPlanCompiler.ValidateContext 逐项比较当前数据库身份，任一 mismatch 返回阻断。Goo 只用旧 canonicalizer 验证旧 hash：有效则标记无效并提示“规则数据库已升级，请重新运行 Stage01/任务规划”，无效则报告损坏；绝不填入当前 hash。

Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter FullyQualifiedName~HbrRuleIdentityPropagationTests

Expected: PASS；相同输入 hash 稳定，package 任一字段变化均失效，legacy Goo 不静默升级。

- [ ] **Step 3: 提交**

~~~powershell
git add src/BIMBaoGui.Stage01/Context src/BIMBaoGui.Stage01/TaskPlanning src/BIMBaoGui.Stage01/GrasshopperTypes tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: propagate HBR rule identity across contexts"
~~~

### Task E: 收口唯一 pack、实例化验证和 Release 验收

**Files:**

- Modify: src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj
- Modify: tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
- Create: tests/BIMBaoGui.Stage01.Core.Tests/HbrRuntimePackagingTests.cs
- Create: tests/test_hbr_runtime_packaging_contract.py

- [ ] **Step 1: 写四个具体 RED 测试**

新增 Production_manifest_contains_exactly_one_runtime_pack、
Test_and_production_packs_have_identical_payload_sha、
All_catalogs_instantiate_from_embedded_pack_without_legacy_resources，以及 pytest
test_csproj_removes_five_legacy_embedded_resources。

Expected: FAIL；生产/测试资源来源尚未统一，且五个旧 EmbeddedResource 仍在 manifest。

- [ ] **Step 2: 最小收口 packaging**

两个 csproj 都以同一 source+compatibility baseline 在各自 obj 目录生成并嵌入同字节 pack；移除五个旧 EmbeddedResource。实例化测试实际构造 Stage01、OfficialMapping、OfficialPluginCompatibility、MVD、TaskRule、RuleActivation 六目录及 OfficialParameterProjectionService；manifest 过滤规则资源后必须只剩一个 HBR_RulePack.hbrpack。

- [ ] **Step 3: 运行 GREEN 与全量验收**

Run: C:\ProgramData\Anaconda3\python.exe -m pytest tests -q
Run: dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
Run: dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo
Run: git diff --check

Expected: pytest/C# 全绿；Release 为 0 warnings、0 errors；生产 manifest 仅一个 runtime pack；git diff 仅含本任务预期文件。

- [ ] **Step 4: 提交**

~~~powershell
git add src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj tests/BIMBaoGui.Stage01.Core.Tests tests/test_hbr_runtime_packaging_contract.py
git commit -m "build: ship only the verified HBR runtime pack"
~~~
