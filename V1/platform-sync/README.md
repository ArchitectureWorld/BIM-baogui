# HBBIM 单模型平台同步预开发 0.1.0

源码属于 `0.4.17-preview.1`；当前安装的稳定 Revit 插件为 `0.4.16`。本目录程序运行不需要 LLM。真实平台本版只读，完整替换在本地模拟平台验证。

## 本地体验

在 PowerShell 运行：

```powershell
pwsh -File V1/platform-sync/Start-LocalDemo.ps1
```

脚本只构建独立窗口和本地程序，不安装 Revit 加载项。窗口使用人工构造、明确标记的导出样本，配合只监听 `127.0.0.1` 的模拟平台。

1. “平台设置”保存 Node、`dist/src/cli.js`、配置文件路径。
2. 读取平台清单，查看项目名称与编号，按完整模型名选择并确认绑定。
3. “同步本次 IFC”核对本次成功导出、冻结文件、生成预览计划。
4. “同步状态”查看已保存的任务。界面这一版只产生计划；不提供真实平台写入。

关闭窗口后本地演练服务自动退出。状态与截图保存在 `.runtime/demo-*`，不进入 Git。`-Smoke` 可运行自动窗口核验。

## 稳定导出与交接

普通“导出项目 IFC”独立保留。成功导出必须完成临时事务回滚、模型快照一致、导出器恢复和回执写入，才登记可同步结果。点击同步时再冻结 IFC 与回执，并校验 SHA-256；工作程序再次复制冻结。交接失败只影响同步，界面明确显示 IFC 已导出、同步未入队。

绑定使用平台项目、模型 ID 和代次，以及 RVT 绝对路径和 ProjectInformation.UniqueId；日期和 V1/V2 文件名变化不会产生新绑定。另存为或复制到新路径需显式核对并重新绑定。缺失、重名、场景不一致和输入被修改都会阻止处理。

## 独立程序

需要 Node 24+；Windows 默认使用专用 Edge profile。可用 `HIFC_SYNC_CHROMIUM` 指定独立 Chromium 程序。不要使用日常浏览器用户目录。

```powershell
cd V1/platform-sync
npm ci --ignore-scripts
npm run build
node dist/src/cli.js help
node dist/src/cli.js setup --config config.local.json
```

真实平台入口使用 `Configure-Platform.ps1` 以隐藏输入读取，Windows DPAPI 加密保存，不把令牌写到配置、命令行或日志。此配置仍为只读。

```powershell
./Configure-Platform.ps1
node dist/src/cli.js inspect --config config.local.json
node dist/src/cli.js status --config config.local.json
```

`inspect` 输出已能证明的页面信息；缺少项目、三维场景或模型代次证据时，返回阻断及具体证据缺口。当前真实适配器没有接受项目/模型身份契约，所以真实绑定、替换、恢复均未启用。只读浏览器暂阻止全部非 GET/HEAD/OPTIONS 请求，平台自身若用 POST 进行只读查询也可能受限，需后续逐项核验后允许。

以下写命令只用于 `adapter=fixture` 的本地配置，真实适配器会提前拒绝：

```powershell
node dist/src/cli.js bind --config config.local.json --model MODEL_ID --handoff handoff.json
node dist/src/cli.js plan --config config.local.json --binding BINDING_ID --handoff handoff.json
node dist/src/cli.js run --config config.local.json --run RUN_ID --apply
node dist/src/cli.js resume --config config.local.json --run RUN_ID
node dist/src/cli.js rollback --config config.local.json --run RUN_ID --apply
```

模型 ID 来自实际检查清单；插件窗口提供选择，不要求用户手填。配置中的相对路径以配置文件目录解析。退出码：0 成功/计划/无变化，2 配置，3 登录，4 阻断/暂停，5 结果未知，6 失败，7 项目锁冲突。

## 中断与恢复

删除前下载并核验真实旧 IFC 的字节、哈希和对应模型代次；验证不完整就不删除。每个写操作先持久化意图，再记录回执。断线、进程退出或回执未知时保留项目锁；`resume` 只核对操作结果，不重放请求。继续执行需再次显式 `run --apply`。回滚只支持已核实完成任务，并需显式命令；不自动删除失败的新模型。

SQLite 项目锁防止本机多进程互相覆盖，不能阻止另一台电脑同时操作平台。每次写入前还会重核项目、模型代次和名称，但跨机器排他仍需平台租约或共享锁支持。任务只在本次模型、输入哈希、转换及计算结果同时匹配时成功；法规是否通过另列。

## 验证边界

`npm test` 包含文件/回执完整性、绑定、故障恢复、多进程锁，以及真实本地浏览器的选择文件/删除/提交交互。模拟平台没有真实 HBBIM 的 IFC→FRAG 前端转换负载或官方计算服务，因此本地通过不能代表正式平台验收。

真实平台的独立浏览器项目读取、当前场景/模型代次证据、旧 IFC 下载及对应关系、自动计算启动与本次结果关联、真实断网恢复和跨机器排他尚需专项验收。文件监听、无人值守和批量继续后置。协议见 [CONTRACT.md](CONTRACT.md)，开发状态见 [预开发记录](../docs/platform-sync-preview.md)。
