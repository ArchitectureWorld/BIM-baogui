# GH H-IFC 映射开发基线 v1

本目录保存从官方 HIFCTool 逻辑与规则中整理出的 **Grasshopper / Rhino.Inside.Revit 独立开发基线**。HIFCTool 仅作为规则证据来源，后续实现不以该插件作为运行依赖。

## 支线定位

- 上游分支：`feat/stage01-stage02-context-pipeline`
- 当前支线：`feat/hifc-mapping-gh-baseline-v1`
- 当前内容不得自动合并到 `main` 或现有阶段分支。

## 基线内容

物化后目录包括：

- `docs/`：稳定架构、官方数据边界、实施顺序与验收门槛；
- `data/`：166 条武汉规划报建规则、对象映射、对象承载决策和 GH 命令契约；
- `generated/`：确定性共享参数、IFC UserDefinedPsets、参数绑定清单；
- `schemas/`：规则包 JSON Schema。

## 为什么暂存为可校验归档

完整基线以 8 个 `archive/chunk-*.b64` 文件保存，避免大体积 JSON 在 API 同步过程中被截断或重编码。归档内容由 SHA-256 和逐文件清单双重校验；物化脚本只使用 Python 标准库。

```text
归档 SHA-256
341db436faa8410fb19695a727810dba262e3ab73c11b0c54566d502f3c759b9
```

## 展开完整目录

在仓库根目录执行：

```bash
python specs/hifc-mapping/v1/materialize.py
```

重复展开并覆盖既有目录：

```bash
python specs/hifc-mapping/v1/materialize.py --clean
```

脚本会依次执行：

1. 按序拼接 8 个 Base64 分片；
2. 校验归档 SHA-256；
3. 安全解压 tar.gz；
4. 对 12 个源文件逐一校验大小与 SHA-256；
5. 生成 `docs/`、`data/`、`generated/`、`schemas/`；
6. 写出 `manifest.sha256.json`。

## 数据边界

- `exampleValue` 是官方示例值，不是默认写入值；
- 当前官方规划报建文件没有编码 REQUIRED / CONDITIONAL / RECOMMENDED；
- 166 条规则均保留可追踪的 IFC Entity、PropertySet、Property、数据类型和 Revit 承载策略；
- 后续开发必须以 canonical HIFC data graph 为唯一真源，再投影到 Revit 和 IFC。
