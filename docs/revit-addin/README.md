# BIMBaoGui Revit 2020 原生插件

## 可直接安装版本

本安装包对应独立 Revit 原生产品线：

```text
feat/revit-native-addin-v1
```

原生插件不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 GHA 程序集，只共同消费权威 HBR 规则数据库。

## 已包含功能

### 01 文件初始化

- 项目身份、子项、模型类型、坐标、高程、真北和项目条件表单；
- 左侧目录 + 右侧连续滚动表单；
- 数据库驱动的字段类型、必填项、示例和校验；
- `X = 南北坐标`、`Y = 东西坐标`；
- Revit 单位、项目位置、项目信息和固定 GUID 参数写入；
- canonical JSON、SHA-256、Extensible Storage 和写入后回读；
- 整体事务回滚与单次 Undo。

### 02 构件与属性准备

- 全模型扫描或读取当前 Revit 选择；
- 数据库类别、ElementKind、精确别名或显式角色匹配；
- 禁止模糊包含、编辑距离或静默猜测；
- 字段状态：正确、待绑定、待写入、待填写、不适用、运行阻断和业务阻断；
- 确定性预览 JSON 与 SHA-256；
- 固定 GUID 的共享参数创建、实例/类型绑定和类别合并；
- 写入前重新生成预览并阻止过期确认；
- 参数级事务隔离、构件级原子事务和部分成功；
- 原生 WPF 构件列表、问题筛选、字段详情和确认写入。

### 03 检测与 H-IFC

Stage03 仍处于独立开发阶段，本安装包暂不宣称具备正式 H-IFC 导出与检查闭环。

## 安装

1. **关闭 Revit 2020**。
2. 将 ZIP 完整解压到普通文件夹，不要直接在压缩包内运行。
3. 双击：

```text
Install.cmd
```

安装器使用当前用户目录，不要求管理员权限。成功后生成：

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin.addin
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\BIMBaoGui.RevitAddin.dll
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin\install-evidence.json
```

然后启动 Revit 2020，打开一个已保存的项目文件，在 Ribbon 中进入：

```text
湖北BIM报规 → 报规工作台
```

## 卸载

关闭 Revit 2020，双击：

```text
Uninstall.cmd
```

## 完整性校验

安装包根目录包含：

```text
SHA256SUMS.txt
```

其中记录安装包内每个文件的 SHA-256。安装脚本也会再次比较源 DLL 与已安装 DLL 的 SHA-256，并写入 `install-evidence.json`。

## 注意事项

- 当前基础版本只允许 Revit 2020；
- RVT 必须先保存且不能为只读或族文档；
- 首次初始化要求确认文件尚未正式建模；
- Stage02 不会伪造没有可靠来源的业务值，只准备参数并标记“待填写”；
- 当前 DLL 未使用商业代码签名证书，Windows 或 Revit 可能显示未知发布者提示；
- 自动化验证覆盖编译、领域测试、安装、哈希核验和卸载 smoke，但不等同于用户电脑上的 Revit 2020 GUI 实机验收。

## 高级命令行方式

安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -SourceRoot .\BIMBaoGui.RevitAddin
```

卸载：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1 -Uninstall
```
