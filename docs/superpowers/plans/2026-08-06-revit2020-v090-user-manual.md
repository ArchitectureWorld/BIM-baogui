# BIMBaoGui v0.9.0 User Manual Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a concise Chinese Markdown guide that tells acceptance personnel how to use the three public Grasshopper components and export RAW IFC, HIFC-MVD IFC, and fields JSON.

**Architecture:** Add one focused user-facing document under `docs/`. Derive component names, wiring, execution behavior, Strict/Force rules, output naming, and failure-report location from the current README, acceptance checklist, and production component source so the guide does not invent unverified behavior.

**Tech Stack:** Markdown, Revit 2020, Rhino 8, Rhino.Inside.Revit, Grasshopper, BIMBaoGui v0.9.0.

## Global Constraints

- Audience is acceptance personnel, not developers.
- Final file is `docs/revit2020-v090-user-manual.md`.
- Explain only the three public components and IFC export workflow.
- Current 359/359 rules are `UNCLASSIFIED`; do not describe Strict clean as currently reachable.
- Force output is not equivalent to Strict acceptance.
- Failure reports remain beside the active GHA and no `.bak` or `.backup` plugin files are allowed.

---

### Task 1: Write and verify the three-component usage guide

**Files:**
- Create: `docs/revit2020-v090-user-manual.md`
- Reference: `README.md`
- Reference: `docs/revit2020-v090-acceptance-checklist.md`
- Reference: `src/BIMBaoGui.Stage01/Stage01Component.cs`
- Reference: `src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs`
- Reference: `src/BIMBaoGui.Stage01/Stage03ValidationExportComponent.cs`

**Interfaces:**
- Consumes: Current public component names, Grasshopper input/output labels, Stage01 `FileContext`, Stage02 preview/confirmation flow, and Stage03 Strict/Force export contract.
- Produces: A standalone Markdown manual that a user can follow from application startup through IFC output verification.

- [ ] **Step 1: Read the current public component contracts**

Run:

```powershell
rg -n "RegisterInputParams|RegisterOutputParams|Name =>|NickName =>|Strict|Force|全部通过才导出|强制原因|输出目录" src/BIMBaoGui.Stage01 README.md docs/revit2020-v090-acceptance-checklist.md
```

Expected: The three component names, input/output labels, Stage03 mode rules, and output filenames are available without relying on historical Stage04 behavior.

- [ ] **Step 2: Create the final manual**

Write `docs/revit2020-v090-user-manual.md` with these exact sections:

```markdown
# BIMBaoGui v0.9.0 三组件使用与 IFC 导出说明

## 1. 使用前准备
## 2. 启动顺序
## 3. 组件 01：文件初始化
## 4. 组件 02：构件与属性准备
## 5. 组件 03：检测、导出与 H-IFC 转译
## 6. 导出文件说明
## 7. 导出失败时怎么处理
```

The Stage03 section must explicitly state:

```text
全部通过才导出 = true: Strict，只要存在活动业务阻断就只生成 fields JSON。
全部通过才导出 = false: Force，必须填写非空强制原因，允许验收导出测试。
任何输入变更后都要让执行输入重新产生 false -> true 上升沿。
技术错误不能被 Force 绕过。
```

- [ ] **Step 3: Verify names and required safety statements**

Run:

```powershell
rg -n "01 文件初始化|02 构件与属性准备|03 检测、导出与 H-IFC 转译|RAW.ifc|HIFC-MVD.ifc|fields.json|UNCLASSIFIED|false -> true|失败报告|\.bak|\.backup" docs/revit2020-v090-user-manual.md
git diff --check -- docs/revit2020-v090-user-manual.md
```

Expected: Every required term is present and `git diff --check` has no output.

- [ ] **Step 4: Run the current documentation contracts**

Run:

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
C:\ProgramData\Anaconda3\python.exe -m pytest tests/test_v090_release_contract.py tests/test_official_export_contract_review.py -q
```

Expected: All tests pass.

- [ ] **Step 5: Commit the manual**

```powershell
git add docs/revit2020-v090-user-manual.md
git commit -m "docs: add three-component IFC export guide"
```
