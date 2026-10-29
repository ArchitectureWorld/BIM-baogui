# H-IFC V1 正式部署交接 · 0.4.20

日期：2026-10-26（Asia/Shanghai）。用户明确授权“进行正式部署”。插件版本 0.4.20，程序集 0.4.20.0；独立平台同步程序 0.2.0。源码继续位于既有 main 工作区，本轮没有提交或推送 Git。

2026-10-29 同步说明：用户已要求将当前内容同步到远程，本文件随源码、测试、公开验证索引和打包工具纳入 main。上文及 10 月 26 日验证 JSON 的 Git 状态保留为当时部署快照，当前同步状态以 Git 提交记录为准。模型、凭据、运行数据库、原始日志及暂缓的 Local/Web 内网方案仍留在本地。

## 当前产品与入口

Revit → **H-IFC V1 → 平台同步 → 平台同步／综合报告**。

四步操作为读取平台、确认对应模型、预览同步、开始同步。仅完整成功导出的 IFC 才能冻结交接。同步按具体平台模型身份绑定，日期和 V1/V2 名称变化不改变目标身份；另存为及复制需重新核对。

本次计算核验成功后自动获取平台“综合报告”，原生 PDF 与同名 TXT 保存至任务来源 RVT 同级目录。名称 `DOC_平台项目名称_YYYYMMDD_V1`，同日新归档递增、次日从 V1 开始；TXT 记录实际参与计算的全部模型及时间。报告独立显示状态，支持下载／重试和打开目录；下载失败不重新上传或计算。

旧的普通 IFC 导出和全部模型属性功能保留。本次相对已验收候选主要变更为正式版本元数据、功能区名称/说明、安装默认包及正式运行配置。

## 本机部署位置

| 项目 | 路径或值 |
|---|---|
| 插件发布包 | `V1/dist/Hifc.V1.Revit2020-0.4.20/` |
| 同步运行包 | `V1/dist/Hifc.PlatformSync-0.2.0/`，仅编译运行代码与锁定的生产依赖 |
| 插件安装 | `%APPDATA%/Autodesk/Revit/Addins/2020/Hifc.V1/` |
| 同步程序安装 | `%LOCALAPPDATA%/HIFC-V1/platform-sync/releases/0.2.0/` |
| 同步配置 | `%LOCALAPPDATA%/HIFC-V1/platform-sync/config.json` |
| 插件界面配置 | `%LOCALAPPDATA%/HIFC-V1/platform-sync-preview/ui-settings.json`，保留既有兼容路径 |
| Node | `C:/Program Files/nodejs/node.exe`，本机 v25.4.0；要求 24+ |

凭据以 Windows 当前用户 DPAPI 保存，不进入发布包或仓库。正式配置采用 real 适配器，仍要求用户选择、预览并确认开始；不会自动执行历史计划。状态和专用浏览器继续引用既有 `V1/platform-sync/.runtime/live-20261026/.runtime/state` 与 `browser`，保留 2 个绑定及 4 条历史任务。**这些是运行数据，不要按普通开发缓存删除。** 来源 RVT 移动后不会静默改绑。

已备份原插件至 `V1/outputs/install-20261026-161350-be66ea12/previous-Hifc.V1/`，原加载项清单也保存在同一目录。安装回执为该目录的 `install-receipt.json`。正式安装前 Revit 已退出，没有强制结束进程。

## 本次验证

- 正式构建：核心 267/267、WPF 151/151、报规表 21/21、红线 33/33、独立同步 80/80；编译无错误。
- 插件 5 个清单文件、同步程序 535 个运行包文件逐项 SHA-256 一致。
- 安装目录中的 CLI 报告版本 0.2.0，成功读取全部历史绑定/任务。
- 安装目录中的程序独立读取真实学校项目，登录有效，模型数 5；当时平台状态为审核中、不可编辑。没有借部署再次执行模型替换。
- 安装目录中的报告命令成功恢复已核验冻结报告，原同步成功状态不变，5 个模型和 PDF 哈希一致；无需再访问平台生成报告。
- Revit 进程 187808 已进入主页。`journal.0774.txt` 记录正式 Hifc.Revit.App 启动成功及 PlatformSyncCommand 注册，插件位置为正式安装目录；未打开客户模型。

本轮验证索引：[0.4.20-validation.json](../validation/0.4.20-validation.json)。本机详细日志位于工作区 `outputs/release-0.4.20-20261026/`，包含 `build.log`、`sync-tests.log`、`deployment-verification.json`、`host-startup-verification.json` 和 `installed-report-retry.json`。

此前真实大门上传、转换、计算、远端 IFC 和综合报告验收见 [平台实测](platform-sync-live-acceptance-20261026.md) 与 [综合报告实测](platform-comprehensive-report-20261026.md)。报告下载成功、计算完成及法规审查通过是三种不同结果。

## 接手与恢复

源码构建使用 `tools/Build.ps1`，插件安装使用 `tools/Install-Revit2020.ps1`（默认 0.4.20）。独立同步程序先在 `platform-sync` 中执行 `npm ci --ignore-scripts` 和 `npm run build`，再运行 `tools/Package-PlatformSync.ps1`；它复制编译代码并依 package-lock 安装生产依赖，生成独立文件清单。本机最初打包/部署脚本及记录仍保留在 `outputs/release-0.4.20-20261026/`。不要将客户文件、登录凭据、浏览器 profile、SQLite 或构建产物提交 Git。

如需回退，先正常关闭 Revit，恢复上一安装备份及加载项清单；同步界面配置也需指回明确选择的相容版本。保留现有状态和证据，不删除远端模型、不自动执行历史任务。

正式启动验证未覆盖在客户模型中点击新窗口的每一步；该界面有独立 WPF 交互验证。真实断网全过程恢复、跨电脑并发、批量及无人值守仍未承诺。损坏的 worker 元数据 JSON 仍需人工核对恢复；正常中断状态、无 PID 遗留、冻结文件发布中断已有回归。
