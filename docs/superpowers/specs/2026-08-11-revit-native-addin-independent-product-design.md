# Revit 原生报规插件独立产品设计

**状态：** 已批准并开始实施  
**日期：** 2026-08-11  
**开发分支：** `feat/revit-native-addin-v1`  
**目标环境：** Autodesk Revit 2020 / .NET Framework 4.8 / WPF  
**首个程序集：** `BIMBaoGui.RevitAddin.dll`

## 1. 产品定位

Revit 原生插件与现有 Grasshopper 插件是两个相对独立的产品实现：

```text
GHA 产品线
  Rhino 8 + Grasshopper + Rhino.Inside.Revit + Revit 2020

Revit 原生产品线
  Revit 2020 + Revit API + WPF + ExternalEvent
```

两条产品线不共享 UI、状态机、事务实现、预览对象或发布节奏。谁先完成真实 RVT 到合规 H-IFC 的完整闭环，谁先作为生产主入口；另一条产品线后续参考已验证的数据结论继续演进。

## 2. 唯一共享边界：HBR 参考数据库

两个产品共同消费同一机器权威源：

```text
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
```

共同数据库必须决定并锁定：

- `propertyId`；
- IFC Entity / PropertySet / Property；
- declared IFC type；
- canonical unit；
- Revit 参数 GUID、名称、StorageType 和 ParameterType；
- INSTANCE / TYPE；
- carrier role；
- Revit 类别与批准名称；
- requirement、condition 和 applicability；
- IFC write strategy 与 owner strategy。

每个发布物必须记录：

```text
productVersion
rulePackageId
rulePackageVersion
rulePackageSha256
```

允许 GHA 和 Revit 插件在同一时间使用不同数据库版本，但禁止任何产品维护第二份私有映射表或在代码中补写未进入权威数据库的映射。

## 3. 独立性约束

Revit 原生插件不得引用：

```text
Grasshopper.dll
RhinoCommon.dll
RhinoInside.Revit.dll
BIMBaoGui.Stage01.gha
```

Revit 原生插件不得复用 GHA 的：

- ComponentGuid；
- GH Data Tree；
- Goo / Param 类型；
- Boolean 上升沿触发模型；
- Rhino.Inside host callback；
- GH 卡片绘制代码；
- GHA 运行状态缓存。

可以参考已有实现、测试和实机证据，但必须在原生产品目录中形成可单独构建、安装、运行和验收的实现。

## 4. 分支与目录

长期产品支线固定为：

```text
feat/revit-native-addin-v1
```

首期目录：

```text
src/BIMBaoGui.RevitAddin/
installer/
docs/revit-addin/
tests/test_revit_addin_scaffold_contract.py
.github/workflows/build-revit-addin.yml
```

现有 `src/BIMBaoGui.Stage01/` 在本支线中默认只读。只有共同数据库、共同数据库生成器、共同数据库 Schema、manifest 和共同验证试件可以被两条产品线共同修改。

## 5. 原生宿主架构

### 5.1 Revit 启动入口

`App : IExternalApplication` 负责：

- 创建“湖北BIM报规”Ribbon Tab；
- 创建“报规工作台”Ribbon Panel；
- 注册打开工作台按钮；
- 注册 WPF DockablePane；
- Revit 关闭时释放 ExternalEvent。

### 5.2 DockablePane

原生工作台采用可停靠 WPF 页面，交互固定为：

```text
左侧：01 文件初始化 / 02 构件与属性准备 / 03 检测与 H-IFC
右侧：当前阶段的连续滚动内容
底部：当前文档、规则包和操作状态
```

首个垂直切片只提供：

- 三阶段导航；
- 当前规则数据库 packageId / version / SHA-256；
- 当前活动 RVT 的标题、路径、Revit 版本、保存状态、族/项目状态和只读状态；
- WPF 到 Revit API 的 ExternalEvent 调度闭环。

### 5.3 ExternalEvent

modeless WPF 不直接调用 Revit DB 修改 API。所有读取和写入请求必须经过：

```text
Workspace UI
  -> RevitRequestQueue
  -> ExternalEvent.Raise()
  -> IExternalEventHandler.Execute(UIApplication)
  -> Revit API service
  -> immutable result
  -> Workspace UI
```

首期队列只实现“读取当前文档快照”。后续 Stage01、Stage02、Stage03 在同一队列中新增明确的 request kind，不允许从后台线程直接访问 Revit Document。

## 6. 规则包生成与加载

原生插件独立编译自己的嵌入式 `HBR_RulePack.hbrpack`，但输入仍是同一个权威 JSON、同一个编译器和同一个 compatibility baseline：

```text
hbr_rule_source.v1.json
  -> tools/build_hbr_rulepack.py
  -> HBR_RulePack.hbrpack
  -> BIMBaoGui.RevitAddin.dll embedded resource
```

原生插件首期必须独立验证：

- magic 为 `HBRP`；
- format version 为 1；
- payloadLength 合法；
- 无尾随字节；
- payload SHA-256 与 header 一致；
- payload 内 packageId / packageVersion 非空。

## 7. 阶段功能目标

### 7.1 Stage01 文件初始化

原生插件自行实现：

- 项目身份和子项身份；
- 模型类型与项目条件；
- X = Northing = 南北坐标；
- Y = Easting = 东西坐标；
- 高程与真北；
- 计量单位；
- 规划目标；
- Extensible Storage；
- 共享参数投影；
- 写入后回读；
- 保存、关闭、重开持久性。

原生实现可以继续读取 GHA 已经写入的现有 Stage01 Storage，但不要求复用 GHA 代码。

### 7.2 Stage02 构件与属性准备

原生插件不保留 GHA v0.9 的选择端口模式，直接按原生交互实现：

- 默认全模型扫描；
- 当前选择或用户筛选作为可选范围；
- 类别 + element kind + 批准族名/类型名的确定性角色判定；
- 只读预览；
- 参数定义按 GUID 独立准备；
- 每个构件独立事务；
- 同一构件全部字段原子提交；
- 整体允许部分成功；
- 失败项可定位、隔离、重试；
- 重复执行幂等。

### 7.3 Stage03 检测与 H-IFC

原生插件独立实现：

- 全模型检查；
- 业务 blocker 与技术 fatal 分离；
- Strict / Force；
- Autodesk IFC4 RAW；
- RAW 不可变；
- H-IFC 转译；
- exact reread；
- fields JSON；
- failure report；
- runId 与 SHA-256 证据链。

现有 GHA 实机中的 `INVALID_IFC / TRANSLATE-IFC` 只能作为诊断输入，原生插件不得复制一个仍未闭环的完成结论。

## 8. 数据库治理

数据库更新流程固定为：

```text
实际模型或官方工具证据
  -> 形成可追溯 evidence
  -> 修改唯一规则源
  -> Schema / semantics / compatibility 检查
  -> 更新 manifest
  -> 升级 packageVersion
  -> 合入 main
  -> 各产品自行选择升级时间
```

任何产品发现映射缺口时，允许暂时显示“规则未覆盖”或“能力未实现”，不允许在产品代码内加入隐性补丁。

## 9. 错误与诊断

原生插件必须区分：

```text
BUSINESS_BLOCKER
TECHNICAL_FATAL
STALE_RESULT
PARTIAL_SUCCESS
USER_CANCELLED
```

日志不得记录不必要的敏感业务值。报告必须绑定：

- 产品版本；
- rule package identity；
- Document identity；
- operation/run identity；
- 阶段；
- 安全错误码；
- 产物路径与 SHA-256。

## 10. 测试策略

### 10.1 静态合同

Python 合同锁定：

- 原生项目不引用 GH/Rhino；
- 原生项目使用同一规则源；
- Ribbon、DockablePane、ExternalEvent 和 manifest 存在；
- CI 构建原生项目。

### 10.2 纯逻辑测试

后续新增独立 .NET 测试项目，正常 ProjectReference 原生 Domain/RuleEngine，不再通过复制生产 `.cs` 文件测试。

### 10.3 Revit 实机

必须在 Revit 2020 验证：

- Ribbon 与 DockablePane；
- ExternalEvent；
- 文档切换；
- Stage01 保存重开；
- Stage02 全模型与部分成功；
- Stage03 Strict/Force 和三件套；
- IFCFlux / 官方检查软件。

## 11. 首个里程碑完成标准

原生插件基础架构里程碑只有在以下条件同时满足时完成：

1. 独立分支存在；
2. 原生项目不引用 GH/Rhino；
3. 规则包由唯一数据库编译并嵌入；
4. Ribbon 可打开 DockablePane；
5. WPF 通过 ExternalEvent 读取当前 RVT；
6. UI 显示 rule package identity 和文档状态；
7. 静态合同通过；
8. Windows CI 能 restore 和 Release build；
9. Revit 2020 实机启动证据完成。

自动化通过不替代第 9 项。
