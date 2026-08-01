# v0.8.1 `OfficialHifcMappingCatalog` 类型初始化失败诊断

## 1. 用户侧真实错误

v0.8.1 已经消除了“初始化状态下拉选项无效”的二次错误，因此本次界面能够显示真正异常：

```text
最近写入：初始化失败，事务已回滚：
“BIMBaoGui.Stage01.Hifc.OfficialHifcMappingCatalog”的类型初始值设定项引发异常。
```

## 2. 复现方法

为避免继续依赖用户截图猜测，新增两层运行时测试：

1. Python 按目录加载 166 条规则与 166 条参数绑定，执行与 C# 目录相同的数据完整性检查；
2. net48 xUnit 直接链接生产 `OfficialHifcMappingCatalog` 源码，嵌入正式 JSON 资源并强制初始化 `Instance`。

第一层测试立即复现出具体问题：

```text
propertyId = ff217d10-c277-50fb-8e5e-8cc2ac8f4585
parameterName = HIFC.组织通用属性集.企业名称
category = null
```

## 3. 根因

生成的映射包同时包含：

- 可以直接绑定到 Revit 类别的映射；
- 当前没有 Revit 原生承载、必须阻断等待官方插件协议的映射，例如 `IfcOrganization`。

`IfcOrganization` 记录没有 `category` 是有意的，因为它不能被伪装绑定到任意 Revit 类别。

但 v0.8.1 的目录加载器在加载全部 166 条规则时错误地要求每条记录都必须存在：

```text
propertyId
parameterName
category
```

因此目录尚未开始筛选 Stage 01 的 `IfcProject` 属性，就在读到第一条无类别的组织映射时抛出异常。由于 `Instance` 使用静态属性初始化，真实 `InvalidDataException` 又被 CLR 包装为 `TypeInitializationException`，界面只能看到“类型初始值设定项引发异常”。

## 4. v0.8.2 修复

### 4.1 目录加载规则

目录加载阶段现在只要求：

- `propertyId`；
- `parameterGuid`；
- `parameterName`；
- 对应官方规则及 canonical 数据类型。

`category` 允许为空，并原样保留。

### 4.2 写入安全门槛

无类别映射只有在兼容策略明确为：

```text
BLOCK_*
```

时才允许存在于目录中。它们可以用于标准完整性、界面和诊断，但不能进入 Revit 参数绑定事务。

新增自动化断言：

```text
所有 category 为空的映射，其 EntityPolicy.IsBlocked 必须为 true
```

### 4.3 消除类型初始化包装

`OfficialHifcMappingCatalog` 和 `OfficialPluginCompatibilityCatalog` 均改为线程安全的惰性加载：

```text
首次调用 Instance
→ 执行 Load()
→ 成功后缓存
```

以后若资源或规则出现异常，将直接返回具体数据错误，而不是只显示 `TypeInitializationException`。

### 4.4 资源诊断

缺少嵌入资源时，错误将同时列出程序集内实际存在的资源名称，便于直接判断逻辑名称或打包问题。

## 5. 自动化门槛

新增并保留：

- 166/166 规则与绑定数量检查；
- propertyId、GUID、参数名唯一性检查；
- binding→rule 完整关联检查；
- categoryless→blocked 策略检查；
- net48 正式目录初始化测试；
- `项目名称` Stage 01 字段解析测试。

该问题在 v0.8.2 构建中应不再出现。下一步实机仍需继续执行“写入并回读”，以进入真正的 Revit 参数安装和官方插件导出验证阶段。
