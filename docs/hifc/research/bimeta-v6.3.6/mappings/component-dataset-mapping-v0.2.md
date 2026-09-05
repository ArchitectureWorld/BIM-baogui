# BIMeta V6.3.6 构件—数据集映射基线 v0.2

> 本基线整理的是安装包内可确认的“业务数据集”。文中的 `DS_*` 是本次逆向建立的规范化名称，**不是已确认的官方 H-IFC Pset 名称**。

## 1. 核心映射逻辑

```text
构件最终数据
= 项目/空间上下文引用
+ 身份与关联数据集
+ 专业通用数据集
+ 构件专用数据集
+ 当前交付 Profile 启用的生命周期数据集
```

BIMeta 的模板把大量值暂存在 Revit 构件实例参数中；导出时再依据构件分类、空间、材质和当前交付阶段，将其转换成 IFC 实体、属性集与关系。

## 2. 公共数据集

| 数据集 | 挂接对象 | 包含数据 |
|---|---|---|
| `DS_PROJECT_BASE` | Project / Site定位上下文 | 南北坐标、东西坐标、高程、角度 |
| `DS_ELEMENT_IDENTITY` | 构件实例暂存；部分字段导出为关系 | 建筑分类编码、建筑序列号、空间分类编码、空间序列号、构件分类编码、构件序列号、分部分项编码、材质编码、类目名称 |
| `DS_DESIGN_COMMON_ARCH` | 建筑构件 | 设计单位、设计资质、长度〔mm〕、宽度〔mm〕、厚度〔mm〕、面积〔㎡〕、体积〔m³〕、材质、楼层 |
| `DS_DESIGN_COMMON_STRUCT` | 结构构件 | 设计单位、设计资质、长度〔mm〕、宽度〔mm〕、厚度〔mm〕、体积〔m³〕、材质、楼层、抗震等级 |
| `DS_PROCUREMENT` | 采购/交付阶段构件 | 采购合同编号、品牌、供应商名称、价格〔元〕、供应商联系电话（手机）、供应商联系电话（座机）、制造商名称、生产批次、出厂编号、出厂日期、产品型号 |
| `DS_CONSTRUCTION` | 施工/验收阶段构件 | 施工单位、进场时间、施工位置、施工日期、施工验收时间 |
| `DS_MAINTENANCE` | 竣工/运维阶段构件 | 保修期〔年〕、保养周期〔月〕、维修商单位名称、维修商联系人、维修商联系电话（手机）、维修商联系电话（座机） |
| `DS_SPACE_DICTIONARY` | Room / Space | 专业/空间类型、房间名称、本地Room类型词典、项目型房间名称词典 |

其中，结构模板原始“通用数据”块同时包含身份字段。v0.2 已拆成 `DS_ELEMENT_IDENTITY` 与 `DS_DESIGN_COMMON_STRUCT`，避免重复。

## 3. 构件—数据集矩阵

> 下表中的 IFC 实体是语义候选，不是已经由 H-IFC 样本确认的最终实体映射。所有构件在采购、施工、竣工阶段还可按 Profile 追加 `DS_PROCUREMENT`、`DS_CONSTRUCTION`、`DS_MAINTENANCE`。

| 构件 | Revit识别入口 | IFC实体候选 | 施工图阶段数据集 | 构件专用数据 |
|---|---|---|---|---|
| 建筑窗 | OST_Windows / FamilyInstance | IfcWindow | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_WINDOW` | 底高度〔mm〕、高度〔mm〕、开启面积〔㎡〕、K值〔W/(㎡*K)〕、太阳得热系数〔建议：无量纲；源：年〕、耐火极限〔h〕 |
| 建筑门 | OST_Doors / FamilyInstance | IfcDoor | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_DOOR` | 底高度〔mm〕、高度〔mm〕、开启面积〔㎡〕、K值〔W/(㎡*K)〕、太阳得热系数〔建议：无量纲；源：年〕、耐火极限〔h〕 |
| 建筑门窗（合并模板） | OST_Doors 或 OST_Windows | IfcDoor / IfcWindow | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_DOOR_WINDOW` | 底高度〔mm〕、高度〔mm〕、开启面积〔㎡〕、K值〔W/(㎡*K)〕、太阳得热系数〔建议：无量纲；源：年〕、耐火极限〔h〕 |
| 建筑幕墙 | OST_Walls（Curtain Wall）/ 幕墙嵌板与竖梃辅助识别 | IfcCurtainWall | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_CURTAIN_WALL` | 幕墙厚度〔mm〕、K值〔W/(㎡*K)〕、太阳得热系数〔建议：无量纲；源：年〕、耐火极限〔h〕 |
| 建筑墙 | OST_Walls / Wall | IfcWall | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_ARCH_WALL` | 墙身厚度〔mm〕、主要材料导热系数〔建议：W/(m·K)；源：W/(㎡*K)〕、主要材料密度〔kg/m³〕、D值、K值〔W/(㎡*K)〕、耐火极限〔h〕、燃烧性能等级 |
| 地面板、楼层板、楼层板带 | OST_Floors / Floor | IfcSlab | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_ARCH_SLAB` | 屋面构造、主要材料导热系数〔建议：W/(m·K)；源：W/(㎡*K)〕、主要材料密度〔kg/m³〕、D值、K值〔W/(㎡*K)〕、耐火极限〔h〕、燃烧性能等级、防水等级 |
| 楼梯 | OST_Stairs / Stairs | IfcStair + IfcStairFlight | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_STAIR` | 梯段宽度〔mm〕、梯段高度〔mm〕、踏步深度〔mm〕、踏步高度〔mm〕、踢面数 |
| 专用功能房间、成品房 | Room/Space 或 Generic Model/FamilyInstance | IfcSpace 或 IfcBuildingElementProxy | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_ARCH` + `DS_SPECIAL_SPACE_OR_POD` | 功能类型、外墙墙体构造、内墙墙体构造、楼面构造、地面构造、顶棚构造 |
| 混凝土柱 | OST_StructuralColumns / FamilyInstance | IfcColumn | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_CONCRETE_COLUMN` | 混凝土强度等级、截面尺寸〔mm〕、保护层厚度〔mm〕、B边钢筋、H边钢筋、角筋、箍筋、节点核心区加密筋、耐火极限〔h〕、燃烧性能等级 |
| 混凝土墙 | OST_Walls / Wall | IfcWall | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_CONCRETE_WALL` | 混凝土强度等级、厚度〔mm〕、内保护层厚度〔mm〕、外保护层厚度〔mm〕、水平分布筋、竖直分布筋、耐火极限〔h〕、燃烧性能等级 |
| 基础组件桩 | OST_StructuralFoundation（通常） | IfcPile | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_PILE` | 桩伸入长度〔mm〕、桩形状、桩长度〔mm〕、桩截面尺寸〔mm〕、桩受力类型、是否扩底、扩底直径〔mm〕、扩底端侧高〔mm〕、纵筋、箍筋、计算沉降量〔mm〕 |
| 基础组件承台 | OST_StructuralFoundation（通常） | IfcFooting / PredefinedType=PILE_CAP | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_PILE_CAP` | 长度〔mm〕、宽度〔mm〕、外径〔mm〕、混凝土强度等级、保护层厚度〔mm〕、与总体基点坐标X轴夹角〔角度值〕、X向底筋、Y向底筋、X向面筋、Y向面筋、沿边线两桩之间的底筋、腰筋、桩数量〔根〕、计算沉降量〔mm〕 |
| 混凝土板 | OST_Floors / Floor | IfcSlab | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_CONCRETE_SLAB` | 混凝土强度等级、上保护层厚度〔mm〕、下保护层厚度〔mm〕、X向面筋、Y向面筋、X向底筋、Y向底筋、耐火极限〔h〕、燃烧性能等级 |
| 混凝土梁 | OST_StructuralFraming / FamilyInstance | IfcBeam | `DS_ELEMENT_IDENTITY` + `DS_DESIGN_COMMON_STRUCT` + `DS_CONCRETE_BEAM` | 混凝土强度等级、横截面宽度〔mm〕、横截面高度〔mm〕、保护层厚度〔mm〕、底筋、1端面筋、2端面筋、贯通筋、腰筋、箍筋、密筋、吊筋、耐火极限〔h〕、燃烧性能等级 |

## 4. “挂载”在 IFC 中的真实含义

```text
Revit构件实例（数据暂存）
├─ IFC实体及Pset：身份、通用、专用、生命周期属性
├─ 空间结构关系：归属建筑、楼层及空间
├─ 分类关系：构件分类、分部分项等编码
└─ 材料关系：材质与材质编码
```

因此，“在 Revit 构件上写入一个叫材质编码的参数”并不等于完成 H-IFC 映射。导出器还需要决定它是普通 Property，还是应转换为 `IfcRelAssociatesMaterial`；建筑、空间和分类编码同理。

### 推荐的规范化挂接层级

| 层级 | 典型数据 |
|---|---|
| Project / Site | 南北坐标、东西坐标、高程、角度 |
| 构件实例 Occurrence | 构件序列号、实际位置、楼层、实际尺寸、施工验收数据 |
| 构件类型 Type | 型号、名义尺寸、热工性能、耐火等级、标准构造等共享数据；最终归属待样本验证 |
| IFC关系 | 建筑/楼层/空间包含、分类关联、材料关联 |
| IFC Pset | 不能表达为实体或关系的业务属性 |

## 5. 典型示例：建筑门

```text
建筑门
├─ DS_ELEMENT_IDENTITY
│  ├─ 建筑/空间/构件分类编码与序列号
│  ├─ 分部分项编码
│  └─ 材质编码、类目名称
├─ DS_DESIGN_COMMON_ARCH
│  ├─ 设计单位、设计资质
│  ├─ 长/宽/厚、面积、体积
│  └─ 材质、楼层
├─ DS_DOOR
│  ├─ 底高度、高度、开启面积
│  ├─ K值、太阳得热系数
│  └─ 耐火极限
└─ IFC语义关系
   ├─ 归属楼层/建筑
   ├─ 关联房间或空间边界
   ├─ 关联分类
   └─ 关联材料
```

## 6. 源模板中已经确认的问题

- 结构模板原始“通用数据”块已包含身份字段；v0.2将其拆为DS_ELEMENT_IDENTITY与DS_DESIGN_COMMON_STRUCT，避免重复挂载。
- 结构构件的“属性表（全部）”专用字段存在复制楼梯字段的明显历史错误，因此本基线以“属性表（施工图）”为准。
- 太阳得热系数的单位在源模板中写为“年”，应视为源数据错误；该值通常应为无量纲。
- K值在不同模板中有“数值”和“文字”两种参数类型，需在自研标准中统一。
- 详细构件模板几乎全部标为实例参数，但Data/建筑.xlsx又把墙属性全部标为类型参数，说明安装包内存在两代或两套模板。
- 桩和承台模板的通用数据中出现材质编码、类目名称重复行。
- 本地安装包不含服务端当前完整H-IFC Pset名称及所有字段，因此当前只能确定业务数据集，不能把候选Pset名称当作最终标准。

## 7. 尚需官方样本验证的部分

- 每个业务数据集对应的精确H-IFC Pset名称
- 每个字段的精确IFC Property名称及数据类型
- 类型级与实例级最终挂接
- 分类编码是否通过IfcRelAssociatesClassification而非仅写文本
- 建筑/空间/材质代码如何转成IFC关系实体
- 规划报建、施工图审查、招投标、竣工、智慧工地五个Profile的字段差异

这意味着 v0.2 已足以作为自研规则引擎和数据模型的业务骨架，但尚不能直接作为最终 H-IFC 导出映射表。
