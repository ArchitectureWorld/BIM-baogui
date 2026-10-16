# HIFC Web First Implementation Plan

> **For agentic workers:** use `superpowers:executing-plans` inline. 用户禁止子 agent。本轮范围已在会话中确认并授权执行，不重复申请代码开发许可。

**Goal:** 交付可以登记登录、远程全面检查、编辑、受守卫写入和复检的 Web 专用 Revit 2020 版本。

**Architecture:** 全部新增代码置于 `V1/web`。服务端只读链接既有规则源文件和冻结资源，客户端只依赖普通 DTO 和 HTTP。Web 插件具有独立程序集、AddInId、安装目录、草稿目录和入口。

**Tech Stack:** .NET 10 / ASP.NET Core、PostgreSQL、Revit 2020 / net48 / WPF、netstandard2.0 协议、HTTPS、Docker Compose。

**Spec:** 既有 Local/Web 设计及 2026-10-17 会话中“只做 Web、不改本地版”的范围修订。

## Global Constraints

- 使用现有 `hifc-web` 分支，独立 worktree；不得切换主工作目录或修改其内容。
- 不修改本地版源码、构建安装脚本、已安装插件；不新建或交付 Local 版。
- 源 RVT 只读。测试仅使用副本，写入回读、回滚、复检和保存重开分开记录。
- 普通用户登记仅手机号与真实姓名，默认 168 小时；管理员自定义延期。登录凭证方案待用户确认后实施。
- Web 客户端、导出配置与接口不得交付完整规则库、案例或私有引擎。
- 公开仓库不推送本轮 Web 改动；仓库可见性变更等待用户明确选择，内部开发可以继续。
- 服务器安装必须依据真实权限；不读取凭据，不代填 sudo 密码，不中断既有服务。

## Review Focus

1. 网络返回后文档、构件、类型或参数已变化，必须拒绝旧方案。
2. 长时间编辑、断网、到期、失败重试保留草稿；恢复联网不得重复执行事务。
3. 缺参数、未填写、空字符串、0 与 false 有区别；实例和类型来源不能混淆。
4. 完整规则仅服务端可见；新客户端程序集不得依赖 Reference.Core 或 RuleEngine。
5. 真实安装、Linux 运行、模型副本操作与用户体验结果分别报告。

### Task 1: 协议和服务端规则适配

**Files:** `src/Hifc.Web.Contracts`、`src/Hifc.Web.RuleEngine`、`tests/Hifc.Web.Tests`。
**Interfaces:** inventory -> CapturePlan -> CheckCapture -> CheckResult；EditDraft -> EditPlan -> WritePermit。

- [x] 测试缺页、重复/乱序页、散列、0/false、原值变化与草稿冲突。
- [x] 只读链接现有规则源文件，提供 JSON 兼容适配，不编辑旧工程。
- [x] 运行既有规则回归及新增远程协议用例。

### Task 2: 账号、检查和写入授权服务

**Files:** `src/Hifc.Web.Server`、服务端集成测试。
**Interfaces:** `/api/v1/accounts/register`、`sessions`、`me`、`checks`、`edit-plans`、`write-receipts`；管理员 Cookie 与 CSRF。

- [ ] 先验证 168 小时、重复登记、换机、延期、到期与越权的失败用例。
- [ ] PostgreSQL 持久化账户、扫描、方案、许可、回执和管理员操作。
- [ ] 完整盘点和限定字段分页齐备后产生方案；120 秒方案、30 秒写许可。
- [ ] 用真实数据库验证重启持久化及所有授权边界。

### Task 3: Web 专用 Revit 客户端

**Files:** `src/Hifc.Web.Client`、`src/Hifc.Web.Revit`、客户端及宿主测试。
**Interfaces:** ExternalEvent 中采集和写入，网络等待期间没有事务；所有异步结果匹配文档、扫描与快照。

- [ ] 测试旧响应、签名、断网、草稿重基、过期和重复写保护。
- [ ] 交付登记、四步流程、全面盘点、字段编辑、定位、分类、写入预览和复检。
- [ ] 本地草稿与短期方案分开保存；事务完整提交或回滚；保存 RVT 由用户决定。
- [ ] 独立 Web 包与 manifest，扫描依赖/资源，验证没有完整规则。

### Task 4: Linux 部署和浏览器管理

**Files:** `deploy`、`tools`、浏览器管理页面、部署测试。

- [ ] 刷新 SSH 身份、Docker、端口、路径和 sudo 权限。
- [ ] 提供可审阅安装/部署脚本、HIFC/PostgreSQL/Caddy 三容器和受限 HTTPS。
- [ ] Windows 浏览器真实登记/登录/延期，插件真实请求 Linux。
- [ ] 只有权限或必须由用户输入秘密的步骤才等待用户；继续其余独立工作。

### Task 5: 体验交付

- [ ] 三模型副本验证检查、自动值、编辑、分类清理、写入回读、回滚和保存重开。
- [ ] 校验原 RVT、Local 源码与已安装文件哈希未改变。
- [ ] 交付独立 Web 包、实际入口、操作单和明确的通过/未通过/未验证清单。
- [ ] 用户体验验收；未经用户明确指示不合并 main、不向成员分发。

## 2026-10-17 执行检查点

- Web 协议、规则适配、客户端签名与 HTTP、账号期限策略及服务端签名：61 项自动测试通过；服务端只读复用的既有规则回归：85 项通过。
- Revit 测试程序集构建成功，0 警告 / 0 错误；当前原生采集和事务仅有待实施接口及测试框架，不能当作功能完成或实机通过。
- Linux SSH 身份匹配，缺少 Docker，sudo 需要交互输入。bootstrap 已只读检查并核对传输哈希，安装尚未执行。
- 首次 Revit 测试启动被发布者提示阻挡，未进入测试回调；只结束了本轮自行启动且未打开模型的进程。已重新显示测试窗口，等待用户为 `Hifc.Web.NativeTests.dll` 选择“载入一次”。
- 普通用户登录凭证方案及仓库发布范围已提出确认，尚未得到答复；未实现登记登录、数据库或 HTTP 宿主，未推送远端。
- 既有 Local 源码 / 构建 / 冻结文件未变；3 个已安装 Local 文件和 6 个源 RVT 的 SHA-256 均与实施前一致。
- 当前状态为开发检查点，尚未形成可体验验收的 Web 安装包。收到必需输入后继续 Task 2–5，不重做已通过的基础工作。
