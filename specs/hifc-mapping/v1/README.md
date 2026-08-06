# GH H-IFC 映射与官方插件兼容历史证据基线 v1

本目录保存从官方 HIFCTool 配置、规划报建规则和现有 GH 产品中整理出的历史证据。下列旧产品路径已被 2026-08-02 三阶段设计替代，不再是当前产品边界：

```text
GH 写入 Revit
→ 官方 H-IFC 插件导出
→ 官方检查软件识别
```

## 历史边界（已替代）

- IFC 仅由官方 H-IFC 插件导出；
- 不开发自有 IFC 导出器；
- 不将 IFC 后处理作为当前 GHA 产品路径；
- Revit 参数写入与回读只证明 Revit 内部一致，不等于官方兼容通过；
- 只有完成 Golden RVT → 官方插件 → Golden IFC → 检查软件闭环，才能把字段标记为正式兼容。

本目录及 2026-08-01 文档继续作为官方插件提取证据和历史研究保留，不再作为当前实施依据。当前唯一产品与架构依据为 `docs/superpowers/specs/2026-08-02-hbr-three-stage-rule-database-design.md`；以下旧设计、计划和 Review 仅供证据追溯：

- `docs/superpowers/specs/2026-08-01-official-plugin-compatible-write-design.md`；
- `docs/superpowers/plans/2026-08-01-official-plugin-compatible-write.md`；
- `docs/reviews/2026-08-01-official-plugin-write-deep-review.md`；
- `data/official_plugin_compatibility_status.v1.json`。

## 官方提取数据

- 规划报建属性：166 条；
- PropertySet：16 个；
- IFC 实体：9 类；
- 官方显式 Revit→IFC 对象映射：4 条；
- 官方规则交叉核验：166/166 通过。

## 必须区分的证据

### 官方已提取

- IFC 实体；
- PropertySet 名；
- IFC 属性名；
- IFC 数据类型与单位；
- 4 条显式 Revit→IFC 对象映射。

### 我们的实现决策，尚需官方软件验证

- `HIFC.<PropertySet>.<Property>` 参数名；
- UUIDv5 参数 GUID；
- 共享参数类别绑定；
- 未被官方显式映射覆盖的对象承载；
- 参数写入后能否由官方插件读取并输出。

## 文件说明

- `data/wuhan_planning_rules.v1.json`：166 条规则主数据；
- `data/official_object_mappings.v1.json`：4 条官方显式对象映射；
- `data/official_plugin_compatibility_status.v1.json`：各实体当前证据与写入策略；
- `generated/GH_HIFC_SharedParameters.txt`：候选共享参数定义；
- `generated/GH_HIFC_ParameterBindings.json`：候选参数绑定清单；
- `generated/GH_HIFC_UserDefinedPsets.txt`：用于比对 PropertySet 与参数别名的生成文件；
- `schemas/`：规则包 JSON Schema；
- `manifest.sha256.json`：基线交付文件校验。

## 强制规则

- `exampleValue` 只作示例，不得自动写入正式模型；
- 必填/条件必填/建议必须来自指南或检查规则，不能从示例推断；
- 规划目标与模型实际值必须分开；
- 核心规则、对象选择和单位转换不得写在 GH `SolveInstance()` 中；
- 找不到官方写入/导出协议的实体必须明确阻断，禁止默认写到 ProjectInformation；
- 当前标准坐标语义固定为 X=南北、Y=东西。
