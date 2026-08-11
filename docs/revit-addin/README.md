# BIMBaoGui Revit 原生插件

## 产品线

本目录记录 `feat/revit-native-addin-v1` 的原生 Revit 2020 产品线。它与 GHA 相对独立，仅共同消费：

```text
specs/hbr-rules/v1/source/hbr_rule_source.v1.json
```

原生插件不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 GHA 程序集。

## 当前实现

当前 `0.1.0` 基础垂直切片包含：

- `IExternalApplication` 启动入口；
- “湖北BIM报规 / 报规工作台”Ribbon；
- WPF DockablePane；
- Stage01 / Stage02 / Stage03 左侧导航；
- ExternalEvent 请求队列；
- 当前 Revit 文档快照；
- 从唯一数据库编译并嵌入的 HBR rule pack；
- packageId / packageVersion / SHA-256 显示；
- Revit 2020 用户级一键安装与卸载；
- 独立 Windows CI 与安装器 smoke test。

当前阶段不代表 Stage01、Stage02 或 Stage03 已经完成。完成边界以设计、实施计划和 Revit 2020 实机证据为准。

## 构建

```powershell
python -m pip install pytest==8.3.5 jsonschema==4.23.0
python -m pytest tests/test_revit_addin_scaffold_contract.py tests/test_revit_addin_installer_contract.py -q
dotnet restore src\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.csproj
dotnet build src\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.csproj -c Release -p:TreatWarningsAsErrors=true
```

Release DLL：

```text
src\BIMBaoGui.RevitAddin\bin\Release\net48\BIMBaoGui.RevitAddin.dll
```

## 安装与卸载

解压 GitHub Actions artifact 后，关闭 Revit 2020，在解压目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1
```

卸载：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -Uninstall
```

安装器使用当前用户目录，不要求管理员权限，并生成：

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\install-evidence.json
```

manifest 中写入绝对程序集路径；安装前后 DLL SHA-256 必须一致。

## 文档

- 设计：`docs/superpowers/specs/2026-08-11-revit-native-addin-independent-product-design.md`
- 实施计划：`docs/superpowers/plans/2026-08-11-revit-native-addin-v1.md`
- 基础实机清单：`docs/revit-addin/acceptance/native-bootstrap-checklist.md`
