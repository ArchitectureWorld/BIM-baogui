# BIMeta V6.3.6 静态逆向与 H-IFC 证据分层架构

## 定位

本目录收录 BIMeta 报建交付软件 V6.3.6 的静态分析、构件—数据集映射基线，以及 H-IFC“交付阶段 → 独立模型 → 对象/构件 → 载体 → 数据集 → 属性”的证据分层架构。

本支线只建立研究与规则证据基线，**不修改现有 Revit 插件生产代码，也不改变既有 HBR 规则包的权威性**。

## 支线与基线

```text
仓库：ArchitectureWorld/BIM-baogui
支线：research/bimeta-v6.3.6
派生基线：feat/revit-native-total-plan-phase1-v0.4.3
研究对象：BIMeta报建交付软件 V6.3.6
架构基线：v0.4 evidence-layered
```

## 当前权威顺序

1. `architecture/v0.4/evidence-baseline.compact.json`：开发 Agent 可直接读取的机器证据基线，明确模型生成条件、软件事实、官方要求、推断和自研定义。
2. `architecture/v0.4/evidence-layered-mindmap.md`：阶段—模型—对象—载体—数据集的 Mermaid 可编辑架构图和证据路径。
3. `mappings/component-dataset-mapping-v0.2.md`：BIMeta 安装包中提取出的构件表号、字段集合与规范化数据集说明。
4. `evidence/core-evidence-index.json`：软件架构、交付入口、规则模型及静态提取统计。
5. `analysis/static-reverse-analysis.md`：完整静态逆向与工作原理解读。
6. `architecture/v0.4/validation-report.json`：v0.4 证据分层成果的自动验证结果。
7. `architecture/v0.4/artifact-manifest.json`：完整可视化原件的文件名、角色、大小和 SHA-256 清单。

## 证据纪律

只有同时满足以下条件的具体声明，才可作为“BIMeta 软件已确认事实”：

```text
evidenceType = SW-DIRECT 或 SW-RUNTIME
AND
softwareConfirmed = true
```

其余来源必须保留原标记：

| 标记 | 含义 | 可否视为 BIMeta 已确认 |
|---|---|---:|
| `SW-DIRECT` | 从安装包、表格、XML、数据库或 DLL 静态提取 | 仅当 `softwareConfirmed=true` |
| `SW-RUNTIME` | 实际运行、建模、导出或网络行为验证 | 是 |
| `OFFICIAL` | 湖北官方指南、标准或示例要求 | 否 |
| `USER` | 用户实际使用经验或明确输入 | 否 |
| `INFERRED` | 基于证据推导的待验证结论 | 否 |
| `DEV-DEFINED` | 自研逻辑名称、编码、载体抽象或占位 | 否 |

特别说明：`AreaSpace`、`TP*`、`AG*`、`UG*`、`DS00～DS04` 等均属于开发抽象；绿地、室外车场、室外车位的具体 Revit 载体尚未从 BIMeta 软件中确认。

## 原始安装程序

- 文件名：`BIMeta报建交付软件-V6.3.6 20260824.exe`
- 大小：`211512032` bytes
- SHA-256：`1b1ac3561e372d2927f9b8d8cac8ff93f0dc5dcfd7c45cde438f83d57e80fe17`
- 仓库状态：**未上传**

原因：该文件为约 211MB 的第三方安装程序。仓库只保存其哈希、提取统计、证据索引和研究成果，避免仓库膨胀及不必要的第三方安装包再分发。

## 已提交目录

```text
analysis/
  static-reverse-analysis.md

evidence/
  core-evidence-index.json
  extraction-manifest-summary.json

mappings/
  component-dataset-mapping-v0.2.md

architecture/v0.4/
  README.md
  evidence-layered-mindmap.md
  evidence-baseline.compact.json
  validation-report.json
  artifact-manifest.json

PAYLOAD_MANIFEST.json
```

## 大体积可视化原件

完整 v0.4 交付包包含离线 HTML、Draw.io、3.3MB 完整 JSON、逐声明 CSV、FreeMind 和浏览器预览图。其文件名、大小及 SHA-256 已写入 `architecture/v0.4/artifact-manifest.json`。

当前会话的 GitHub 写入接口不接受本地文件路径，只接受内联 UTF-8 文本；1.2～3.3MB 原件在当前工具输出链路中无法可靠逐字转运。因此本支线提交可审计的 Markdown、紧凑 JSON 和校验清单，并明确把未提交原件列入 `PAYLOAD_MANIFEST.json`，没有伪报为已上传。

## 开发 Agent 读取顺序

```text
README.md
→ architecture/v0.4/evidence-baseline.compact.json
→ architecture/v0.4/evidence-layered-mindmap.md
→ mappings/component-dataset-mapping-v0.2.md
→ evidence/core-evidence-index.json
→ analysis/static-reverse-analysis.md
```

## 已知边界

- 当前 `SW-RUNTIME = 0`，没有把静态推断伪装成运行确认。
- 未恢复服务器下发的全部标准、映射和值域。
- 规划对象的具体 Revit 载体及最终 `IfcEntity / Pset / Property / Rel` 仍需最小模型运行验证。
- 地下模型仅在项目实际存在地下部分时生成；无地下项目不创建、不校验 `PLAN-BELOW`。
- 本目录不能取代仓库现有正式 HBR 规则权威文件；后续只有经过运行验证和人工批准的条目才能进入生产规则包。
