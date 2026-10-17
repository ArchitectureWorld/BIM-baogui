# HIFC Web 0.5.0

本目录是独立的 Web 开发和交付边界。2026-10-17 用户确认“首先 Web，暂时不要碰本地版本”，并授权执行。

- 只新增本目录及必要的 Web 文档；不修改 `V1/src`、`V1/tools`、现有构建属性、冻结参考文件和本机 Local 安装。
- Windows 的 Web 插件采集模型、保留草稿并执行受守卫的 Revit 事务；Linux 服务负责规则、检查、账号和授权。
- 规则服务只读编译既有参考规则和冻结资源；这些代码与资源不进入 Web 客户端。
- 开发、构建、临时数据和模型副本放在本目录忽略的 `.local` / `artifacts` 中。
- 注册、期限、草稿、异步响应和写入要求沿用既有设计；完整操作与实际验证结果随交付补充。

实施依据：[本轮计划](implementation-plan.md)、[既有设计](../docs/superpowers/specs/2026-10-16-hifc-local-web-design.md)。本轮 Web 优先约束高于旧计划中的 Local 前置拆分安排。

## 当前开发状态

已实现普通协议、两轮分页完整性、草稿冲突、只读复用的服务端规则适配、账号期限策略、持久化签名密钥、短期方案与许可签名，以及具有 HTTPS、超时和旧响应守卫的 HTTP 客户端。客户端不引用规则引擎或冻结资源。

`Hifc.Web.Server` 已包含 HTTP 宿主、策略、签名和 PostgreSQL 账号存储：数据库时钟登记、唯一手机号、168 小时期限、管理员延期、并发版本检查及事务内审计已实现。2026-10-17 用户选择先部署服务器、稍后确定登录方案。当前仅开放状态、就绪检查、协议与公钥接口，账号、模型和管理操作统一返回 `AUTHENTICATION_NOT_CONFIGURED`。检查、方案与回执的数据库持久化尚未实施。

`Hifc.Web.Revit` 的原生采集和事务及实机测试仍在开发中。服务器基础环境已部署，当前没有可交给成员使用的 Web 安装包，完整模型流程与用户体验验收尚未完成。

## 构建和验证

在 Windows PowerShell 运行：

```powershell
pwsh -File V1/web/tools/Build-Web.ps1
```

脚本使用独立的 `%USERPROFILE%\.dotnet-hifc` SDK，不修改全局 PATH 或 Local 构建脚本。结果和详细日志位于忽略的 `.local/build`；有基线时同时核验旧源码、已安装 Local 文件和原 RVT。

Revit 副本测试使用 `tools/Run-NativeTests.ps1`；有正在运行的 Revit 时会停止启动新宿主，不中断用户进程。测试模块首次加载的发布者提示需用户手动选择“载入一次”。测试模块与模型副本不属于正式安装或平台验收。

Linux 环境脚本：[bootstrap-host.sh](deploy/bootstrap-host.sh)，默认只读 `--check`；管理员审阅后从当前 Windows 运行 [Install-WebHost.cmd](tools/Install-WebHost.cmd)，仅在交互终端输入 sudo 密码。

## 真实数据库验证

`tools/Prepare-PostgresTest.sh` 仅供 zetta-pc 的开发验证：将 Ubuntu 官方 PostgreSQL 包解压到 `/home/zetta/hifc-web-dev-20261017/postgres-test`，不安装系统包；数据库只监听该用户目录内的 Unix socket，使用 peer 认证。此环境用于验证持久化，不代替正式 Docker 部署。

脚本已上传至 `/home/zetta/hifc-web-bootstrap-20261017/Prepare-PostgresTest.sh`。以 `--start` 启动后，在单独终端建立测试隧道：

```powershell
ssh -N -o ExitOnForwardFailure=yes -L '127.0.0.1:55432:/home/zetta/hifc-web-dev-20261017/postgres-test/socket/.s.PGSQL.55432' zetta-pc
```

在 Web 目录运行 `pwsh -File tools/Build-Web.ps1 -WithDatabase`，额外执行 12 项真实数据库测试。数据库测试程序另提供 `--write-persistence PATH` / `--verify-persistence PATH` 两阶段验证；中间通过远端脚本 `--restart` 重启。验证会检查 PostgreSQL 的实际启动时间已变化，再核对账号、到期时间、版本和审计记录。结束后以 `--stop` 停止本轮测试数据库，并关闭自己建立的隧道。

2026-10-17 已在真实 PostgreSQL 16.15 上通过上述 12 项用例及重启验证，详细边界见 [SSH 数据库检查点](validation/2026-10-17-ssh-database-checkpoint.json)。

## 已部署的服务器基础环境

- 入口：`https://192.168.6.45:8443/`；仅放行当前已核实 Windows `192.168.218.195/32`。
- 目录：`/srv/hifc-web/current` 指向独立版本目录，PostgreSQL、服务端签名密钥和 Caddy CA 各自保存在持久化目录。
- 运行：Docker Engine 29.9.1 / Compose 5.6.0；PostgreSQL 17.11、HIFC 和 Caddy 2.11.7 三个容器均已通过健康检查，镜像按摘要或内容 ID 锁定。
- 验证：实际 Linux 运行时通过 66 项 Web 测试和 85 项既有规则回归；真实 PostgreSQL 17 通过 12 项数据库用例。重建全部容器后，账号、期限、审计、CA 与签名身份保留。
- 来源限制：Docker `DOCKER-USER` 与 Caddy 同时限制来源；隔离容器来源实际触发丢弃计数，服务器自身的未放行请求返回 403。第二台实体客户端尚未验证。
- 主机保护：安装前的 FORWARD ACCEPT 策略已恢复并配置 `ip-forward-no-drop`；原有 TCP 监听均保留，只新增 LAN IPv4 的 8443。临时镜像下载代理和 SSH 转发已关闭。

Windows 通过固定私有 CA 与主机名校验，实际取得状态页、就绪和协议接口的 200 响应。私有 CA 公共证书保存在本目录忽略的 `.local/certificates/hifc-web-root-ca.crt`；Windows 信任库尚未导入，浏览器访问前需单独处理信任。验证工具 `tools/Verify-WebServer.py` 使用指定 CA 的标准 SSL 上下文，不使用忽略证书选项；Windows curl 的 Schannel 因该私有 CA 未提供 CRL 而报告未知吊销状态。

运维：`sudo systemctl status hifc-web`；组件状态：`sudo docker compose --project-directory /srv/hifc-web/current --project-name hifc-web ps`。部署与验证分别由 `tools/Publish-WebServer.ps1`、`deploy/install-release.sh`、`deploy/verify-runtime.sh` 和 `tools/Verify-WebServer.py` 负责。

登录及后台、Portainer、备份和异机恢复、客户端安装、第二台实体机器与模型体验验收仍待后续阶段。完整回执见 [服务器基础部署检查点](validation/2026-10-17-server-foundation.json)。
