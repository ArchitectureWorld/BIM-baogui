# HBBIM 单模型同步预开发 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans; independently implement the C# boundary and the TypeScript worker after agreeing the handoff contract.

**Goal:** 在保留现有 IFC 导出的前提下，实现可验证、可恢复的单模型同步预开发，并按用户授权合入 main。

**Architecture:** Revit 仅在完整导出成功后提供可信回执和冻结文件；独立程序保存绑定与执行状态，通过专用浏览器操作。真实平台本轮仅只读，完整替换与故障恢复在本地模拟平台验证。

**Tech Stack:** .NET Framework 4.8 / WPF; Node 24 / TypeScript / Playwright / SQLite.

**Spec:** `D:/18_建模项目/湖北BIM云平台/网页相关报告/HBBIM_IFC_Automation_Handoff_v1.0.md` SHA256 `f2e3cc1c43beebf91c566ad0a928f8ed2638309666997258b41b3c7b37aa1c21`; approved scope and wire contract: `V1/platform-sync/CONTRACT.md`.

## Global Constraints

- 不安装或覆盖当前 Revit 插件，不改 RVT，不写真实平台；源码最终合入 main 已获批准。
- 完整成功回执 + 冻结 IFC + SHA-256 才能入队；文件存在不代表成功。
- 绑定不依赖日期/版本文件名；另存为及复制模型需重新绑定。
- 删除前需要经核验的真实旧 IFC；未知写入结果必须核对，不自动重放。
- 浏览器转换期间不刷新、不跳项目；计算完成与法规通过分别表达。
- 专用浏览器 profile；令牌用 Windows DPAPI，本地日志不含凭据；trace/HAR 关闭。
- 仅单模型手动任务；批量、watch、无人值守及真实平台写入验收后置。

## Review Focus

1. 导出文件成功写出但回滚/回执失败：不能出现可同步任务。
2. 源文件、冻结文件或备份被修改：在每个写入边界重新校验。
3. 同名模型的新旧代次及历史“已完成”：必须有本次运行关联。
4. 进程崩溃和双进程竞争：持久化意图，未知任务保留项目锁。
5. 另存为、重命名与凭据泄漏：严格文档身份，日志仅输出脱敏摘要。

## Tasks and ledger

- [x] 0. 记录现有 0.4.16 源码基线（main `2894987`），创建隔离分支 `codex/hbbim-sync-preview`。
- [x] 1. C# 成功导出冻结交接、WPF 设置/同步/状态入口，测试失败导出、改动文件、再次导出、异文档与独立 WPF。
  Files: `V1/src/Hifc.Reference.Core/PlatformSyncHandoff.cs`, `V1/src/Hifc.Revit/PlatformSync/*`, `NativeExportService.cs`, `App.cs`; tests under `V1/tests`.
  Interface: manifest schema and CLI in CONTRACT.md. Native handoff registered only after receipt write returns. Sync freeze failure must not turn export success into export failure.
  Verify: core + WPF test executables; compile Revit plugin without installing.
- [x] 2. 独立程序配置、SQLite 状态/锁、绑定、计划/恢复与备份验证。
  Files: `V1/platform-sync/src/{contracts,security,handoff,store,engine,cli}.ts`; `tests/engine.test.ts`.
  Interface: `SyncEngine.plan(binding,manifest)`, `run(runId)`, `resume(runId)`, `rollback(runId)`; adapter provides snapshot/backup/delete/upload/submit/progress.
  Verify: Node unit/integration tests for no-write plan, wrong project, no/multi-match, copy/SaveAs, backup invalid, stale state, concurrent processes, lost responses, rollback.
- [x] 3. Playwright 独立浏览器与本地平台闭环；真实平台只读适配器硬性禁止写入。
  Files: `V1/platform-sync/src/{browser,fixture}.ts`; `tests/browser.test.ts`.
  Verify: full local browser replacement, conversion/calculation distinction, network/response loss, duplicate submit prevention; read-only real connection attempt with sanitized evidence.
- [x] 4. 文档、版本标识、启动脚本、审查修复与 main 合并。
  Files: `V1/platform-sync/README.md`, `V1/docs/platform-sync-preview.md`, `V1/README.md`, verification summary.
  Verify: complete existing .NET/Python suite and new Node/browser suite; installed DLL hash unchanged; main merged, no runtime/private files staged.

### Decisions / evidence

- Existing baseline was uncommitted; source-only baseline commit preserves all current development, excludes model/output/private report artifacts. No source content changed by the commit.
- C# boundary/UI and TypeScript worker are independent after CONTRACT.md; parallel implementation is permitted by the user's earlier explicit authorization. Max two implementation agents plus root; no overlapping file ownership.
- Merge approval applies to local main; no remote push or installation inferred.

### Execution evidence

- Task 1: complete. `V1/tools/Build.ps1` -> core 235/235, WPF 121/121, Revit plugin compile zero warnings/errors. Separate standalone WPF host -> real Node CLI inspect/bind/freeze/PLANNED passed. Cross-language C# manifest independently verified by Node. Build UI path shortened after reproducing .NET48 MAX_PATH failure in the longer managed worktree; rerun green.
- Tasks 2–3: complete within preview scope. Node 24.19.0 TypeScript build + 33/33 tests, including 8 browser integration groups and a true child-process SQLite lock case. Backup and request intent checks cover changed bytes, crash remnants, lost delete/upload/submit acknowledgment and stale completion. Python regression 34/34 passed.
- Live readonly: dedicated visible browser connected to 智慧规划云平台; no model writes. No conclusive project/scene/model-generation identity, so inspection exits 4 and binding is blocked. This is evidence of a connection only; actual platform binding/apply acceptance remains incomplete and is documented.
- User separately authorized stable 0.4.16 installation mid-task. Installed DLL `f3b16b8f8c001a5382475a9f581e14c5342bada240f4ef995fd058ae2490d603` verified; unchanged throughout preview development. No Revit process started for this install.
- Final review: independent fresh reviewer, two findings. P1 backup was not checked after delete/upload; P2 pre-delete recovery could fail forever on an existing backup. Both reproduced RED, fixed in one pass, final Node suite 33/33 GREEN. No deferred review findings.
- Launcher: `Start-LocalDemo.ps1 -Smoke` passed; actual WPF display visually inspected. Fixture counters delete/upload/submit all zero, state PLANNED. Public screenshot and validation index committed; detailed logs retained under local artifacts.
- Task 4: complete. Feature commit dcef783 merged to main via fast-forward; main Build.ps1 rerun 235/235 core, 121/121 WPF and zero-warning Revit compile. Main-directory Start-LocalDemo.ps1 -Smoke passed, no fixture writes. Stable installer remains pinned 0.4.16, preview automatic build-and-install guarded, CI adds Node 24/local browser regression.
