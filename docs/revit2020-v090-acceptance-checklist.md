# BIMBaoGui v0.9.0 Revit 2020 验收清单

## 构建与部署

- [ ] Python 合约测试全部通过。
- [ ] .NET Release 测试全部通过，Release 构建 0 警告、0 错误。
- [ ] vulnerable package scan 未发现已知漏洞。
- [ ] `artifact-manifest.json` 记录 commit SHA、GHA SHA-256、文件大小和程序集版本 `0.9.0.0`。
- [ ] 旧 GHA 已移到 `%APPDATA%\Grasshopper\Libraries\BIMbaogui` 之外备份。
- [ ] 活动目录只存在 `BIMBaoGui.Stage01.gha` 一个 BIMBaoGui GHA。

## 宿主启动

- [ ] 使用 Revit 2020 打开安全测试 RVT，确认无未保存的无关修改。
- [ ] 新 journal 中 Rhino.Inside.Revit、官方 H-IFC 和 BIMFlux 均为 `API_SUCCESS`。
- [ ] Grasshopper 中可见 Stage 01、Stage 02、Stage 03 三个组件。

## Stage 01

- [ ] 首次初始化要求空白项目确认和实质模型门禁。
- [ ] 旧版有效初始化可迁移，不要求虚假的空白项目确认。
- [ ] 损坏或不完整的 DataStorage 记录确定性阻断，不自动迁移或覆盖。
- [ ] 写入、回读、PayloadHash、FileGuid 和 WorkflowVersion 均一致。
- [ ] 非空 `IfcOrganization` 数据会产生官方协议阻断；空组织记录保持兼容。

## Stage 02

- [ ] 连接的 `HBR_FileContext` schema 为 `0.9.0`。
- [ ] 当前 RVT 的 FileGuid、PayloadHash、WorkflowVersion 与上下文一致时才能编译任务。
- [ ] 陈旧上下文、错误 RVT 或缺少有效 Stage 01 存储时被阻断。
- [ ] 只读项目仍允许编译任务，因为 Stage 02 不写入 Revit。

## Stage 03

- [ ] 打开 GH 时 Toggle 已为 `true`，首次求解不写入。
- [ ] 将 Toggle 调为 `false`，再调为 `true`，只执行一次写入。
- [ ] 连续保持 `true` 不重复写入；再次 `true -> false -> true` 才允许下一次执行。

## 官方 H-IFC 与 IFC

- [ ] 使用官方 H-IFC 导出新文件 `20260731test02-v090-validation.ifc`，不覆盖原 IFC。
- [ ] 核对所有非空且协议兼容的 Stage 01 属性、值、单位和重复源参数对。
- [ ] 记录缺失、额外或不一致属性，以及 exporter log、Revit journal、时间戳和 IFC SHA-256。
- [ ] 只有 Golden RVT -> 官方 H-IFC -> 新 IFC -> 检查结果全部通过后，才声明 v0.9.0 可直接使用。
