# Revit 原生插件基础架构构建证据（2026-08-11）

## 1. 身份

| 项目 | 值 |
|---|---|
| 产品支线 | `feat/revit-native-addin-v1` |
| 构建提交 | `9bc79d92016d44d984a1bde88993e4a464887998` |
| 产品版本 | `0.1.0` |
| 目标框架 | `.NET Framework 4.8` |
| 目标宿主 | `Autodesk Revit 2020` |
| 规则包 | `HBR-WUHAN-PLANNING / 1.0.0` |

## 2. GitHub Actions

| 项目 | 值 |
|---|---|
| Workflow | `Build BIMBaoGui Revit Add-in` |
| Run ID | `31475401018` |
| Job ID | `93727819871` |
| 结论 | `success` |
| 原生静态合同 | `8 passed` |
| 共享规则数据库合同 | `171 passed` |
| Release build | `0 warning / 0 error` |
| Artifact ID | `9095039383` |
| Artifact 名称 | `BIMBaoGui-Revit2020-Native-v0.1.0` |
| Artifact 压缩大小 | `66,520 bytes` |
| Artifact SHA-256 | `145f154b3eb0bc1da0c6b9714d901093d3078cfaff39a3eef2ee8f1f50c29481` |

工作流逐步通过：

1. checkout；
2. .NET SDK / Python；
3. 原生插件静态合同；
4. Stage01 共享规则构建依赖 restore；
5. 唯一 HBR 数据库与规则包合同；
6. 原生项目 restore；
7. 原生项目 Release build；
8. artifact 打包和上传。

## 3. 下载后复核

下载 GitHub Actions artifact 后重新计算：

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| `BIMBaoGui-Revit2020-Native-v0.1.0.zip` | 66,520 bytes | `145f154b3eb0bc1da0c6b9714d901093d3078cfaff39a3eef2ee8f1f50c29481` |
| `BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.dll` | 681,984 bytes | `9dfc5632eb10f88bd653cd4039f4180f9288ca7afb80cdd3bf5c0fa1e979319c` |
| `BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.pdb` | 7,492 bytes | `251a28c0b71c68679e1b59ff8ffe8eaf4f5be22e83f9133fd4036eb526db1b8f` |
| `BIMBaoGui.RevitAddin.addin` | 469 bytes | `2f0399f27f2383e7725030367184be346ebb2fd640c4a0a68c91619d4f8ca919` |

程序集静态复核确认包含：

```text
BIMBaoGui.RevitAddin.Resources.HBR_RulePack.hbrpack
HBR-WUHAN-PLANNING
packageVersion = 1.0.0
```

## 4. 当前已证明的内容

本证据证明：

- 独立原生 Revit 项目可在 Windows CI 中 restore 和编译；
- Release build 为 0 warning / 0 error；
- 原生项目不依赖 Grasshopper、RhinoCommon 或 Rhino.Inside.Revit；
- 原生程序集从同一机器权威 JSON 编译并嵌入 HBR rule pack；
- Ribbon、DockablePane、ExternalEvent 和 `.addin` manifest 已进入编译产物；
- artifact 下载前后的 SHA-256 一致。

## 5. 尚未证明的内容

当前仍未完成 Revit 2020 实机启动，因此不能声明：

- Revit 已成功加载 `.addin`；
- Ribbon 按钮和 DockablePane 已在实机显示；
- ExternalEvent 已在 Revit 2020 UI 中实际回调；
- Stage01、Stage02 或 Stage03 业务功能已经完成；
- 当前产物可以生成合规 H-IFC。

下一道验收门是将本 artifact 安装到 Revit 2020，保存 Ribbon、DockablePane、当前文档读取和 Revit journal 证据。
