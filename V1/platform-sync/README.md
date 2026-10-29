# HBBIM 单模型平台同步 0.2.0

本目录源码为 `0.2.0`，运行不需要 LLM。2026-10-26 已随 Revit 正式版 `0.4.20` 部署；正式程序从独立发布目录运行，未直接使用开发目录的编译文件。安装配置支持经人工确认的单模型同步，实际项目状态和完整核验仍会限制写入。路径、备份和验证见 [正式交接](../docs/HANDOFF-0.4.20.md)。

候选窗口已复用模型管理的共用主题：四步操作、项目/IFC 与平台模型卡片、同步状态表，技术设置收到底部。界面效果及 149 项 WPF 回归见 [界面统一记录](../docs/platform-sync-ui-20261026.md)。

2026-10-26 用户恢复可编辑状态并授权测试后，已完成学校大门真实替换、IFC→FRAG、自动计算、独立远端 IFC 哈希核验及另外四个模型的保护核验。实测修正长文件名截断和嵌套弹窗定位，最终同步测试 64/64。过程中有调试介入恢复，真实断网及无人干预恢复仍未验收；正式版未更新。详见 [真实平台验收记录](../docs/platform-sync-live-acceptance-20261026.md)。

同日新增综合报告自动归档，最终同步回归更新为 **80/80**、WPF **151/151**。真实平台取得 14 页原生 PDF 和列出 5 个参与模型的 TXT，详见 [综合报告验收](../docs/platform-comprehensive-report-20261026.md)。

## 本地体验

在 PowerShell 运行：

```powershell
pwsh -File V1/platform-sync/Start-LocalDemo.ps1
```

脚本只构建独立窗口和本地程序，不安装 Revit 加载项。窗口使用人工构造、明确标记的导出样本，配合只监听 `127.0.0.1` 的模拟平台。

1. “平台设置”保存 Node、`dist/src/cli.js`、配置文件路径。
2. 读取平台清单，查看项目名称与编号，按完整模型名选择并确认绑定。
3. “同步本次 IFC”核对本次成功导出、冻结文件、生成预览计划。
4. 候选窗口新增“确认并开始同步”。重新核对当前导出、项目与可编辑状态后，展示具体替换对象并启动后台程序；关闭插件窗口不终止后台转换。“同步状态”读取持久化任务。

关闭窗口后本地演练服务自动退出。状态与截图保存在 `.runtime/demo-*`，不进入 Git。`-Smoke` 可运行自动窗口核验。

## 稳定导出与交接

普通“导出项目 IFC”独立保留。成功导出必须完成临时事务回滚、模型快照一致、导出器恢复和回执写入，才登记可同步结果。0.4.19 的成功回执在 `%LOCALAPPDATA%/Hifc.V1/export-records/<export-id>/`，从 `ProjectExportScope.ReceiptPath` 取得，不再推断为 IFC 同级文件。点击同步时再冻结 IFC 与回执，并校验 SHA-256；工作程序再次复制冻结。交接失败只影响同步，界面明确显示 IFC 已导出、同步未入队。

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

`inspect` 核对可见项目名称/编号、`QueryProjectSign`、活动场景和真实模型记录。首次单项目试验需在配置的 `expected_project` 明确设置经页面核实的 `project_id/project_name/project_code`；不按名称猜测项目。模型 `generation=record-sha256:...` 是实际模型/位置元数据指纹，不是平台提供的版本号。绑定所选模型时独立下载 IFC 得到哈希，备份前后再次核对代次；非目标模型保护比较完整身份与元数据清单。

只读模式允许已实测的正常界面导航与只读 POST，阻止模型写请求。`approval_state=1/3/4` 或未知状态禁止同步，运行中每次写入边界重新检查。连接诊断只记录步骤、路径和状态，不记录令牌、请求正文或个人信息。

以下流程先绑定、保存计划，再显式执行。`fixture` 用于本地演练；`real-readonly` 默认配置拒绝模型写入；`real` 已完成学校大门单模型验收，仍为候选写适配器，只能用于明确授权的可编辑项目。真实自动回滚继续禁止。

```powershell
node dist/src/cli.js bind --config config.local.json --model MODEL_ID --handoff handoff.json
node dist/src/cli.js plan --config config.local.json --binding BINDING_ID --handoff handoff.json
node dist/src/cli.js start --config config.local.json --run RUN_ID --apply
node dist/src/cli.js resume --config config.local.json --run RUN_ID
node dist/src/cli.js rollback --config config.local.json --run RUN_ID --apply
```

模型 ID 来自实际检查清单；插件窗口提供选择，不要求用户手填。配置中的相对路径以配置文件目录解析。退出码：0 成功/计划/无变化，2 配置，3 登录，4 阻断/暂停，5 结果未知，6 失败，7 项目锁冲突。

## 中断与恢复

删除前下载并核验真实旧 IFC 的字节、哈希和对应模型代次，刷写磁盘后才继续。每个写操作先持久化意图，再记录真实页面请求/响应关联；本地操作号不冒充服务端任务号。上传文件再次从平台下载核验，不能直接把本地输入哈希当作远端证明。

`start --apply` 仅启动已经保存、验证过的计划，返回后台 PID；也可用 `run --apply` 前台运行。断线或响应未知时保留项目锁和浏览器，当前 apply worker 先只读核对，证据确认后才继续未执行的后续步骤；不重复删除或选择同一上传文件。独立 `resume` 对模型只读核对；核验成功后可自动生成并下载综合报告。关闭浏览器或进程后的证据不足会保持 `UNKNOWN_RESULT`；若上传窗口已丢失，程序阻止自动提交，尚未保证所有断网点都能自动恢复。

## 综合报告

`run/resume` 核验本次计算成功后自动获取平台顶部“综合报告”。PDF 与同名 UTF-8 BOM TXT 保存到成功任务 `handoff.document_path` 所在目录，名称为 `DOC_平台项目名称_YYYYMMDD_V1`；同日新归档递增，次日从 V1 开始。TXT 列出本次实际参与计算的全部 IFC 名称、身份和时间。普通仅导出流程不因此生成报告。

窗口提供“下载／重试”“打开目录”和独立报告状态；也可调用：

```powershell
node dist/src/cli.js report-start --config config.local.json --run RUN_ID
node dist/src/cli.js report --config config.local.json --run RUN_ID
```

报告请求核对当前项目、持久化计算指纹、参与模型集合/代次、PDF 生成时间和来源。下载失败不改变模型同步成功状态，也不重新上传或计算。已冻结的 PDF/TXT 可离线恢复，重试复用原路径；被人工修改的交付文件不覆盖。后台重复启动由活跃进程检查及项目锁拦截，界面允许残留下载状态重试。原 RVT 移动后须核对位置，程序不搜索同名文件或静默改绑；文件不存在时显示原因。

`real-readonly` 允许成功任务经限定入口生成/读取综合报告，仍禁止模型写入。报告保存成功与法规审查通过分别显示。

本次计算必须同时具备新模型的实际几何请求、计算文件内无歧义的新模型前缀、正常页面提交被受理、随后观察到当前项目从处理中到成功，以及遮罩消失。历史“已完成”、预处理结束或仅返回受理均不算成功。计算与法规审查分开显示。

观察以请求发出顺序为边界：受理之前的迟到查询不计入本轮，完成查询必须发于已看到处理中之后。短暂只读查询失败允许恢复。当前平台结果尚未证明有服务端任务 ID/时间戳，本地 `observed-save:` 仅为观测指纹；未看到处理中时不会推断成功，异机并发计算仍需真实验收。

SQLite 项目锁只覆盖同一个 `state_dir/sync.sqlite`，不能阻止不同状态目录或另一台电脑同时操作平台。每次写入前还会重核项目、模型代次和名称，但跨机器排他仍需平台租约或共享锁支持。任务只在本次模型、输入哈希、转换及计算结果同时匹配时成功；法规是否通过另列。

## 验证边界

`npm test` 包含文件/回执完整性、绑定、故障恢复、多进程锁，以及真实本地浏览器的选择文件/删除/提交交互。模拟平台没有真实 HBBIM 的 IFC→FRAG 前端转换负载或官方计算服务，因此本地通过不能代表正式平台验收。

真实项目读取、场景/模型代次、旧 IFC 备份、新 IFC 上传与 FRAG 转换、自动计算及独立远端下载已在学校大门单模型中通过核验。真实断网、无人干预恢复及候选 Revit 窗口发起同步仍须专项验收。文件监听、无人值守和批量继续后置。协议见 [CONTRACT.md](CONTRACT.md)，本轮交接见 [候选交接](../docs/HANDOFF-platform-sync-20261026.md)。
