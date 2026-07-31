# GH H-IFC 映射开发基线 v1

本目录保存从官方 HIFCTool 静态解析结果整理出的 GH / Rhino.Inside.Revit 开发基线。HIFCTool 只作为规则和行为证据来源，项目运行时不依赖该插件。

## 内容

- `docs/`：稳定架构、官方数据边界和实施验收门槛；
- `data/`：166 条武汉规划报建规则、对象映射和命令契约；
- `generated/`：确定性共享参数、IFC UserDefinedPsets 和参数绑定清单；
- `schemas/`：规则包 JSON Schema。

## 重要边界

- `exampleValue` 仅为官方示例值，不是默认值；
- required / conditional / advisory 尚未由官方规划规则文件编码；
- 当前基线作为独立开发支线存在，未经明确授权不得合并到主线。
