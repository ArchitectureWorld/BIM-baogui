# Revit 2020 原生插件可安装版 Stage02 实施计划

**目标：** 在 `feat/revit-native-addin-v1` 上交付可双击安装、可在 Revit 2020 原生面板中完成 Stage01 与 Stage02 的插件包。

## 验收门槛

1. 原生程序集保持 `net48`，不引用 Grasshopper、RhinoCommon 或 Rhino.Inside。
2. Stage02 支持全模型扫描与明确的自定义选择，二者不能静默混用。
3. 角色匹配仅允许类别/ElementKind、数据库精确别名或用户显式角色；禁止模糊推断。
4. 预览必须冻结规则包、文档、构件、参数和值证据并生成 SHA-256。
5. 参数绑定按参数事务隔离；构件写入按构件事务原子提交，允许部分成功。
6. 所有 Revit API 操作通过 ExternalEvent。
7. 安装包必须包含 `Install.cmd`、`Uninstall.cmd`、PowerShell 安装器、`.addin`、DLL、README 与校验清单。
8. Windows CI 必须通过领域测试、Release 编译、安装/卸载 smoke，并上传完整安装包。

## 开发顺序

- [x] RED：Stage02 规则目录、Inventory 与精确角色匹配合同测试。
- [ ] GREEN：实现规则目录、Inventory 与角色匹配。
- [ ] RED/GREEN：实现确定性预览、字段状态与建议值计划。
- [ ] RED/GREEN：实现 Revit 全模型采集与自定义选择。
- [ ] RED/GREEN：实现共享参数绑定、值转换、构件级事务及回读。
- [ ] RED/GREEN：实现 Stage02 WPF 工作台、问题筛选和确认写入。
- [ ] 打包：补充双击安装/卸载入口和离线说明。
- [ ] 验证：Windows CI、安装 smoke、产物结构与 SHA-256。
