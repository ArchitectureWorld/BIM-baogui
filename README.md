# BIM-baogui

湖北省 BIM 规划报建自动化工具。v0.9.0 面向 **Revit 2020 + Rhino 8 + Rhino.Inside.Revit**，当前公开流程由三个编译型 Grasshopper 组件完成。

## 当前公开组件

```text
湖北BIM报规｜01 文件初始化
湖北BIM报规｜02 构件与属性准备
湖北BIM报规｜03 检测、导出与 H-IFC 转译
```

- **01 文件初始化**：保持既有项目身份、坐标 X / Y、高程、真北和项目条件的初始化行为，输出携带规则包身份的 `HBR_FileContext`。
- **02 构件与属性准备**：按当前项目和条件匹配所需载体与字段；选择、预览并一次性确认后，安装 Revit UI 可见、可编辑的共享参数并写入建议值，保存后持久。
- **03 检测、导出与 H-IFC 转译**：全模型扫描并区分缺构件、名称不匹配、缺参数、空值和未分类；通过门禁后执行标准 IFC4 导出与确定性后处理。

## 推荐流程

```text
Stage01 → Stage02 → Stage03
```

Stage03 内部使用 **Autodesk Revit 标准 IFC4** 导出 `<RVT名>-<runId>-RAW.ifc`，再后处理为 `<RVT名>-<runId>-HIFC-MVD.ifc`，并输出 `<RVT名>-<runId>-fields.json`。不要求官方 H-IFC 插件重新导出。

- **Strict**：Strict 遇到任何活动业务阻断时只输出 fields JSON，不发布 IFC。
- **Force**：Force 必须提供非空原因；报告中的“去除首尾空白后的记录原因”采用 `forceReason.Trim()` 后的文本。Force 只绕过业务阻断，技术致命错误永不绕过。
- **文件安全**：RAW 不改写；不覆盖已有目标。失败时保留已有证据，不把半成品冒充为成功产物。

### Stage03 Grasshopper 接线

- 将 Grasshopper `Boolean Toggle` 接到“全部通过才导出”：`true`（默认值）= Strict，所有活动业务阻断处理完才导出；`false` = Force 测试放行。
- Force 时，将非空 `Panel` 文本接到“强制原因”；技术致命错误始终阻断，Force 不可绕过。
- “执行”建议接 `Button`。切换模式、强制原因、输出目录或其他输入后，将“执行”先复位为 `false`，再产生 `false → true` 上升沿重新运行。
- 卡片显示 Strict / Force、字段计数、运行状态，以及 RAW IFC、HIFC-MVD IFC 和 fields JSON 三条路径。

单一规则包 `.hbrpack` 是三个阶段的规则源；`packageId / version / hash` 从 FileContext 传播到预览、检测、产物和报告。Stage01、Stage02、Stage03 的失败报告与活动 GHA 同目录。

## 当前开发归档

当前实现快照、开发依据、三阶段已开发能力、实机验收边界和下一步 Stage02 计划，统一记录在：

- [2026-08-11 GH 插件开发基线归档](docs/archive/2026-08-11-gh-plugin-development-baseline.md)
- [2026-08-11 机器可读验证证据](docs/archive/2026-08-11-validation-evidence.json)

唯一可编辑映射规则仍是 [hbr_rule_source.v1.json](specs/hbr-rules/v1/source/hbr_rule_source.v1.json)。归档文件只记录快照，不是第二份规则源。

## 安装

固定构建产物：

```text
BIMBaoGui.Stage01.gha
```

部署目录：

```text
%APPDATA%\Grasshopper\Libraries\BIMbaogui
```

活动插件目录只能保留一个 `BIMBaoGui.Stage01.gha`，并保持 0 个 `.bak` / `.backup`。部署时关闭 Revit、Rhino.Inside.Revit 和 Grasshopper，直接覆盖固定名文件；回滚依靠 Git 中的目标提交重新构建。

启动顺序：

```text
Revit 2020
→ Rhino.Inside.Revit / Start
→ Grasshopper
→ 湖北BIM报规 / 报规工作流
```

## 开发验证

本地使用与 CI 一致的 Python 3.13。激活项目使用的 Python 环境后，先安装固定版本的测试依赖，再执行验证；CI 继续由 `actions/setup-python` 提供 Python 3.13。

```powershell
python -m pip install --disable-pip-version-check pytest==8.3.5 jsonschema==4.23.0
dotnet restore src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj
dotnet restore tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
python -m pytest -q
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo
git diff --check
```

Release 构建产物位于：

```text
src\BIMBaoGui.Stage01\bin\Release\net48\BIMBaoGui.Stage01.gha
```

Revit 2020 实机步骤见 [v0.9.0 验收清单](docs/revit2020-v090-acceptance-checklist.md)。自动化测试通过不等于实机验收完成；在该清单全部留证前，不宣称所有字段或 IFC owner 策略已经过指定 RVT 的实机验证。
