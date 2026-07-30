# BIM-baogui

湖北省 BIM 规划报建自动化工具。当前交付为 **Revit 2020 + Rhino 8 + Rhino.Inside.Revit** 环境中的编译型 Grasshopper 插件。

## 当前组件

```text
湖北BIM报规
└─ 报规工作流
   └─ 湖北BIM报规｜01 文件初始化
```

组件将文件身份、坐标高程、参建单位、项目条件、校验和提交操作集中在一个 Grasshopper 运算器内；画布不再使用 Panel 表单或旧版 C# Script 电池。

## 技术边界

- 宿主：Autodesk Revit 2020
- Rhino：Rhino 8
- 运行方式：从 Revit 的 Rhino.Inside.Revit 面板启动 Grasshopper
- 第一代文件范围：新建或刚完成子项拆分、尚未导入 CAD、尚未正式建模的 RVT
- 模型写入：通过 Revit 2020 API 事务执行
- 数据保存：Revit `DataStorage + Extensible Storage`
- 提交原则：显式点击“写入并回读”；普通 GH 求解不修改 Revit

## 安装

将构建产物：

```text
BIMBaoGui.Stage01.gha
```

复制到：

```text
%APPDATA%\Grasshopper\Libraries-Inside-Revit-2020
```

然后按以下顺序启动：

```text
Revit 2020
→ Rhino.Inside.Revit / Start
→ Grasshopper
→ 湖北BIM报规 / 报规工作流 / 文件初始化
```

## 开发验证

```powershell
python -m pytest tests -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

编译后的插件位于：

```text
src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha
```

## 数据依据

内置 Stage 01 注册表包含：

- 102 项 `IfcProject / IfcOrganization` MVD 初始化字段
- 12 项工作流内部字段
- 项目条件触发数据
- 精确实体、Pset、属性名称和 IFC 类型

用户默认只看到初始化必要字段，可在组件内启用“显示全部 102 项 Stage 01 MVD 字段”。
