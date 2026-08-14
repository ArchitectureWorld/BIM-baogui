# Revit 原生插件 MCP Bridge 设计

**状态：** 已批准，进入实施  
**日期：** 2026-08-11  
**开发分支：** `feat/revit-native-addin-mcp-v0.3`  
**基线提交：** `35fa0ca6a8b07ba86231ee8305020fb23dcdb7c2`  
**基线安装包：** `BIMBaoGui-Revit2020-Native-Stage01-Stage02-v0.2.0`  
**目标版本：** `0.3.0`  
**目标环境：** Autodesk Revit 2020 / .NET Framework 4.8 / WPF / .NET 8 self-contained MCP Server  
**MCP 协议基线：** `2025-11-25`  
**官方 C# SDK：** `ModelContextProtocol 1.3.0`

## 1. 目标

在不改变 Revit 原生插件现有 Stage01、Stage02、Ribbon、DockablePane、ExternalEvent、事务、参数 GUID、规则数据库和人工操作路径的前提下，增加一个标准 MCP 入口，使 Agent 可以通过受控工具调用现有业务能力。

目标调用链固定为：

```text
MCP Client
  -> BIMBaoGui.McpServer.exe（stdio）
  -> 本机 Named Pipe
  -> Revit 内 MCP Bridge
  -> RevitExternalEventDispatcher
  -> 现有 Stage01 / Stage02 服务
  -> Revit API
```

MCP 只是旁路适配层，不成为第二套报规业务实现。

## 2. 不回归约束

以下内容必须保持与 `v0.2.0` 一致：

- `src/BIMBaoGui.RevitAddin/Stage01/**` 的业务行为；
- `src/BIMBaoGui.RevitAddin/Stage02/**` 的业务行为；
- Stage01 Extensible Storage Schema GUID、字段和 payload；
- Stage01 坐标语义：`X = 南北坐标`、`Y = 东西坐标`；
- Stage02 精确角色匹配、预览 SHA-256、写入前重新预览；
- Stage02 参数级事务、构件级原子事务和部分成功；
- HBR 规则源、规则包版本和规则包 SHA-256；
- 现有 Ribbon、DockablePane、按钮、人工 Stage01/02 操作；
- 现有安装目录和用户级安装方式；
- 原生插件不引用 Grasshopper、RhinoCommon、Rhino.Inside 或 GHA。

MCP 支线不得为了适配协议而修改 Stage01/02 业务判定。若 MCP 输入无法映射到现有业务服务，必须返回阻断，不得在 MCP 层补一套规则。

## 3. 组件架构

### 3.1 `BIMBaoGui.McpContracts`

新增 `netstandard2.0` 纯合同程序集：

```text
src/BIMBaoGui.McpContracts/
```

职责：

- Named Pipe 请求与响应 DTO；
- Bridge protocol version；
- session discovery DTO；
- MCP 工具输入/输出中跨进程共享的 DTO；
- 错误码和状态枚举；
- 长度前缀消息帧的纯字节合同。

约束：

- 不引用 Revit API；
- 不引用 MCP SDK；
- 不引用 WPF；
- 不包含业务规则；
- 不记录业务字段值到日志。

### 3.2 Revit 内 `McpBridge`

新增：

```text
src/BIMBaoGui.RevitAddin/McpBridge/
```

职责：

- Revit 启动后创建当前进程专属 Named Pipe；
- 写入当前用户专属 session discovery 文件；
- 校验 token、协议版本、消息大小和 method；
- 将 MCP 请求转换为现有 `RevitExternalEventDispatcher` 请求；
- 等待 immutable result；
- 返回结构化响应；
- 维护 Stage01 validation lease 与 Stage02 preview lease；
- Revit 关闭时停止监听并删除 discovery 文件。

Bridge 不直接写 Revit Document。所有 Revit API 读取和修改仍必须经过 `ExternalEvent`。

### 3.3 `BIMBaoGui.McpServer`

新增独立 `net8.0` 可执行项目：

```text
src/BIMBaoGui.McpServer/
```

职责：

- 使用官方 `ModelContextProtocol 1.3.0`；
- 使用标准 `stdio` transport；
- 所有日志只写 `stderr`；
- 发现本机 Revit Bridge session；
- 通过 Named Pipe 调用 Bridge；
- 暴露版本化 MCP tools；
- 不加载 Revit API；
- 不直接读取或修改 RVT；
- 不复制 HBR 规则数据库。

发布方式：

```text
win-x64
self-contained
single-file
PublishTrimmed=false
```

用户无需单独安装 .NET Runtime。

## 4. 本机 Session 与安全

### 4.1 Discovery 目录

每个 Revit 进程写入：

```text
%LOCALAPPDATA%\BIMBaoGui\Revit2020\bridges\<processId>.json
```

内容固定包含：

```text
bridgeProtocolVersion
processId
pipeName
sessionToken
revitVersion
pluginVersion
rulePackageId
rulePackageVersion
rulePackageSha256
startedUtc
```

### 4.2 Pipe 名称

```text
BIMBaoGui.Revit2020.<processId>.<randomNonce>
```

### 4.3 安全规则

- Named Pipe ACL 只允许当前 Windows 用户；
- discovery 目录位于当前用户 LocalAppData；
- session token 使用 256-bit CSPRNG；
- 每个请求必须携带 token；
- token 不进入普通日志；
- Pipe 只接受本机连接；
- 单条请求上限 8 MiB；
- 单条响应上限 32 MiB；
- 未知 method fail-closed；
- 协议版本不一致返回 `BRIDGE_PROTOCOL_MISMATCH`；
- 多个 Revit session 同时存在且调用未指定 processId 时返回 `MULTIPLE_REVIT_SESSIONS`，不静默选择。

## 5. Bridge 消息合同

使用 4-byte little-endian 长度前缀 + UTF-8 JSON：

```text
[length:int32][json bytes]
```

请求：

```json
{
  "protocol_version": "1.0",
  "request_id": "uuid",
  "session_token": "secret",
  "method": "stage02.preview",
  "timeout_ms": 120000,
  "payload": {}
}
```

响应：

```json
{
  "protocol_version": "1.0",
  "request_id": "uuid",
  "success": true,
  "status": "OK",
  "error_code": "",
  "message": "",
  "payload": {}
}
```

请求与响应必须保持同一个 `request_id`。

## 6. MCP Tools

工具名使用稳定 snake_case，不使用任意代码执行工具。

### 6.1 只读工具

```text
bimbaogui_list_revit_sessions
bimbaogui_get_document_status
bimbaogui_get_rule_package_identity
bimbaogui_stage01_get_form_schema
bimbaogui_stage01_read
bimbaogui_stage01_validate
bimbaogui_stage02_preview
```

### 6.2 写入工具

```text
bimbaogui_stage01_write
bimbaogui_stage02_write
```

禁止暴露：

```text
execute_csharp
execute_revit_api
run_script
click_ui
arbitrary_transaction
```

## 7. 工具语义

### 7.1 `bimbaogui_list_revit_sessions`

返回所有可连接的 Revit 2020 session：

- processId；
- pluginVersion；
- rule package identity；
- pipe reachable；
- startedUtc。

不访问 Revit Document。

### 7.2 `bimbaogui_get_document_status`

输入：

```text
revit_process_id（多 session 时必填）
```

返回现有 `CurrentDocumentSnapshot` 的等价结构：

- 是否存在活动文档；
- 标题和路径；
- Revit 版本；
- 项目/族文档；
- 保存状态；
- 只读状态。

### 7.3 `bimbaogui_get_rule_package_identity`

返回插件当前实际嵌入规则包：

```text
packageId
packageVersion
rulePackageSha256
pluginVersion
```

### 7.4 `bimbaogui_stage01_get_form_schema`

返回原生 Stage01 当前字段目录、数据类型、必填状态、示例、枚举、项目条件和模型类型。只从现有 `NativeRuleCatalog` 投影，不维护 MCP 私有表单定义。

### 7.5 `bimbaogui_stage01_read`

调用现有 `NativeStage01RevitReadService.Read`，返回：

- 初始化状态；
- payload JSON；
- payload SHA-256；
- FileGuid；
- workflow version；
- 回读消息。

### 7.6 `bimbaogui_stage01_validate`

输入：

```text
payload_json
revit_process_id
```

流程：

1. 使用现有 payload codec 解码；
2. 使用现有 Stage01 validator 校验；
3. 对 canonical payload 计算 validation hash；
4. 在 Revit Bridge 内保存 30 分钟 validation lease；
5. 返回 errors、warnings、canonical payload 和 validation hash。

不写 Revit。

### 7.7 `bimbaogui_stage01_write`

输入：

```text
validation_hash
confirm=true
confirm_blank_project
allow_reinitialize
revit_process_id
```

流程：

1. `confirm` 必须为 `true`；
2. validation lease 必须存在且未过期；
3. 调用现有 `NativeStage01RevitService.Execute`；
4. 现有预检、事务、回读和回滚逻辑保持不变；
5. lease 一次消费后失效；
6. 返回现有 write result 的结构化投影。

### 7.8 `bimbaogui_stage02_preview`

输入：

```text
scope = full_model | current_selection
revit_process_id
```

流程：

1. 调用现有 `NativeStage02RevitService.CreatePreview`；
2. 保存 preview + resolved request 的 30 分钟 lease；
3. 返回 preview hash、summary、元素与字段状态；
4. 不写 Revit。

### 7.9 `bimbaogui_stage02_write`

输入：

```text
preview_hash
confirm=true
revit_process_id
```

流程：

1. `confirm` 必须为 `true`；
2. preview lease 必须存在且未过期；
3. 调用现有 `NativeStage02RevitWriteService.Execute`；
4. 该服务继续重新生成预览并比较 SHA-256；
5. 参数级事务、构件级事务、回读和部分成功保持不变；
6. lease 一次消费后失效；
7. 返回 success、partialSuccess、requiresNewPreview、计数和消息。

## 8. 并发与超时

- Bridge 使用一个监听循环，可接受多个顺序连接；
- 同一 Revit 进程的 Revit API 请求仍由现有 ExternalEvent queue 串行执行；
- WPF 与 MCP 共用该 queue，不允许并发启动两个 Revit Transaction；
- MCP Server 每次工具调用建立短连接；
- 默认超时：
  - status / identity / schema：15 秒；
  - Stage01 read / validate：30 秒；
  - Stage01 write：120 秒；
  - Stage02 preview：120 秒；
  - Stage02 write：300 秒；
- 超时只取消等待，不允许从后台线程中断正在执行的 Revit Transaction；
- Revit 操作完成后结果可以被安全丢弃，但不得留下半提交状态。

## 9. 错误码

至少包含：

```text
REVIT_NOT_CONNECTED
MULTIPLE_REVIT_SESSIONS
REVIT_SESSION_NOT_FOUND
BRIDGE_PROTOCOL_MISMATCH
BRIDGE_AUTH_FAILED
BRIDGE_MESSAGE_TOO_LARGE
BRIDGE_TIMEOUT
BRIDGE_BUSY
UNKNOWN_METHOD
INVALID_ARGUMENT
CONFIRMATION_REQUIRED
LEASE_NOT_FOUND
LEASE_EXPIRED
STALE_RESULT
BUSINESS_BLOCKER
TECHNICAL_FATAL
PARTIAL_SUCCESS
```

MCP Server 把业务失败作为结构化工具结果返回，不把可预期业务阻断伪装为进程崩溃。

## 10. Revit 启停行为

`App.OnStartup` 顺序：

1. 保持现有 Ribbon 与 DockablePane 注册；
2. 初始化现有 `RevitExternalEventDispatcher`；
3. 尝试启动 MCP Bridge；
4. MCP Bridge 启动失败时记录安全错误，但 Revit 插件仍返回 `Result.Succeeded`；
5. 人工 Stage01/02 功能不得因 MCP Bridge 故障不可用。

`App.OnShutdown` 顺序：

1. 停止 MCP Bridge；
2. 删除当前进程 discovery 文件；
3. 释放现有 ExternalEvent。

## 11. 安装与客户端配置

安装包新增：

```text
BIMBaoGui.RevitAddin/BIMBaoGui.McpContracts.dll
BIMBaoGui.McpServer/BIMBaoGui.McpServer.exe
McpProbe.cmd
mcp-server-config.example.json
```

安装器继续安装 Revit add-in，并把 MCP Server 放入：

```text
%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.0\
```

安装器生成包含绝对路径的：

```text
%LOCALAPPDATA%\BIMBaoGui\McpServer\mcp-server-config.json
```

通用配置结构：

```json
{
  "mcpServers": {
    "bimbaogui-revit": {
      "command": "C:\\...\\BIMBaoGui.McpServer.exe",
      "args": []
    }
  }
}
```

安装器不擅自修改第三方 MCP Client 的全局配置文件。

## 12. 测试策略

### 12.1 非回归门禁

新增 `v0.2.0` 功能基线清单，锁定 Stage01/02 生产文件 SHA-256。MCP 支线若改变这些文件，CI 必须失败，除非先形成独立业务变更设计并显式升级基线。

### 12.2 合同测试

Python 合同锁定：

- MCP 项目存在且目标为 `net8.0`；
- 官方 SDK 固定为 `1.3.0`；
- MCP Server 使用 stdio；
- 日志写 stderr；
- Revit Bridge 使用 Named Pipe、token 和 current-user ACL；
- 所有写工具要求 `confirm` 和 lease hash；
- 不存在任意代码执行工具；
- 原生插件仍不引用 GH/Rhino；
- 安装包包含 MCP Server、contracts、配置和 probe。

### 12.3 .NET 单元测试

- frame length 与 UTF-8 JSON round-trip；
- 超大消息拒绝；
- session discovery；
- token 校验；
- multiple-session fail-closed；
- lease create/read/consume/expire；
- MCP 工具参数校验；
- fake pipe bridge integration；
- tool list 固定为批准的 9 个工具。

### 12.4 Windows CI

必须完成：

1. 现有 Stage01/02 全部测试；
2. MCP contracts tests；
3. MCP Server tests；
4. Revit add-in Release build；
5. MCP Server self-contained publish；
6. `--probe` 无 Revit 时返回结构化 `REVIT_NOT_CONNECTED`；
7. fake bridge 集成测试；
8. 安装、哈希、配置生成与卸载 smoke；
9. 安装包结构和 SHA-256 清单验证。

### 12.5 Revit 2020 实机

自动化不替代以下实机验收：

- 不打开工作台时 MCP 也能读取文档状态；
- MCP Bridge 故障不影响人工工作台；
- 人工 Stage01/02 与 MCP Stage01/02 结果一致；
- MCP Stage02 preview 后修改模型，write 返回 stale；
- 多 Revit 实例时不静默选错文档；
- 保存、关闭、重开后 Stage01 仍可读取；
- 卸载后 Revit add-in、MCP Server 和 discovery 文件清理正确。

## 13. 完成标准

`v0.3.0` 只有在以下条件同时满足时才可交付：

1. `v0.2.0` Stage01/02 功能基线未漂移；
2. Revit 人工入口保持可用；
3. 9 个批准 MCP tools 可被标准 MCP Client 枚举；
4. 只读工具可在 Revit 工作台未打开时调用；
5. 写工具必须经过 confirm + lease + 现有业务门禁；
6. MCP Server 为可直接运行的 self-contained EXE；
7. 安装包可双击安装和卸载；
8. Windows CI 全绿、0 warning / 0 error；
9. 产物 SHA-256 与安装证据完整；
10. Revit 2020 实机验收结论明确记录。
