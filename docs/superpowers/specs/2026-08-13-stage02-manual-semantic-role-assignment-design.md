# Revit Stage02 手动语义类型分配设计规范

- 日期：2026-08-13
- 目标仓库：`ArchitectureWorld/BIM-baogui`
- 目标分支：`feat/revit-native-addin-mcp-v0.3`
- 建议产品版本：`0.4.2`
- 设计状态：待用户最终确认后进入实施计划
- 适用环境：Autodesk Revit 2020

## 1. 背景与问题

当前 Stage02 的“当前 Revit 选择”不是任意构件处理入口。它会先按规则库中的 Revit 类别与 `ElementKind` 白名单筛选，再检查用户所选构件是否仍在可用清单中。建筑地坪等尚未进入自动识别白名单的构件会在参数创建前被判定为：

```text
CUSTOM_ELEMENT_UNAVAILABLE
```

这导致用户即使已经明确选中“建筑地坪｜集中绿地”，也不能将其声明为报规语义上的“绿地”并准备对应属性。

本设计增加正式的“手动指定构件语义类型”能力，使用户能够明确声明：

```text
Revit 实际构件
→ 报规语义类型
→ 固定属性模板
→ H-IFC Owner 策略
```

该能力不是“任意选择一个属性集并强行写入”，而是受规则库约束的语义角色分配。

## 2. 证据结论与语义边界

### 2.1 绿地是独立的规划报批模型单元

《湖北省工程建设管理 BIM 云平台建筑工程模型交付指南（试行）》将绿地列为总平面中的“规划报批类模型单元”，要求按不同绿地分类命名，例如“集中绿地”。绿地至少包含：

- 分类名称：固定为“绿地”；
- 投影面积；
- 绿地类型；
- 折算系数。

因此，绿地不应仅作为整个项目的一个汇总文本字段，而应能够逐个对象识别、逐个对象填写和逐个对象统计。

### 2.2 `SITE` 与具体场地对象必须分层

本设计将两类语义严格分开：

```text
SITE
= 整个项目场地的唯一上层语义
= Revit 中由 ProjectInformation + ProjectLocation + 坐标数据代理
= IFC 中对应唯一 IfcSite

SITE_GREEN_OBJECT 等
= 场地中的具体报规对象
= Revit 中由建筑地坪、面域、楼板、通用模型等批准载体表达
= 每个构件均有自己的语义角色和属性值
```

`SITE` 只承载场地总体信息和汇总指标；“集中绿地 A”“宅旁绿地 B”等不能被压缩为同一个 `SITE` 对象。

### 2.3 官方实例与 IFCFlux MVD 存在实体冲突

现有官方总平实例中，绿地对象实际为：

```text
IFCBUILDINGELEMENTPROXY
└─ Pset_绿地信息属性集
   ├─ 类型
   ├─ 投影面积
   ├─ 绿地类型
   └─ 折算系数
```

但现有 IFCFlux 规划报建 MVD 提取把 `Pset_绿地信息属性集` 声明在 `IfcSite` 下。两者存在已知实体冲突。

本设计的原则是：

1. Stage02 先正确建立“逐构件语义角色”和固定 GUID 参数；
2. Stage03 的对象级导出优先依据官方实例，使用独立导出对象；
3. 不得为了迎合单一 MVD 声明，把多块绿地静默合并到唯一 `IfcSite`；
4. 最终发布前必须用官方实例 fixture 与 IFCFlux 实机共同验证；
5. 若兼容需要双写，必须形成明确、可测试的兼容策略，不能在代码中临时猜测。

## 3. 用户交互设计

## 3.1 Stage02 顶部控制区

当作用范围选择“当前 Revit 选择”时，新增：

```text
识别方式：
○ 自动识别
● 手动指定

批量语义类型：
[ 绿地 ▼ ]

[生成预览] [确认写入]
```

### 自动识别

维持现有严格策略：

```text
模型类型
+ Revit 类别
+ ElementKind
+ 精确别名
→ 自动角色
```

不得新增模糊包含、编辑距离或按几何外观猜测。

### 手动指定

用户明确选择报规语义类型。系统只允许选择当前模型类型、项目条件和载体策略均批准的类型。

例如选中建筑地坪后，可选择：

```text
绿地
建筑基底
规划净用地
消防登高场地
区内道路
……
```

实际可选项必须由规则库动态生成，不得在界面中硬编码一套与规则包脱节的列表。

## 3.2 批量统一指定 + 逐项改写

选中多个构件后：

1. 用户在“批量语义类型”中选择一个类型；
2. 该类型默认应用到所有当前选中构件；
3. 下方预览列表允许逐个构件改写；
4. 每一行必须显示最终生效的语义类型，而不是只显示批量值。

示例：

| Revit 构件 | 类别/类型 | 批量结果 | 逐项改写 | 最终角色 |
|---|---|---|---|---|
| A | 建筑地坪/集中绿地 | 绿地 | — | 绿地 |
| B | 建筑地坪/宅旁绿地 | 绿地 | — | 绿地 |
| C | 建筑地坪/消防场地 | 绿地 | 消防登高场地 | 消防登高场地 |

## 3.3 预览列表信息

每个选中构件至少显示：

- ElementId；
- UniqueId；
- Revit 类别；
- `ElementKind`；
- 元素名称；
- 族名；
- 类型名；
- 自动识别结果；
- 手动角色；
- 分配模式：`AUTO` / `MANUAL`；
- 角色可用性；
- 预计属性模板；
- 阻断原因。

新的错误提示必须区分：

```text
SELECTION_ELEMENT_MISSING
当前选择中的构件已经不存在。

SELECTION_ELEMENT_NOT_ELIGIBLE
构件是链接、导入或视图专用对象，不能进入写入流程。

AUTO_ROLE_UNSUPPORTED
自动识别规则不支持当前类别或 ElementKind。

MANUAL_ROLE_NOT_ALLOWED_FOR_CARRIER
用户选择的语义类型不允许使用当前 Revit 构件作为载体。

STAGE01_CONDITION_INACTIVE
语义类型对应的项目条件未在 Stage01 中启用。

ROLE_ASSIGNMENT_CONFLICT
同一构件存在互斥或重复语义角色。

ROLE_TEMPLATE_UNAVAILABLE
规则包没有提供该语义角色对应的属性模板。
```

不得再把所有情况都折叠成 `CUSTOM_ELEMENT_UNAVAILABLE`。

## 4. 选择范围与角色识别分离

当前逻辑把“选中构件是否属于当前文档”和“构件是否可自动识别”为同一个过滤步骤。本设计将其拆开。

### 4.1 第一层：选择范围资格

当前选择只检查：

- 构件属于当前活动文档；
- UniqueId 可回读；
- 不是 ElementType；
- 不是链接模型构件；
- 不是导入对象；
- 不是视图专用对象；
- 是可持久化的模型元素。

这一层不再使用 HBR 类别白名单排除建筑地坪。

### 4.2 第二层：语义角色资格

#### 自动模式

继续使用现有严格角色匹配器。

#### 手动模式

读取用户指定角色后，执行：

```text
模型类型允许
AND Stage01 项目条件允许
AND manualCarrierPolicy 允许
AND 角色属性模板存在
AND H-IFC Owner 策略已定义
```

手动模式不是无条件绕过规则，而是由用户作出语义声明后，使用更宽但仍受控的载体许可表。

## 5. 规则包扩展

## 5.1 新增对象级语义角色

建议第一批至少增加：

```text
SITE_TOTAL_LAND
SITE_NET_LAND
SITE_BUILDING_FOOTPRINT
SITE_GREEN_OBJECT
SITE_ROAD_REDLINE
SITE_ROAD_CENTERLINE
SITE_INTERNAL_ROAD
SITE_FIRE_LANE
SITE_FIRE_FIELD
SITE_OUTDOOR_PARKING
SITE_CIVIL_DEFENSE_AREA
SITE_STRUCTURE
```

现有的：

```text
PROJECT
SITE
BUILDING
STOREY
SPACE
SLAB
……
```

继续作为 IFC 通用实体或项目级语义，不替代上述报规对象角色。

## 5.2 载体策略结构

每个语义角色区分：

```json
{
  "roleId": "SITE_GREEN_OBJECT",
  "autoCarriers": [],
  "manualCarriers": [],
  "propertyTemplateIds": [],
  "ifcOwnerStrategy": ""
}
```

### `autoCarriers`

用于自动识别，必须保持严格。

### `manualCarriers`

用于用户明确指定后的合法载体。第一版绿地角色建议支持经过 Revit 2020 实机验证的：

- 建筑地坪；
- 面域；
- 楼板；
- 通用模型实例。

必须使用运行时确认过的 Revit CategoryKey 与 `ElementKind`，不能仅凭界面中文名称硬编码。

## 5.3 绿地属性模板

`SITE_GREEN_OBJECT` 至少关联：

```text
分类名称
投影面积
绿地类型
折算系数
```

其中：

| 属性 | 来源策略 |
|---|---|
| 分类名称 | 系统固定值“绿地” |
| 投影面积 | 批准的载体几何提取器；无法可靠提取则待填写 |
| 绿地类型 | 枚举选择 |
| 折算系数 | 正实数，人工确认或规则建议 |

绿地类型枚举：

```text
集中绿地
宅旁绿地
水域
屋顶绿地
口袋公园
道路绿地
附属绿地
其它绿地
```

## 6. 语义角色持久化

手动指定不能只在当前预览中有效。

新增：

```text
NativeStage02SemanticAssignmentStore
```

建议使用文档级 `DataStorage + Extensible Storage` 保存 canonical JSON，而不是把插件控制字段暴露为普通共享参数。

记录结构：

```json
{
  "schemaVersion": "1.0.0",
  "rulePackageId": "HBR-WUHAN-PLANNING",
  "rulePackageVersion": "1.0.0",
  "assignments": [
    {
      "elementUniqueId": "...",
      "roleId": "SITE_GREEN_OBJECT",
      "assignmentMode": "MANUAL",
      "assignedUtc": "...",
      "carrierCategory": "...",
      "carrierElementKind": "..."
    }
  ]
}
```

保存要求：

- 按 UniqueId 排序后 canonical 序列化；
- 保存 SHA-256；
- 写入后回读；
- 与 Stage02 参数写入放在同一可回滚事务边界中；
- 构件删除后保留为可清理的 stale record，不得误指向其他构件；
- 模型另存为时记录随 RVT 保存；
- 不使用本机 LocalAppData 保存构件语义，因为语义属于模型业务数据。

## 7. Stage02 写入流程

完整流程：

```text
读取当前 Revit 选择
→ 建立宽范围 Selection Inventory
→ 执行自动识别或读取手动角色
→ 校验 Stage01 模型类型与项目条件
→ 校验 manualCarrierPolicy
→ 生成角色与属性模板预览
→ 用户逐项确认或改写
→ 冻结 preview_hash
→ 写入前重建现场预览
→ 比较 preview_hash
→ 创建/合并固定 GUID 共享参数绑定
→ 写入系统固定值和可靠建议值
→ 保存语义角色记录
→ 回读参数和角色记录
→ 提交或整体回滚
```

不得：

- 为缺少可靠来源的业务值伪造默认值；
- 将所有选中构件强行绑定到任意角色；
- 因类别绑定是类别级操作，就把所有同类构件都标记为该语义；
- 在预览后模型发生变化时继续消费旧确认。

## 8. Revit 参数绑定边界

共享项目参数的类别绑定是类别级的，因此：

```text
给一个建筑地坪创建“绿地类型”参数
→ 该参数会对建筑地坪类别整体可见
```

但语义角色与实际导出范围是实例级的：

```text
只有 AssignmentStore 中标记为 SITE_GREEN_OBJECT 的实例
→ 才需要填写绿地属性
→ 才进入 Stage03 绿地对象导出
```

因此必须同时维护：

1. 类别级参数绑定；
2. 实例级语义角色记录。

不能仅依据“某构件上存在绿地参数”推断它就是绿地。

## 9. `SITE` 与场地对象的最终数据分层

## 9.1 唯一场地级数据

```text
SITE / IfcSite
```

Revit 代理载体：

- ProjectInformation；
- ProjectLocation；
- 项目基点/测量点；
- 共享坐标；
- Stage01 内部存储。

典型数据：

- 场地名称；
- 地块编号；
- 坐标与高程；
- 总用地面积；
- 绿地率；
- 建筑密度；
- 容积率；
- 场地级总体说明。

## 9.2 多个对象级数据

```text
SITE_GREEN_OBJECT
SITE_FIRE_FIELD
SITE_NET_LAND
……
```

Revit 载体：

- 建筑地坪；
- 面域；
- 楼板；
- 通用模型；
- 其他经过批准的模型构件。

典型数据：

- 每块绿地的面积、类型和折算系数；
- 每块消防场地的面积、名称和所属建筑；
- 每块规划用地对象的名称、面积和版本。

## 10. Stage03 H-IFC 集成原则

### 10.1 绿地对象

官方总平实例已证明绿地作为独立：

```text
IfcBuildingElementProxy
```

并挂接 `Pset_绿地信息属性集`。

因此第一实现目标为：

```text
Revit 选定载体
→ 导出对象 GlobalId
→ IfcBuildingElementProxy（或经官方实例验证的等价独立实体）
→ Pset_绿地信息属性集
```

### 10.2 唯一 `IfcSite`

`IfcSite` 继续承载整个场地的总体数据和汇总指标，不能替代各块绿地对象。

### 10.3 IFCFlux 兼容门禁

由于 IFCFlux MVD 与官方实例存在实体冲突，Stage03 报告必须同时输出：

- 官方实例一致性；
- IFCFlux MVD 一致性；
- 实际 attachment owner；
- 预期 owner；
- 冲突状态。

在未取得 IFCFlux 实机证据前，状态不得伪装为通过。

## 11. MCP 同步

MCP Stage02 入口必须与人工工作台共用同一服务。

建议扩展现有 Stage02 请求：

```json
{
  "scope": "current_selection",
  "identification_mode": "manual",
  "bulk_role_id": "SITE_GREEN_OBJECT",
  "overrides": [
    {
      "element_unique_id": "...",
      "role_id": "SITE_FIRE_FIELD"
    }
  ]
}
```

仍保持：

```text
preview
→ preview_hash
→ confirm=true
→ write
```

MCP 不得绕过：

- 载体许可；
- Stage01 条件；
- preview_hash 过期检查；
- 参数合同；
- 事务回读。

## 12. 测试设计

## 12.1 纯领域测试

覆盖：

- 宽范围选择 Inventory；
- 自动模式仍严格；
- 手动模式允许批准载体；
- 未批准载体保持阻断；
- 批量角色与逐项 override；
- 角色冲突；
- 项目条件未启用；
- canonical assignment JSON；
- stale UniqueId；
- preview hash 稳定性。

## 12.2 Revit 服务合同测试

覆盖：

- `AssignedRoleId` 不再恒为空；
- 当前选择中的建筑地坪不会在 Inventory 阶段被误报不存在；
- 手动绿地角色可生成属性预览；
- 参数绑定到正确 Revit 类别；
- 仅选中实例写入角色记录；
- 写入后回读；
- 故障整体回滚。

## 12.3 UI 合同测试

覆盖：

- 自动/手动切换；
- 批量语义类型下拉；
- 逐行改写；
- 构件类别、类型、角色和错误原因可见；
- `CUSTOM_ELEMENT_UNAVAILABLE` 不再承担类别不支持的含义；
- 确认写入只在预览有效时启用。

## 12.4 Stage03 fixture

至少覆盖官方总平实例中的：

```text
绿地:绿地:352971
Pset_绿地信息属性集
投影面积
绿地类型
折算系数
```

并验证 Owner、Pset、Property、类型和值的 exact 回读。

## 13. 验收标准

使用截图中的建筑地坪进行人工验收：

1. 在 Revit 中选择“建筑地坪｜集中绿地”；
2. Stage02 选择“当前 Revit 选择”；
3. 切换为“手动指定”；
4. 批量语义类型选择“绿地”；
5. 成功生成预览，不再出现 `CUSTOM_ELEMENT_UNAVAILABLE`；
6. 预览显示分类名称、投影面积、绿地类型、折算系数；
7. 确认写入后，固定 GUID 参数出现在 Revit；
8. 该实例的语义角色保存为 `SITE_GREEN_OBJECT`；
9. 关闭并重新打开 Revit 后，角色仍能回读；
10. 同类别另一个未分配角色的建筑地坪不会被误当成绿地；
11. 多选时可统一指定，并可逐项改写；
12. Stage01 未启用“绿地”条件时明确阻断；
13. Stage03 能定位该构件导出的独立 IFC Owner；
14. 最终 IFCFlux 状态仍以真实人工检查为准。

## 14. 非目标

本版本不包含：

- 任意 Pset 自由组合器；
- 用户自行输入任意 IFC 实体；
- 对链接模型构件写入；
- 对导入 CAD 对象直接写入；
- 模糊语义猜测；
- 自动更新系统；
- 未经证据验证的 IfcSite/IfcBuildingElementProxy 双写。

## 15. 实施拆分建议

### Phase A：Stage02 语义分配基础

- 宽范围 Selection Inventory；
- 手动角色请求模型；
- 批量 + override；
- AssignmentStore；
- UI 与 MCP；
- 参数预览和写入。

### Phase B：首批总平对象角色

- 绿地；
- 规划总用地；
- 规划净用地；
- 建筑基底；
- 消防场地；
- 区内道路。

### Phase C：Stage03 Owner 集成

- 官方样例 Owner 策略；
- GlobalId 对应；
- H-IFC 属性挂接；
- exact 回读；
- IFCFlux 人工验收证据。

---

## 设计决策摘要

```text
用户选择的不是“任意属性集”，而是“构件的报规语义类型”。

自动识别保持严格。
手动指定受 manualCarrierPolicy 约束。

批量统一指定。
支持逐个构件改写。

语义角色保存进 RVT。
共享参数按类别绑定，角色按实例保存。

SITE 是唯一场地总体。
绿地等是多个独立场地对象。

官方样例中的绿地使用独立 IFCBUILDINGELEMENTPROXY。
不把多块绿地静默压到唯一 IfcSite。
```
