# Stage 01 GHA 架构说明

## 目标

以一个编译型 Grasshopper 运算器完成 Revit 2020 单文件初始化，不依赖外部 JSON、旧版脚本电池、网页表单或 Revit WPF 面板。

## 运行链路

```text
Revit 2020
  └─ Rhino.Inside.Revit
      └─ Rhino 8 / Grasshopper
          └─ BIMBaoGui.Stage01.gha
              ├─ 自定义 GH_ComponentAttributes 交互
              ├─ Stage01 字段与规则内核
              ├─ Rhino.Inside.Revit 宿主检测和动作排队
              └─ Autodesk.Revit.DB / UI API
```

## 层次

| 层 | 职责 |
|---|---|
| `Stage01Component` | GH 生命周期、输出、状态、持久化 |
| `Stage01ComponentAttributes` | 单组件内字段、选项、按钮和状态渲染 |
| `Core` | 数据模型、确定性载荷、校验 |
| `Infrastructure` | 内置 MVD 注册表加载与默认值 |
| `Revit` | 活动文档、空白门禁、事务写入、Extensible Storage、回读验证 |

## 写入事务

```text
点击“写入并回读”
→ Rhino.Inside.Revit.EnqueueAction
→ TransactionGroup
→ Transaction
→ 单位、ProjectLocation、ProjectInfo、DataStorage
→ Commit
→ Revit API 回读
→ 一致：Assimilate
→ 不一致：RollBack
```

## 单文件交付

MVD 注册表以 EmbeddedResource 方式嵌入 `.gha`。用户运行时只需要 `BIMBaoGui.Stage01.gha`。
