# Revit 原生插件基础架构构建证据（2026-08-11）

## 1. 产品与分支身份

| 项目 | 值 |
|---|---|
| 仓库 | `ArchitectureWorld/BIM-baogui` |
| 产品支线 | `feat/revit-native-addin-v1` |
| 最终代码提交 | `bcbe0fa8c0548f658fa940da262b660cc9706b1a` |
| 产品版本 | `0.1.0` |
| 目标框架 | `.NET Framework 4.8` |
| 目标宿主 | `Autodesk Revit 2020` |
| 规则包 | `HBR-WUHAN-PLANNING / 1.0.0` |
| 唯一可编辑数据库 | `specs/hbr-rules/v1/source/hbr_rule_source.v1.json` |

本支线的 Revit 原生插件与 GHA 是相对独立的产品实现。原生项目不引用 Grasshopper、RhinoCommon、Rhino.Inside.Revit 或 GHA 程序集，只共同消费同一个 HBR 参考数据库。

## 2. 原生插件 Windows CI

| 项目 | 值 |
|---|---|
| Workflow | `Build BIMBaoGui Revit Add-in` |
| Run ID | `31476806920` |
| Job ID | `93732277858` |
| Head SHA | `bcbe0fa8c0548f658fa940da262b660cc9706b1a` |
| 结论 | `success` |
| 原生宿主静态合同 | `8 passed` |
| 安装器静态合同 | `7 passed` |
| 共享规则数据库合同 | `171 passed` |
| 原生项目 restore | `success` |
| Release build | `0 warning / 0 error` |
| 安装/卸载 smoke test | `success` |
| Artifact ID | `9095584696` |
| Artifact 名称 | `BIMBaoGui-Revit2020-Native-v0.1.0` |
| Artifact 压缩大小 | `70,118 bytes` |
| Artifact SHA-256 | `f51f374e4fa6afd53707165e1684110ec4ff3c18c64f58ed56f0d7c3e6bae7d2` |

工作流逐步通过：

1. checkout；
2. .NET SDK / Python；
3. 原生 Ribbon、DockablePane、ExternalEvent、规则包加载静态合同；
4. 用户级安装器与卸载器静态合同；
5. Stage01 共享规则构建依赖 restore；
6. 唯一 HBR 数据库与规则包合同；
7. 原生项目 restore；
8. 原生项目 Release build；
9. 隔离 `%APPDATA%` 下的一键安装、绝对 manifest 路径、安装证据哈希与卸载清理 smoke test；
10. artifact 打包和上传。

## 3. GHA 产品线回归

原生支线没有修改现有 GHA 生产源码，但仓库级合同会同时加载新增测试，因此对同一个最终提交重新执行了完整 GHA 流水线。

| 项目 | 值 |
|---|---|
| Workflow | `Build BIMBaoGui GHA` |
| Run ID | `31476806916` |
| Job ID | `93732278249` |
| Head SHA | `bcbe0fa8c0548f658fa940da262b660cc9706b1a` |
| 结论 | `success` |

以下步骤全部成功：

- committed diff whitespace；
- .NET restore；
- HBR rule-pack compiler / packaging contracts；
- HBR mapping baseline rebuild；
- Python 仓库全量合同；
- NuGet vulnerability scan；
- .NET Core 全量测试；
- GHA Release build；
- GHA 程序集验证；
- GHA artifact manifest 与 artifact 上传。

因此当前原生插件基础架构没有破坏既有 GHA 产品线。

## 4. 最终下载包复核

GitHub Actions 最终 artifact 下载后重新计算：

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| `BIMBaoGui-Revit2020-Native-v0.1.0.zip` | 70,118 bytes | `f51f374e4fa6afd53707165e1684110ec4ff3c18c64f58ed56f0d7c3e6bae7d2` |
| `BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.dll` | 681,984 bytes | `9529f05894cab47c09db35bf6c9633f85a8f3fdf6524f92d658f4bc485f908d2` |
| `BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.pdb` | 7,492 bytes | `bb1569b20dd8c64765ac897c0c8a1fc79743086daf30358a21f9234785f47b32` |
| `BIMBaoGui.RevitAddin.addin` | 469 bytes | `2f0399f27f2383e7725030367184be346ebb2fd640c4a0a68c91619d4f8ca919` |
| `Install-Revit2020.ps1` | 6,107 bytes | `89c5c5b74e22d9ae5b6547558bfc1f7bc0ec0c37cc6e8e5e0a17682414cc4151` |
| `README.md` | 2,294 bytes | `2fdb6003766650badf1c94baea3033de7f65b5bed746fb9e66ed35aee7797b96` |

程序集静态复核确认包含：

```text
BIMBaoGui.RevitAddin.Resources.HBR_RulePack.hbrpack
HBR-WUHAN-PLANNING
packageVersion = 1.0.0
```

## 5. 安装器 smoke test 证据

CI 在隔离的临时 `%APPDATA%` 中执行：

```powershell
Install-Revit2020.ps1 -SourceRoot <Release目录> -Force
Install-Revit2020.ps1 -Uninstall -Force
```

已验证：

- 用户级 `Autodesk\Revit\Addins\2020` 目录自动创建；
- `.addin` manifest 自动生成；
- manifest 中 Assembly 为绝对路径；
- DLL 源文件、暂存文件和正式安装文件 SHA-256 一致；
- `install-evidence.json` 正确生成；
- 安装 DLL SHA-256 为 `9529f05894cab47c09db35bf6c9633f85a8f3fdf6524f92d658f4bc485f908d2`；
- 卸载后 manifest 和产品目录均被删除。

## 6. 当前已经证明的内容

本证据证明：

- 独立 Revit 原生产品支线已经建立；
- 原生项目可在 Windows CI 中 restore 和编译；
- Release build 为 0 warning / 0 error；
- 原生项目不依赖 GHA、Grasshopper、RhinoCommon 或 Rhino.Inside.Revit；
- 原生程序集从同一机器权威 JSON 编译并嵌入 HBR rule pack；
- Ribbon、DockablePane、ExternalEvent、当前文档快照与 `.addin` manifest 已进入编译产物；
- 用户级安装、绝对路径 manifest、文件哈希核验与卸载流程已通过自动 smoke test；
- 最终 artifact 下载前后的 SHA-256 一致；
- 同一最终提交上的既有 GHA 完整流水线通过。

## 7. 尚未证明的内容

当前仍未完成用户机器上的 Revit 2020 实机启动，因此不能声明：

- Revit 2020 已实际加载该 `.addin`；
- Ribbon 按钮和 DockablePane 已在用户机器显示；
- ExternalEvent 已在用户 Revit 2020 UI 中完成真实回调；
- Stage01 文件初始化业务功能已经完成；
- Stage02 全模型准备与部分成功写入已经完成；
- Stage03 检测、IFC4 RAW、H-IFC 和 fields JSON 已经完成；
- 当前产物能够生成或通过检查软件验证合规 H-IFC。

下一道验收门是安装最终 artifact 到 Revit 2020，保存 Ribbon、DockablePane、当前文档读取、安装证据和 Revit journal；随后进入原生 Stage01 的模型、Storage、事务写入与回读开发。
