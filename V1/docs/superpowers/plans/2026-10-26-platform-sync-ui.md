# 平台同步界面统一 Implementation Plan

> 使用 superpowers:executing-plans 在当前会话直接实施。用户已确认设计；本次是同一窗口的连续改动，不启动子 agent。

**Goal:** 平台同步窗口复用现有模型管理主题，整理四步操作、项目/文件/目标卡片和同步状态。

**Architecture:** 保留 WPF 及原同步调用；PlatformSyncWindow 复用 LiquidGlassTheme，必要时拆分同类 partial 布局文件。DTO 只增加用于显示的文件名/审核状态。独立预览项目链接同一主题和布局源文件。

**Tech Stack:** Revit 2020 / .NET Framework 4.8 / 原生 WPF。

**Spec:** ../specs/2026-10-26-platform-sync-ui-design.md

## Global Constraints

- 继续在现有已授权工作区修改候选，不安装、提交或推送；不启动真实上传。
- 不更改同步引擎、IFC 或 Revit 模型属性；保留已有防重入、文档核对、确认及审核阻断。
- 复用 LiquidGlassTheme；技术配置在底部，缺失/失效时展开。
- 视觉调整通过截图验证，不增加逐一断言颜色/圆角的冗余测试。

## Review Focus

- 首次无配置、失效配置与有效配置重开：设置应能找到，有效时收起。
- 审核中：不得启动，禁用旁显示原因；后台仍做新鲜校验。
- 长 IFC/模型名与窄窗口：完整名称可查看，按钮不超出可操作区域。
- 状态表不能把计算完成显示成法规通过；内部任务 ID 移到详情但仍可查询。
- 改配置、切文档、重复点击保留已有保护。

## Task 1: 共用主题与信息布局

Files: src/Hifc.Revit/PlatformSync/PlatformSyncWindow.cs; optional PlatformSyncWindow.Layout.cs; PlatformSyncClient.cs; tools/Hifc.PlatformSync.Preview/Hifc.PlatformSync.Preview.csproj; tests/Hifc.Ui.Tests/PlatformSyncWindowTests.cs and csproj.

- [x] 为有效配置折叠/无效展开、审核状态就地禁用及状态文件名/技术详情编写行为回归，观察预期失败。
- [x] 应用共用主题；顶部标题/编号操作，中部项目与平台模型卡片，下方状态表和底部设置。
- [x] 保留完整名称提示与诊断，更新旧测试中已获授权变更的按钮文案。
- [x] WPF 现有全集通过；捕获并检查未配置、预览、审核中、运行中、失败、窄窗和 DPI 布局。

## Task 2: 独立集成和交接

- [x] Release 编译正式插件项目及独立预览项目；不安装。
- [x] 独立 WPF + 真实 Node CLI 执行本地 fixture smoke，仅生成计划；修复演练样本与当前交接格式不一致的必要问题。
- [x] 自查差异、检查截图，记录候选结果和边界，提供用户可见预览。

## Progress

- 用户确认界面方案；已经查阅共用主题、原窗口、当前截图及既有测试。既有同步候选源码未提交，保留其全部功能。

- 完成：4 个新增行为回归先失败，最终 WPF 149/149；包括既有防重入、文档切换、取消确认及审核阻断。
- 完成：共享主题直接复用，布局拆为同类 partial；未改同步引擎、客户模型或正式安装。
- 独立 WPF + 真实 CLI fixture smoke 首次因旧人工 IFC 样本没有 IFCPROJECT 被拒绝；补齐样本后通过 PLANNED，零删除/上传/提交，未放宽校验。
- 自查并目视核验：长名完整提示、窄窗口、150% WPF 布局缩放、审核状态、运行/失败状态及底部配置；该缩放不是 Windows 跨屏 DPI 实测。
- Release 插件和独立预览构建均 0 警告/0 错误；正式 DLL 哈希保持。交付见 docs/platform-sync-ui-20261026.md 与 validation/platform-sync-ui-20261026.json。
- 本轮未提交、推送或安装，不创建子 agent。
