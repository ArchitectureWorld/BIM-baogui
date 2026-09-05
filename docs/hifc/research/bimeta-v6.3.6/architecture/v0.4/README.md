# H-IFC_交付架构_证据分层可编辑思维导图_v0.4

## 核心变化

1. 每个节点与每条属性分别记录 `evidenceClaims`。
2. 默认HTML视图为“软件证据路径”，不会把官方要求或自研假设伪装成BIMeta事实。
3. 规划阶段具体Revit载体全部恢复为 `TBD`；`AreaSpace` 等仅保留为已废弃的v0.3开发抽象。
4. `TP* / AG* / UG* / DS00～DS04` 明确标为 `DEV-DEFINED`。
5. `B.* / C.*` 软件表号、CategoryList入口、构件字段标为 `SW-DIRECT`。
6. 本版没有运行BIMeta，因此 `SW-RUNTIME = 0`。
7. 车位相关UI资源仅记录为“相关软件信号”，`softwareConfirmed=false`，不得据此确认室外/室内、车场/车位或Revit载体。

## 文件角色

| 文件 | 用途 |
|---|---|
| `evidence-layered-mindmap.md` | GitHub和开发Agent可直接阅读的证据分层架构。 |
| `validation-report.json` | 结构、证据规则和关键降级项的验证结果。 |
| `artifact-manifest.json` | 本目录研究成果的角色、来源与哈希说明。 |
| `../../mappings/component-dataset-mapping-v0.2.json` | 软件静态提取后的构件/字段证据源。 |
| `../../evidence/core-evidence-index.json` | 核心软件证据索引。 |
| `../../evidence/extraction-manifest.csv` | 安装包提取文件与校验清单。 |

## 开发强制规则

```text
只有 `evidenceType=SW-DIRECT/SW-RUNTIME` 且 `softwareConfirmed=true` 的具体 claim，才可作为BIMeta软件事实。
OFFICIAL 只代表规范要求。
USER 只代表用户输入或使用经验。
INFERRED 必须进入验证队列。
DEV-DEFINED 只用于开发组织，不能导出为官方/BIMeta命名。
```

## 当前确认边界

### 可作为静态软件事实

- BIMeta存在 `ProjectBase` 数据对象，以及 `dNorthSouth`、`dEastWest`、`dElevation`、`dAngle` 字段。
- `CategoryList.xml` 中直接出现的 Revit Category、对象类型与几何适配入口。
- `B.1、B.2、B.9、B.40、B.44、B.45` 建筑构件表号与字段。
- `C.7、C.8、C.11、C.14、C.46、C.49` 结构构件表号与施工图字段。
- 安装包中直接出现的报规、图审、招投标、竣工验收、智慧工地监管等导出入口名称。

### 不能作为软件事实

- 绿地、室外车场、室外车位具体使用 Revit Area、Room、Space、Floor、Generic Model 或 DirectShape。
- `AreaSpace`、`RoadArea`、`BuildingObject`、`StoreyObject` 是 BIMeta 内部类型。
- `TP* / AG* / UG* / DS*` 是 BIMeta 或官方数据集名称。
- 任何尚未通过运行对照确认的 `IfcEntity / Pset / Property / Rel`。

## 已知边界

- 静态提取无法确认运行时服务器下发规则。
- 未运行BIMeta，未抓取网络请求，未对导出H-IFC做行为验证。
- 最终 `IfcEntity / Pset / Property / Rel` 仍需最小模型对照验证。
- 结构类 C.* 文件的“属性表（全部）”存在楼梯模板污染，当前只采用其施工图页。
