# HIFC Local / Web 局域网服务 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. 用户要求主 agent 单线程实施，禁止子 agent、委派及平行 agent。每项先验证失败用例，再实现、验证和小范围提交。本文只编制计划，不代表安装或开发已经执行。

2026-10-16归档修订：纳入主会话评估、长时间草稿、断网/到期状态及本地Revit写入说明。用户要求创建独立 `hifc-web` 分支保存讨论；本次授权为文档归档和分支上传，开发及服务器部署尚未开始。阅读入口：[README](../README.md)。

**Goal:** 以当前 Windows 运行 Revit 和管理浏览器，以 zetta-pc 运行私有规则服务，交付 `HIFC-0.5.0-Local`、`HIFC-0.5.0-Web`、双字段注册、168 小时有效期与管理员自定义延期。

**Architecture:** 一个私有仓库、一套规则引擎、两种插件组合。Web 的完整规则和案例留在 Linux，Windows 仅采集当前模型及执行受守卫的写入；Local 在 Windows 本地调用相同规则。Linux 用 Docker Compose 部署 HIFC、PostgreSQL、Caddy 和 Portainer，Windows 浏览器及部署脚本完成日常管理。

**Tech Stack:** Revit 2020 / .NET Framework 4.8 / WPF；共享 Contracts、RuleEngine 目标 netstandard2.0；ASP.NET Core 10 / Razor Pages / PostgreSQL 17；Ubuntu 24.04 / Docker Engine + Compose；Caddy 2 / Portainer CE；Windows DPAPI、服务端 UTC 时钟、HTTPS。

**Spec:** [设计依据](../specs/2026-10-16-hifc-local-web-design.md)。[只读环境快照](2026-10-16-hifc-environment.json)。执行者先读这两份，再执行本计划。

## Global Constraints

- 仓库 `D:\18_建模项目\湖北BIM云平台`，稳定主线 `main`；用户明确新增 `hifc-web` 文档及后续开发分支。调查基线为 `ecc768b491d38b60d9785c06f3bbd12f0237ba67` / 0.4.2，文档分支父提交为归档期间主会话已提交的 `cb34b20a80c172c5c24982e8a58be44c01464a4f` / 0.4.3。进入开发前对齐 main 后续已验证修复并重新记录基线；保留选中构件属性编辑与Area导出修正。保留其他未提交修改，不切换正在使用的主工作目录或顺带提交其他功能。
- 版本名严格为 `HIFC-版本号-Local` / `HIFC-版本号-Web`；目标新版本 0.5.0。Local 自用，Web 为内部成员使用。
- 注册界面只有“手机号、真实姓名”。普通账号注册后默认 168 小时，只显示“有效 / 已到期”；没有试用、停用、用户套餐或第三种角色。
- 管理员只有用户本人；普通用户所有产品能力相同，由有效期决定能否请求新方案。延期接受自定义 1–36500 天或指定日期，不新增在线申请审批流。
- 用户通过现有企微联系管理员；本版不接企微应用、短信、微信登录或公网身份服务。
- Revit API 全部留在 Windows 的 `ExternalEvent` / 合法宿主回调中；HTTP 等待期间不得持有模型事务，不把 Revit 对象跨线程或序列化到服务器。
- 每次检查完整采集当前模型；只道路宽度保留现有手填标红；不新增模型业务规则、施工图规则或材质功能。
- 原 RVT、冻结参考输入、其他插件、Linux 现有服务与网络连接均保留。模型实测只用三个副本；失败回滚和写后全面复检是完成条件。
- 密钥、管理员初始化资料、数据库备份、真实手机号、令牌不提交 Git；Web 包不得包含完整规则、案例资源或私有引擎依赖。
- 归档准备时再次核实远端为 public。完整内网资料需在用户确认的私有发布范围内推送；公开仓库只能采用另行选定的脱敏文档。文档分支上传不自动授权改变整个仓库可见性；服务开发前仍须完成 Task 0 的私有化检查。
- 用户在 Windows 终端自行完成必要的 sudo/管理员秘密输入；不向 agent 提供或在命令、日志中回显密码。安装、证书信任和 Revit 退出安排在明确的实施阶段。

## Review Focus

1. 两项登记信息不作为可猜测的管理员凭据；重试、换机、手机号重复不会领取第二个 168 小时；Task 6、7 测试。
2. 北京时间日期结束、到期瞬间、服务器重启及已有 JWT 尚未到期的组合仍按数据库期限授权；Task 6、10 测试。
3. HTTP 返回时切换了模型、删除/重建构件、替换类型、编辑了原值或开始了另一次检查，不能写旧方案；Task 4、8、10 测试。
4. Web 的隐藏按钮、导出临时 TSV、依赖程序集和资源也可能包含全量映射，不能只检查安装包表面的 JSON 文件；Task 3、9、11 测试。
5. Docker 端口过滤、跨子网来源、CA 信任、虚拟机挂载、应用/数据库回退与异机恢复必须在实际两机验证；Task 1、12、13、14 测试。

补充时序：人工编辑超过方案120秒、账号到期后保留草稿、事务已开始但许可计时到点、写入完成但复检失败。Task 4、9、10、14 均需验证；不得把草稿有效期等同于写入方案或许可有效期。

---

## A. 给管理员看的实施总表

| 阶段 | 交付物 | 你需要参与的动作 | 通过后才进入下一阶段 |
|---|---|---|---|
| 0：私有化与环境 | 私有仓库、Linux Docker、独立目录、Windows SDK | 确认仓库私有化；从 Windows SSH 终端完成一次 sudo 授权 | 无其他服务损坏，工具与路径核实 |
| 1：拆分并保持 Local 可用 | 无网络依赖的 Local，新公共协议及规则引擎 | 到实机安装时保存并关闭 Revit | 三模型结果与所选基线等价，覆盖0.4.2原流程及0.4.3新增编辑/Area修正 |
| 2：Web 最小模型流程 | 基础账号/期限服务、HTTPS、Revit → Linux 检查 → Revit 写入和复检 | 确认证书指纹、处理 Revit 加载提示、实机操作 | Local/Web业务结果等价；长时间草稿、断网、过期、模型变化守卫通过 |
| 3：完整账号管理 | 两字段登记交互、用户列表、自定义延期、换机绑定 | 通过 Windows 页面初始化并操作你自己的管理员资料 | 普通用户与管理员边界、时间计算、换机和页面持久化均通过 |
| 4：部署与维护 | 两个管理入口、版本包、定时备份、回退工具 | 确认证书指纹；一次异机恢复演练 | 重启、升级回退、恢复后业务均验证 |
| 5：内部发包 | 仅给成员分发 Web，Local/Server 由你保管 | 选第一批内部测试成员 | 真实第二台用户 Windows 注册和操作通过 |

阶段 1、2 是可单独验收的交付，不把整个任务压成一次大替换。预计并发性能不按 CPU 数量口头保证；首轮以 5 个同时检查会话做负载验证并记录结果。

任务编号保留以便追踪，实际依赖顺序为：Task 0 → 1 → 2 → 3 → 4 → 5 → 6 → 8 → 9 → 11 → 12（core三容器）→ 7 → 10 → 12（full运维完善）→ 13 → 14。各任务自己的测试随实现立即执行，Task 10整合故障回归，不延后前面任务的必需验证。Task 6先提供正式的身份、期限与授权服务；管理员完整页面后置。最小流程只向受限的开发者测试机器开放，不能设置匿名写入或可带入正式版本的授权绕过开关。

## B. 软件安装决定：明确装在哪里

### Linux zetta-pc 必须安装

宿主包：`ca-certificates`、`curl`、`docker-ce`、`docker-ce-cli`、`containerd.io`、`docker-buildx-plugin`、`docker-compose-plugin`。从 Docker 官方 Ubuntu noble 仓库安装，非 Docker Desktop，非另一套临时容器平台。

Compose 项目名固定 `hifc`。最小模型流程先部署前三个服务；内部发包前增加 Portainer，最终包含四个长期运行服务：

| 容器 | 镜像族 | 状态存放 | 宿主公开端口 |
|---|---|---|---|
| `hifc-server` | 自建 HIFC Server，基于 ASP.NET 10 noble | `/srv/hifc/data/server`、只读 rules、secrets | 无，8080 仅容器内 |
| `postgres` | `postgres:17-bookworm` | `/srv/hifc/data/postgres` | 无，5432 仅容器内 |
| `caddy` | Caddy 2 官方镜像 | `/srv/hifc/data/caddy` | `192.168.6.45:8443:443` 和 `192.168.6.45:9443:9443` |
| `portainer` | Portainer CE 官方 LTS 镜像 | `/srv/hifc/data/portainer` | 无，9000 仅容器内，Caddy 代理 |

PostgreSQL 通过 `core` 网络连接应用；Portainer 通过独立 `ops` 网络连接 Caddy；只有 Caddy 接入两个网络。Portainer 是唯一允许挂载 Docker socket 的 HIFC 容器，普通业务容器没有此能力。启动前以锁定镜像的监听与官方 CLI 说明验证内部 HTTP 9000 可用，对外 HTTPS 统一由 Caddy 提供。

发布脚本把所有镜像解析成 `image@sha256:...` 写入 `release-lock.json`；没有 digest 的发布包不进入部署。代码构建用 SDK 10.0.402 容器；Linux 宿主不安装 .NET、PostgreSQL 裸机服务、Node.js、Revit 或图形桌面依赖。

首次下载安装包和官方镜像需要访问微软、Docker等官方源；正常业务运行只需要局域网。若首次获取失败，保留失败原因，不自动改用不明镜像源；可由当前Windows下载同一锁定镜像并经SSH导入，必须比对原始digest。

### Windows 必须准备

- 沿用 Revit 2020、.NET Framework 4.8、PowerShell、OpenSSH、浏览器。
- 用户目录安装 .NET SDK 10.0.402 到 `C:\Users\2899\.dotnet-hifc`，不删除 SDK 7，不改全局 PATH。项目脚本通过 `Get-DotNet.ps1` 解析该目录，CI 则使用 setup-dotnet 的 SDK 10。
- 导入本项目 CA 的公开根证书至当前 Windows 用户信任库，并核对指纹。只给用户发公开证书，不能把 CA 私钥打进插件包。
- 安装 HIFC 插件时保留一次仅启用一个版别的约束；不要求 Windows Docker、Linux 桌面远程控制或短信平台。

## C. 目标代码边界与协议

所有下列路径相对仓库根。现有冻结数据在 `V1/reference/`，保持内容与 SHA-256 不变。

| 路径 | 职责 / 修改来源 |
|---|---|
| `V1/src/Hifc.Contracts/` | 协议 DTO、字段类型、单位、分页完整性、通用守卫键；不含业务规则 |
| `V1/src/Hifc.RuleEngine/` | 从 Reference.Core 迁入目录、案例识别、规则、自动建议，显式加载版本化 RulePack |
| `V1/src/Hifc.Revit.Host/` | 公共 WPF、Revit 读取、几何、事务、IFC 临时物化；不引用私有规则引擎 |
| `V1/src/Hifc.Revit.Local/` | Local 组合入口、LocalRulesBackend，引用 RuleEngine 与 Host |
| `V1/src/Hifc.Revit.Web/` | Web 组合入口、WebRulesBackend、HTTPS、登录凭证，引用 Contracts 与 Host |
| `V1/src/Hifc.Server/` | ASP.NET Core API、Razor Pages、账号、授权、私有 RulePack，引用 RuleEngine |
| `V1/tests/Hifc.Protocol.Tests/` | 双运行时 DTO、规范化序列化、分页与签名边界 |
| `V1/tests/Hifc.RuleEngine.Tests/` | Linux/Windows 上同一规则的等价及原始样本验证 |
| `V1/tests/Hifc.Server.Tests/` | 使用实际 PostgreSQL 容器的注册、授权、管理员、API 测试 |
| `V1/tests/Hifc.Web.Client.Tests/` | 延迟、取消、凭证、过期响应与签名许可 |
| `V1/tests/Hifc.Package.Tests/` | ZIP 清单、程序集依赖/资源、配置范围检查 |
| `V1/deploy/linux/` | Dockerfile、compose、Caddyfile、宿主 bootstrap、备份/回退脚本 |
| `V1/tools/admin/` | Windows 预检、部署、回退、备份拉取和恢复操作入口 |
| `.github/workflows/hifc-web.yml` | Linux 服务/规则及包内容检查；不替代 Revit 实机 |

### 接口约定

下面第一个接口在 `Hifc.Contracts`，后两个接口及 RulePack 类型在 `Hifc.RuleEngine`；命名在所有任务中一致：

```csharp
interface IModelRulesBackend {
    Task<CapturePlan> StartCheckAsync(ModelInventory input, CancellationToken ct);
    Task<CheckResult> CompleteCheckAsync(CheckCapture input, CancellationToken ct);
    Task<EditPlan> PreviewAsync(CheckResult basis, EditDraft[] drafts, CancellationToken ct);
    Task<WritePermit> AuthorizeWriteAsync(EditPlan plan, CancellationToken ct);
    Task<ExportPlan> PrepareExportAsync(ExportScope scope, CancellationToken ct);
}
interface IRulePackStore { RulePack Load(string ruleVersion); }
interface IRuleEngine {
    CapturePlan CreateCapturePlan(ModelInventory input, RulePack pack);
    CheckResult Evaluate(CheckCapture input, RulePack pack);
    EditPlan BuildEditPlan(CheckResult basis, EditDraft[] drafts, RulePack pack);
    ExportPlan BuildExportPlan(ExportScope scope, RulePack pack);
}
```

接口 DTO 均只包含普通值对象；实例/类型 Owner、PropertySet、Binding、Source、RawType、RawValue、Unit、HasValue、ReadOnly、SourceSignature 分开传递，0、false、空字符串、未填写、缺参数保持区别。`RulePack` 只存在 RuleEngine，不进入客户端 Contracts 的公开返回类型。

所有请求响应都有 `ProtocolMajor`、`ClientVersion`、`RuleVersion`、`RequestId`、`DocumentSessionId`、`ScanId`、`SnapshotHash`。读取和写入响应只接受完全匹配当前会话、扫描世代和协议的结果。

本地 `EditDraft` 与短期 `EditPlan`、`WritePermit` 分开持久化。草稿记录文档键、构件 UniqueId、属性归属/GUID或绑定键、原值及来源签名、人工拟写值、关联类目与规则版本。`DraftRebaseResult` 包含 `Reusable`、`Conflicts`、`InvalidTargets`；`Conflicts` 保留原值、当前值、拟写值供人工处理。这些类型定义在 `Hifc.Contracts/EditDtos.cs`，不包含私有规则内容。

REST 前缀固定 `/api/v1`：

| 方法 / 路径 | 输入 / 输出 | 权限 |
|---|---|---|
| `GET /compatibility` | 客户端/协议支持范围、服务器时间、产品/规则版本 | 匿名，不能返回规则正文 |
| `POST /accounts/register` | phone、realName、客户端自动凭证；AccountSession | 普通登记，服务端固定角色 User |
| `POST /sessions` | phone、自动凭证；AccountSession | 凭证验证，不凭姓名取回账号 |
| `POST /accounts/rebind` | phone、一次性码、新凭证 | 码单次使用，期限不变 |
| `GET /me` | 本人姓名、号码、期限、服务器时间 | 已登录，到期账号也可用 |
| `POST /checks/start` | ModelInventory 元数据页；CapturePlan | 有效账号，分页完全后生成取数要求 |
| `POST /checks/{id}/capture-pages` | 参数取数页、页序/散列 | 同一账号、会话和完整性清单 |
| `POST /checks/{id}/complete` | 页清单；CheckResult | 有效账号且两轮数据完整 |
| `POST /edit-plans` | 扫描、草稿；签名 EditPlan | 有效账号、扫描未过期 |
| `POST /edit-plans/{id}/authorize` | 方案散列、幂等键；WritePermit | 重新核对 DB 截止时间，许可 30 秒 |
| `POST /write-receipts` | 本机事务/回读结果摘要 | 本人原许可；到期后仍可补交已有回执 |
| `POST /export-plans` | 当前实际导出范围；ExportPlan | 有效账号，不发完整目录 |
| `GET /admin/users`、`POST /admin/users/{id}/extend` | 用户列表、days 或 untilDate、rowVersion | 仅管理员 Cookie + CSRF |
| `POST /admin/users/{id}/rebind-code` | 一次性绑定码 | 仅管理员，10 分钟过期 |
| `GET /health/live`、`GET /health/ready` | 最小存活状态；就绪检查 DB/规则/密钥 | 对外最小结果，详细信息仅管理员 |

绑定码、客户端凭证属于程序持有的秘密，不是第三个注册表单字段。管理员在 `/admin` 单独初始化并认证；用户不必输入额外密码。此派生实现建议需随计划整体审阅。

## D. 实施任务

### Task 0：隐私发布边界与基线封存

**Files:** Create `V1/validation/0.5.0-planning/baseline.json`、`V1/tools/Test-RepositoryPrivacy.ps1`；Modify `.gitignore`、`V1/.gitignore`；Test `V1/tests/Hifc.Package.Tests/RepositoryPrivacyTests.cs`。

**Interfaces:** Consumes 当前远端及基线；Produces 已验证 private 的仓库状态、基线安装包哈希和可恢复版本，不更改规则内容。

- [ ] 先保存 `git status --porcelain=v1`、HEAD、当前安装 DLL/manifest 哈希和三个源 RVT 哈希，输出到本地受控验收目录，不把真实用户资料打入 Git。
- [ ] 用真实元数据验证 `public` 必须让 `Test-RepositoryPrivacy.ps1` 返回失败，不能把有管理员权限误认为仓库已私有。
- [ ] 经整份计划获得执行授权后，将 `ArchitectureWorld/BIM-baogui` 改为 private。可用 `gh repo edit ArchitectureWorld/BIM-baogui --visibility private --accept-visibility-change-consequences`；CLI 网络失败时通过 GitHub 页面/API完成，不跳过核验。
- [ ] 登录态元数据必须为 private；未登录访问源码、Release/Actions 下载不能继续公开读取。说明旧副本不能撤回，清点现有公开安装包，不再公开发布 Local/Server。
- [ ] 加入忽略项：`*.env`、真实 `secrets/`、备份、用户登记、私钥、部署回执中的秘密。既有冻结规则继续保存在私有仓库，不能仅靠 .gitignore 隐藏已经提交的内容。
- [ ] 运行 `pwsh -File V1/tools/Test-RepositoryPrivacy.ps1 -Repository ArchitectureWorld/BIM-baogui`，期望 `visibility=private; failures=0`；最后 `git diff --check`，仅提交本任务文件，保留 0.4.2 基线包供回退。

### Task 1：两台机器的开发和容器环境

**Files:** Create `V1/tools/Get-DotNet.ps1`、`V1/tools/admin/Test-HifcHosts.ps1`、`V1/deploy/linux/bootstrap-host.sh`、`V1/deploy/linux/host-preflight.sh`、`V1/global.json`；Test `V1/tests/deploy/test_host_preflight.sh`。

**Interfaces:** `Test-HifcHosts.ps1 -SshHost zetta-pc -ExpectedUser zetta -ExpectedHostname pc` 输出结构化诊断；`bootstrap-host.sh --apply` 只安装所列包和创建 HIFC 路径。默认模式仅检查。

- [ ] 写失败用例：无 Docker、无 Compose、sudo 不可用、8443/9443 占用、SSH 身份不符、路径已存在且归属不符，必须分别报出原因，不把它们统称为连接失败。
- [ ] 对当前真实环境运行只读预检，期望 Docker/Compose 缺失和需要管理员授权被正确列出。先检查现有包是否还有 Snap/Podman 等安装，不能直接卸载冲突包。
- [ ] Windows 安装独立 SDK：下载微软 `dotnet-install.ps1`，执行 `-Version 10.0.402 -InstallDir "$env:USERPROFILE\.dotnet-hifc"`；用该目录 `dotnet.exe --version` 验证。项目 `global.json` 固定 10.0.402，`Get-DotNet.ps1` 明确选择此 SDK，不改全局 PATH。
- [ ] Linux 由用户在 Windows 执行 `ssh -t zetta-pc`，自行输入 sudo 密码运行审阅过的 bootstrap。不要设置全账号免密 sudo，也不要把密码交给脚本参数。
- [ ] bootstrap 先备份 apt 源配置、监听端口和现有网络过滤规则；确认 Ubuntu noble/amd64。安装 `ca-certificates curl`，创建 `/etc/apt/keyrings/docker.asc`，按 Docker 官方仓库设置 `docker.sources`，然后安装 `docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin`，启用 `docker.service`。步骤与退出码逐一记录；失败即停。
- [ ] 安装命令的依据固定为 <https://docs.docker.com/engine/install/ubuntu/>；执行时记录 apt 实际包版本。调用 `sudo docker version`、`sudo docker compose version`、`sudo docker run --rm hello-world`，都成功才算 Docker 可用；不只检查二进制存在。
- [ ] 创建 `/srv/hifc/{releases,config,secrets,rules,data,backups-staging,logs}` 及 data 子目录；逐路径检查不是已有服务目录或符号链接。秘密目录 0700，普通用户不授予通用 Docker 管理组权限。应用数据目录归属按锁定镜像的实际非 root UID/GID设置并验证读写。
- [ ] 再检查 SSH、既有 3000/3080/8787/9749/RDP 服务，记录与安装前差异；HIFC 还未运行，不开放新业务端口。
- [ ] Windows验证命令：`pwsh -File V1/tools/admin/Test-HifcHosts.ps1 -SshHost zetta-pc`；Linux 验证命令：`sudo /srv/hifc/releases/bootstrap/host-preflight.sh --expect-docker --expect-free-ports 8443,9443`；期望 `failures=0`。提交脚本和脱敏回执结构。

### Task 2：中立协议、身份字段和分页完整性

**Files:** Create `V1/src/Hifc.Contracts/{Hifc.Contracts.csproj,ModelDtos.cs,EditDtos.cs,AccountDtos.cs,IModelRulesBackend.cs,SnapshotCanonicalizer.cs}`；Test `V1/tests/Hifc.Protocol.Tests/{Hifc.Protocol.Tests.csproj,SerializationTests.cs,CompletenessTests.cs}`；Modify `V1/Directory.Build.props`。

**Interfaces:** 产出 C 节的 DTO 与 `IModelRulesBackend`。`SnapshotCanonicalizer.Hash(CheckCapture): string` 对规范字段顺序、单位和值状态生成 SHA-256；不使用与序列化器默认顺序相关的字符串作为散列依据。

- [ ] 写用例：`0`、`false`、空字符串、null、HasValue=false、缺参数、同名不同 Pset、类型 Owner、m² / 内部面积单位在 net48 与 net10 往返后保持语义；中文名称和空数组也必须保留。
- [ ] 写用例：上传少一页、重复页内容不同、重复 ElementUniqueId、页属于另一文档/账号、总数量不符，均不得 Complete 或产生 EditPlan。
- [ ] 写 `DraftRoundTripPreservesBaselineAndManualValue`：草稿序列化、程序重开和登录凭证更换后，文档/构件/字段身份、原值、人工值及来源签名不变；不持久化可复用的过期许可。
- [ ] 协议首版 `ProtocolMajor=1`。单页 JSON上限 4 MiB，最多 500 个元素，按字节再拆分；单次模型最多 100000 个记录、累计 256 MiB，超限明确报错而非静默截断。中间上传会话无操作 15 分钟释放。
- [ ] 使用显式 DTO 和序列化设置；不接受任意 .NET 类型名反序列化，不把未知 Action 解释为赋值。增加稳定错误码，如 `ACCOUNT_EXPIRED`、`SNAPSHOT_STALE`、`INCOMPLETE_CAPTURE`、`PROTOCOL_UNSUPPORTED`。
- [ ] 运行 `$dotnet test V1/tests/Hifc.Protocol.Tests -c Release` 的 net48/net10 目标，期望全部通过。`$dotnet` 来自 `Get-DotNet.ps1`；提交 Contracts、协议测试和版本变更。

### Task 3：提取同一规则引擎，保持现有业务结果

**Files:** Create `V1/src/Hifc.RuleEngine/{Hifc.RuleEngine.csproj,IRuleEngine.cs,IRulePackStore.cs,RulePack.cs,FileRulePackStore.cs,RuleEngine.cs}`；从 `V1/src/Hifc.Reference.Core/{ReferenceCatalog,CaseWorkflow,RecognitionEvidence,AutoFillPolicy,ReferencePolicy,ModelChecks}.cs` 按职责迁移；Create `V1/tests/Hifc.RuleEngine.Tests/{Hifc.RuleEngine.Tests.csproj,ParityTests.cs,RulePackTests.cs}`。

**Interfaces:** `IRulePackStore.Load(ruleVersion):RulePack`；IRuleEngine 四个方法见 C 节。ModelDtos 在 Contracts；业务推导、案例内容、完整目录只在 RuleEngine。显式加载 RulePack，不能出现 Web 客户端找不到规则时回退嵌入基线的路径。

- [ ] 先从 0.4.2 的三个模型历史快照及已有识别样本导出中立测试输入，固定文件哈希；以原引擎结果为基线比较角色、字段、来源、自动值、错误和写入动作。
- [ ] 对归档基线0.4.3新增的选中构件属性编辑、停车Area原生导出与IFC标识核对建立回归清单，读取该版本已有验证说明并补足新拆分后的实测；不只对旧0.4.2快照比较后就宣布完整等价。
- [ ] 迁移前先让跨平台测试因缺少新引擎而失败，再替换 `System.Web.Script.Serialization`，保留冻结映射/案例输入和原始类型拼写。所有新增包使用中央版本管理并提交 packages.lock.json；在双运行时验证后锁定，不用浮动包版本。
- [ ] FileRulePackStore 校验 manifest、原文件 SHA-256、路径范围和 RuleVersion；未知版本、缺文件、篡改、目录穿越返回失败，不能静默换成另一版本。
- [ ] 测试现有规则：道路宽度才手填标红、Area 停车位、首层建筑基底、已有 false、构件类型共享、单体信息预置、别名需核对、施工图不套报规规则。
- [ ] 在 Windows 与 Linux SDK 容器分别跑 `Hifc.RuleEngine.Tests`；相同输入得到相同语义结果及规范散列。记录新引擎覆盖了哪些原 85 项，不把旧测试数量当成必须凑齐的数字。
- [ ] 提交迁移代码与测试，不删除现有可恢复 0.4.2 包，不把业务规则顺带重写。

### Task 4：Revit 公共宿主与完整采集适配

**Files:** Create `V1/src/Hifc.Revit.Host/{Hifc.Revit.Host.csproj,Capture/ModelInventoryReader.cs,Capture/ParameterCaptureReader.cs,Writes/RevitWriteExecutor.cs,Drafts/DraftRebaser.cs}`；迁移并修改现有 `Management/{CaseModelReader,CaseEdits,ManagerController,ManagerWindow,ManagerUiModels,ManagementState}.cs`、`RevitMappingService.cs`；调整 `V1/tests/Hifc.Ui.Tests` 链接路径并新增 `DraftRecoveryTests.cs`。

同时迁移0.4.3已提交的 `Management/{SelectionController,SelectionDraft,SelectionService,SelectionWindow}.cs`；其取数、分类方案和赋值入口同样走新后端接口，不能遗漏这个入口并保留旧Core的完整规则依赖。

**Interfaces:** `ModelInventoryReader.Read(Document, ScanContext):ModelInventory`；`ParameterCaptureReader.Read(Document,CapturePlan):CheckCapture`；`RevitWriteExecutor.Apply(Document,EditPlan,WritePermit):WriteReceipt`；`DraftRebaser.Rebase(EditDraft[],CheckResult):DraftRebaseResult`。Host 不引用 RuleEngine 或旧含资源的 Reference.Core。

- [ ] 写当前文档身份、epoch、ScanId 不匹配、同名多来源、构件删除重建、类型替换的失败测试；迟到单元格提交仍必须指向最初的行。
- [ ] 拆开当前 CaseModelReader 的“先查规则再读值”耦合：第一轮只读元数据和可靠原生几何摘要，第二轮按 CapturePlan读指定参数；完全保留原生 Area、标高、项目信息、链接覆盖报告及资源排除。
- [ ] WPF 的类目列表和字段描述来自 CheckResult/局部上下文，不再调用静态 CaseWorkflow 获取完整目录。每次检查重新收集，不让列表过滤、缓存或以前完成状态影响全量扫描。
- [ ] 把 CaseEdits 的纯业务方案生成移到 RuleEngine；保留 Owner/GUID/实例类型绑定校验、旧值签名、单位转换、共享参数定义、事务组、回读及故障回滚在 RevitWriteExecutor。不能把任意服务器“代码”当 C# 执行。
- [ ] HTTP 的异步工作只接触不可变 DTO，结果通过 ExternalEvent应用；同一文档一次扫描一个世代，可取消，旧响应永不替换新扫描。
- [ ] 写 `ExpiredPlanPreservesDrafts`、`UnchangedFieldRebasesManualValue`、`ChangedOwnerOrValueRequiresConflictResolution`、`DeletedElementDoesNotRetargetByIntegerId`：编辑20分钟或关闭重开后仍有草稿；重新全检只复用目标、原值及来源签名均一致的修改；冲突显示原值/现值/拟写值并阻止写入，不能以自动建议覆盖人工值。
- [ ] 写 `OfflineOrExpiredAccountCanSaveExistingDrafts`：已有检查字段可继续编辑和保存，新增服务器检查、取数方案与写入不可用；需新类目字段时明确等待服务。复检成功后仅按已核实的稳定身份恢复草稿。
- [ ] WPF 测试继续单独编译真实 UI 源码与 FakeSession，不要求 GitHub 托管机拥有 Revit API；Revit Host 的编译与实机测试留在当前 Windows。
- [ ] 运行 UI 回归、宿主构建和只用临时模型的守卫测试。提交 Host 拆分及测试适配，不在这一步安装运行中的 Revit 插件。

### Task 5：交付可独立工作的 Local 版

**Files:** Create `V1/src/Hifc.Revit.Local/{Hifc.Revit.Local.csproj,LocalRulesBackend.cs,LocalComposition.cs}`；Modify `V1/tools/{Build.ps1,Install-Revit2020.ps1}`；Create `V1/tests/Hifc.Native.Tests/EditionParityAcceptance.cs`。

**Interfaces:** LocalRulesBackend 实现 IModelRulesBackend，调用同一 RuleEngine；Local 的写入守卫不依赖网络授权。编译期版别固定，不通过用户可编辑 settings.json 将 Web 切成 Local。

- [ ] 用同一 Capture 输入验证 Local 后端与 RuleEngine 结果一致；断网下检查、编辑、写入、保存重开可用。
- [ ] 统一显示名与安装包 `HIFC-0.5.0-Local`；保留现有四步界面及模型暂存，增加当前版别/规则版本显示。Local 私有规则随包，发行目录仅管理员可取。
- [ ] 安装器只启用一个 HIFC 版别，备份旧 manifest、DLL、profiles，检测 Revit 占用并要求正常退出；其他插件哈希保持不变。
- [ ] 在三个模型副本跑 Local 实机验收，包含分类清理、自动值、重复手改、原子写入、注入故障回滚和保存重开。对原始模型再次核对哈希。
- [ ] 更新 edition-aware 的 Native harness，不再硬编码只查找旧 `Hifc.V1.Revit` assembly；把生产 Host 与组合 DLL 的实际路径、版本和 SHA-256写入回执。
- [ ] Local 三模型通过后才合入稳定主线，保留 0.4.2 回退包。Web 尚未完成不影响这个可独立验收的里程碑。

### Task 6：账号数据库、168 小时与自定义延期

**Files:** Create `V1/src/Hifc.Server/{Hifc.Server.csproj,Accounts/Account.cs,Accounts/AccountService.cs,Accounts/ExpiryPolicy.cs,Accounts/DeviceCredentialService.cs,Accounts/IServerClock.cs,Accounts/PostgresClock.cs,Data/HifcDbContext.cs,Data/Migrations/}`；Test `V1/tests/Hifc.Server.Tests/{Hifc.Server.Tests.csproj,RegistrationTests.cs,ExpiryTests.cs,AdminAuthorizationTests.cs}`。

**Interfaces:** `RegisterAsync(RegisterRequest):AccountSession`；`AuthenticateAsync(SessionRequest):AccountSession`；`ExtendAsync(Guid userId,ExtensionRequest request,OwnerIdentity owner):AccountView`；`RequireValidAsync(Guid userId):AuthorizationDecision`。AccountView 仅两种时间状态；数据库无用户停用列、试用等级或普通用户套餐字段。

- [ ] 实际 PostgreSQL 容器中建唯一 phone 索引。写“100 个并发相同手机号登记只生成一行、一次 RegisteredAt/ExpiresAt”的失败用例；同凭证幂等、不同凭证失败。
- [ ] `IServerClock.UtcNowAsync` 在生产只绑定查询PostgreSQL时钟的 PostgresClock；固定日期测试通过测试进程DI覆盖，不读取客户端时间、不提供生产环境改时钟开关。数据库集成测试额外断言实际 RegisteredAt 与实际 ExpiresAt 相差168小时。
- [ ] 测试 `2026-10-16T02:00:00Z` 登记，普通账号到 `2026-10-23T02:00:00Z` 排他截止；边界前 1 毫秒有效，恰好等于截止时刻无效。
- [ ] 测试北京时间 `2026-10-23` 的日期延期存为 `2026-10-23T16:00:00Z`（北京时间 24 日零点）；浏览器显示有效至 23 日结束。
- [ ] 测试未到期/已到期按天延长、0/负数/非整数/超过36500天拒绝、日期未延长拒绝；并发管理员编辑需 rowVersion，冲突时不覆盖。
- [ ] 普通注册请求携带 role=Admin、ExpiresAt 或 RegisteredAt 必须拒绝；没有任何“手机号等于管理员所以自动升级”的路径。初始化单一 Owner 后关闭初始化入口。
- [ ] 实现客户端随机凭证的服务端摘要验证、一次性绑定码（10 分钟、单次使用）与凭证版本。重绑不改 RegisteredAt/ExpiresAt，旧凭证立即无法再次换取会话。
- [ ] JWT 15 分钟有效，但每个检查、方案和写入许可都再查数据库期限及凭证版本；账号到期或重绑后已有旧 JWT 不绕过限制。首次登记的服务器 UTC 时间不得来自客户端。
- [ ] 最小模型流程也使用本任务真实认证及授权代码。测试账号由受限初始化入口建立，使用虚构登记资料和受保护凭证；不把凭证写进源码、镜像或安装包，不为先验证模型而开放匿名规则/写入接口。
- [ ] 运行 `$dotnet test V1/tests/Hifc.Server.Tests -c Release --filter "Registration|Expiry|AdminAuthorization"`，Docker DB 测试退出码0才提交实体、迁移和服务。该任务不添加短信或企微依赖。

### Task 7：Windows 浏览器管理后台

**Files:** Create `V1/src/Hifc.Server/{Pages/Admin/Index.cshtml,Pages/Admin/Users.cshtml,Pages/Admin/UserDetail.cshtml,Pages/Admin/System.cshtml,Pages/Admin/Login.cshtml,Owner/OwnerBootstrap.cs,Owner/OwnerAuthentication.cs}`；Test `V1/tests/Hifc.Server.Tests/AdminPageTests.cs`；Create `V1/tests/browser/{package.json,package-lock.json,playwright.config.ts,admin.spec.ts}`。

**Interfaces:** 管理页面调用 Task 6 的 ExtendAsync；初始化 CLI 只从交互/受限 secret 文件接收用户本人资料，创建唯一 Owner，返回一次性初始化状态，不打印密码。普通用户没有后台 Cookie。Owner是角色Admin的唯一实例，不是第三种角色。

- [ ] 写失败测试：匿名和普通账号不能查询用户列表、延长自己、生成绑定码或调用初始化；CSRF 缺失和旧 rowVersion 拒绝，用户中文姓名不能成为 HTML/脚本注入。
- [ ] 实现后台只含真实姓名、手机号、注册时间、到期时间、有效/已到期、自定义天数、日期、重新绑定；只通过用户联系渠道申请，不加入停用/审批/试用按钮。
- [ ] 自定义天数与日期两种输入互斥，提交前展示原到期与新到期，保存后从服务器回读；列表可按手机号、姓名及到期状态检索。
- [ ] Owner Cookie 使用 Secure、HttpOnly、SameSite=Strict，所有更改操作验证 CSRF；Data Protection密钥持久化，进程重启不会因为临时密钥丢失而破坏登录数据。
- [ ] 系统页只向管理员显示服务版本、协议/规则版本、备份时间、最近失败摘要和两个管理入口；普通错误页不暴露连接字符串、文件系统路径或完整规则。
- [ ] 用当前 Windows 浏览器在实际 Linux 部署后复验：新增普通账号、任意天数、指定日期、页面刷新和服务重启后值不变。保存 HAR/截图时清除 Cookie、令牌和真实手机号。
- [ ] 先运行服务测试，再运行 `npm --prefix V1/tests/browser test -- admin.spec.ts`；浏览器测试依赖固定在测试目录，服务器不需要 Node。提交后台和测试。

### Task 8：规则 API、分段取数及 Web 客户端

**Files:** Create `V1/src/Hifc.Server/{Checks/CheckSessionService.cs,Checks/ChecksEndpoints.cs,Checks/PublicResultProjection.cs,Plans/PlanEndpoints.cs}`、`V1/src/Hifc.Revit.Web/{Hifc.Revit.Web.csproj,WebComposition.cs,WebRulesBackend.cs,Auth/DeviceCredentialStore.cs,Auth/RegistrationWindow.cs}`；Test `V1/tests/Hifc.Web.Client.Tests/{CaptureFlowTests.cs,DocumentEpochTests.cs,CredentialTests.cs}`、`V1/tests/Hifc.Server.Tests/CheckFlowTests.cs`。

**Interfaces:** WebRulesBackend 实现同一 IModelRulesBackend；`DeviceCredentialStore.Save/Load/Delete` 仅保存 DPAPI CurrentUser 保护的数据；`CheckSessionService` 按 userId + DocumentSessionId + ScanId 隔离上传；PublicResultProjection 只返回当前模型适用字段与可解释证据，不返回整库文件、内部路径和其他案例的原始记录。

- [ ] 写测试：用户 A 的扫描页、方案或结果不能被用户 B 读取/追加；一模型未收齐参数页时不可 Complete；不同文档相同数字 ElementId 不能共享状态。
- [ ] 首次注册前在本机生成随机凭证并安全保存，HTTPS Register 提交手机号、姓名与程序凭证。相同凭证重试不新增授权；账号已存在且本机没有原凭证时引导“已有账号 / 更换电脑”，不直接发出他人登录凭证。
- [ ] 注册表单只有两项，登录后自动保持当前用户状态，显示真实姓名和准确到期时间；到期显示联系管理员，已有字段仍可编辑并保存草稿。用户退出时清除当前会话，凭证恢复遵循设计的重绑流程；草稿身份不依附可过期的会话令牌。
- [ ] 接入元数据 → CapturePlan → 参数页 → CompleteCheck 的两轮流程。以当前实际模型发取数要求，不下发整个 catalog 让客户端自行选择。缺页、超限、取消、同一页不同内容都明确失败。
- [ ] Web 请求 10 秒连接超时、60 秒单请求总超时；仅兼容性查询、状态查询、同幂等键上传可最多重试 2 次，间隔 1/3 秒并遵守 Retry-After；修改到期日、取得写入许可不能用新幂等键盲重试。取消保持可立即响应。
- [ ] 不可变 DTO 在后台发送；继续操作 Revit、切换文档、开始新扫描时，旧结果按 DocumentSessionId/ScanId/epoch 丢弃。错误回复显示网络/账号/协议问题，不能把它们当模型缺失或检查通过。
- [ ] PublicResultProjection 对规则来源作限定展示；Web 返回的字段定义满足本次属性编辑，原参考软件的完整案例记录、内部文件路径和完整配置不进入响应。正常用户仍能看到本次检查的依据说明。
- [ ] 验证浏览器后台、WPF 和网络日志都不输出令牌、原始设备凭证、完整手机号列表或整个请求包；测试用虚构手机号，不把同事真实信息写入测试夹具。
- [ ] 运行 Web 客户端与服务端检查流程测试，并用 Linux 服务处理一份真实模型采集副本；比较本次 Web 与 Local 字段、分类、建议值的语义结果，私有调试来源文字单独投影比较。提交 API 和 Web 组合入口。

### Task 9：写入与 IFC 导出的完整远程链

**Files:** Create `V1/src/Hifc.Server/{Plans/EditPlanService.cs,Plans/WritePermitService.cs,Plans/ExportPlanService.cs,Security/PlanSigner.cs}`、`V1/src/Hifc.Revit.Web/Writes/WebPermitValidator.cs`；Modify Host 中 `RevitWriteExecutor.cs` 和从现有 `NativeExportService.cs` 拆出的 `Export/RevitExportExecutor.cs`；Test `V1/tests/Hifc.Server.Tests/PlanAuthorizationTests.cs`、`V1/tests/Hifc.Native.Tests/WebWriteAcceptance.cs`。

**Interfaces:** 签名 EditPlan 绑定账号、文档、完整快照、规则版本、旧值基线、Actions、PlanId、有效期；`AuthorizeWriteAsync` 返回最长30秒且不越过账号截止时间的 WritePermit。采用标准 RS256/JWS 及成熟 JOSE 库，客户端只有公开验证密钥，私钥只在 server secrets。ExportPlan 只含当前实际导出范围的定义和物化动作。

- [ ] 先测篡改签名、Action、Owner、Source、方案散列、UserId、文档、过期规则版本，全部不能进入 Revit 事务。未知算法和未知 keyId 拒绝，不自动接受客户端提供的验证密钥。
- [ ] EditPlan 120 秒有效，WritePermit截止取 `min(now+30秒, account.ExpiresAt, plan.ExpiresAt)`。许可调用时检查数据库期限和凭证版本；同幂等键可重取同一许可但不延长到期，不同账号或方案散列不能复用许可。方案过期不清除草稿，先重新全检及处理冲突，再生成新方案和许可。
- [ ] WritePermit记录存PostgreSQL，唯一约束为账号+PlanId+幂等键，保存计划散列、文档/扫描、期限及已回执标记；不把完整RVT或取数包长期入库。收到许可后客户端以服务器时间与单调计时核验剩余时长，Windows墙钟修改不延长许可。
- [ ] 点击写入后先完成网络授权，再通过 ExternalEvent 对全部来源重读；在实际事务开始前再次检查方案及许可剩余时长。分类、旧类目清理、新类目自动值和人工值在一个事务组提交；原定义、共用字段和其他实例类型值保留。
- [ ] 写 `PermitExpiresBeforeTransactionRejects`、`ExpiryAfterTransactionStartKeepsAtomicOutcome`、`ConnectionLostAfterTransactionStartDoesNotReplay`：开始前过期不改模型；合法开始后即使许可/账号到期或网络断开，也按本机完整回读结果提交或整体回滚。事务内不发HTTP，许可在本机按PlanId标记消费，不能重用于下一次事务。
- [ ] 实测写入后用户又修改、删除重建、重复点击、网络延迟、许可已过期、模型切换、参数只读与同名歧义；每种情况都应明确拒绝或整体回滚，不能写一部分后标成功。
- [ ] 成功写入必须回读原值/新值并开始全面复检；已成功项写入人工历史并清除对应待写草稿，未完成项保留。回执上传失败可补交但不能重新执行模型写入。复检网络失败或账号恰好到期时显示“已写入，全面复检未完成”，恢复连接只补回执和复检。
- [ ] `WriteReceipt`分别记录本机事务/回读、服务器回执、全面复检及RVT保存状态。默认写入不自动保存用户模型，不把Revit内存中成功当成文件已落盘；实际模型验收另行执行保存与重开。
- [ ] 当前 `NativeExportService` 的全量 `BuildExportConfiguration()` 调用改为使用 ExportPlan 限定配置；桥接 GUID仍按 Pset/Binding/Property 精确路由，Revit 临时视图/参数及实例改动完成后回滚。
- [ ] Web 版所有入口（主窗口、准备映射、取数、工规导出、隐藏开发按钮）统一走受控后端；没有本地完整规则 fallback。仅允许无私有目录的本机几何读取和既有数据查看离线执行。
- [ ] 三个模型副本分别验证写入、清理、回滚及保存重开；IFC 用既有工具检查结构和值。服务端计划或窗口回读通过不作为原参考软件 A/B 或平台验收结论。提交执行器与真实回执。

### Task 10：自动化回归、时序和故障覆盖

**Files:** Create/extend `V1/tests/Hifc.Server.Tests/{ExpiryDuringWriteTests.cs,ApiFailureTests.cs}`、`V1/tests/Hifc.Web.Client.Tests/ResponseOrderingTests.cs`、`V1/tests/Hifc.Ui.Tests/WebWorkflowTests.cs`；Modify `.github/workflows/hifc-v1.yml`；Create `.github/workflows/hifc-web.yml`、`V1/tools/Test-WebAll.ps1`、`V1/tools/Test-WebAll.sh`。

**Interfaces:** `Test-WebAll` 输出含 suite、版本、平台、失败数和证据位置的 JSON；缺少 Docker/Revit/证书时返回明确不可验证，不跳过后伪称全绿。

- [ ] 写并运行5类输入：手机号重试/换机；截止时刻前后；异步乱序/模型切换；全量数据泄漏路径；两机部署与恢复。每类至少有一个能使实现错误时失败的行为断言。
- [ ] 包括长时间打开窗口直到到期、已签发 JWT 未过期但账号过期、取得方案后管理员延长期限、写成功但复检失败、重复幂等授权返回丢失等时序。
- [ ] 加入20分钟草稿恢复、仅部分字段冲突、许可在ExternalEvent排队期间到期、事务中到期及断网、恢复联网不重放写入，以及“已写入但未保存RVT”的明确状态；自动测试使用可控测试时钟，不真实等待20分钟。
- [ ] Windows CI 用 SDK10 构建协议的 net48/net10 与真实独立 WPF 测试；现有 Python IFC 回归保留。Linux CI 用 SDK10 跑规则引擎、真实 PostgreSQL 服务测试与镜像/包扫描。
- [ ] 不在 CI 日志输出真实 secrets，也不将备份、管理员初始化文件和 Local 完整包作为可给普通用户下载的 artifact。回执先脱敏再归档。
- [ ] 当前 Windows 的 Revit 构建和模型 E2E独立记录。WPF RaiseEvent 测试明确为输入事件回归，真实鼠标点击/双击/编辑/下拉另做人工或可用自动化操作，不互相冒充。
- [ ] 运行 `$dotnet test ...` 的相关完整套件、实际UI exe、Python unittest 与 Docker集成测试，输出对应 exit=0。已经通过的套件只有实现变化/故障时重跑，不无依据重复全量测试。
- [ ] 每次任务以小范围 git add/commit 提交本任务代码，禁止 git add -A 吞入其他工作或用户文件。提交和 CI 记录对应确切 HEAD。

### Task 11：两种插件安装包和私有服务端发布

**Files:** Create `V1/tools/{Build-Editions.ps1,Test-WebPackage.ps1,Install-Edition.ps1}`、`V1/tests/Hifc.Package.Tests/{AssemblyBoundaryTests.cs,ExportLeakTests.cs}`、`V1/deploy/linux/release-manifest.schema.json`；Modify `V1/src/*/*.csproj` 的输出与引用、`V1/Directory.Build.props`、安装说明。

**Interfaces:** `Build-Editions.ps1 -Version 0.5.0` 生成两个 ZIP、Server 部署包和每件文件 SHA-256；`Install-Edition.ps1 -Edition Local|Web -Version 0.5.0` 只替换本产品清单并可恢复0.4.2。

- [ ] 写失败包：表面不含 JSON 但 DLL 嵌入 contract/case资源，间接引用 RuleEngine，settings指向源目录，包含管理员密钥，全量导出 TSV 或错误 fallback；这些都必须让 Web 包测试失败。
- [ ] 对 Web ZIP 和所有托管依赖做引用图/嵌入资源检查；允许仅当前模型生成的临时导出配置，禁止随安装包预置完整规则、案例、源码或 Local 私有 DLL。
- [ ] 显示产品名、文件版本、ZIP 文件名、安装器与后台下载项均为 `HIFC-0.5.0-Local` / `HIFC-0.5.0-Web`，程序集内部名称可稳定，不把“Web”解释成纯浏览器可以直接操作 Revit。
- [ ] 同一 Revit年只启用一个HIFC版别，备份旧文件和manifest，正常退出宿主后安装。登录凭证在当前用户保护区，模型草稿与登录身份分开，迁移/重绑不清除草稿。
- [ ] `https://192.168.6.45:8443/downloads` 只给有效用户 Web 包和公开CA证书；Local、Server、rules、secrets不在站点静态根目录。到期用户只看到版本及联系方式，不给新服务授权。
- [ ] 执行 `pwsh -File V1/tools/Test-WebPackage.ps1 -Path V1/dist/HIFC-0.5.0-Web.zip`，期望 bannedAssemblies=0、bannedResources=0、secretFiles=0、failures=0；再解压到干净目录验证实际加载依赖和安装回退。
- [ ] 通过后提交打包工具与清单，不把含私有规则的 Local 包上传到普通用户可访问处。

### Task 12：Linux 四容器部署与 Windows HTTPS 访问

**Files:** Create `V1/deploy/linux/{Dockerfile.server,compose.yaml,compose.ops.yaml,Caddyfile.core,Caddyfile,install-release.sh,check-network.sh,release-lock.json.template}`、`V1/tools/admin/{Deploy-Hifc.ps1,Test-HifcDeployment.ps1}`。

**Interfaces:** `Deploy-Hifc.ps1 -SshHost zetta-pc -Version 0.5.0 -Bundle <ServerPackage> -Mode Core|Full`；`install-release.sh --bundle <path> --expected-sha256 <hash> --mode core|full` 验证归档和镜像，部署项目 `hifc`，不能接受任意shell片段；`Test-HifcDeployment` 接受相同Mode并从当前Windows发真实 HTTPS 请求。

本任务分两次实施：最小模型流程使用Task 11构建并检查的Server/Web包，Core模式只启用HIFC、PostgreSQL、Caddy三个容器及8443；管理后台完成后以Full模式增加Portainer和9443。`compose.yaml`为三容器基础，`compose.ops.yaml`为运维覆盖，分别选择 `Caddyfile.core` 与完整 `Caddyfile`，不把尚未启动的运维入口标为通过。正式发包必须完成Full验收。

- [ ] Dockerfile多阶段构建，SDK10.0.402构建、ASP.NET10运行；hifc-server以官方非root app用户运行，只读根文件系统，写入仅指定挂载点/临时目录，设置内存/CPU边界。初始总资源预算应用2CPU/2GiB、DB2CPU/2GiB、Caddy与Portainer合计1CPU/1GiB；5并发负载后依据实测调整，不声称已经有性能保证。
- [ ] PostgreSQL用户仅有本库所需权限；凭据经受限 secret 文件挂载，Compose文件与镜像层中没有明文密码。服务首次启动完成Owner初始化前不开放普通登记。
- [ ] Linux创建独立 `releases/0.5.0`、规则版本目录和持久化卷，拒绝路径穿越/绝对归档项/跨目录符号链接。上传前确认本地文件存在，上传后用SHA-256与本地比较；由用户在Windows SSH终端完成所需sudo步骤。
- [ ] 先 `docker compose -p hifc ... config --quiet`，再按Core或Full选择对应Compose文件，拉取锁定镜像并启动所需服务。记录模式、容器实际imageId、digest、监听端口和健康状态；Core不能发布9443，Full必须完成运维入口来源限制。
- [ ] Caddy业务监听容器443，映射LAN8443；运维监听容器9443，映射LAN9443。显式启用 `tls internal`，只绑定设计的LAN地址，禁用无用公开HTTP入口。业务请求与管理Cookie的代理头只信任Caddy，不信任任意客户端伪造的 Forwarded/X-Forwarded-*。
- [ ] 从Caddy持久卷导出**公开**根证书，Windows显示SHA-256指纹；用户确认后导入CurrentUser Root，再验证浏览器和.net48 HttpClient。错误证书、错误SAN、未知CA均应失败；不使用 `-SkipCertificateCheck` 或始终返回true的验证回调。
- [ ] Docker采用官方默认iptables后端，在DOCKER-USER中跳转到本项目HIFC专用链，使用conntrack原目标IP/端口匹配8443/9443，防止DNAT后把443误当外部8443；不清空其他链。8443允许显式测试机IP，9443仅当前管理员IP，保留既有22/RDP与其他服务。IPv4/IPv6监听都核查；第二台普通机可以访问业务，不能访问9443。若IP更换，预检提示更改受控允许列表，不自动放开全网。
- [ ] Core先用真实受认证请求及 `health/ready` 核实 DB、规则版本和签名密钥均可用。Full浏览器实际打开 `/admin` 与9443，完成Owner账号和Portainer唯一管理员初始化，记录仅非秘密回执；不能以主页200代替业务就绪。
- [ ] 从当前Windows注册虚构测试账号，调用一次规则检查并在Revit模型副本写入、回读、全检。`Test-HifcDeployment.ps1 -SshHost zetta-pc` 应报告 HTTPS、身份、版本、权限、API、源限制全部通过。
- [ ] 安装前后再核对原有服务和管理连接可用；失败时只停本项目，不能关闭Docker全局网络、清空防火墙或重启整台Linux来掩盖问题。

### Task 13：备份、Windows维护、升级和回退

**Files:** Create `V1/deploy/linux/{backup.sh,restore.sh,rollback-release.sh,hifc-backup.service,hifc-backup.timer}`、`V1/tools/admin/{Backup-Hifc.ps1,Restore-Hifc.ps1,Rollback-Hifc.ps1,Install-BackupTask.ps1}`；Test `V1/tests/deploy/{test_backup_restore.sh,test_rollback.sh}`。

**Interfaces:** backup生成带版本、DB dump、规则、配置、密钥和SHA清单的受控归档；restore只接受经过验证的本项目备份及明确目标；Windows定时拉取只复制已完成的归档。所有文件操作验证最终绝对路径留在 `/srv/hifc` 或指定备份目录，不能递归清理计算错误的路径。

- [ ] 先写失败测试：DB dump失败、第二卷没挂载、备份不完整、散列错误、密钥缺失、当前业务有写事务、恢复版本不匹配，不能更新“备份成功/恢复成功”。
- [ ] 备份使用PostgreSQL官方工具的逻辑dump，不直接拷贝运行中的data目录。核对Second卷实际source/文件系统再复制，缺卷时返回失败。临时文件完成校验后原子改名为可下载归档。
- [ ] Linux systemd timer每日北京时间02:30执行，仅操作hifc项目；保留7个每日和4个每周版本。配置文件、签名/认证密钥与CA持久化资料纳入管理员专有备份，归档不由普通静态下载路由提供。
- [ ] Windows建立 `HIFC-Backup-Pull` 定时任务03:00拉取至 `D:\HIFC-Backups\zetta-pc\`，机器未开机则下次登录补拉。Windows通过已配置SSH key执行严格限定的读操作，使用受保护目录ACL；同卷备份与跨机拉取的成功时间分开记录。
- [ ] 升级脚本流程固定：检测版本/空间/活动任务 → 暂停新写入许可并等待30秒许可窗口结束 → 完整备份 → 应用兼容迁移 → 启动指定digest → ready检查与合成检查 → 恢复接入。失败保留诊断并回退应用；不自动删除旧卷。
- [ ] 新增数据库字段采用扩展式迁移，旧应用可以读兼容schema；不可兼容迁移只有完整恢复DB+密钥+规则+镜像版本后才可标记回退完成。至少保留前两个已验证版本。
- [ ] 在独立临时Compose项目 `hifc-restoretest`、仅loopback验收端口（不接正式LAN端口）恢复一份备份，检查账号、截止日期、重绑状态、签名、CA和规则；演练完成后只清理该测试项目，不删除正式卷。并在一次明确维护窗口验证服务自动恢复/开机启动，不擅自重启承载其他工作的KVM。
- [ ] 管理员日常账号操作在 `/admin`；Portainer仅用于本项目的容器状态/日志/重启；发布/回退通过 `Deploy-Hifc.ps1`、`Rollback-Hifc.ps1`，无需用户手拼Linux命令。提交脚本、图文说明和脱敏恢复报告。

### Task 14：两机、三模型与内部成员最终验收

**Files:** Create `V1/tests/Hifc.Native.Tests/LocalWebEndToEndAcceptance.cs`、`V1/validation/0.5.0/acceptance-index.json`、`V1/docs/0.5.0-Local-Web-安装管理与验收.md`、`V1/docs/管理员日常操作.md`、`V1/docs/成员首次使用.md`；更新根 README与V1 README。

**Interfaces:** acceptance-index记录确切Git HEAD、产品/协议/规则版本、安装与服务器镜像哈希、每个用例结论与证据位置；明确自动测试、独立WPF、Revit实际模型、真实鼠标、原软件/平台的不同验证范围。

- [ ] 保留原件，复制 `D:\18_建模项目\湖北BIM云平台\测试文件\01\revit原文件\` 的总图、厂房、职工后勤楼到本轮验收目录，记录源和副本SHA-256；所有保存重开只发生在副本。
- [ ] 同一输入分别跑Local/Web：阶段确认、类型识别、全量盘点、只道路宽度标红、Area停车位、布尔/项目预置、双击定位、直接编辑已有值、分类刷新与旧值清理、保存/重开、红/橙草稿历史。比较数据与实际模型，不只比较截图。
- [ ] Windows真实鼠标完成单击已有值、再次修改已保存值、下拉、双击定位、写入、关闭重开；若自动化仍不可用，按操作清单由用户完成并记录，不能以RaiseEvent冒充鼠标完成。
- [ ] 编辑期间让方案失效，再写入时全检并保留人工输入；另外制造字段冲突，验证只要求处理冲突项。核实断网/到期仍可保存草稿，事务已开始后的网络异常不造成部分写入，复检恢复不重复写入，写入与RVT保存状态可区分。
- [ ] 以实际服务器账号时间测试：注册默认168小时、到期禁用新方案、任意天数延期、指定日期、重复注册、换机、服务器/浏览器重启后期限不变。测试时使用可控的测试数据库时钟接口或独立测试实例，不修改Linux系统时间影响其他服务。
- [ ] 注入网络断开、超时、服务器重启、响应乱序、旧JWT、模型改动、坏签名、只读参数、晚发事务故障、缺页、损坏备份；每项都验证用户数据保留及明确状态。
- [ ] 第二台内部Windows按用户实际许可范围访问8443，完成两字段登记与Revit基本流程，同时不能进入管理员后台或9443；记录其实际源IP并在发布前从测试名单整理为明确的成员机器允许列表。
- [ ] 5个同时检查会话运行同一组完整输入，记录响应耗时、超时、CPU/内存、排队、公平性和DB连接；预期无跨用户数据、无非预期500、服务不失去健康、写入许可无串用。先报告实测容量，再决定是否扩大人数。
- [ ] 执行一次实际版本回退及异机备份恢复，再通过“Windows登录 → Linux规则 → Revit写入与复检”的完整流程。只恢复进程存活不能判通过。
- [ ] 再核对三个原始RVT哈希和其他插件/服务状态；生成已通过、未通过、未验证项目清单。原软件A/B、官方平台验收仍单独记录，不借Web上线宣布完成。
- [ ] 发布前确认远端仍private，普通成员只能下载Web；用户审阅结果后再分发。移除本次临时验收addin，不卸载其他插件；保留回退包、部署回执和管理员操作说明。

## E. 发布前必读的操作清单

### E1. 首次部署按此顺序

1. 文档归档完成后，另行取得开发执行授权；确认两字段注册后的自动凭证/换机绑定建议。同步 main 已验证修复并记录新的开发基线。
2. 复核并完成仓库私有化与下载边界，封存0.4.2及届时选定的可恢复基线。
3. 当前Windows装独立SDK10；通过Windows SSH由用户完成zetta-pc的Docker安装。
4. 单线程依次完成协议、规则、Host拆分，并先验收Local。
5. 先实现基础身份/期限/授权，再实现Web检查、写入、限定范围导出及包检查；完整管理页面后置。
6. 构建锁定Server发布包，先以Core模式部署到 `/srv/hifc`，配置来源限制和HTTPS，使用真实认证的受限开发者测试身份。
7. 用户保存并正常关闭Revit后安装Web，处理发布者提示，在三个副本验证采集、远程检查、本机写入、回读及复检；通过后完善普通登记和管理员页面。
8. Full模式加入Portainer，配置Linux备份和Windows拉取，实测恢复/回退；最后由第二台内部Windows登记和使用。

### E2. 管理员以后实际怎么操作

| 事情 | Windows 上的入口 | 完成证据 |
|---|---|---|
| 看谁注册、何时到期 | `https://192.168.6.45:8443/admin` | 姓名、手机号、注册与到期时间 |
| 延长自定义天数或日期 | 用户详情 → 延期 | 保存后回读的新期限与审计记录 |
| 同事换电脑 | 用户详情 → 一次性绑定码 | 新电脑保持原注册/到期时间 |
| 看服务异常 | 后台系统页；必要时9443运维页 | 当前版本、健康、日志摘要 |
| 重启本产品进程 | 9443只重启hifc项目相应容器 | ready及合成检查重新通过 |
| 更新版本 | `Deploy-Hifc.ps1 -SshHost zetta-pc -Version <v> -Bundle <包>` | 升级前备份、镜像与健康回执 |
| 回到前一版 | `Rollback-Hifc.ps1 -SshHost zetta-pc -Version <v>` | 应用、schema和规则兼容或完整恢复报告 |
| 检查备份 | 后台系统页＋本机 `D:\HIFC-Backups\zetta-pc` | Linux备份和Windows拉取两个时间均正常 |

这些脚本是本计划后续要实现的交付物，目前不能假定路径下已有可用脚本。

### E3. 本轮权限与未执行项

- SSH只读连接已核实；没有借此安装Docker、创建 `/srv/hifc`、启动容器、修改网络/证书或读取秘密。
- 管理员授权是Linux安装实际权限要求，当前zetta账号无免密sudo；届时由用户在Windows终端交互完成，不要求把密码发送给agent。
- 目前8443/9443未占用只是快照；部署前重新核实。UFW/转发规则未读到有权限的完整结果，不能声称已经配置安全边界。
- .NET SDK、DB、容器、CA、后台及所有新脚本均为计划目标；环境快照是现在事实，避免把方案写成已部署。

## F. 回退与停止条件

| 条件 | 处置 |
|---|---|
| SSH主机身份或主机密钥变化 | 停止远端操作，由用户核对；不关闭主机密钥校验 |
| 无sudo、端口冲突、目录归属冲突 | 保留现场并报告确切原因；不强装、不占已有服务端口 |
| 规则散列/协议/签名不匹配 | 不运行新检查或事务，保留当前模型和草稿 |
| Revit仍占用DLL | 等用户保存并正常关闭，不杀进程 |
| 包含完整规则的Web包或公开下载 | 阻止发布，修复依赖/路由后重新扫描 |
| 新镜像健康失败 | 停止新写许可并恢复上一已验证镜像/规则；需要时恢复匹配DB与密钥 |
| 第二卷未挂载或Windows没拉到备份 | 后台明确显示缺失/逾期状态，不声称已做异机备份 |
| 任一三模型写入/回滚/重开失败 | 不对普通成员发包，保留复现副本和日志定位 |

停止条件针对后续实施，不能以等待部署权限为由停止本轮计划编制。

## G. 计划覆盖与自检

| 用户要求 / 主要风险 | 对应任务 |
|---|---|
| 两种命名、同一主线、两种依赖图 | 0、2–5、11 |
| Linux是否装Docker及安装包、权限、检查 | 1、12 |
| 全部在局域网，Windows日常管理 | 7、12、13 |
| 手机号＋真实姓名两字段，只有本人管理员 | 6–8 |
| 默认168小时、没有试用/停用、任意天数/日期 | 6、7、10、14 |
| 完整私有数据不随Web交付，包括导出/隐藏入口 | 0、3、8、9、11 |
| Revit全面采集、异步守卫、事务回读、保存重开 | 4、5、8–10、14 |
| 长时间编辑、草稿恢复、冲突处理、到期/断网与事务边界 | 2、4、8–10、14 |
| Windows管理员入口、证书、来源限制、第二台成员机 | 1、7、12、14 |
| 开机/故障恢复、备份、异机拉取、完整回退 | 12–14 |

自检要求：每个新类型在C节或所属任务有定义；每个新脚本有创建任务；相邻任务使用同一接口名称；没有用“以后再说”代替安装/权限/目录决策。此计划不包含程序实现、源模型修改或服务器部署结果。

## H. 参考与事实边界

- 当前源码与主线约定：根README、`docs/mainline.md`、`V1/Directory.Build.props`。
- 当前历史验证：`V1/validation/0.4.2-validation.json`；本轮只读取，不宣称重新通过。
- 只读机器调查：同目录 `2026-10-16-hifc-environment.json`；资源、IP、空闲端口在执行前重新验证。
- Docker Ubuntu 安装：<https://docs.docker.com/engine/install/ubuntu/>。
- Docker 端口过滤：<https://docs.docker.com/engine/network/packet-filtering-firewalls/>。
- 微软发布元数据：<https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json>，本轮选择SDK10.0.402 / .NET10 LTS。
- 仓库可见性：GitHub `get_repo` 在计划调查及本次归档准备时均返回public；分支上传前根据用户选定范围再次核实。历史调查时gh CLI曾出现GraphQL EOF，已用连接器独立核实；本记录不是新的网络故障结论。

执行方式已由用户限定为主agent单线程，不再要求用户选择子agent方案。完整计划经审阅后再进入实施；若范围调整，只更新相关设计、任务和验收，不重启已完成的调查。
