# HIFC Web 0.5.0

本目录是独立的 Web 开发和交付边界。2026-10-17 用户确认“首先 Web，暂时不要碰本地版本”，并授权执行。

- 只新增本目录及必要的 Web 文档；不修改 `V1/src`、`V1/tools`、现有构建属性、冻结参考文件和本机 Local 安装。
- Windows 的 Web 插件采集模型、保留草稿并执行受守卫的 Revit 事务；Linux 服务负责规则、检查、账号和授权。
- 规则服务只读编译既有参考规则和冻结资源；这些代码与资源不进入 Web 客户端。
- 开发、构建、临时数据和模型副本放在本目录忽略的 `.local` / `artifacts` 中。
- 注册、期限、草稿、异步响应和写入要求沿用既有设计；完整操作与实际验证结果随交付补充。

实施依据：[本轮计划](implementation-plan.md)、[内网账号与自动更新记录](2026-10-19-intranet-update-plan.md)、[既有设计](../docs/superpowers/specs/2026-10-16-hifc-local-web-design.md)。当前账号方式以 2026-10-19 用户确认的内网简化方案为准；本轮 Web 优先约束高于旧计划中的 Local 前置拆分安排。

2026-10-19 用户另行要求同步最新主线并发布 `HIFC-Web` 分支。本轮将 `main@337bbcc` 的 Local `0.4.4` 原样合入，Web 额外修改仍限本目录，原 Local checkout、安装与模型保持。Web 产品和程序集继续为 `0.5.0` / `0.5.0.0`，源码规则标识更新为 `reference-0.4.4-web1`；详见 [主线同步核查](2026-10-19-main-sync.md)。本次同步没有重新部署线上运行包。

## 当前开发状态

已实现普通协议、两轮分页完整性、草稿冲突、只读复用的服务端规则适配、账号期限策略、持久化签名密钥、短期方案与许可签名，以及具有 HTTPS、超时和旧响应守卫的 HTTP 客户端。客户端不引用规则引擎或冻结资源。

`Hifc.Web.Server` 已包含真实登记登录、检查、预览、许可及回执接口。普通用户填写手机号和真实姓名即可登记或登录，不再使用绑定码或电脑绑定限制；默认 168 小时，重登与换电脑保持同一账号和原期限。姓名冲突会拒绝登录，管理员账号不开放这种普通登录方式。本机通过 Windows DPAPI 加密记住人员信息；管理员继续使用独立密码、Cookie 和 CSRF 保护的网页后台，可延期或删除账号。删除会撤销各电脑上的会话及关联工作流。

`Hifc.Web.Revit` 使用独立的 `HIFC-V1-Web` 页签，延续 Local 的界面主题，提供模型列表、属性编辑、写入预览和账号窗口。草稿留在本机；所有 Revit 取数与事务通过 ExternalEvent 执行，网络返回后重新核对文档与快照。分类会实际写入类目、清理旧实例值并保留类型值。2026-10-19 已通过 7 项原生保护检查，厂房、后勤楼、总图三个模型副本完成真实生产 HTTPS 检查、编辑、写入、回读、复检、保存及重开；15 张 WPF 画面已核对。用户人工体验验收和正式平台报验仍单独记录。

`tools/Publish-WebClient.ps1` 生成独立 Web ZIP，包含四个客户端 DLL、更新 EXE 及配置、安装器、公共 CA、更新签名公钥和操作说明。安装包预置 zetta 地址，同事无需输入 IP；安装器核对文件与固定证书指纹，通过 Windows 证书库直接检测已有信任，缺失时自动导入当前用户的受信任根证书库，再验证生产 HTTPS。无需手选证书目录；Windows 如要求信任确认，由用户确认一次。安装应使用运行 Revit 的 Windows 账号，切换成其他管理员账号时会停止；Revit 发布者提示仍由用户本人确认。`tools/Publish-WebServer.ps1` 将 ZIP 与版本资料一起部署到 zetta，首页提供下载和管理员入口。

更新清单位于 `https://192.168.6.45:8443/updates/latest`，更新包由同一 zetta 服务的 `/updates/packages/` 提供；私钥留在 zetta。插件在后台检查和下载，校验签名、来源、大小与散列，等待本轮 Revit 退出后切换独立版本目录，下次启动生效；失败保留旧版及人员资料、草稿。旧客户端须先手动安装一次带更新器的新版。实际 Revit 生命周期更新已在隔离安装目录中验证，未覆盖正在运行的插件文件。

## 构建和验证

在 Windows PowerShell 运行：

```powershell
pwsh -File V1/web/tools/Build-Web.ps1
```

脚本使用独立的 `%USERPROFILE%\.dotnet-hifc` SDK，不修改全局 PATH 或 Local 构建脚本。结果和详细日志位于忽略的 `.local/build`；有基线时同时核验旧源码、已安装 Local 文件和原 RVT。

Revit 原生测试使用 `tools/Run-NativeTests.ps1`，三模型完整流程使用 `tools/Run-FullExperience.ps1`。它们只打开核对过原文件哈希的副本；已有 Revit 时拒绝启动新宿主。发布者提示由用户手动选择“载入一次”。本机回环集成环境与生产 Linux HTTPS 分开验证。

Linux 环境脚本：[bootstrap-host.sh](deploy/bootstrap-host.sh)，默认只读 `--check`；管理员审阅后从当前 Windows 运行 [Install-WebHost.cmd](tools/Install-WebHost.cmd)，仅在交互终端输入 sudo 密码。

## 真实数据库验证

`tools/Prepare-PostgresTest.sh` 仅供 zetta-pc 的开发验证：将 Ubuntu 官方 PostgreSQL 包解压到 `/home/zetta/hifc-web-dev-20261017/postgres-test`，不安装系统包；数据库只监听该用户目录内的 Unix socket，使用 peer 认证。此环境用于验证持久化，不代替正式 Docker 部署。

脚本已上传至 `/home/zetta/hifc-web-bootstrap-20261017/Prepare-PostgresTest.sh`。以 `--start` 启动后，在单独终端建立测试隧道：

```powershell
ssh -N -o ExitOnForwardFailure=yes -L '127.0.0.1:55432:/home/zetta/hifc-web-dev-20261017/postgres-test/socket/.s.PGSQL.55432' zetta-pc
```

在 Web 目录运行 `pwsh -File tools/Build-Web.ps1 -WithDatabase`，额外执行 33 项真实数据库 / HTTP 测试。数据库测试程序另提供 `--write-persistence PATH` / `--verify-persistence PATH` 两阶段验证；中间通过远端脚本 `--restart` 重启。验证会检查 PostgreSQL 的实际启动时间已变化，再核对账号、到期时间、版本和审计记录。结束后以 `--stop` 停止本轮测试数据库，并关闭自己建立的隧道。

2026-10-17 已在真实 PostgreSQL 16.15 上通过上述 12 项用例及重启验证，详细边界见 [SSH 数据库检查点](validation/2026-10-17-ssh-database-checkpoint.json)。

## 证书统一安装（2026-10-19）

当前安装器发行版为 `0.5.0-dev-20261019T064903Z`，安装脚本来源 `6fc6056507812ca8f7621f631119d13647a78683`。[下载当前安装包](https://192.168.6.45:8443/downloads/HIFC-0.5.0-Web.zip?v=0.5.0-dev-20261019T064903Z)。用运行 Revit 的 Windows 账号重新下载、解压，关闭 Revit 后直接双击“安装 Web 版.cmd”，无需管理员运行或手选证书目录。Windows 如显示信任确认，核对后确认一次。

安装器直接读取当前用户及计算机的受信任根证书库，已有正确证书就复用，缺失时自动补入当前用户根库并重新读取确认，随后使用正常证书链及主机名校验访问生产 HTTPS。证书 SHA256 和 SHA1 指纹继续固定；放在中间证书目录的已有证书保留。以不同管理员账号运行会停止，避免插件与证书进入另一个用户的配置目录。已复现旧代码在 Windows 根证书存在而 PowerShell `Cert:` 不可用时仍提示手动导入的情况；同事截图本身没有提供实际存储位置或账号信息。

本次只替换安装文件，6 个客户端载荷文件和服务器 DLL 沿用 `cc433ee5fae132d535a9f9888ca7232af3c26e5b` 的已验收二进制；主线 `0.4.4` 的源码同步继续单独保留。更新清单和 ZIP 均来自 zetta，数据库及 Caddy 容器未重启，原发行目录和数据库备份保留。

候选包通过 8 项证书/账号路径回归，以及受限 PowerShell 下的实际 CMD 校验、隔离安装、损坏包拒绝。证书状态测试使用独立命名的 Windows X509 库，真实用户根库保持不变；已安装真实根证书且移除 `Cert:` 驱动器的进程完成实际 HTTPS 与隔离安装。正式包回下载后另通过 6 项证书回归、CMD 校验、签名与两条下载路径散列核对，并由既有更新器在隔离目录实际激活。回下载阶段 Revit 已重新运行，保留该进程，未重复完整安装或实机模型测试。验证宿主为 Windows 10 22H2 / Windows PowerShell 5.1；同事 Windows 11 实体机仍待重试。

证据：[证书安装修复记录](validation/2026-10-19-certificate-installer-fix.json)。

## Windows 安装入口修复（2026-10-19）

同事 Windows 11 的截图为 PowerShell 禁止执行脚本，原因是旧 CMD 未指定安装进程的执行策略。发行版 `0.5.0-dev-20261019T040011Z`（源 `cc433ee5fae132d535a9f9888ca7232af3c26e5b`）已修正启动参数，只对本次安装生效，不改长期策略；同时保留失败退出码，发布时必须验证实际 CMD 入口。

[下载修正安装包](https://192.168.6.45:8443/downloads/HIFC-0.5.0-Web.zip?v=0.5.0-dev-20261019T040011Z)。重新下载，解压到新文件夹，用将使用 Revit 的 Windows 账号直接双击“安装 Web 版.cmd”，无需更改系统执行策略。

已从正式 zetta 链接重新下载并核对 ZIP，在真实 Windows PowerShell 5.1 的 `Restricted` 父进程、中文与空格路径下完成校验、隔离用户目录安装及损坏包拒绝；6 个文件和独立 Web 清单正确，旧安装及 Local 哨兵保留，前后长期策略一致。测试宿主为 Windows 10 22H2，针对截图中的策略条件完成复现；同事 Windows 11 实体机重试仍待确认。本轮未修改 Revit / 服务端业务代码，三模型流程沿用下面的完整体验基线，不将其写成对新包重新执行过的模型验收。

证据：[安装入口修复记录](validation/2026-10-19-installer-entry-fix.json)。

## 完整体验交付基线（2026-10-19）

- 发行版 `0.5.0-dev-20261019T030204Z`，代码来源 `6f860c069f0a6140835b22a380fde6f45aac3c86`；后续交付记录提交不改变该已验证运行包。
- 正式入口及更新文件均在 zetta；[同事安装包](https://192.168.6.45:8443/downloads/HIFC-0.5.0-Web.zip) 面向 Revit 2020。解压后关闭 Revit，运行“安装 Web 版.cmd”。
- 允许的办公网段为 `192.168.6.0/23` 和 `192.168.218.0/24`；入口只绑定 zetta 的 LAN IPv4 `192.168.6.45:8443`。
- Linux Web 78、规则 85、真实数据库 / HTTP 33 项通过；三个容器重建后健康，账号、期限、审计、CA 和签名身份保持。Windows 桌面 / 更新 16 项及原生 7 项检查通过，三个模型副本分别写入 84、69、19 项并保存重开。
- 本机 Web 安装的 6 个文件与正式 ZIP 一致；Local 三个安装文件、六个原模型和既有 Local 源码保持。测试插件清单、专用测试数据库及隧道已清理。
- 自动更新验证使用实际 Revit 进程和生产 HTTPS 下载，安装目录为隔离夹具；同事实体电脑的首次安装仍待现场体验。用户人工验收状态为待确认。

证据：[本轮交付记录](validation/2026-10-19-intranet-delivery.json)。详细日志、界面画面与原始结果保存在本机忽略的 `.local` 目录。

## 服务器基础环境历史检查点（2026-10-17）

以下保留当时的基础部署状态。

- 入口：`https://192.168.6.45:8443/`；仅放行当前已核实 Windows `192.168.218.195/32`。
- 目录：`/srv/hifc-web/current` 指向独立版本目录，PostgreSQL、服务端签名密钥和 Caddy CA 各自保存在持久化目录。
- 运行：Docker Engine 29.9.1 / Compose 5.6.0；PostgreSQL 17.11、HIFC 和 Caddy 2.11.7 三个容器均已通过健康检查，镜像按摘要或内容 ID 锁定。
- 验证：实际 Linux 运行时通过 66 项 Web 测试和 85 项既有规则回归；真实 PostgreSQL 17 通过 12 项数据库用例。重建全部容器后，账号、期限、审计、CA 与签名身份保留。
- 来源限制：Docker `DOCKER-USER` 与 Caddy 同时限制来源；隔离容器来源实际触发丢弃计数，服务器自身的未放行请求返回 403。第二台实体客户端尚未验证。
- 主机保护：安装前的 FORWARD ACCEPT 策略已恢复并配置 `ip-forward-no-drop`；原有 TCP 监听均保留，只新增 LAN IPv4 的 8443。临时镜像下载代理和 SSH 转发已关闭。

Windows 通过固定私有 CA 与主机名校验，实际取得状态页、就绪和协议接口的 200 响应。私有 CA 公共证书保存在本目录忽略的 `.local/certificates/hifc-web-root-ca.crt`；Windows 信任库尚未导入，浏览器访问前需单独处理信任。验证工具 `tools/Verify-WebServer.py` 使用指定 CA 的标准 SSL 上下文，不使用忽略证书选项；Windows curl 的 Schannel 因该私有 CA 未提供 CRL 而报告未知吊销状态。

运维：`sudo systemctl status hifc-web`；组件状态：`sudo docker compose --project-directory /srv/hifc-web/current --project-name hifc-web ps`。部署与验证分别由 `tools/Publish-WebServer.ps1`、`deploy/install-release.sh`、`deploy/verify-runtime.sh` 和 `tools/Verify-WebServer.py` 负责。

该段记录早先的基础环境，不能代表后续完整体验部署。历史回执见 [服务器基础部署检查点](validation/2026-10-17-server-foundation.json)。

## 管理员初始化

首次部署账号功能后，在已配置 `zetta-pc` SSH 的电脑运行安装包中的“初始化管理员.cmd”。手机号、真实姓名及 12–128 字符密码直接输入服务器交互终端，密码不回显。此入口只允许首次创建管理员，无默认密码或公开初始化接口。后台：`https://192.168.6.45:8443/admin`。
