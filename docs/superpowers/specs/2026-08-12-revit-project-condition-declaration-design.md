# Revit Stage01 项目条件必填声明设计

## 目标

将“项目条件”从默认布尔值集合改为必须由用户或 MCP 明确声明的 Stage01 前置步骤，同时保持现有 HBR 参考数据库、Stage01 Payload 结构、Stage02 条件消费方式和人工/MCP 双入口一致。

## 当前问题

权威规则包中的项目条件默认均为 `false`。当前校验只验证条件键存在，因此用户没有操作时也会被解释为“所有条件均不存在”。这无法区分：

1. 用户确认项目确实没有任何列出的条件；
2. 用户尚未填写项目条件。

此外，项目条件当前位于目录末尾，不符合它对后续 Stage02 适用性判断的前置地位。

## 状态模型

新增原生工作流哨兵条件：

```text
conditionId: workflow.project_conditions.none
displayName: 无上述项目条件（已确认）
```

该哨兵不进入 HBR 权威参考数据库，不代表 IFC 属性，也不参与 Stage02 规则匹配；它只用于表达用户已经完成“无条件”声明。它与现有项目条件一起保存在 Stage01 Payload 的 `conditions` 对象中，因此不新增 Payload 根字段、不修改 Extensible Storage Schema，也不提升 `0.9.0` Payload 协议版本。

项目条件声明只有四种状态：

| 状态 | 实际条件 | 无上述条件 | 结果 |
|---|---:|---:|---|
| 未声明 | 全部 false | false 或缺失 | 阻断 |
| 已声明实际条件 | 至少一个 true | false 或缺失 | 通过 |
| 已声明无条件 | 全部 false | true | 通过 |
| 冲突 | 至少一个 true | true | 阻断 |

## 人工界面

- `项目条件`固定为 Stage01 左侧目录第一项，并且新建、读取及重新加载后默认打开。
- 页面标题明确标注“必填”。
- 用户必须勾选一个或多个实际条件，或者勾选“无上述项目条件（已确认）”。
- 勾选任一实际条件时，自动取消“无上述项目条件”。
- 勾选“无上述项目条件”时，自动取消全部实际条件。
- 取消最后一个实际条件且未选择“无上述项目条件”时，恢复未声明状态并阻断校验。
- 左侧目录以 1 个未完成必填项提示未声明或冲突状态。

## 校验与兼容

新增错误码：

```text
PROJECT_CONDITION_DECLARATION_MISSING
PROJECT_CONDITION_DECLARATION_CONFLICT
```

兼容策略：

- 旧 Payload 中至少一个实际条件为 `true`：视为已明确选择实际条件，继续有效。
- 旧 Payload 中所有实际条件均为 `false`，且没有哨兵：要求用户补做一次声明。
- Payload 同时包含实际条件和哨兵为 `true`：拒绝写入。
- 现有条件键完整性校验继续保留。

## MCP

`bimbaogui_stage01_get_form_schema` 将：

- 把 `default_active_group` 返回为项目条件目录；
- 返回 `condition_declaration.required = true`；
- 返回哨兵 ID、显示名称和互斥规则；
- 在条件列表中标识实际条件与“无上述条件”选项。

`bimbaogui_stage01_validate` 和人工界面调用同一个 `NativeStage01Validator`，不存在 MCP 绕过路径。

## H-IFC 边界

Stage01/Stage02 的写入与 Revit 回读只能证明数据进入 RVT，并不能证明 H-IFC 已识别。只有后续 Stage03 完成 IFC4 RAW 导出、H-IFC 转译、exact 回读及官方检查软件验证后，才能宣称 H-IFC 闭环通过。当前界面和文档不得把 Stage01/02 成功表述为 H-IFC 验收成功。

## 不变项

- HBR 权威参考数据库及其 SHA-256；
- Stage01 Payload 根结构与协议版本 `0.9.0`；
- Extensible Storage GUID 与字段；
- 固定 Revit 参数 GUID；
- X 为南北坐标、Y 为东西坐标；
- Stage01 事务、回读和回滚；
- Stage02 扫描、预览哈希、事务及部分成功；
- 现有 9 个 MCP 工具名称。
