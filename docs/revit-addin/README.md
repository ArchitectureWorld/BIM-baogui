# BIMBaoGui Revit 2020 原生插件 + MCP

## 唯一 Revit 产品线

```text
产品版本：0.4.3
唯一开发分支：feat/revit-native-total-plan-phase1-v0.4.3
目标软件：Autodesk Revit 2020
```

本安装包同时包含人工工作台与 MCP 入口，不再维护单独的“非 MCP 版”。即使不配置任何 MCP Client，Revit Ribbon、DockablePane、Stage01、Stage02 和 Stage03 均可独立使用。

原生插件不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 GHA，只共同消费同一份权威 HBR 参考数据库。

工作台顶部的插件版本、构建号、Commit 和 DLL 路径均来自当前 Revit 进程实际加载的 `BIMBaoGui.RevitAddin.dll`。GitHub Actions 构建写入真实 Build Number 与 Commit SHA；本地开发构建显示 `Build local / Commit unknown`。

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
  → 同一套 Stage01 / Stage02 / Stage03 服务
```

MCP Bridge 启动失败不会阻断 Ribbon、DockablePane 或人工工作流。

# 01 文件初始化

## 项目条件是第一个必填步骤

打开 Stage01 后，左侧第一个目录固定为：

```text
项目条件（必填）
```

必须完成以下二选一声明：

1. 勾选一个或多个实际项目条件；
2. 勾选“无上述项目条件（已确认）”。

两种状态互斥。全部未选择时，Stage01 校验和写入均被阻断；旧版 RVT 所有条件均为 `false` 且没有明确声明时，重新写入前必须补做一次声明。Stage02 和 Stage03 也执行同一门禁。

## Payload 0.9.1 与现场值对账

v0.4.1 将 Stage01 Payload 从 `0.9.0 → 0.9.1`。检测到规范且哈希有效的 `0.9.0` 记录时，插件只生成 `0.9.1` 内存候选并显示“等待迁移确认”；读取动作不会改写原 Storage。只有用户点击“写入并回读”，或 MCP 按 `validate → confirm=true → write` 显式提交后，才会发布新的 `0.9.1` canonical Payload。

不同字段采用各自的权威来源：

- 项目名称、项目编号、X（南北）、Y（东西）、高程和真北从当前 Revit 现场读取；
- 子项、模型类型、项目条件、参建组织和规划目标以 Stage01 Payload 为业务权威；
- 报规目标单位固定为 `m / m² / °`，当前 RVT 单位只用于现场对账；
- 已初始化文件读取时同时保留上次确认值与当前 RVT 值，存在差异时显示“现场值漂移”，不会静默覆盖任一侧；
- `Corrupt`、未来版本以及无法安全迁移的数据保持阻断，不用默认值掩盖错误。

Stage02 与 Stage03 只消费 `Current` 状态；处于“等待迁移确认”的文件不能继续进入后续阶段。

## 其他 Stage01 功能

- 项目身份、子项、模型类型、坐标、高程、真北、单位和组织信息；
- 左侧目录 + 右侧连续滚动表单；
- 必填字段连续优先显示，选填字段按目录统一折叠；
- 已有模型构件的 RVT 也可以首次初始化；
- 已初始化文件覆盖写入仍必须启用“允许重新初始化”；
- `X = 南北坐标`、`Y = 东西坐标`；
- canonical JSON、SHA-256、Extensible Storage、固定 GUID 参数和写入后回读；
- 整体事务回滚与单次 Undo。

# 02 构件与属性准备

- 全模型扫描或读取当前 Revit 选择；
- 数据库类别、ElementKind、精确别名或显式角色匹配；
- 禁止模糊包含、编辑距离或静默猜测；
- 字段状态：正确、待绑定、待写入、待填写、不适用、运行阻断和业务阻断；
- 确定性预览 JSON 与 SHA-256；
- 固定 GUID 的共享参数创建、实例/类型绑定和类别合并；
- 写入前重新生成预览并阻止过期确认；
- 参数级事务隔离、构件级原子事务和部分成功。

# 03 检测、H-IFC 与 IFCFlux 人工验收

Stage03 已形成可供实际测试的完整内部链路：

```text
当前 Revit 模型和固定 GUID 参数现场扫描
→ 严格模式 / 有理由的测试强制导出门禁
→ Autodesk IFC4 RAW 导出
→ H-IFC 属性集与属性补全
→ STEP 语法复读
→ Owner / Entity / Pset / Property / IFC 类型 / 值精确回读
→ 输出 H-IFC、fields.json、validation.json 和 IFCFlux 检查清单
→ IFCFlux 人工检测
```

## 严格模式

严格模式是默认模式。存在技术阻断、载体不明确、字段未就绪、Owner 无法唯一确定、值或类型不符合规则时，不生成正式 H-IFC。

## 测试强制导出

人工工作台显示“测试强制导出（不会计为正常通过）”，选中后必须填写“强制原因（必填）”。它只允许跳过可诊断的业务阻断，不允许跳过 Revit 版本错误、文档不可用、Stage01 未初始化、项目条件未声明、RAW 导出失败、STEP 解析失败或 Owner 无法安全确定等技术错误。MCP `bimbaogui_stage03_scan` 同样必须为 `forced_test` 传入非空 `force_reason`。

测试强制导出成功仍固定记录 `is_test_export=true`、`counts_as_normal_export_pass=false` 和 `official_acceptance_status=PENDING`，不会把红色 checklist 改成正常通过。

强制测试文件名包含：

```text
FORCED_TEST_HIFC.ifc
```

无法安全挂接的字段不会被猜测写入，并在报告中标记为跳过。

## 导出位置

Stage03 读取当前已保存的 Revit 模型后，导出根目录默认设为该 `.rvt` 文件所在文件夹。用户可以直接编辑路径或通过“浏览”选择其他文件夹；路径在输入框失去焦点、浏览确认、开始导出或点击“打开导出位置文件夹”时保存。

该记录不是全局“上一次路径”，而是按 Revit 模型规范化完整路径分别保存。例如 A 模型与 B 模型可以各自保持不同的导出目录。记录位于本机：

```text
%LOCALAPPDATA%\BIMBaoGui\RevitAddin\stage03-output-directories.json
```

“打开导出位置文件夹”始终针对当前模型配置的导出根目录；文件夹尚不存在时会先创建，因此无需等到成功导出后才能打开。

## 输出文件

每次导出创建一个独立运行目录，至少包含：

```text
*_RAW.ifc
*_HIFC.ifc 或 *_FORCED_TEST_HIFC.ifc
*_fields.json
<scan_hash>-validation.json
*_IFCFlux_checklist.md
```

每次完成求值且报告目录可写的扫描，还会在导出根目录生成 `<scan_hash>-stage03-scan-evidence.json`；业务红项阻止导出时也保留该扫描证据。同名文件只在字节完全一致时复用，不一致时拒绝覆盖。

失败时保留 RAW IFC、candidate/quarantine 和 `*_failure.json`，用于定位 STEP、Owner、Pset、Property、类型或发布阶段错误。

RAW IFC 不原地修改。插件在转译前后复核其长度与 SHA-256，正式 H-IFC 只在 candidate 文件完成磁盘复读和 exact 校验后发布。

## 内部与外部状态

```text
INTERNAL_VALIDATED
插件内部结构和字段 exact 回读通过。

INTERNAL_FAILED
插件内部验证失败，不建议送入 IFCFlux。

IFCFLUX_MANUAL_PENDING
已生成文件，但尚未取得用户在 IFCFlux 中的人工检查结果。
```

IFCFlux 没有 API，因此插件不会伪造 `IFCFlux 已通过`。用户必须手动在 IFCFlux 中打开最终 `.ifc` 文件，并按同目录的检查清单和 `fields.json` 抽查对象、属性集、属性名、值和类型。

# MCP 工具

当前受控工具共 13 个：

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
bimbaogui_stage03_scan
bimbaogui_stage03_export
bimbaogui_stage03_get_last_result
bimbaogui_stage03_revalidate_file
```

不提供任意 C#、任意 Revit API、任意脚本、UI 模拟点击或任意 Transaction 工具。

写操作必须满足：

```text
Stage01：validate → validation_hash → confirm=true → write
Stage02：preview → preview_hash → confirm=true → write
Stage03：scan → scan_hash → confirm=true → export
```

Stage01/Stage02/Stage03 租约有效期 30 分钟且只能消费一次。Stage03 导出前会现场重建扫描并比较 `scan_hash`；模型、规则或参数改变后，旧确认自动失效。

# 安装

1. 关闭 Revit 2020。
2. 将 ZIP 完整解压到普通文件夹，不要直接在压缩包内运行。
3. 双击：

```text
Install.cmd
```

安装器使用当前用户目录，不要求管理员权限。覆盖安装旧版本时，会删除 BIMBaoGui MCP Server 目录中的旧语义版本目录，避免版本叠罗汉。

安装位置：

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.HifcCore.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.McpContracts.dll

%LOCALAPPDATA%\BIMBaoGui\McpServer\0.4.3\BIMBaoGui.McpServer.exe
%LOCALAPPDATA%\BIMBaoGui\McpServer\mcp-server-config.json
%LOCALAPPDATA%\BIMBaoGui\RevitAddin\stage03-output-directories.json（首次记忆导出目录后生成）
```

# 检查 MCP 连接

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

# 卸载

关闭 Revit 2020，双击：

```text
Uninstall.cmd
```

卸载器只删除 BIMBaoGui 的 Revit 插件目录、BIMBaoGui MCP 版本目录、生成的通用配置和过期 Bridge discovery，不修改第三方 MCP Client 配置。

# 完整性校验与边界

安装包根目录包含 `SHA256SUMS.txt`。安装脚本再次比较 Revit 插件 DLL、H-IFC Core DLL、MCP Contracts DLL 和 MCP Server EXE 的 SHA-256，并将结果写入 `install-evidence.json`。

- 仅支持 Revit 2020；
- RVT 必须先保存，且不能为只读或族文档；
- Stage02 不伪造没有可靠来源的业务值；
- 当前二进制未使用商业代码签名证书，Windows 或 Revit 可能显示未知发布者；
- 自动化验证覆盖编译、H-IFC fixture、MCP SDK、安装、哈希核验和卸载，但不能替代用户电脑中的 Revit 2020 与 IFCFlux 人工验收；
- 只有用户在 IFCFlux 中确认成功后，才能宣称外部检查软件已经识别该具体模型文件。
