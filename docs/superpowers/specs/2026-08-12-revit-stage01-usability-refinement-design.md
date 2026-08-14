# Revit Stage01 实机验收问题修订设计

日期：2026-08-12  
目标分支：`feat/revit-native-addin-mcp-v0.3`  
回灌分支：`feat/revit-native-addin-v1`

## 1. 目标

在不改变权威 HBR 规则数据库、Stage01 数据合同、固定参数 GUID、Stage02 行为和 MCP 工具语义的前提下，解决 Revit 2020 实机验收中发现的三个问题：

1. 首次初始化不再要求空模型，也不再扫描并阻断已有模型构件；
2. 状态/报告文本长度不再挤压表单和操作区；
3. 每个目录中的必填项优先显示，选填项默认折叠为一个统一区域。

本次属于 Revit 原生插件交互与 Stage01 前置门禁调整，不修改 GHA 产品线。

## 2. 已确认的问题根因

### 2.1 首次初始化被模型内容阻断

当前实现针对 `NoRecord` 状态同时执行：

- 要求 `ConfirmBlankProject = true`；
- 调用 `NativeStage01BlankModelGate.FindBlockingElements(document)`；
- 只要发现正式模型构件，就返回 `MODEL_NOT_BLANK`。

该逻辑把“文件初始化”错误地限制为“只能在空模型中执行”。实际工作流允许对已有模型补充报规初始化信息，因此该门禁应取消。

### 2.2 报告区无限增高

当前 Stage01 页面和外层 `WorkspaceControl` 各自显示一份状态文本，并且对应 Grid 行均使用 `Auto` 高度。长状态信息会：

- 在两个位置重复出现；
- 按字符数量持续增高；
- 压缩中间 `*` 高度的表单工作区；
- 最终把目录、输入项和操作控件挤出可视范围。

### 2.3 必填与选填字段平铺

当前 `NativeStage01View.RenderForm()` 按数据库顺序逐项渲染，必填项与选填项交错，导致用户无法优先完成强制信息。

## 3. 设计方案

## 3.1 取消首次初始化空模型门禁

### 行为变化

首次初始化时，只保留以下前置条件：

- 当前存在活动文档；
- Revit 版本为 2020；
- 当前为项目文档而不是族文档；
- RVT 已保存；
- RVT 非只读；
- Stage01 领域校验通过；
- Extensible Storage 未损坏且不是未来版本。

以下两项正式取消：

- “确认当前文件尚未开始正式建模”复选框；
- 对现有模型构件的扫描和阻断。

### 仍然保留的覆盖保护

若 RVT 已经存在当前版本 Stage01 初始化记录，仍然必须显式勾选“允许重新初始化”。

原因：是否存在模型与是否允许覆盖已有初始化数据是两件不同的事。前者取消，后者继续保留。

### 兼容策略

- `NativeStage01WriteRequest.ConfirmBlankProject` 在内部和 MCP DTO 中暂时保留一个兼容周期，但不再影响业务判定；
- MCP 工具输入中的 `confirm_blank_project` 继续允许传入，标记为 deprecated，并被忽略；
- 既有客户端不会因为字段消失而报错；
- 新版原生 UI 不再显示该复选框；
- 删除 `NativeStage01BlankModelGate.cs`、`NativeStage01DocumentState.BlockingElements`、`BLANK_CONFIRMATION_REQUIRED` 和 `MODEL_NOT_BLANK` 生产逻辑及其旧测试；不保留不可达的空模型门禁代码。

## 3.2 状态与报告区域改为固定高度

### 页面内详细状态

Stage01 和 Stage02 各自保留一个详细状态区域，但改为：

- 固定高度 96 px；
- `ScrollViewer` 包裹 `TextBlock`；
- 垂直滚动条按需出现；
- 水平方向自动换行；
- 长报告不再改变页面布局高度。

### 外层全局状态

`WorkspaceControl` 底部状态改为单行摘要条：

- 固定高度 32 px；
- `TextTrimming = CharacterEllipsis`；
- 只显示当前阶段和简要结果；
- 不再重复承载完整报告内容。

Stage01/Stage02 页面内部保留完整状态，外层只承担导航级摘要，从而消除双份长报告同时膨胀的问题。

### 行布局约束

- 标题和操作区保持 `Auto`；
- 中间表单/构件区保持 `*`；
- 详细状态区使用固定高度；
- 外层全局状态条使用固定高度。

无论报告文本增长到多少字符，操作按钮、目录和表单区都必须继续可见。

## 3.3 选填项采用统一折叠区（方案 A）

### 默认显示顺序

每个 Stage01 左侧目录对应的右侧表单按以下顺序显示：

1. 目录标题；
2. 组织目录的组织切换工具栏（如适用）；
3. 所有必填字段；
4. 一个统一的选填项折叠区。

### 折叠区标题

统一使用：

```text
选填项（共 N 项，已填写 M 项）
```

其中：

- `N` 为当前目录的选填字段总数；
- `M` 为当前目录中已有非空值的选填字段数；
- 对组织字段，`M` 按当前组织记录计算。

### 展开规则

- 每个目录第一次进入时默认收起；
- 用户展开或收起后，在当前 Revit 会话内记住该目录的状态；
- 切换目录再返回时恢复之前状态；
- 读取 RVT 后，即便已有选填值，也保持默认/记忆状态，不强制展开；
- 若校验结果中存在选填字段错误，则对应目录的选填区自动展开；
- 必填字段永远不进入折叠区。

### 特殊目录

- `项目条件` 目录保持现有勾选列表，不纳入选填折叠逻辑；
- Deferred/ReadOnly 字段仍按其 required 判定归类；若非必填，则进入选填折叠区；
- 没有选填字段的目录不显示空折叠区。

## 4. 不变内容

本次不修改：

- HBR RulePack ID、版本和 SHA-256；
- Stage01 canonical JSON；
- Payload schema version；
- Extensible Storage Schema GUID 与字段名；
- FileGuid 生成和复用规则；
- Revit 项目坐标、单位、项目位置写入；
- `X = 南北坐标`、`Y = 东西坐标`；
- 固定共享参数 GUID；
- Stage01 写入事务、回读和失败回滚；
- 已初始化文件的“允许重新初始化”门禁；
- Stage02 全模型扫描、预览哈希和部分成功事务；
- 现有 9 个 MCP 工具名称；
- MCP Named Pipe、ExternalEvent 和一次性确认租约。

## 5. 数据流

### 人工首次初始化

```text
填写/读取表单
→ 领域校验
→ 文档状态与 Storage 校验
→ 不扫描模型构件
→ 写入事务
→ 回读
→ 固定高度状态区显示结果
```

### MCP 首次初始化

```text
stage01_validate
→ validation_hash
→ stage01_write(confirm=true)
→ 忽略兼容字段 confirm_blank_project
→ 复用同一 Stage01 预检与写入服务
```

### 表单渲染

```text
ActiveFields
→ IsRequired 分组
→ 必填字段直接渲染
→ 选填字段进入单一 Expander
→ 校验错误驱动自动展开
```

## 6. 错误处理

- 存储损坏、未来版本、未保存、只读、族文档和领域校验错误仍 fail-closed；
- 取消模型扫描后，不再生成包含大量构件名称的 `MODEL_NOT_BLANK` 报告；
- 长错误报告只在固定高度滚动区域内显示；
- 外层全局摘要仅显示短结果，不复制完整异常详情；
- 选填字段错误必须可见，通过自动展开而不是隐藏在折叠区中。

## 7. 测试设计

### 7.1 门禁测试

- 已有模型构件且无 Stage01 记录，只要领域与文档状态校验通过，就允许初始化；
- 首次初始化不再要求 `confirmBlankProject`；
- `Current + allowReinitialize=false` 仍阻断；
- Corrupt、UnsupportedFuture、ReadOnly、Unsaved、FamilyDocument 行为保持不变；
- `NativeStage01RevitService` 不再调用或引用 `NativeStage01BlankModelGate`。

### 7.2 UI 合同测试

- Stage01 UI 不再出现“确认当前文件尚未开始正式建模”；
- Stage01/Stage02 详细状态区包含固定高度 `ScrollViewer`；
- Workspace 全局状态条固定高度并启用字符省略；
- 页面中间工作区仍为 `GridUnitType.Star`；
- 10,000 字符状态文本不会改变表单工作区的最小可见性。

### 7.3 折叠区测试

- 必填字段全部位于 Expander 外部；
- 所有选填字段只进入一个 Expander；
- Expander 默认收起；
- 标题正确显示总数和已填写数；
- 每个目录独立记忆展开状态；
- 选填字段出现校验错误时自动展开；
- 项目条件目录不显示选填 Expander；
- 组织目录按当前组织记录计算“已填写 M 项”。

### 7.4 回归测试

- Stage01 canonical JSON、Payload hash、Storage GUID、参数 GUID 不变；
- Stage02 领域测试与 Revit 合同测试全部通过；
- MCP stdio 工具发现仍为 9 个工具；
- `stage01_write` 旧客户端携带 `confirm_blank_project` 时仍可调用；
- 非 MCP 与 MCP 两个安装包均能完成安装、卸载和哈希校验。

## 8. 发布与分支策略

1. 在 `feat/revit-native-addin-mcp-v0.3` 完成实现和完整 CI；
2. 生成新的 MCP 安装包；
3. 将 Stage01 门禁与 UI 修订提交回灌到 `feat/revit-native-addin-v1`；
4. 非 MCP 分支不携带任何 MCP Bridge 或 MCP Server 文件；
5. 两个安装包的 Stage01 人工交互和写入结果保持一致。

## 9. 验收标准

### 场景 A：已有模型的首次初始化

- 打开一个已包含模型构件、尚未初始化的已保存 RVT；
- 不出现空模型确认复选框；
- 不显示模型构件阻断列表；
- 完成必填项后可直接“写入并回读”。

### 场景 B：超长报告

- 构造超过 10,000 字符的状态或错误信息；
- 报告区出现内部滚动条；
- 报告区高度不增长；
- 顶部按钮、左侧目录和右侧表单仍可见；
- 外层状态条只显示单行摘要。

### 场景 C：必填优先

- 进入任一包含必填和选填字段的目录；
- 必填项直接显示且连续排列；
- 选填项默认折叠为单一区域；
- 展开后显示全部选填项；
- 切换目录再返回时保持本次会话中的展开状态；
- 选填项校验错误时自动展开。

## 10. 明确不包含的范围

本次不开发 Stage03、IFC4 RAW、H-IFC enrichment 或官方检查软件闭环；不修改权威映射数据库；不重构 Stage02；不增加新的 MCP 工具。