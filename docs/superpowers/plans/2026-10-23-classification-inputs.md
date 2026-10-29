# Classification inputs Implementation Plan

> Use superpowers:executing-plans inline. User confirmed scope; no further design confirmation or parallel agents needed.

**Goal:** 统一可键入的分类下拉、真实主子联动和自定义值写入/导出。

**Architecture:** 新增共享分类字典政策，复用冻结平台快照和既有草稿写入。严格快照 ValueError 保留用于提示，指定文字分类在 ValueForExport 中接受自定义原文。三个窗口复用同一选项及提示；手动草稿补父级通知。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / Revit 2020。

**Spec:** `../specs/2026-10-23-classification-inputs-design.md`，用户已确认。

## Global constraints

- 基线 main `f1322d1`；隔离分支 `codex/classification-inputs`；不覆盖已安装 0.4.17-preview.2。
- 冻结规则及参考合同不改；不编造类别到用途的父子关系。
- 仅指定文字分类接受自定义；数值、布尔、固定红线规则及其他枚举继续校验。
- 保留已有值、多选各自值和父级修改后的旧子级；提示与阻断区分。

## Review focus

- 已知但不属于父级的旧子功能仍显示，不会因选项刷新丢失。
- 多选父级不同，子项不套第一个父级；不动未编辑对象值。
- 自定义用途能过导出政策，不改变民用建筑；空用途必填政策仍保留。
- 未启用平台方案仍可参考字典，但不自动切换规则方案。
- 原子写入、来源冲突和必填数值不能被文字分类例外削弱。

## Task 1: Core classification policy

- [x] Add failing `ClassificationInputTests` to core registry: 11/23 dictionary, independent building fields, exact custom export, unknown Pset/type isolation and unchanged road/redline/numeric checks.
- [x] Run core tests and observe failures from missing feature.
- [x] Add `ClassificationInputs.cs`: supported-field identity, choices and advisory hint. Adapt only specified text classification in `CaseExportPolicy.ValueForExport`; retain strict snapshot validator.
- [x] Run whole core suite green.

## Task 2: Shared WPF editors

- [x] Add failing real WPF interaction tests for manual dropdown, parent-change filtering/old-value retention, mixed parents, custom value requests and common/manager fallback dictionary.
- [x] Adapt `ManagerUiModels.RuleInputOptions`, manager editable combo policy, `SelectionDraft` field options/parent notifications and `SelectionWindow` combo editor/commit/hints; reuse PlanningWindow options.
- [x] Run full UI and remaining suites. Inspect screenshots. Verify native build and custom-value materialization through a controlled generated fixture when host available.
- [x] Update preview version/docs/validation with precise installed and native-test boundaries; preserve currently installed version during development, then deploy under the user's existing authorization. Commit only approved source/tests/docs.

## Ledger

- Plan follows confirmed inline scope. Existing isolated checkout reused; no new worktree needed.
- Current installed 0.4.17-preview.2 was deployed before this work, with 5 manifest files and exact native-verified DLL hashes checked.
- Core RED: the old export policy rejected three custom classification tests. UI RED: manual editor lacked the editable dropdown and manager still used a shortened category table. GREEN: core 261, WPF 135, workbook 21 and redline 33; Python 34. Native compilation has no warnings or errors.
- Generated Revit fixture `validation/property-data-native-20261023-220755`: 39 native checks and 51 independent IFC checks passed. Five custom classifications survived production writes, save/reopen and native IFC as IfcLabel; manual floor/region area 123.45 m2, height 3 m and three booleans remained correct.
- A fixture lookup used `row.Slot` although project information contains multiple `row.Slots`; only the harness was corrected, then continued through ExternalEvent in the same process. Candidate plugin/core bytes did not change. Earlier harness failure is retained.
- Full mapping audit retains 35 deliberately missing sources; all 19 materialized fields pass the separate strict mapping audit. This is generated-fixture verification, not customer migration or platform acceptance.
- Ruling: follow the user's existing instruction to deploy after testing and the approved local-main workflow; do not request the same deployment choice again. Preserve the managed worktree because it contains frozen native evidence; do not push remote or clean evidence.
- Final whole-branch review: no Critical, one Important accepted. Manager multi-selection displayed the first classification and unconditional editor commit could stage it for other objects. A real WPF RED in `validation/ui-954d1f1f` reproduced the unintended draft. Fixed only classification fields to display the multiple-values sentinel and ignore untouched sentinel commits; explicit shared edits remain supported. Full GREEN is now core 261, WPF 136, workbook 21 and redline 33 (`classification-reviewed-build.log`).
- Version advanced to 0.4.18-preview.2, keeping preview.1 binaries and earlier evidence frozen. Final native run `validation/property-data-native-20261023-223126` passed 38 host checks and 48 independent IFC checks, without a continuation. Native plugin/core hashes are 2ae8803b85d36ad30d1d78c7ad74f51576fbc94b1e92b45d96f791fa82f09918 / aeb7386207e8124b8d662ff5ef42bfe24825ae7d24bd9542180473963ad6b330. Test Revit exited normally, formal manifest restored, no temporary manifest remains. Installation remains the next step.
- Installed under the existing user authorization after local-main fast-forward `4bc6494` and green main build. Promoted exact native-tested DLL/PDB bytes; preserved main V1 settings. Five installed manifest files match hashes and lengths, assembly version 0.4.18.0. Receipt and 0.4.17 backup: `outputs/install-20261023-224045-4d802e09/`. Customer files/platform unchanged; remote not pushed. Managed worktree retained for frozen evidence.
