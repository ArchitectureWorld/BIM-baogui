# 仓库深度 Review 与治理记录（2026-08-11）

本文记录本轮仓库、代码、规则、测试、Git 分支和本地遗留证据的审查结果。它不替代映射规则源、冻结 Tag 或实机验收记录。

## 1. 审查结论

- 当前产品提交为 `b3cf2b821ffcca7bfbe787d5e1c328ce9c3b8d15`。
- 唯一可编辑映射源仍是 `specs/hbr-rules/v1/source/hbr_rule_source.v1.json`，SHA-256 为 `78a95f16548d6ec43b9a6a4a019f3eafdad2be2467abd2c3e41b6518aec9f3a4`。
- 冻结映射 Tag 仍是 `hbr-planning-mapping-v1.0.0`，指向 `0c5d2c1100c9c80c4306354bab553debe8f191ca`；从该 Tag 到产品提交，`specs/hbr-rules/v1` 无差异。
- 生产项目只嵌入 `BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack`。旧映射、bindings 和兼容性数据在项目文件中均为 `None`，不是第二套运行时规则库。
- 当前部署 GHA 的 SHA-256 为 `dcff8939bc70ce335a4603e83de46ba4577571841985055cc57b98c68451217e`，与归档的最新代码二进制一致；`b3cf2b8` 相比该二进制只新增文档设计，不含生产代码变化。
- 自动化、构建和依赖漏洞门禁通过，但这不等于 Revit 2020、IFCFlux 或官方检查器实机闭环通过。

## 2. 本轮已矫正的老旧数据

### 2.1 当前状态页

`docs/hbr-three-stage-progress.md` 原先仍停留在 `b6eef87 / 2026-08-11 11:01`，并把 Stage02 的旧实现与新版目标混写。本轮已更新到 `b3cf2b8`，并明确区分：

- Stage02 v0.9.0：现有选择、预览、确认写入和 runtime 状态展示已开发，但指定 RVT 实机闭环未完成；
- Stage02 v1.1：全模型构件属性准备设计已确认、待实现，不是已交付功能。

历史归档文件保持原快照，不覆盖修改，避免把历史证据伪装成当前状态。

### 2.2 fresh checkout 开发合同

- `.gitattributes` 增加对自身的 LF 约束，避免 Windows fresh checkout 把该文件检出为 CRLF 后触发发布合同失败；
- README 在 pytest 前补充生产项目和测试项目的 `dotnet restore`，使本地步骤与 CI 的真实前置条件一致；
- 发布合同增加相应回归测试，锁定 restore 顺序和 Stage02 v0.9.0 / v1.1 状态边界。

### 2.3 本机逆向证据

旧 worktree 中 6 个未跟踪逆向文件含用户目录、Program Files、安装包和 IFCFlux 本机路径，不适合直接提交。它们已可恢复迁移到：

`D:\18_建模项目\湖北BIM云平台\repository-local-evidence-archive\BIM-baogui-20260811`

迁移前后文件数量、大小和 SHA-256 一致：

| 相对路径 | 字节 | SHA-256 |
|---|---:|---|
| `docs/reverse-engineering/data/evidence-manifest.json` | 4,101 | `9d8a1e035dd6d0516a969f252c34d13292ae2b860dd298569b2c513ae0a768a8` |
| `docs/reverse-engineering/data/official-hifc-all-scenarios-properties.csv` | 1,582,794 | `292076b9c52c27aa8cf26ab621b5200cd80ec2a1ac285a903b87e84a17f9c3ea` |
| `docs/reverse-engineering/data/official-object-mappings.csv` | 1,578 | `85458a8066beffae05bcc05eb68230dfcf2761e61aad2b0ee3661a9fe7017d4a` |
| `docs/reverse-engineering/data/official-planning-runtime-mapping.csv` | 47,233 | `0b5397e6a6c709ce40d6a2cb3503c47f93e2db158476505c2b7fc9fca280216d` |
| `docs/reverse-engineering/Revit-IFC-HIFC映射逆向报告.md` | 24,344 | `99ae9e4925114bc8f79c30f7dd7e6b77aa5a920cfeb186fdf73e67303235b05f` |
| `tools/reverse-engineering/export-hifc-mapping-evidence.ps1` | 8,368 | `637523209bf976ca12b0b0dadf2c487e878efd5ea26cf5f25739a0443f2d04b7` |

这些文件仍可用于本机证据追溯；若未来需要入库，应先把绝对路径改成来源类型、相对标识或脱敏占位符，再单独审查。

## 3. 分支与 PR 清理记录

清理前，默认分支 `main` 停在 `6c053d1`，比产品分支少 366 个提交，并且没有分支保护或 ruleset。所有待删除远端分支均再次验证为产品分支祖先，独有提交为 0。

已关闭过时 Draft PR：

- PR #1 `feat/stage01-gha-file-initialization -> main`；
- PR #2 `feat/stage01-stage02-context-pipeline -> feat/stage01-gha-file-initialization`；
- PR #3 `feat/gh-official-hifc-write-integration-v1 -> feat/hifc-mapping-gh-baseline-v1`。

已删除的远端祖先分支：

| 分支 | 删除前提交 | 独有提交 |
|---|---|---:|
| `feat/gh-official-hifc-write-integration-v1` | `0bf29af` | 0 |
| `feat/hbr-planning-mapping-v1.0.0` | `0c5d2c1` | 0 |
| `feat/hifc-mapping-gh-baseline-v1` | `3d9e350` | 0 |
| `feat/stage01-gha-file-initialization` | `7074a30` | 0 |
| `feat/stage01-stage02-context-pipeline` | `3f030f9` | 0 |
| `fix/official-hifc-hardening-v090` | `feed834` | 0 |

保留项：

- `feat/gh-plugin-hbr-planning-v1.0.0`：当前产品分支；
- `hbr-planning-mapping-v1.0.0`：正式冻结映射 Tag；
- `archive/official-hifc-hardening-wip-20260811`：远端归档 Tag，剥离后指向 `c61f3c41952a4dff0bf637ef41fb24ea6e0a10c5`，保存清理前 14 个未提交修改。该 Tag 是未验证 WIP，不是发布基线。

本治理变更合入后，`main` 应成为唯一默认集成线；后续 Stage02 v1.1 从更新后的 `main` 新建短生命周期功能分支。

## 4. 深度 Review 发现

### Important：仍未满足的产品验收边界

1. 359 条 requirement 当前仍全部为 `UNCLASSIFIED`。因此 runtime 为 57 条 `NOT_IMPLEMENTED`、302 条 `UNCLASSIFIED_REQUIREMENT`，不能表述为 359 条均已生产支持。
2. 两类未实现 Owner 策略分别覆盖 32 条 `CANONICAL_SPATIAL_ZONE_RECORD` 和 25 条 `USER_SELECTED_EXPORTABLE_GENERIC_MODEL`。
3. 最新留证的 Stage03 实机 run 在 `TRANSLATE-IFC` 以 `INVALID_IFC` 结束且没有 fields JSON。结构 fixture、自动化或文件存在均不能替代真实 IFCFlux/检查器闭环。
4. Stage02 v1.1 只有已确认设计，没有实施代码、构建产物或实机结果。

### Important：仓库治理风险

1. `main` 长期落后 366 个提交，正式产品代码只存在于功能分支；本治理变更必须先合入 `main`，再继续新功能开发。
2. `main` 没有分支保护或 repository ruleset。治理合并后应至少要求 PR、CI build 通过和禁止 force push。
3. hardening WIP 与当前产品线方向不同，不能直接合并。归档 Tag 只用于未来逐项重放、测试和取舍。

### Minor：可维护性债务

1. `HbrIfcEnricher.cs` 和 `Stage03WorkflowCoordinator.cs` 均接近 2,000 行，Stage02/Stage03 多个核心文件超过 1,000 行。现有测试量大，但后续 v1.1 若继续直接扩展这些文件，会提高回归和审查成本。
2. `BlankFileGate`、UI 关闭和临时文件清理存在少量 best-effort 空 `catch`。当前语义多为兼容性探测或清理，不构成已复现故障；后续应优先增加受控诊断，而不是继续扩大静默异常范围。
3. 历史 Review、计划、验收清单仍保留若干本机绝对路径。这些是可追溯历史证据，不应机械重写；新增当前文档不再复制真实用户目录，测试数据使用显式假路径。

## 5. fresh 验证结果

在隔离 worktree、执行两个项目 restore 后完成：

| 验证 | 结果 |
|---|---:|
| 定向状态/EOL/restore 合同 | 4 passed |
| Python 全量 | 570 passed |
| .NET Core 全量 | 1286 passed / 0 failed / 0 skipped |
| Release warnings-as-errors | 0 warning / 0 error |
| NuGet 漏洞扫描 | 1 project / 0 vulnerable package |
| `git diff --check` | clean |

## 6. 后续开发与测试顺序

1. 先把本治理变更合入 `main`，开启最小分支保护；不要在已归档旧分支继续开发。
2. 从 `main` 新建 Stage02 v1.1 功能分支，按已确认设计拆为规则投影、全模型扫描、范围输入、预览、参数定义准备、按构件原子写入、部分成功汇总和 GH UI 八个可独立验收工作包。
3. 每个工作包执行单元测试与合同测试；模型扫描和写入增加 Revit adapter seam 测试，锁定错文档、旧预览、元素删除、类型变化、部分失败和重复上升沿。
4. 完整自动化继续要求 Python、.NET、严格 Release、规则包可重建、单一嵌入资源、NuGet 漏洞和 LF/diff 门禁。
5. 自动化通过后再构建、部署、哈希核验，并用固定 RVT 完成 Stage01 -> Stage02 v1.1 -> Stage03。最终必须保存 RVT、RAW IFC、HIFC-MVD IFC、fields JSON、失败报告、IFCFlux/检查器结果和全部 SHA-256。

在第 5 步闭环前，只能声明“映射基线稳定、代码合同通过”，不能声明“报规全链路已完成正式验收”。
