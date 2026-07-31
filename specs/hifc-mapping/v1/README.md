# GH H-IFC 映射开发基线 v1

本目录保存从官方 HIFCTool 逻辑和配置中整理出的 **Grasshopper / Rhino.Inside.Revit 独立开发基线**。HIFCTool 仅作为规则与行为证据来源，后续实现不以该插件作为运行依赖。

## 分支关系

- 上游开发分支：`feat/stage01-stage02-context-pipeline`
- 当前独立支线：`feat/hifc-mapping-gh-baseline-v1`
- 当前支线未经明确授权不得合并到上游分支或 `main`

## 内容

- `docs/`：稳定架构、官方数据边界、实施顺序与验收门槛；
- `data/`：166 条武汉规划报建属性规则、对象映射、对象承载决策和 GH 命令契约；
- `generated/`：确定性共享参数、IFC UserDefinedPsets 和参数绑定清单；
- `schemas/`：规则包 JSON Schema；
- `manifest.sha256.json`：全部交付文件的大小及 SHA-256 校验值。

## 当前数据规模

- 规划报建属性：166 条；
- PropertySet：16 个；
- IFC 实体：9 类；
- 官方显式 Revit—IFC 对象映射：4 条；
- 确定性 Revit 共享参数：141 个；
- 官方规则交叉核验：166/166 通过。

## 稳定边界

- `exampleValue` 仅为官方示例值，不是自动写入模型的默认值；
- 官方规划报建规则文件尚未编码 REQUIRED / CONDITIONAL / RECOMMENDED；
- GH 核心应以 canonical HIFC data graph 为唯一真源，再分别投影到 Revit 与 IFC；
- 规则判断、参数创建、对象映射、单位转换和 IFC 写入不得硬编码在 Grasshopper 组件的 `SolveInstance()` 中。
