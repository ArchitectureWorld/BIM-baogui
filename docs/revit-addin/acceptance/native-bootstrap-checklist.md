# Revit 2020 原生插件基础架构实机验收

## 安装前

- [ ] 关闭全部 Revit 进程。
- [ ] 解压 `BIMBaoGui-Revit2020-Native-v0.1.0.zip`。
- [ ] 记录 ZIP 的 SHA-256。
- [ ] 确认解压目录包含 `Install-Revit2020.ps1` 和 `BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.dll`。

## 一键安装

在解压目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1
```

安装器应写入：

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\install-evidence.json
```

- [ ] 安装命令成功完成。
- [ ] manifest 中 `Assembly` 为绝对路径。
- [ ] `install-evidence.json` 的 DLL SHA-256 与实际文件一致。

## Revit 启动

- [ ] 启动 Revit 2020，没有 Add-in 加载错误。
- [ ] Ribbon 出现 `湖北BIM报规`。
- [ ] `报规工作台`面板出现“打开报规工作台”按钮。
- [ ] 点击后 DockablePane 出现并可停靠、浮动、关闭和重新打开。
- [ ] 左侧出现 01 / 02 / 03 三阶段导航。
- [ ] 页面显示 `HBR-WUHAN-PLANNING / 1.0.0` 和规则 SHA-256。

## 当前文档读取

分别验证：

- [ ] 无活动文档；
- [ ] 未保存项目文档；
- [ ] 已保存可写项目文档；
- [ ] 只读文档；
- [ ] 族文档。

点击“刷新当前文档”后，标题、路径、Revit 版本、保存状态、项目/族状态和只读状态应正确显示。保存 Revit journal 与截图。

## 卸载

关闭 Revit 后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -Uninstall
```

- [ ] manifest 已删除。
- [ ] 产品目录已删除。
- [ ] 再次启动 Revit 2020 后不再显示插件。
