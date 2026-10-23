# Revit 布尔属性、报规表及红线资料 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans with independent workbook/redline modules in parallel. Parent owns shared integration; no overlapping edits.

**Goal:** 落实用户确认的五项改进，保留人工面积优先、首层建筑基底默认是及稳定的全项目 IFC 导出。

**Architecture:** 复用现有检查/草稿/写入核心。布尔业务参数采用原生 YesNo，旧项目先预检、捕获全部值再原子迁移；IFC 临时路由继续兼容原生导出器。报规表及人工红线坐标通过本地 XLSX 读取与预览接入，不使用 Office/LLM，不上传真实平台。红线几何只读检查，手动工具写入后静默全面复检。

**Tech Stack:** Revit 2020 / .NET Framework 4.8 / WPF / Open XML ZIP / existing C# and Python acceptance tools.

**Spec:** 用户本轮五项建议及确认：两处复选框；空布尔默认否，首层建筑基底仍默认是，已有人工值保留；Excel 一栋一张表、冲突预览；红线坐标先人工填写；两用地载体外轮廓/水平位置相同、实体竖向分离。只读 Excel 证据在主目录 `artifacts/plugin-suggestions-20261023/workbooks.json`。

## Global Constraints

- 工作基线 main `550d736`；复用已附加隔离工作区，分支 `codex/revit-property-data`。稳定安装 0.4.16 在候选验证完成前不改。
- 所有布尔空值默认否；仅建筑基底按已确认基底楼层/首层默认是；有效人工是/否优先。数字编号不按 0/1 猜成布尔。
- 改参数类型不能丢旧值或调度表/过滤器引用；不安全来源报告阻断，不静默删除。迁移/写入在事务内回读，失败回滚。
- Excel 空值不清除现值；明确冲突由用户选；旧表混合单体与“6455.60/37.6”不自动猜拆。未映射字段保存为资料，不冒充官方 IFC 属性。
- 红线人工数据保留原模板。坐标口径、单位、X/Y 对应关系未确认只收集，不能报比对通过。
- 同轮廓的规划净用地/净用地控制红线不是重复业务载体；检查外轮廓而非仅面积，检查实际实体碰撞/净距，不移动几何。
- 每次手动写入仍全面复检；不强制弹出另一个管理窗口。已有打开窗口可更新，不抢焦点。
- 原 RVT 和原 Excel 不作为开发试写对象；宿主验收用副本。平台同步预览维持只读。

## Review Focus

1. 空布尔与有效 false/0、首层既有人工否、人工指定非首层基底的区别。
2. 旧文字布尔在全类别绑定上的其他实例/类型值、引用和异常来源。
3. Excel 多栋混表、同名重复字段、层名唯一性、公式无缓存及数值/单位冲突。
4. 红线相同面积但轮廓/位置不同、曲线与多环、未知坐标变换、实体厚度导致碰撞。
5. modeless 生命周期、扫描过期、暂存恢复和整体事务失败不丢人工修改。

## Tasks / ownership

- [x] 1. Parent: Boolean policy + native YesNo source/migration + checkboxes + silent recheck.
  Files: `AutoFillPolicy.cs`, new `BooleanPolicy.cs`, `RevitMappingService.cs`, new `BooleanParameterService.cs`, `CaseEdits.cs`, `ModelEdits.cs`, `ManagerWindow.cs`, `SelectionWindow.cs`, controllers, existing test registries. Tests RED→GREEN for defaults, legacy values, unknown values, native migration atomicity and IFC boolean readback.
- [x] 2. Workbook worker: `ReportingWorkbook*.cs` in core, `ReportingWorkbookWindow.cs`, separate `Hifc.Workbook.Tests`.
  Interface: reader returns tables/items with source sheet/cell/hash and explicit ambiguous/unmapped issues. Planner consumes existing `ModelElementCheck[]` and kind, produces selected property changes (ElementId/UniqueId/PropertySet/Source/value) and raw source metadata. Window confirms selected changes; does not write model itself. Parent stages into existing property grid and 4-write; raw accepted records persist with RVT through project data storage.
  Verify real provided workbook readonly, normalized one-building tables, formula caches, merged cells, duplicate fields, conflict default unselected, blank retention, manual floor areas. No changes to original workbook.
- [x] 3. Redline worker: core `Redline*.cs`, `Management/RedlineWindow.cs`, `Management/RedlineController.cs`, `Management/RedlineGeometry.cs`, separate `Hifc.Redline.Tests`.
  Interface: static `RedlineController.Show(UIApplication)` entry callable from API context; pure coordinate import/validation and pair checking; local persistent project data. Window manual file import, metadata confirmation, read-only geometry comparison, template-compatible output. No automatic model-derived reference coordinates, no geometry edits or platform writes.
  Verify identical/offset/different outline with equal area, sequence/closure, unknown units/frame, first-floor not involved, native shared-coordinate control points and exact solid collision in host.
- [x] 4. Parent integration: manager toolbar events and staged workbook writes; shared metadata storage; ribbon entry, build/version/docs/CI. Full existing + new suites and real WPF interactions.
- [x] 5. Controlled Revit copy acceptance: legacy/typed bool and empty default + first-floor preservation, manual write silent scan, workbook staged→write→save/reopen, redline shared-coordinate and no geometry change; export IFC and independently audit values/types/manual areas.
- [ ] 6. Fresh independent review, fixes, merge authorized changes to main; installation only after candidate tests and DLL availability, original model migration remains explicit user operation. No remote push inferred.

## Ledger

- User approved the prior plan, overriding only empty booleans to false while preserving base-floor true default. No additional design approval is required.
- Parallelism: workbook and redline are independent modules/new files; parent alone edits shared controllers/mapping/window/test registries. At most two implementer agents plus parent.
- Baseline installed version 0.4.16; source already includes platform preview. New release metadata will clearly distinguish candidate from installed version, and all web writes remain disabled.

## Execution evidence — 2026-10-23

- Tasks 1–4 implementation and standalone regressions finished; task 5 native acceptance remains the gate for completion and task 6 merge. Checkbox rendering inspected in `V1/validation/ui-61a0a414/`.
- Exact current build: core 248, WPF 131, workbook 21, redline 33; Python 34 and platform-sync Node 33 passed. Revit and native-host builds have zero warnings/errors. Customer workbook/template sample tests add one test per suite, both readonly and original hashes unchanged.
- Fresh independent review covered Boolean owner/migration safety, workbook provenance/units and manual draft ownership, lifecycle callbacks, and quantity export. Important findings were reproduced as failing tests and fixed; final length/depth/height-difference gap now shares one import/template/export policy with 248 core and 21 workbook tests green.
- Ruling: canonical m²/m values remain explicit through staging and native conversion; equal raw text is not proof of equal physical quantity when project display units differ. Plain Number, text and identifiers stay unscaled.
- Native fixture `property-data-native-20261023-194546` launched against exact final candidate. Superseded earlier unloaded starts before any model creation and restored their manifests; stable installed DLL was not replaced. Publisher operation is a required host interaction, not evidence that fixture execution passed.

- Final native acceptance: 36/36 checks; initial host 26 checks followed by a hash-verified same-process continuation after a harness-only file-sharing fix. Product plugin/core stayed unchanged. Actual IFC 40/40 checks, 16/16 materialized mappings and 3 typed booleans; full missing-input report retains 38 blank fixture properties.
- Latest complete build: core 256/256, WPF 131/131, workbook 21/21, redline 33/33; Python 34/34 and Node 33/33. Formal 0.4.16 installation and original XLSX hashes remain unchanged; temporary manifests restored and test host exited normally.
- Final scope ruling: keep generated-model business completeness separate from mapping correctness. Preserve the complete failed-completeness audit and strictly verify every materialized field with the existing verifier; no failure codes are suppressed. Real customer migration and real platform acceptance remain pending.
