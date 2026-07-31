# 官方 H-IFC 兼容写入集成设计

## 目标

保留现有 `01 文件初始化` 与 `02 模型任务与骨架分流` 的 GH 产品交互。新增官方 H-IFC 兼容写入组件，将属性按已提取的官方属性名称、稳定 GUID、Revit 类别、实例/类型作用域和数据类型写入 Revit，最终继续由官方插件导出 IFC。

## 冻结边界

- 不开发 IFC 导出器。
- 不做 IFC 文件后处理。
- GH 只负责参数安装、类别绑定、属性写入、回读验证和兼容性诊断。
- 参数定义来自 `specs/hifc-mapping/v1/generated/GH_HIFC_SharedParameters.txt`。
- 参数绑定来自 `specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json`。
- 写入时可使用 `propertyId`、`parameterGuid` 或完整 `parameterName` 定位属性。
- 空 ElementId 列表默认写入 `ProjectInformation`；提供 ElementId 时写入指定实例或其类型。

## 组件

新增 `湖北BIM报规｜03 官方H-IFC属性写入`：

### 输入

1. `执行`：Boolean。
2. `元素Id`：Integer List；为空时使用 ProjectInformation。
3. `属性`：Text List；支持 propertyId、GUID、完整参数名。
4. `值`：Text List；一项值可广播到多个属性或元素。

### 输出

1. `成功`：Boolean。
2. `状态`：Text。
3. `消息`：Text List。
4. `写入数量`：Integer。

## 写入事务

`Validate → TransactionGroup → 安装缺失共享参数 → 绑定类别 → Parameter.Set → Regenerate → 回读 → Assimilate`。任何写入或回读失败均整体回滚。

## 数据转换

- TEXT → String。
- INTEGER → Integer。
- YESNO → 0/1，并接受 true/false、是/否、1/0。
- LENGTH → 输入按米，转换为 Revit 内部单位。
- AREA → 输入按平方米，转换为 Revit 内部单位。
- VOLUME → 输入按立方米，转换为 Revit 内部单位。
- 其他 Double → 直接写入数值。

## 验收

- Revit 2020 + Rhino 8 + Rhino.Inside.Revit。
- 新 `.gha` 可加载。
- 同一属性重复执行不会重复创建参数。
- GUID、名称和类别绑定与映射包一致。
- 写入后按 GUID 回读一致。
- 失败事务整体回滚。
- 官方插件可继续执行 IFC 导出。