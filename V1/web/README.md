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

`Hifc.Web.Server` 目前是策略和签名库，尚未交付 HTTP 宿主、数据库或登记登录；`Hifc.Web.Revit` 及实机测试仍在开发中。当前没有可交给成员使用的 Web 安装包，也没有 Linux 端到端通过结论。

## 构建和验证

在 Windows PowerShell 运行：

```powershell
pwsh -File V1/web/tools/Build-Web.ps1
```

脚本使用独立的 `%USERPROFILE%\.dotnet-hifc` SDK，不修改全局 PATH 或 Local 构建脚本。结果和详细日志位于忽略的 `.local/build`；有基线时同时核验旧源码、已安装 Local 文件和原 RVT。

Revit 副本测试使用 `tools/Run-NativeTests.ps1`；有正在运行的 Revit 时会停止启动新宿主，不中断用户进程。测试模块首次加载的发布者提示需用户手动选择“载入一次”。测试模块与模型副本不属于正式安装或平台验收。

Linux 环境脚本：[bootstrap-host.sh](deploy/bootstrap-host.sh)，默认只读 `--check`；管理员审阅后从当前 Windows 运行 [Install-WebHost.cmd](tools/Install-WebHost.cmd)，仅在交互终端输入 sudo 密码。
