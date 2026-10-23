# IFC Export Name Order Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; this is a sequential task and no sub-agents are needed.

**Goal:** 按用户批准的名称顺序导出，支持项目名与具体单体名人工确认。

**Architecture:** 核心提供名称拆分与显式名称组装；独立 WPF 窗口维护名称和目录、预览最终路径；Revit 适配器沿用既有类型检测和导出服务。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / Revit 2020。

**Spec:** [已批准设计](../specs/2026-10-23-export-name-order-design.md)

## Global Constraints

- `OUT_项目名称_阶段_具体单体名称或总图_YYYYMMDD_V1.ifc`；可选 `3D_`；自动递增。
- 不修改 RVT 文件名、业务属性或模型几何。
- 总图使用“总图”；阶段与类型不得编造。只合本地 main，不推送。

## Review Focus

- 多栋共享项目时各自拥有版本序列。
- 无规范分隔符、横杠旧名、非法路径字符：建议可编辑，不静默生成模糊单体名。
- 切换目录后版本重算，确认前已有新文件时再次重算。
- 关闭或取消窗口不写文件、不更改模型。
- 总图名称不可误编辑为具体建筑，单体名不可空。

## Task 1: 核心与窗口

**Files:** `V1/src/Hifc.Reference.Core/ExportFileNaming.cs`、`V1/src/Hifc.Revit/Management/ExportNamingWindow.cs`、`V1/src/Hifc.Revit/ExportDestinationPicker.cs`；对应 `ExportNamingTests.cs` 与 `ExportNamingWindowTests.cs`。

- [ ] 更新核心真实行为断言，增加 WPF 名称编辑、目录切换、取消与重新计算版本测试，先观察失败。
- [ ] 实现 `SuggestNames(modelPath,title,kind)`、`StemForNames(projectName,buildingName,stage,kind,date)`，保留旧版本分配函数。
- [ ] 实现名称/目录窗口并接入 Picker，保留自动识别与后续核对。
- [ ] 运行核心和 WPF，确认由失败变为通过。

## Task 2: 验证、交付

- [ ] 构建 0.4.19-preview.1 并运行相关回归；视觉核验真实 WPF 截图。
- [ ] 审查差异、输出回执、版本与文档同步，提交明确文件并合入本地 main。
- [ ] 确认 Revit 关闭后安装通过验证的包，核对安装哈希与备份；准确区分独立 WPF 验证与宿主加载。
