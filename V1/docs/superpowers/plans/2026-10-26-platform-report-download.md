# Platform Report Download Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 自动归档与本次计算对应的 PDF 和模型清单 TXT。

**Architecture:** TypeScript ReportService 保存独立报告状态、冻结文件及发布意图，RealBrowserAdapter 从正常平台入口获取报告并校验计算／模型证据。CLI 在成功同步后自动调用，并提供独立报告后台任务；WPF 显示报告状态和下载／打开目录。

**Tech Stack:** Node 24、TypeScript、Playwright、SQLite、.NET Framework 4.8 WPF。

**Spec:** `../specs/2026-10-26-platform-report-download-design.md`

## Global Constraints

- `DOC_平台项目名称_YYYYMMDD_V1.pdf` 与同名 `.txt`；同日递增、次日重置。
- 使用导出来源 RVT 同级目录，不覆盖文件，不重传模型。
- 保留原平台 PDF；TXT 使用 UTF-8 BOM，列出本次实际计算模型及时间。
- 不改正式安装、原模型、反编译目录；保留既有未提交变更；本次不提交或推送 Git。

## Review Focus

- 历史任务与平台当前计算不一致时拒绝错误归档。
- 并发、崩溃、单边文件冲突不能覆盖交付或产生错误配对。
- 平台错误 HTML、跨域下载或异常报告类型不得保存成 PDF。
- 下载重试不调用模型删除／上传／计算。
- 当前 Revit 已切换文档时仍按选中历史任务的来源路径保存，并清楚显示位置。

### Task 1: 报告状态和文件交付

**Files:** `src/report.ts`, `src/types.ts`, `tests/report.test.ts` (relative to platform-sync).
**Interfaces:** `ReportService.download(runId): Promise<Run>` consumes `PlatformAdapter.report(run): Promise<ReportPayload>`; produces `Run.report: ReportDelivery`.
- [x] 写测试并确认缺少功能时失败：命名、每日版本、PDF/TXT配对、错误PDF、旧结果、重试、单边冲突。
- [x] 实现状态独立持久化、冻结原始PDF、预留配对路径与原子单文件发布；恢复时核验摘要。
- [x] `npm run build` 和 `node --test dist/tests/report.test.js` 通过。

### Task 2: 平台报告获取及命令行自动触发

**Files:** `src/real-report.ts`, `src/real-browser.ts`, `src/cli.ts`, `src/worker.ts`, `tests/real-report.test.ts`, `tests/cli.test.ts`.
**Interfaces:** `downloadRealReport(...)` returns `ReportPayload` only after project/result/model correspondence; `report-start` launches `report` independently.
- [x] 在路由夹具中覆盖完整获取、错项目、旧结果、外部模型变化、错误文件、禁止重复模型写入。
- [x] 实现严格限定的报告请求与 PDF 读取；记录实际模型清单和本轮计算时间。
- [x] 成功同步后调用 ReportService，失败独立返回；CLI 可重试成功任务的报告。
- [x] 在学校成功任务上只下载报告，记录真实响应、计算关联、模型未变化及PDF完整性。

### Task 3: WPF 与交付说明

**Files:** `PlatformSyncClient.cs`, `PlatformSyncWindow.cs`, `PlatformSyncWindow.Layout.cs`, UI tests, preview client, README and handoff.
**Interfaces:** `ReportAsync(settings,runId): Task<SyncWorkerLaunch>`; `SyncRun.report` provides paths/status/reason.
- [x] 增加先失败的 UI 回归：仅成功任务可下载，独立失败提示，报告目录、统一样式。
- [x] 加入报告卡片、重试和打开目录；支持旧任务缺少报告字段。
- [x] 同步测试、WPF测试和Revit候选编译通过，截图检查；完整变更复核并记录真实验收边界。

## Progress / rulings

- 用户已确认方案，直接在既有 main 工作区实施；不再就内部实现重复询问。
- 任务相互依赖，顺序实施。构建和独立读取可批量运行；不分派并行开发。
- 下载日期采用本机本地日期；报告自身生成日期另记入 TXT，避免混淆。

- 最终结果：同步 80/80、WPF 151/151，Revit 候选构建 0 警告/0 错误；真实综合报告 14 页、5 模型已交付。详见 ../../platform-comprehensive-report-20261026.md。
- 原学校项目目录已改名带 _0，哈希核对后完成本轮验收交付归置；正式安装及历史任务绑定不变。
