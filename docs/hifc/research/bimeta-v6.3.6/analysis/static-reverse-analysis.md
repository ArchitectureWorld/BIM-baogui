# BIMeta 报建交付软件 V6.3.6：静态逆向与工作原理解读

> 分析对象：`BIMeta报建交付软件-V6.3.6 20260824.exe`  
> 文件大小：211,512,032 bytes（约 201.7 MiB）  
> SHA-256：`1b1ac3561e372d2927f9b8d8cac8ff93f0dc5dcfd7c45cde438f83d57e80fe17`  
> 分析日期：2026-09-04  
> 分析方式：**仅静态分析，未直接运行未知 EXE**。安装包载荷已完整解析，1,369 个业务文件逐项通过安装器内置 SHA-1 校验，0 个不匹配。

---

## 1. 先给结论

**BIMeta 不是一个单纯的 IFC 导出器，而是一套“Revit 数据治理 + 标准规则引擎 + IFC 语义转换 + 在线标准服务”的交付平台。**

它真正有价值的部分，不是“把三维几何存成 IFC”，而是：

1. 判断 Revit 里的构件到底属于哪一类标准对象；
2. 按项目、地区、交付阶段加载对应标准；
3. 给构件补齐分类编码、标识、属性、单位和允许值；
4. 在导出前检查坐标、房间、系统、标识和属性；
5. 使用按 Revit 年份适配的 IFC 内核，把这些数据放到正确的 IFC 实体、属性集和关系中；
6. 按 `IFC4 / SZ-IFC / CD-IFC / H-IFC` 等不同交付配置输出，并执行后续处理。

一句大白话：

> **Revit 模型是原材料；标准库是报建表格；规则引擎是自动填表员；模型检查是预审员；IFC 内核是装箱机；H-IFC 配置是湖北报建窗口要求的装箱清单。**

---

## 2. 软件整体架构

```text
┌──────────────────────────────────────────────────────────────┐
│ 1. 安装与启动层                                              │
│ Inno Setup → JZFamilyMainPortal → 检测 Revit → 部署 AddIn   │
└──────────────────────────────┬───────────────────────────────┘
                               ↓
┌──────────────────────────────────────────────────────────────┐
│ 2. Revit 接入层                                              │
│ Ribbon Application + IFC Application + IFC DBApplication    │
└──────────────────────────────┬───────────────────────────────┘
                               ↓
┌──────────────────────────────────────────────────────────────┐
│ 3. 业务编排层                                                │
│ 标准库 / 一键赋值 / 智能赋参 / 坐标设置 / 模型检查 / 导出   │
└──────────────────────────────┬───────────────────────────────┘
                               ↓
┌──────────────────────────────────────────────────────────────┐
│ 4. 标准与规则层                                              │
│ 分类树 + 标识规则 + 属性定义 + 同义词 + 映射 + 范围 + 公式  │
└───────────────────┬──────────────────────┬───────────────────┘
                    ↓                      ↓
          ┌─────────────────┐    ┌──────────────────────────┐
          │ 5A. 本地数据层  │    │ 5B. 在线标准服务         │
          │ XML/XLSX/SQLite │    │ 用户/团队/标准/规则 API  │
          └────────┬────────┘    └────────────┬─────────────┘
                   └──────────────┬────────────┘
                                  ↓
┌──────────────────────────────────────────────────────────────┐
│ 6. IFC 输出层                                                │
│ 分版本 IFC-for-Revit 内核 → 实体/Pset/空间关系 → 后处理     │
└──────────────────────────────────────────────────────────────┘
```

### 已解析出的总体规模

| 项目 | 结果 |
|---|---:|
| 安装器 | Inno Setup 6.0.0，32 位 Delphi 安装壳 |
| 安装包大小 | 211,512,032 bytes |
| 解压后的业务载荷 | 1,075,044,033 bytes |
| 业务文件 | 1,369 个 |
| DLL | 514 个 |
| XML | 70 个 |
| IFC EXPRESS `.exp` | 32 个 |
| IFC XSD | 52 个 |
| Excel 规则/字典 | 19 个 |
| Revit 族 `.rfa` | 27 个 |
| Revit 样板 `.rte` | 9 个 |
| Revit 插件清单 `.addin` | 3 个 |
| 含 IFC 名称的路径 | 197 个 |

---

## 3. 安装和启动原理

### 3.1 外层 EXE 只是安装器

原文件本体由两部分组成：

- 前约 0.7 MiB：Inno Setup 安装引导程序；
- 后约 200.6 MiB：LZMA2 压缩的业务载荷。

所以直接盯着最外层 EXE 反编译，看到的主要会是安装界面，而不是 BIMeta 的业务逻辑。

### 3.2 启动器负责适配不同 Revit 版本

主入口 `JZFamilyMainPortal.exe` 中可以确认以下职责：

- `GetRevitProducts`：查找已安装的 Revit；
- `ValidVersion`：判断是否在支持范围；
- `CreateAddInFiles`：生成/部署 Revit `.addin`；
- `CopyRevitVesionFiles`：拷贝对应年份的二进制；
- `GetAllUsersAddInFolder` / `GetCurrentUserAddInFolder`：定位 Revit 插件目录；
- `StartRevit`：启动目标 Revit。

安装器还会把安装位置写入：

```text
Software\JZFamily\install
    installPath = <安装目录>
```

并把 `JZFamilyMainPortal.exe` 的快捷方式设置为管理员方式运行。

### 3.3 为什么要按年份放很多 DLL

包内明确分为：

- `Bin4.8/2016 ... 2024`：传统 .NET Framework 4.8 路线；
- `Bin8.0/2025 ... 2026`：.NET 8 路线。

Revit 每个大版本的 API 二进制兼容性并不稳定，所以插件不能简单用一份 DLL 覆盖所有版本。BIMeta 的做法是：**业务模型尽量共用，Revit 适配层和 IFC 导出层按年份编译。**

---

## 4. Revit 是怎样接入 BIMeta 的

安装包中有三个独立的 Revit 插件入口：

| 插件清单 | 类型 | 入口类 | 作用 |
|---|---|---|---|
| `000JzFamily.addin` | Application | `MenuApp` | 创建 BIMeta Ribbon、业务命令、登录与升级入口 |
| `JZRevit.IFC.App.addin` | Application | `App_IFCExporter_StartUp` | 启动和调度 IFC 导出流程 |
| `JZRevit.IFC.DB.addin` | DBApplication | `ExporterApplication` | 在 Revit 数据库/导出服务层注册 IFC 引擎 |

这说明它不是“点按钮后调用 Revit 默认导出”这么简单。它把：

1. 界面和命令；
2. IFC 导出调度；
3. IFC 数据库级导出服务；

拆成了三个入口。

### 4.1 Ribbon 暴露出来的真实业务流程

`JZFamilyRibbon.xml` 中共有 36 个按钮，主要分为：

- 用户中心：登录、注销、个人中心；
- 构件工厂：载入构件；
- 数据准备：数字字典、项目坐标点、房间、标高、族与类型命名；
- 属性治理：一键赋值、智能赋参、属性检查、自定义属性、批量编辑；
- 模型治理：模型检查、专业拆分、楼层拆分；
- IFC 交付：IFC4、SZ-IFC、CD-IFC，以及五类 H-IFC。

这套按钮顺序已经暗示了产品设计思想：

> **先整理模型和数据，再检查，最后导出。**

---

## 5. 核心：标准规则引擎是怎样工作的

这是整个软件最值得学习的部分。

### 5.1 它采用“声明式标准”，不是把所有规则写死在按钮代码里

数据模型中直接存在以下对象：

| 模型 | 含义 |
|---|---|
| `StandardData` | 一套标准及其团队、类型 |
| `StandardClassData` | 分层分类树，含父子关系、层级、同义词 |
| `StandardIdentData` | 某分类用哪个参数识别、参数组、类型/实例方式 |
| `StandAttributeData` | 标准属性：编码、单位、类型、范围、默认值、推荐值 |
| `RuleData / RuleDetailData` | 成组的条件规则：字段、运算符、值、顺序 |
| `AttMappingData / AttMappingDetail` | 条件满足后怎样得到目标属性值 |
| `synonymData` | 分类名或属性名的同义词 |
| `RangeData` | 可选范围和匹配字段 |
| `SaveDefaultValueData` | 项目/标准级默认值 |
| `ProfessionMappingData` | Revit 族/类别到标准分类及编码的映射 |
| `ProjectBase` | 南北坐标、东西坐标、高程、角度 |
| `ModelCheckData / QCCheckData` | 检查树、通过数、不通过数、构件定位 |

其关系可简化为：

```text
标准 Standard
└─ 分类树 Classification Tree
   └─ 某个标准构件类
      ├─ 识别方式 Identification
      ├─ 属性清单 Attributes
      │  ├─ 数据类型 / 单位 / 值域
      │  ├─ Revit 对应参数
      │  ├─ 默认值 / 推荐值
      │  ├─ 同义词
      │  └─ 条件映射 / 公式
      └─ 检查规则 Validation Rules
```

### 5.2 它怎样识别“这个 Revit 构件是什么”

不是只看 Revit Category。可见的数据结构支持组合判断：

- Revit Category；
- 族名、类型名；
- 某个标识参数；
- 参数值；
- 分类同义词；
- 规则组中的运算符和值；
- 项目所属标准、团队和交付类型。

这解决了一个现实问题：同一类门、窗、墙，在不同项目中的族名往往并不统一。标准系统必须允许“防火门”“甲级防火门”“FM-甲”等名称最终归到同一个标准分类。

### 5.3 它怎样自动得到属性值

业务程序集直接暴露出以下方法名称：

- `GetStandardAttributeValue`
- `GetCombinationOrFormulaValue`
- `GetAttributeValueBySuggest`
- `GetAttributeValueByRange`
- `GetAttributeSynonymsValue`
- `GetAttributeMappings`
- `GetParamValue`
- `GetCorresParamName`

因此可以确认，它支持的不只是复制参数，还包括：

1. 从对应 Revit 参数直接读取；
2. 通过同义参数名寻找；
3. 从下拉范围选择或匹配；
4. 使用标准推荐值或默认值；
5. 按多个参数组合或公式计算；
6. 通过条件映射得到结果；
7. 做单位和数据类型转换；
8. 回写到类型参数或实例参数。

概念性伪代码如下。这里表达的是已经确认的机制，具体优先级仍需运行时跟踪：

```text
profile = 读取项目所选标准、地区和交付阶段
rules   = 获取分类树、标识、属性、同义词、映射和值域

elements = 收集 当前选择 / 当前视图 / 全模型 中的构件

for element in elements:
    class = 按 Category、族名、类型名、标识参数和同义词分类

    for attribute in class.required_attributes:
        candidates = [
            对应 Revit 参数,
            同义参数,
            条件映射,
            组合或公式,
            推荐值,
            默认值
        ]
        value = 解析并规范化(candidates, attribute.type, attribute.unit)
        写入或检查(element, attribute, value)

errors = 检查坐标、标识、房间、系统、必填值和值域
```

### 5.4 本地 Excel 是规则模板的重要组成部分

安装包中有 19 个 Excel 文件。门、窗、幕墙、墙、板、楼梯、房间、柱、梁、桩、承台等都有独立属性表。

构件属性表的典型字段包括：

```text
参数分组方式
数据类别
类型参数名称
实例参数名称
是否共享参数
参数特征
规程
参数类型
单位
允许值/类型
备注
当前值
数据类型
```

表中不仅有“参数名”，还有：

- 建筑、空间、构件、分部分项和材质分类编码；
- 类型参数与实例参数的区分；
- 共享参数标识；
- 数据类型和单位；
- 枚举值，如耐火极限、燃烧性能、防水等级、抗震等级；
- 门窗、墙板梁柱、桩和承台的专用属性。

这就是“一键赋值”和“属性检查”能够通用化的基础：**规则在数据里，程序负责解释规则。**

---

## 6. 本地数据与云端标准是怎样分工的

### 6.1 本地数据

本地主要包含：

- XML：专业编码、Revit Category 映射、过滤设置、Ribbon 定义；
- XLSX：构件属性模板、房间字典、分类编码和值域；
- SQLite：机电、照明、管线、房间名称和用户默认值；
- RFA/RTE：标准族和 Revit 样板；
- JSON/EXP/XSD：IFC 实体、属性集和 Schema 支撑文件。

数据库实际内容进一步表明：

- `JZDB.db`：25 张业务表、约 9,653 行，主要是照明利用系数、照度标准、管线和电气字典；
- `JZFamilyData.db`：9 张表、约 1,049 行，核心是 148 个房间类型与 901 个房间名称；
- `UserDb`：用户侧照明布置参数和历史设置。

**本地 SQLite 并不是完整的湖北报建规则库。**

### 6.2 云端标准服务

网络请求程序集直接存在：

- 获取标准列表；
- 获取分类树；
- 获取标识定义；
- 获取属性列表；
- 获取属性映射；
- 获取属性同义词；
- 获取值域；
- 保存默认值和同义词；
- 获取用户团队和默认团队；
- 登录、设备检查和注销。

因此完整版逻辑是：

```text
安装包内置基础模板和运行框架
        +
登录后从服务端取得当前标准、团队配置和最新规则
        =
用户实际看到的可用标准库
```

这带来一个非常重要的逆向结论：

> **仅凭这个安装包，可以较完整地复原“规则引擎如何工作”，但不能保证拿到服务端当前全部 H-IFC 规则数据。**

也就是说，安装包不是一份静态的“湖北映射大全”；它更像一个标准解释器，具体标准内容有一部分在线下发。

---

## 7. 模型检查原理

已确认的检查方法包括：

- `CheckBuildValues`
- `CheckSystemTypes`
- `CheckIdentValues`
- `CheckRoomUsage`
- `CheckBasePoint`
- `CheckProjectBasePoint`
- `CheckValue`

检查界面支持三种典型范围：

```text
Selection：选择构件
View：当前视图
All：全模型
```

检查结果不是一段文字，而是树状对象：

- 分类节点；
- 构件节点；
- 参数节点；
- 通过/不通过数量；
- ElementId；
- 可见性、展开状态；
- 可定位到 Revit 构件。

因此其本质是：

> **把标准规则转成一组可以逐构件执行的断言，并把失败项重新关联到 Revit ElementId。**

例如：

```text
墙体必须存在“构件分类编码”
门的防火等级必须属于允许枚举
结构柱必须有柱编号
房间用途必须匹配标准房间字典
项目基点必须包含南北、东西、高程和角度
某属性的数据类型和单位必须可转换
```

---

## 8. IFC 导出内核是怎样工作的

### 8.1 不是简单调用系统“另存为 IFC”

包内有完整的：

- `JZRevit.IFC.Export.dll`
- `JZRevit.IFC.Common.dll`
- `JZRevit.IFC.Version.dll`
- `RevitAPIIFC.dll`
- `GeometryGymIFC.dll`
- IFC2X2 / IFC2X3 / IFC4 的 EXPRESS 与 XSD 文件；
- 按 Revit 2016—2026 分版的导出程序集。

日志中的源码路径直接指向 `IFC-for-Revit` 工程结构。因此可以高置信确认：

> **BIMeta 的 IFC 内核是基于 IFC-for-Revit 路线做的版本化集成或定制，而不是从零写一个几何导出器。**

### 8.2 IFC 导出要完成四种转换

#### A. Revit 对象 → IFC 实体

例如墙、板、门、窗、房间、管道、风管等，要选择正确的 IFC 实体类型。

#### B. Revit 参数 → IFC 属性和属性集

“参数写进 Revit”并不等于“IFC 中位置正确”。导出器还要决定：

- 写到哪个 `Pset`；
- 属性叫什么；
- 使用 Text、Label、Identifier、Length、Area、Boolean 还是枚举；
- 是类型级还是实例级；
- 是否关联分类引用。

#### C. Revit 层级 → IFC 空间关系

项目、场地、建筑、楼层、空间、构件必须建立正确的包含和关联关系。

#### D. Revit 坐标 → IFC 坐标与定位

软件中明确存在：

```text
dNorthSouth
dEastWest
dElevation
dAngle
```

并且界面有“项目坐标点设置”和专门的基点检查。这说明坐标不是附属信息，而是报规交付的核心输入。

### 8.3 导出配置不是一个，而是一组 Profile

Ribbon 中可以确认以下输出：

- IFC4.0；
- SZ-IFC；
- CD-IFC；
- H-IFC（规划报建）；
- H-IFC（施工图审查）；
- H-IFC（招投标）；
- H-IFC（竣工验收）；
- H-IFC（智慧工地监管）。

因此，**H-IFC 不是单一、固定不变的格式，而是一组以湖北标准为共同底座、面向不同阶段的交付配置。**

不同 Profile 很可能会改变：

- 必填分类；
- 必填属性；
- 属性集；
- 检查强度；
- 输出范围；
- 文件命名或包装；
- 后处理流程。

其中“具体每个 Profile 的完整字段差异”仍需要服务端规则或运行样本验证，不能仅靠安装包做绝对结论。

---

## 9. 为什么“写了同名属性”仍可能识别失败

这是理解报建 IFC 最关键的一点。

官方平台通常不会只检查某个字符串是否出现。它可能同时检查：

1. 构件是不是正确的 IFC Entity；
2. 属性是不是挂在正确的 Pset；
3. Pset 是挂在实例还是类型；
4. 属性名称是否精确；
5. 数据类型是否精确；
6. 单位是否正确；
7. 分类编码是否通过 IFC 分类关系挂接；
8. 构件是否位于正确的建筑/楼层/空间层级；
9. 项目坐标和方向是否正确；
10. GUID、文件头、Schema 版本和后处理包装是否满足要求。

所以：

```text
Revit 中存在“规划净用地”参数
≠
H-IFC 中已经形成平台要求的数据结构
```

必须经过一张明确的映射表：

```text
Revit 对象/参数
    → 标准分类
    → IFC Entity
    → Pset 名称
    → Property 名称
    → IFC 数据类型/单位
    → 实例/类型挂接
    → 空间或分类关系
```

---

## 10. 哪些数据适合写入 Revit，哪些应在导出阶段生成

| 数据类别 | 推荐位置 | 原因 |
|---|---|---|
| 项目信息、栋号、专业、阶段 | Revit 项目/共享参数 | 需要持续维护和人工可见 |
| 构件分类编码、用途、材质、等级 | Revit 类型或实例参数 | 便于建模过程中检查和修改 |
| 房间名称、用途和空间分类 | Revit Room/Space | 与空间对象天然关联 |
| 项目南北/东西坐标、高程、角度 | Revit 项目信息或专用数据对象 | 需要在建模和导出前核对 |
| IFC Entity 选择 | 导出器 | 取决于 Revit 对象和交付 Profile |
| Pset 组装、IFC 数据类型和单位 | 导出器 | 这是 IFC 语义层，不只是 Revit 参数 |
| 空间包含关系和分类关联 | 导出器 | 需要创建 IFC Relationship 实体 |
| 几何、放置、GUID | 导出器 | 由模型和导出上下文计算 |
| 文件头、Profile 标记、压缩/封装 | 导出或后处理 | 属于最终交付文件层 |

因此得到一个比“必须前处理还是必须后处理”更准确的结论：

> **大部分业务属性可以、也应该提前写入 Revit；但 H-IFC 的结构化表达必须由正确的导出器完成。后处理不是理论上永远必需，但 BIMeta 本身确实存在导出后的外部处理流程，因此完全复刻它的交付结果时不能先假定后处理可省略。**

---

## 11. 对我们自研 BIM 报规工具的直接启示

最合理的自研架构不是“Grasshopper 里堆一大串字段”，而是分成三个明确层次：

### 第一层：唯一标准模型

建立机器可读的统一规则对象，例如：

```text
StandardProfile
Classification
IdentificationRule
AttributeDefinition
ParameterMapping
ValueRule
ValidationRule
IFCMapping
```

Excel 只能作为编辑和导入界面，不能作为唯一运行时真相。

### 第二层：Revit 数据适配器

负责：

- 创建共享参数；
- 区分类型/实例；
- 读取和回写；
- 单位转换；
- Category/族/类型识别；
- 项目基点；
- Room/Space；
- 实时检查和错误定位。

RhinoInside/GH 可以作为交互和计算入口，但最终写入必须通过 Revit API 的稳定数据层完成。

### 第三层：IFC 语义导出与验证器

负责：

- Revit → IFC Entity；
- 参数 → Pset/Property；
- 分类关联；
- 空间结构；
- 坐标；
- Profile 差异；
- 导出后结构检查；
- 与官方平台样本对比。

推荐的数据链是：

```text
GH / Revit UI
      ↓
统一标准 JSON/数据库
      ↓
Revit 参数写入器 + 模型检查器
      ↓
自研 IFC 映射导出器
      ↓
独立 H-IFC Validator
```

### 最重要的开发原则

1. **不要把“Revit 参数名”当成标准本身。**
2. **不要把“IFC 文件能打开”当成报建合格。**
3. **映射表必须精确到 Entity、Pset、Property、类型、单位和关系。**
4. **标准规则与程序代码解耦。**
5. **每一条映射都用最小模型和官方软件做单变量验证。**

---

## 12. 技术栈为什么这样组合

| 技术 | 在软件中的用途 |
|---|---|
| .NET Framework 4.8 / .NET 8 | 兼容 Revit 2016—2026 |
| WPF + WinForms | Revit 内窗口、树表、批量编辑和弹窗 |
| Revit API / RevitAPIIFC | 模型读取、参数写入和 IFC 接入 |
| IFC-for-Revit 派生内核 | 几何和 IFC 实体导出 |
| GeometryGymIFC / EXPRESS / XSD | IFC 模型、Schema 和语义处理 |
| SQLite | 本地字典、缓存、房间和机电数据 |
| MySQL/NHibernate 相关库 | 旧业务或数据访问框架 |
| NPOI/OpenXML/libxl | Excel 模板和批量数据导入导出 |
| ODA/Teigha 模块 | DWG/DGN/PDF/几何等辅助格式能力；具体调用范围需动态验证 |
| Web API / RestSharp | 标准库、用户、团队和文件服务 |
| Tencent COS | 更新包或云文件存储 |
| WebView2 | 内嵌网页、个人中心或在线业务界面 |

---

## 13. 工程质量与风险观察

这些内容不等同于“恶意软件判断”，只是安装包工程审查结果。

### 已确认的问题

1. **最外层安装器未发现 Authenticode 签名目录。** 这只是来源可信度信号不足，不足以单独判断安全性。
2. **版本元数据不完全统一。** 主 EXE 和更新 XML 为 `6.3.6.0`，包内另有同名主 DLL 为 `6.3.5.0`。
3. **存在较多历史配置。** 例如配置仍写 Revit 2016—2024、产品 2021，但包内已经包含 2025—2026。
4. **网络配置混用 HTTPS、HTTP、域名和裸 IP。** 对账号、规则和更新链路而言，这属于应改进的安全与运维问题。
5. **发布包保留了 PDB、开发机源码路径和测试日志。** 不影响核心功能，但暴露内部工程结构并增加体积。
6. **更新日志中存在密钥长度错误和临时更新包缺失记录。** 说明升级链路曾出现异常。
7. **核心业务程序集做了较强的混淆/保护。** 类型和方法元数据仍可读，但很多 IL 方法体被故意破坏或运行时还原，静态反编译结果不能当作真实源码。

### 不能据此下结论的事项

- 不能仅因未签名就断言有恶意行为；
- 不能仅凭字符串判断某个服务当前仍在使用；
- 不能在未运行和抓包的情况下确认每个网络接口的实时行为；
- 不能从本安装包单独恢复服务端最新、完整的 H-IFC 标准库。

---

## 14. 证据强度分级

| 结论 | 置信度 | 依据 |
|---|---|---|
| 软件是 Revit 插件平台而非独立查看器 | 已证实 | `.addin`、RevitAPI、Ribbon 和入口类 |
| 标准规则采用分类树、属性、同义词、映射和值域模型 | 已证实 | 数据模型类型、字段和请求方法 |
| 本地 Excel/SQLite 只是一部分数据 | 已证实 | 文件内容与服务端标准 API 并存 |
| IFC 内核基于 IFC-for-Revit 路线 | 高置信 | 导出程序集结构、Schema、日志源码路径 |
| H-IFC 分五种生命周期 Profile | 已证实 | Ribbon 命令和导出枚举 |
| 导出存在外部后处理步骤 | 高置信 | 导出日志与 `Utils_Process`/外部进程调用痕迹 |
| 每种 H-IFC 的完整字段清单 | 尚未完全确认 | 一部分规则通过登录后在线下发 |
| 所有网络接口当前可用 | 未验证 | 本次未执行程序、未抓包 |

---

## 15. 学习这套原理的推荐顺序

### 第一步：Revit 插件基础

理解：

- `IExternalApplication`、`IExternalCommand`；
- Document、Element、Category、Family、Type、Instance；
- Shared Parameter；
- Transaction；
- FilteredElementCollector；
- Project Base Point / Survey Point；
- Room / Space。

### 第二步：声明式规则引擎

重点学习：

- 分类树；
- 属性 Schema；
- 同义词与映射；
- 值域和默认值；
- 条件规则组；
- 规则版本和 Profile；
- 错误定位与可解释性。

### 第三步：IFC 语义

重点不是先学几何，而是：

- IFC Entity；
- Type 与 Occurrence；
- Pset 与 Property；
- Spatial Structure；
- Classification Association；
- Unit；
- GlobalId；
- IFC2X3 与 IFC4 差异。

### 第四步：Exporter 与 Validator

最后再做：

- Revit → IFC 映射；
- 几何与坐标；
- Profile 输出；
- STEP 文件结构；
- 结构化校验；
- 官方软件回归测试。

---

## 16. 本次最关键的三个结论

### 结论一

**BIMeta 的核心竞争力是标准语义和规则系统，而不是 IFC 几何导出本身。**

### 结论二

**“属性写进 Revit”是必要的数据准备，但“官方 H-IFC 能识别”取决于导出器是否把它映射到正确 IFC 位置。**

### 结论三

**安装包已经足以还原产品架构和规则运行方式，但不足以单独还原服务端当前全部 H-IFC 映射值；精确映射仍应通过官方样本与单变量测试确认。**

---

## 17. 关键证据文件索引

```text
FamilyProduct.ini
UpdateList.xml
Data/000JzFamily.addin
Data/JZRevit.IFC.App.addin
Data/JZRevit.IFC.DB.addin
Data/JZFamilyRibbon.xml
Data/ExportSetting.xml
Data/CategoryList.xml
Data/FilterSetting.xml
Data/*.xlsx
Data/JZDB.db
Data/JZFamilyData.db
Data/UserDb
Bin4.8/JZFamilyInteraction.dll
Bin4.8/JzDataModel-FamilyManager.dll
Bin4.8/JzWebAPIRequest-FamilyManage.dll
Bin4.8/JZRevit.IFC.Export.dll
Bin4.8/JZRevit.IFC.Common.dll
Bin4.8/JZRevit.IFC.Version.dll
Bin8.0/2025/*
Bin8.0/2026/*
```

完整文件清单及每个文件的 SHA-1 已另附在提取清单中。
