# BIM-baogui

湖北省 BIM 规划报建自动化工具。v0.9.0 面向 **Revit 2020 + Rhino 8 + Rhino.Inside.Revit**，以编译型 Grasshopper 插件连接文件初始化、任务分流、官方 H-IFC 参数写入和 MVD IFC 规范化。

## 工作流组件

```text
湖北BIM报规 / 报规工作流
├─ 01 文件初始化
├─ 02 模型任务与骨架分流
├─ 03 官方 H-IFC 属性写入
└─ 04 MVD IFC 规范化
```

- **01 文件初始化**：校验项目身份、坐标高程、规划目标和项目条件；写入 Revit DataStorage、内部唯一参数与官方精确源参数，并执行回读。
- **02 模型任务与骨架分流**：读取强类型 `HBR_FileContext 0.9.0`，校验当前 Revit 文件的 GUID、载荷哈希和工作流版本，再生成 `HBR_TaskPlan`。
- **03 官方 H-IFC 属性写入**：只在当前 Grasshopper 会话观察到 `false -> true` 后执行；打开已经为 `true` 的 Toggle 不会触发写入。
- **04 MVD IFC 规范化**：读取官方导出的 IFC4，保留几何和非目标数据，按官方示例统一 `Pset_` 属性集标识并按 MVD 修正 IFC 值类型，输出新的 `-MVD.ifc`。

初始化通过、官方参数协议兼容和真实官方 H-IFC 导出验收是三个独立结论。Revit 参数回读成功不等于 IFC 已通过验收。

## 安装

固定构建产物名称：

```text
BIMBaoGui.Stage01.gha
```

部署目录：

```text
%APPDATA%\Grasshopper\Libraries\BIMbaogui
```

活动目录中只能保留一个 BIMBaoGui GHA。部署时直接覆盖固定名文件，不创建插件备份；回滚使用 Git 提交重新构建。覆盖前必须关闭 Revit、Rhino.Inside.Revit 和 Grasshopper。

启动顺序：

```text
Revit 2020
-> Rhino.Inside.Revit / Start
-> Grasshopper
-> 湖北BIM报规 / 报规工作流
```

推荐 IFC 流程：

```text
Stage 01 写入 Revit 参数
-> 官方 H-IFC 导出新的 IFC4
-> Stage 04 输入官方 IFC 路径
-> 输出同目录 <源文件名>-MVD.ifc
```

Stage 04 不覆盖源 IFC、不覆盖已存在的目标 IFC、不创建 `.bak` 或 `.backup`。失败时生成 `BIMBaoGui.Stage04.failure-*.json`，位置与活动 GHA 相同。

## 开发验证

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
C:\ProgramData\Anaconda3\python.exe -m pytest -q
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --nologo
git diff --check
dotnet list src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj package --vulnerable --include-transitive
```

构建产物位于：

```text
src\BIMBaoGui.Stage01\bin\Release\net48\BIMBaoGui.Stage01.gha
```

Revit 2020 实机步骤见 [v0.9.0 验收清单](docs/revit2020-v090-acceptance-checklist.md)。

## 数据与边界

- Stage 01 注册表包含 102 项 `IfcProject / IfcOrganization` MVD 初始化字段与 12 项工作流内部字段。
- 官方规则目录包含 166 条属性映射；官方精确源参数按 Revit carrier 共享别名并在写入前检查值冲突。
- Stage 04 的属性集和属性名称以官方 IFC 示例及官方 H-IFC 映射为准，值类型以 MVD 注册表为准；首版只处理 IFC4。
- `IfcOrganization` 的官方 Revit carrier/export 协议仍未确认；填写非空组织数据会使 `OfficialProtocolCompatible=false` 并阻断 Stage 02。
- 仅支持 Revit 2020。原始 RVT 和 IFC 不应被自动覆盖，正式验收必须输出独立的新 IFC 文件。
