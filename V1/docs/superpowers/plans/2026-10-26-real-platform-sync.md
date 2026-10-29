# 真实平台单模型同步 Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. 本轮直接实施，必要独立审查与主线工作并行，总并发不超过 3。

**Goal:** 让既有同步程序真实替换学校项目大门 IFC 并核验本次转换和计算。

**Architecture:** 保留 SyncEngine、Store 和冻结交接协议；新增真实平台契约解析与浏览器适配器、真实动作证据持久化，并补齐插件后台执行入口。写入通过平台自身网页链路执行。

**Tech Stack:** Node 24+、TypeScript、Playwright 1.56.1、SQLite、Revit 2020/.NET Framework 4.8。

**Spec:** `../specs/2026-10-26-real-platform-sync-design.md`

## Global Constraints

- 只处理学校大门；源 RVT 其他属性不得静默改变；导出副本采用用户已确认的分类、民用建筑否和 37.6㎡口径。
- 不动反编译目录；不改正式安装插件直到候选核验完成。
- 真实身份、旧文件备份、当前结果关联均需实证；不能降低现有哈希/回执保护来让测试通过。
- `real-readonly` 保持禁止写，`fixture` 原演练继续可用。新增 `real` 显式选择。
- 账号秘密只进现有 DPAPI 文件；所有日志脱敏。

## Review Focus

- 应用 DOM 已出现但登录和项目仍未加载：等待明确就绪而非误判为空清单。
- 同名模型或同名替换、他人修改非目标模型：必须阻断错误替换，不拿文件名当唯一身份。
- 文件已上传但响应丢失：恢复不得再次选择文件。
- 提交后有旧“已完成”文本、转换仍运行：保持任务未完成且浏览器不关闭。
- 原 RVT 与前次 IFC 数据不同：先对齐本轮数据依据，禁止测试时悄然丢失旧属性。

## Task 1: 真实只读契约与备份

Files: create `platform-sync/src/real-contract.ts`, `platform-sync/src/real-browser.ts`, `platform-sync/tests/real-contract.test.ts`; modify `types.ts`, `security.ts`, `browser.ts`.

Interfaces: `RealBrowserAdapter` implements `PlatformAdapter`; project/model parsers accept validated response records and produce `Snapshot/Model`; credential remains a constructor-only secret.

- [x] Capture minimal sanitized runtime responses plus DOM to validate project, scene, model and file download contracts.
- [x] Add failing tests for malformed data, ambiguous identity, stable generations and real-readonly preservation.
- [x] Implement bounded startup, read/backup identity verification and exact project selection.
- [x] Run build and contract tests; validate real platform reads and a real old IFC download without writes.

## Task 2: 真实写入与恢复

Files: create `platform-sync/src/real-evidence.ts`, `platform-sync/tests/real-browser.test.ts`; modify `real-browser.ts`, `engine.ts`, `types.ts`.

Interfaces: existing `deleteModel/upload/submit/progress/operation` methods; persisted local intent/evidence stores actual remote identities, never synthetic server acknowledgments.

- [x] Write failing tests for non-target mutation, stale completion, lost upload response and submit recovery.
- [x] Persist full non-target baseline in Run; enforce it before/after writes and completion.
- [x] Implement exact scoped delete, file chooser, submit and conversion/calculation observation against verified network/DOM contracts.
- [x] Keep uncertain outcomes pending; resume observes only and never replays writes.
- [x] Run full platform-sync tests including actual local browser upload flow.

## Task 3: CLI 与插件执行入口

Files: modify `platform-sync/src/cli.ts`, `security.ts`, `PlatformSyncClient.cs`, `PlatformSyncWindow.cs`; tests in existing CLI and WPF suites.

- [x] Add failing tests that explicit real apply is enabled only for a verified saved plan and that preview remains read-only.
- [x] Add background worker launch and durable status refresh so UI timeouts do not kill conversion.
- [x] Provide actionable Chinese states for login, missing proof, running, unknown and completed.
- [x] Verify credential redaction, normal export independence and exact document binding.

## Task 4: 大门实测与文档

Files: execution evidence under `outputs/platform-sync-live-20261026/`; update `platform-sync/README.md`, `CONTRACT.md`, `docs/platform-sync-preview.md` and relevant handoff/version records after actual results.

- [x] Resolve export-data choice; use formal export core and verify complete IFC receipt and frozen bytes.
- [x] Select unique actual remote gate model; obtain verified old IFC backup and review replacement plan.
- [ ] Run the independent program through upload, conversion, automatic calculation and final non-target verification. **Deferred: user confirmed the actual project remains under review; no platform writes are allowed.**
- [x] Independently review the change; fix important findings with regression tests.
- [x] Record actual validation and any remaining limitations. Do not claim upload success without platform evidence.

## Progress

- 2026-10-26: user explicitly chose real-program completion followed by gate validation. Existing release is read-only. Original gate RVT single-field edit is already saved and independently reread; latest source and old IFC differ in other business fields.
- 2026-10-26: public platform source downloaded for contract discovery; independent encrypted connection initialized. Existing inspect returned no identity evidence because readiness/identity support is incomplete; no platform writes performed.

- 2026-10-26 收尾：真实只读、大门冻结交接、本地候选与文档已完成；同步 62/62、WPF 144/144、Revit Release 编译通过。真实配置仍 read-only，正式 0.4.19 不变。三项独立复核问题经失败回归后修复，详见进度账本及专项交接。
