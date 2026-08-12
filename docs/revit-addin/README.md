# BIMBaoGui Revit 2020 原生插件 + MCP

## 唯一 Revit 产品线

```text
产品版本：0.3.2
唯一开发分支：feat/revit-native-addin-mcp-v0.3
目标软件：Autodesk Revit 2020
```

本安装包同时包含人工工作台与 MCP 入口，不再维护单独的“非 MCP 版”。即使不配置任何 MCP Client，Revit Ribbon、DockablePane、Stage01 和 Stage02 仍可独立使用。

原生插件不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 GHA，只共同消费同一份权威 HBR 参考数据库。

## 两种使用入口

### 人工入口

```text
Revit Ribbon → 湖北BIM报规 → 报规工作台
```

### Agent / MCP 入口

```text
MCP Client
  → BIMBaoGui.McpServer.exe（stdio）
  → 当前用户 Named Pipe
  → Revit 内 MCP Bridge
  → RevitExternalEventDispatcher
  → 同一套 Stage01 / Stage02 服务
```

MCP Bridge 启动失败不会阻断 Ribbon、DockablePane 或人工 Stage01/02 操作。

## 01 文件初始化

### 项目条件是第一个必填步骤

打开 Stage01 后，左侧第一个目录固定为：

```text
项目条件（必填）
```

必须完成以下二选一声明：

1. 勾选一个或多个实际项目条件；
2. 勾选“无上述项目条件（已确认）”。

两种状态互斥：

- 勾选任一实际条件，会自动取消“无上述项目条件（已确认）”；
- 勾选“无上述项目条件（已确认）”，会自动清空全部实际条件；
- 全部未选择时，Stage01 校验和写入均被阻断；
- 同时选择实际条件与“无上述项目条件”时，校验拒绝写入；
- 旧版 RVT 如果所有条件均为 `false` 且没有明确声明，重新写入前必须补做一次声明；
- 旧版 RVT 已经勾选至少一个实际条件时，可继续按原数据读取。

Stage02 也会执行同一门禁。未完成项目条件声明时，不能生成 Stage02 预览。

### 其他 Stage01 功能

- 项目身份、子项、模型类型、坐标、高程、真北、单位和组织信息；
- 左侧目录 + 右侧连续滚动表单；
- 必填字段始终优先、连续显示；
- 每个目录的选填字段统一放入一个“选填项（共 N 项，已填写 M 项）”折叠区；
- 选填区默认收起，并在当前 Revit 会话内记住各目录的展开状态；
- 选填字段存在校验错误时自动展开；
- 已经包含模型构件的 RVT 也可以首次初始化；
- 已经存在 Stage01 初始化记录时，覆盖写入仍必须启用“允许重新初始化”；
- `X = 南北坐标`、`Y = 东西坐标`；
- Revit 单位、项目位置、项目信息和固定 GUID 参数写入；
- canonical JSON、SHA-256、Extensible Storage 和写入后回读；
- 整体事务回滚与单次 Undo；
- MCP 表单 Schema、读取、校验租约和确认写入。

## 02 构件与属性准备

- 全模型扫描或读取当前 Revit 选择；
- 数据库类别、ElementKind、精确别名或显式角色匹配；
- 禁止模糊包含、编辑距离或静默猜测；
- 字段状态：正确、待绑定、待写入、待填写、不适用、运行阻断和业务阻断；
- 确定性预览 JSON 与 SHA-256；
- 固定 GUID 的共享参数创建、实例/类型绑定和类别合并；
- 写入前重新生成预览并阻止过期确认；
- 参数级事务隔离、构件级原子事务和部分成功；
- 原生 WPF 构件列表、问题筛选、字段详情和确认写入；
- MCP `preview_hash` 一次性租约与确认写入。

## 状态与报告区域

- Stage01 与 Stage02 的详细状态区域固定为 96 px；
- 超长状态、阻断列表和失败报告在区域内部滚动；
- 工作台底部只显示固定高度的单行摘要；
- 长报告不会再把按钮、左侧目录或右侧表单挤出可视区域。

## H-IFC 验证边界

**Stage01、Stage02 的成功只能证明数据已写入 RVT 并完成回读，不能证明 H-IFC 已识别。**

当前安装包尚未完成 Stage03，因此暂不具备以下闭环：

```text
Revit 模型检测
→ Autodesk IFC4 RAW 导出
→ H-IFC 转译
→ H-IFC exact 回读
→ 属性路径与类型核对
→ 官方检查软件 / IFCFlux 验证
```

只有上述 Stage03 全流程通过后，才能宣称 H-IFC 可识别或报规检查闭环通过。

## MCP 工具

当前只提供以下 9 个受控工具：

```text
bimbaogui_list_revit_sessions
bimbaogui_get_document_status
bimbaogui_get_rule_package_identity
bimbaogui_stage01_get_form_schema
bimbaogui_stage01_read
bimbaogui_stage01_validate
bimbaogui_stage01_write
bimbaogui_stage02_preview
bimbaogui_stage02_write
```

不提供任意 C#、任意 Revit API、任意脚本、UI 模拟点击或任意 Transaction 工具。

写操作必须满足：

```text
Stage01：validate → validation_hash → confirm=true → write
Stage02：preview → preview_hash → confirm=true → write
```

租约有效期 30 分钟且只能消费一次。Stage02 写入前仍会重新扫描并比较预览 SHA-256。

`stage01_write` 仍兼容旧客户端传入的 `confirm_blank_project` 字段，但该字段已废弃并被忽略；首次初始化不再要求空模型。

## 安装

1. 关闭 Revit 2020。
2. 将 ZIP 完整解压到普通文件夹，不要直接在压缩包内运行。
3. 双击：

```text
Install.cmd
```

安装器使用当前用户目录，不要求管理员权限。覆盖安装 v0.3.2 时，会删除 BIMBaoGui MCP Server 目录中旧的语义版本目录，避免 `0.3.0`、`0.3.1`、`0.3.2` 叠罗汉。

安装位置：

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.McpContracts.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\install-evidence.json

%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.2\BIMBaoGui.McpServer.exe
%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.2\install-evidence.json
%LOCALAPPDATA%\BIMBaoGui\McpServer\mcp-server-config.json
```

启动 Revit 2020 后，MCP Bridge 会随插件加载，不要求先打开报规工作台。

## 检查 MCP 连接

保持 Revit 2020 开启，双击：

```text
McpProbe.cmd
```

退出状态：

```text
0 = 已连接一个 Revit Bridge
2 = 未发现 Revit Bridge
3 = 检测到多个 Revit 会话
4 = 技术错误
```

## 配置 MCP Client

安装器会生成：

```text
%LOCALAPPDATA%\BIMBaoGui\McpServer\mcp-server-config.json
```

将其中 `bimbaogui-revit` 节点复制到使用的 MCP Client 配置中。安装器不会擅自修改 ChatGPT、Codex、Claude、Hermes 或其他第三方客户端配置。

如果同时打开多个 Revit 2020，后续工具调用必须明确提供 `revit_process_id`，插件不会静默选择文档。

## 卸载

关闭 Revit 2020，双击：

```text
Uninstall.cmd
```

卸载器只删除 BIMBaoGui 的 Revit 插件目录、全部 BIMBaoGui MCP 语义版本目录、生成的通用配置和过期 Bridge discovery，不修改第三方 MCP Client 配置。

## 完整性校验

安装包根目录包含：

```text
SHA256SUMS.txt
```

安装脚本会再次比较 Revit 插件 DLL、MCP Contracts DLL 和 MCP Server EXE 的 SHA-256，并将安装结果写入 `install-evidence.json`。

## 注意事项

- 仅支持 Revit 2020；
- RVT 必须先保存，且不能为只读或族文档；
- 已有模型不影响首次初始化；
- 已初始化文件重新覆盖时仍需勾选“允许重新初始化”；
- Stage02 不会伪造没有可靠来源的业务值，只准备参数并标记“待填写”；
- 当前二进制未使用商业代码签名证书，Windows 或 Revit 可能显示未知发布者；
- 自动化验证覆盖编译、领域测试、MCP SDK 握手、安装、探针、哈希核验和卸载 smoke，但不能替代用户电脑上的 Revit 2020 GUI 实机验收；
- H-IFC 识别必须等待 Stage03 实现并通过真实导出和检查软件验证。

## 高级命令行方式

安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -SourceRoot .
```

探针：

```powershell
& "$env:LOCALAPPDATA\BIMBaoGui\McpServer\0.3.2\BIMBaoGui.McpServer.exe" --probe
```

卸载：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -Uninstall
```
