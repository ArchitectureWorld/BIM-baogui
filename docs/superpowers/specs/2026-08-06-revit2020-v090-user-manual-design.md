# BIMBaoGui v0.9.0 一页式使用说明书设计

## 目标

为验收人员提供一份简短、可直接照做的 Markdown 使用说明书，只解释三个公开 Grasshopper 组件如何依次使用，以及如何导出 IFC。

## 交付文件

最终文件为 `docs/revit2020-v090-user-manual.md`。

## 内容范围

1. 运行前提：Revit 2020、Rhino 8、Rhino.Inside.Revit、Grasshopper，以及唯一 GHA 的固定部署位置。
2. 固定启动顺序：Revit 2020 -> Rhino.Inside.Revit -> Grasshopper。
3. `01 文件初始化`：说明必要输入、执行方式、输出的 `FileContext`，以及它如何连接到 Stage02。
4. `02 构件与属性准备`：说明选择构件、预览、确认写入、查看结果，以及如何在 Revit 属性面板中核对可见参数。
5. `03 检测、导出与 H-IFC 转译`：说明输出目录、Strict/Force、强制原因、执行按钮和 `false -> true` 上升沿。
6. 导出结果：解释 `-RAW.ifc`、`-HIFC-MVD.ifc`、`-fields.json` 的用途和生成条件。
7. 最少故障说明：失败报告位于 GHA 同目录；技术错误不能被 Force 绕过；不得创建或保留插件备份文件。

## 表达方式

- 面向验收人员，不展开代码、内部架构或完整 Task12 证据表。
- 使用编号步骤和接线表，确保可直接在 Grasshopper 中照做。
- 明确当前 359/359 规则为 `UNCLASSIFIED`：Strict 通常只输出 fields JSON；验收导出测试可使用填写了非空原因的 Force。
- 明确 Force 产物不等于 Strict 全通过。

## 完成标准

- 三个组件的先后关系、关键端口和点击动作完整。
- 用户能够按文档得到 RAW IFC、HIFC-MVD IFC 和 fields JSON。
- 不把尚未完成的 Task12 实机结果写成已验证事实。
- 不包含占位符、待定项或无法执行的抽象描述。
