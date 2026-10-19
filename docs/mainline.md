# H-IFC V1 主线

当前版本为 **0.4.4**，程序集版本为 **0.4.4.0**，面向 **Revit 2020**，文档更新于 **2026-10-19**。使用方式见 [V1 说明](../V1/README.md) 及 [0.4.4 功能说明](../V1/docs/0.4.4-报规地下区域与指标.md)。

2026-10-16，用户确认以当时可用的 0.4.2 升为 `main`，后续功能继续在该主线开发。

## 当前约定

- `main` 是唯一活动主线，源码位于 `V1/`，CI 为 `.github/workflows/hifc-v1.yml`。
- 旧 GH、旧 Revit/MCP 的根目录源码、测试、安装工具和工作流退出当前树。V1 自带的映射、成功案例、源码、测试与安装工具独立保留。
- Git 同步包含源码、测试、脚本和公开文档；模型、IFC、DLL、运行目录和暂缓的内网方案保留本地。
- 构建包为 `V1/dist/Hifc.V1.Revit2020-0.4.4/`。安装由 `V1/tools/Install-Revit2020.ps1` 单独执行，要求 Revit 已正常关闭。
- 目前仅道路宽度要求手填并适用未填标红；新增地下区域按项目实际适用，不增加必要构件或强制手填项。

## 验证

当前版本核心 97 项、独立 WPF 67 项、Python 24 项回归通过。受控地下模型副本的 31 项 Revit 宿主检查通过，三组原生 IFC 分别核验 59、10、7 项属性，schema 错误均为零。详见 [0.4.4 验证摘要](../V1/validation/0.4.4-validation.json)。

三个客户模型副本的历史写入与保存重开证据按原版本保留；本轮尚未取得客户真实地下项目的正式平台验收回执。GitHub Actions 运行核心、WPF 和 Python 检查，见 [CI 工作流](https://github.com/ArchitectureWorld/BIM-baogui/actions/workflows/hifc-v1.yml)。

## 旧版恢复

远端原 `main` 历史通过合并提交保留，不使用强推覆盖。旧开发分支在新主线验证后清理，其原始提交和未提交内容已有独立备份。

本机备份目录：`D:\18_建模项目\湖北BIM云平台\repository-local-evidence-archive\main-promotion-20261016-135428`。

- `repository-before-main.bundle`：清理前全部远端分支、标签及历史，已执行 `git bundle verify`。
- `archive-manifest.json`：工作副本、原 HEAD、状态哈希及备份清单。
- 各工作副本子目录：暂存/未暂存补丁与未提交文件压缩包。

旧源码目录不再作为开发入口。需要回查旧实现时可在独立目录克隆 bundle，不覆盖当前主线。
