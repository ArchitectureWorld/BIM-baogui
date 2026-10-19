# V1 验证记录

当前发布版本为 **0.4.4 / Revit 2020**，程序集版本为 **0.4.4.0**，更新于 **2026-10-19**。

- [当前版本验证摘要](0.4.4-validation.json)：核心 97 项、独立 WPF 67 项、Python 24 项；受控地下副本 31 项宿主检查；三组原生 IFC 分别核验 59、10、7 项属性。
- [当前使用说明](../README.md) 与 [0.4.4 专项说明](../docs/0.4.4-报规地下区域与指标.md)：操作步骤、模型载体及验证边界。
- [CI 工作流](https://github.com/ArchitectureWorld/BIM-baogui/actions/workflows/hifc-v1.yml)：核心、WPF 和 Python 检查。

本目录保留各轮原始验证记录。`development-status.json` 是 **0.3.2** 历史快照，`management-development-status.json` 是 **0.4.0** 历史快照；文件里的版本号、计数、路径和安装哈希属于当轮事实。查看当前状态请使用 `0.4.4-validation.json`。

`0.4.2-validation.json`、`ui-*`、`workflow-*` 及 `history/` 也按各自版本或运行日期解读。历史测试数量不能与当前版本数量混用。

0.4.4 完整宿主回执保存在本机 `underground-native-20261019-123055-c6ddda53/`。源码仓库只提供公开摘要，模型、IFC、DLL 和运行产物保留本地。本轮受控模型结果尚未代表客户真实地下项目的正式平台验收。
