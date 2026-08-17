# BIMBaoGui Revit 2020 Native + MCP v0.4.3 实机验收清单

> 这是验收模板，不记录任何预填的通过结论。Task 14 执行真实 Revit、官方 HIFCTool 和 IFCFlux 验收后，才可填写现场证据。

## 固定状态字段

```text
AUTOMATED_VERIFIED:
REVIT2020_HOST_VERIFIED:
OFFICIAL_HIFC_EXPORT_VERIFIED:
IFCFLUX_CHECKER_VERIFIED:
```

每个状态都应填写实际状态、执行人、日期和证据路径；未执行时保持为空或明确写 `PENDING`，不得用本模板推断通过。

## 场景 A：空模型

```text
插件 build/commit:
RVT 路径/SHA-256:
规则包 SHA-256:
01 结果:
02A 结果:
02B 结果:
03 严格扫描/普通导出/强制测试导出结果:
保存重开结果:
官方 HIFCTool 结果:
IFC SHA-256:
IFCFlux 版本/报告 SHA-256:
AUTOMATED_VERIFIED:
REVIT2020_HOST_VERIFIED:
OFFICIAL_HIFC_EXPORT_VERIFIED:
IFCFLUX_CHECKER_VERIFIED:
```

## 场景 B：不完整模型

```text
插件 build/commit:
RVT 路径/SHA-256:
规则包 SHA-256:
01 结果:
02A 结果:
02B 结果:
03 严格扫描/普通导出/强制测试导出结果:
保存重开结果:
官方 HIFCTool 结果:
IFC SHA-256:
IFCFlux 版本/报告 SHA-256:
AUTOMATED_VERIFIED:
REVIT2020_HOST_VERIFIED:
OFFICIAL_HIFC_EXPORT_VERIFIED:
IFCFLUX_CHECKER_VERIFIED:
```

## 场景 C：Golden RVT

外部验收链固定为 `Golden RVT -> official HIFCTool -> IFCFlux exact identity`。

```text
插件 build/commit:
Golden RVT 路径/SHA-256:
规则包 SHA-256:
01 结果:
02A 结果:
02B 结果:
03 严格扫描/普通导出/强制测试导出结果:
保存重开结果:
官方 HIFCTool 结果:
IFC SHA-256:
IFCFlux 版本/报告 SHA-256:
AUTOMATED_VERIFIED:
REVIT2020_HOST_VERIFIED:
OFFICIAL_HIFC_EXPORT_VERIFIED:
IFCFLUX_CHECKER_VERIFIED:
```
