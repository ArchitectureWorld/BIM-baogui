# BIM-baogui

湖北省 BIM 规划报建自动化开发仓库。当前以 **Revit 2020 + Rhino.Inside.Revit + 纯 Grasshopper** 为第一代技术路线，先完成单文件工作流中的 **01 文件初始化**。

## 当前开发范围

```text
Revit 2020 当前活动项目
        ↓
Rhino.Inside.Revit
        ↓
Grasshopper 内完成全部交互
        ↓
空白文件门禁
        ↓
文件与项目数据填写
        ↓
显式提交到 Revit
        ↓
重新读取并逐项比较
        ↓
初始化通过 / 回读失败 / 已修改待重提
```

第一代只支持空白或刚完成子项拆分、尚未导入 CAD、尚未正式建模的 RVT 文件。已有模型的坐标校正、跨文件复核、WPF/Dockable Pane 和自定义 GHA 留到后续路径。

## 技术基线

- Autodesk Revit 2020
- Rhino 7
- Rhino.Inside.Revit
- Grasshopper 1
- 原生 GH 组件 + GhPython / C# Script
- 不依赖额外第三方 GH 插件

## 数据基线

| 数据 | 数量 |
|---|---:|
| IFC 实体 | 12 |
| Pset | 50 |
| MVD 属性 | 356 |
| 第一阶段 `IfcProject` 属性 | 77 |
| 第一阶段 `IfcOrganization` 属性 | 25 |
| 第一阶段 MVD 属性合计 | 102 |
| 工作流内部字段 | 12 |

原始工作簿位于 `data/《MVD》规划报建.xlsx`。派生注册表保持精确实体、Pset 和属性名，并同时保留原始类型与内部归一类型。详见 [`docs/data-source-summary.md`](docs/data-source-summary.md)。

## 仓库结构

```text
BIM-baogui/
├─ data/                    # 原始 MVD 与版本化注册表
├─ docs/
│  ├─ decisions/           # 架构决策记录
│  ├─ verification/        # Revit 2020 运行时验收证据
│  └─ ...                  # 设计与实施计划
├─ gh/                      # Grasshopper 定义、脚本与使用说明
├─ references/              # IFCFlux 等不可再生原始依据
├─ tests/                   # 静态一致性与生成器测试
├─ tools/                   # 可重复生成 .ghx 的工具
└─ examples/                # 输入模板与验收示例
```

## 当前交付状态

- [x] MVD 全量字段注册表
- [x] 第一阶段字段注册表
- [x] 第一阶段设计与实施计划
- [x] GH 画布蓝图
- [x] 注册表一致性检查
- [ ] 可直接打开的 `01_文件初始化.ghx`
- [ ] Revit 2020 运行时验收
- [ ] 经运行时验证后另存的二进制 `.gh`

## 验证

```bash
python tests/validate_registry.py
python -m unittest discover -s tests -p 'test_*.py' -v
```

`.ghx` 是 Grasshopper 可直接打开的 XML 定义，也是仓库中的可审查源文件。经 Revit 2020 + Rhino.Inside.Revit 环境运行通过后，可在 Grasshopper 中另存为二进制 `.gh`。

## Git 工作流

功能在独立分支开发，通过 Pull Request 合并到 `main`。分支、提交、数据变更和 GH 文件要求见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。

## 重要原则

- 普通 GH 求解不得修改 Revit；只有明确提交操作可以写入。
- 写入后必须重新从 Revit 读取并比较。
- 规划目标与模型实算值必须分离。
- 不得以占位值伪造尚未取得的业务数据。
- 原始 MVD 名称和类型异常必须可追溯，不得静默修正。
