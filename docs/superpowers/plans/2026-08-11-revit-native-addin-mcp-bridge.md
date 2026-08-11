# Revit 原生插件 MCP Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保持 Revit 原生插件 v0.2.0 Stage01/Stage02 功能与人工操作路径不变的前提下，增加一个可直接安装的标准 MCP stdio Server，并通过当前用户专属 Named Pipe 调用现有 Revit ExternalEvent 业务服务。

**Architecture:** Revit 2020 内部新增一个只负责 IPC、认证、lease 和命令路由的 `McpBridge`；外部 `BIMBaoGui.McpServer.exe` 使用官方 C# MCP SDK 1.3.0 和 stdio transport。两端通过 `BIMBaoGui.McpContracts` 的长度前缀 JSON 合同通信，所有 Revit API 操作继续进入现有 `RevitExternalEventDispatcher`。

**Tech Stack:** Revit 2020 API、.NET Framework 4.8、WPF、ExternalEvent、Windows Named Pipe、PipeSecurity、.NET Standard 2.0、.NET 8、ModelContextProtocol 1.3.0、Microsoft.Extensions.Hosting 8.0.x、xUnit、pytest、GitHub Actions、PowerShell。

## Global Constraints

- 基线提交固定为 `35fa0ca6a8b07ba86231ee8305020fb23dcdb7c2`。
- MCP 开发分支固定为 `feat/revit-native-addin-mcp-v0.3`。
- Revit 插件目标固定为 `net48` 和 Autodesk Revit 2020。
- MCP Server 目标固定为 `net8.0`、`win-x64`、self-contained、single-file、`PublishTrimmed=false`。
- MCP SDK 固定为 `ModelContextProtocol 1.3.0`。
- MCP transport 固定为 stdio；日志只能写 stderr。
- Revit 与 MCP Server 的 IPC 固定为 current-user ACL Named Pipe。
- Stage01、Stage02 业务实现不得复制到 MCP Server。
- `src/BIMBaoGui.RevitAddin/Stage01/**` 和 `Stage02/**` 不得因 MCP 改造改变业务行为。
- 所有 Revit API 读取和修改必须经过现有 ExternalEvent queue。
- 所有写工具必须要求 `confirm=true` 和未过期 lease hash。
- 禁止任意 C#、任意 Revit API、任意脚本或 UI 点击工具。
- 原生产品仍不得引用 Grasshopper、RhinoCommon、Rhino.Inside 或 GHA。

---

## File Structure

```text
src/
├─ BIMBaoGui.McpContracts/
│  ├─ BIMBaoGui.McpContracts.csproj
│  ├─ BridgeProtocol.cs
│  ├─ BridgeMessages.cs
│  ├─ BridgeFrameCodec.cs
│  ├─ BridgeSessionModels.cs
│  └─ ToolContracts.cs
│
├─ BIMBaoGui.McpServer/
│  ├─ BIMBaoGui.McpServer.csproj
│  ├─ Program.cs
│  ├─ BridgeSessionLocator.cs
│  ├─ NamedPipeBridgeClient.cs
│  ├─ BimBaoGuiTools.cs
│  ├─ McpToolErrors.cs
│  └─ ProbeCommand.cs
│
└─ BIMBaoGui.RevitAddin/
   ├─ McpBridge/
   │  ├─ McpBridgeHost.cs
   │  ├─ McpBridgeDiscoveryWriter.cs
   │  ├─ McpNamedPipeServer.cs
   │  ├─ McpBridgeCommandRouter.cs
   │  ├─ McpRevitCommandGateway.cs
   │  ├─ McpLeaseStore.cs
   │  ├─ McpStage01Adapter.cs
   │  └─ McpStage02Adapter.cs
   ├─ App.cs
   └─ BIMBaoGui.RevitAddin.csproj

tests/
├─ BIMBaoGui.McpContracts.Tests/
├─ BIMBaoGui.McpServer.Tests/
├─ BIMBaoGui.RevitAddin.Tests/
├─ test_revit_addin_mcp_non_regression.py
├─ test_revit_addin_mcp_contract.py
└─ test_revit_addin_mcp_installer_contract.py

specs/revit-addin/
└─ v0.2.0-functional-baseline.sha256.json

installer/
├─ Install-Revit2020.ps1
├─ Install.cmd
├─ Uninstall.cmd
├─ McpProbe.cmd
└─ mcp-server-config.example.json
```

---

### Task 1: Freeze the v0.2.0 functional baseline

**Files:**
- Create: `specs/revit-addin/v0.2.0-functional-baseline.sha256.json`
- Create: `tests/test_revit_addin_mcp_non_regression.py`
- Modify: `.github/workflows/build-revit-addin.yml`

**Interfaces:**
- Consumes: files under `src/BIMBaoGui.RevitAddin/Stage01/**` and `src/BIMBaoGui.RevitAddin/Stage02/**` from commit `35fa0ca6...`.
- Produces: a machine-readable SHA-256 baseline and CI command that fails on business-file drift.

- [ ] **Step 1: Write the failing non-regression test**

```python
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "specs/revit-addin/v0.2.0-functional-baseline.sha256.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_stage01_stage02_business_files_match_v020_baseline():
    manifest = json.loads(BASELINE.read_text(encoding="utf-8"))
    actual = {
        item["path"]: sha256(ROOT / item["path"])
        for item in manifest["files"]
    }
    expected = {item["path"]: item["sha256"] for item in manifest["files"]}
    assert actual == expected
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
python -m pytest tests/test_revit_addin_mcp_non_regression.py -q
```

Expected: FAIL because the baseline manifest does not exist.

- [ ] **Step 3: Generate the exact baseline manifest**

The JSON must contain every production `.cs` file under Stage01 and Stage02, sorted by ordinal path:

```json
{
  "schema_version": "BIMBAOGUI_REVIT_FUNCTIONAL_BASELINE_V1",
  "source_commit": "35fa0ca6a8b07ba86231ee8305020fb23dcdb7c2",
  "files": [
    {
      "path": "src/BIMBaoGui.RevitAddin/Stage01/NativeStage01Validator.cs",
      "sha256": "<actual lowercase SHA-256>"
    }
  ]
}
```

- [ ] **Step 4: Add the test to Windows CI**

Add a step before any build:

```yaml
- name: Verify v0.2.0 Stage01 and Stage02 non-regression baseline
  shell: pwsh
  run: python -m pytest tests/test_revit_addin_mcp_non_regression.py -q
```

- [ ] **Step 5: Run the test and commit**

Expected: PASS with one test and zero failures.

```bash
git add specs/revit-addin/v0.2.0-functional-baseline.sha256.json \
  tests/test_revit_addin_mcp_non_regression.py \
  .github/workflows/build-revit-addin.yml
git commit -m "test: freeze native Stage01 Stage02 v0.2 baseline"
```

---

### Task 2: Create the shared MCP contracts and frame codec

**Files:**
- Create: `src/BIMBaoGui.McpContracts/BIMBaoGui.McpContracts.csproj`
- Create: `src/BIMBaoGui.McpContracts/BridgeProtocol.cs`
- Create: `src/BIMBaoGui.McpContracts/BridgeMessages.cs`
- Create: `src/BIMBaoGui.McpContracts/BridgeFrameCodec.cs`
- Create: `src/BIMBaoGui.McpContracts/BridgeSessionModels.cs`
- Create: `src/BIMBaoGui.McpContracts/ToolContracts.cs`
- Create: `tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj`
- Create: `tests/BIMBaoGui.McpContracts.Tests/BridgeFrameCodecTests.cs`

**Interfaces:**
- Produces: `BridgeProtocol.Version`, `BridgeRequest`, `BridgeResponse`, `BridgeSessionDescriptor`, `BridgeFrameCodec.ReadAsync/WriteAsync`.
- Consumed by: Revit Bridge and external MCP Server.

- [ ] **Step 1: Write frame-codec tests**

```csharp
[Fact]
public async Task RoundTripUsesFourByteLittleEndianLengthAndUtf8Json()
{
    var source = new BridgeRequest
    {
        ProtocolVersion = BridgeProtocol.Version,
        RequestId = Guid.NewGuid().ToString("D"),
        SessionToken = "token",
        Method = "ping",
        TimeoutMs = 15000,
        PayloadJson = "{\"value\":\"中文\"}"
    };

    using var stream = new MemoryStream();
    await BridgeFrameCodec.WriteAsync(stream, source, CancellationToken.None);
    stream.Position = 0;
    BridgeRequest result = await BridgeFrameCodec.ReadRequestAsync(
        stream,
        CancellationToken.None);

    Assert.Equal(source.RequestId, result.RequestId);
    Assert.Equal(source.PayloadJson, result.PayloadJson);
}

[Fact]
public async Task OversizedRequestIsRejectedBeforeAllocation()
{
    using var stream = new MemoryStream();
    stream.Write(BitConverter.GetBytes(BridgeProtocol.MaxRequestBytes + 1));
    stream.Position = 0;
    await Assert.ThrowsAsync<BridgeProtocolException>(() =>
        BridgeFrameCodec.ReadRequestAsync(stream, CancellationToken.None));
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release
```

Expected: FAIL because contract types do not exist.

- [ ] **Step 3: Implement the contract project**

`BridgeProtocol.cs` must define exact values:

```csharp
public static class BridgeProtocol
{
    public const string Version = "1.0";
    public const int MaxRequestBytes = 8 * 1024 * 1024;
    public const int MaxResponseBytes = 32 * 1024 * 1024;
}
```

`BridgeFrameCodec` must:

- serialize compact UTF-8 JSON;
- write 4-byte little-endian length before JSON;
- read exactly the declared number of bytes;
- reject negative, zero or oversized lengths;
- throw `EndOfStreamException` on truncated frames;
- never close the caller-owned stream.

- [ ] **Step 4: Run tests and commit**

Expected: all contracts tests PASS.

```bash
git add src/BIMBaoGui.McpContracts tests/BIMBaoGui.McpContracts.Tests
git commit -m "feat: add shared MCP bridge contracts"
```

---

### Task 3: Implement session discovery and lease storage

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeDiscoveryWriter.cs`
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpLeaseStore.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/McpLeaseStoreTests.cs`
- Create: `tests/BIMBaoGui.McpServer.Tests/BridgeSessionLocatorTests.cs`
- Create: `src/BIMBaoGui.McpServer/BridgeSessionLocator.cs`

**Interfaces:**
- Produces: `McpLeaseStore<T>.Create/Get/Consume`, `BridgeSessionLocator.ListAsync/ResolveAsync`, discovery file format.
- Consumed by: Stage01/Stage02 adapters and MCP tools.

- [ ] **Step 1: Write lease tests**

```csharp
[Fact]
public void ConsumeIsSingleUseAndExpiredLeasesFailClosed()
{
    var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
    var store = new McpLeaseStore<string>(clock, TimeSpan.FromMinutes(30));
    string hash = store.Create("payload-hash", "value");

    Assert.Equal("value", store.Consume(hash));
    Assert.Throws<McpLeaseException>(() => store.Consume(hash));

    string expired = store.Create("expired", "value");
    clock.Advance(TimeSpan.FromMinutes(31));
    Assert.Throws<McpLeaseException>(() => store.Get(expired));
}
```

- [ ] **Step 2: Run tests and verify RED**

Expected: missing `McpLeaseStore` and `BridgeSessionLocator`.

- [ ] **Step 3: Implement deterministic lease behavior**

`McpLeaseStore<T>` must:

- use an ordinal hash key supplied by caller;
- reject empty keys;
- store a defensive snapshot supplied by adapter;
- expire after 30 minutes;
- return `LEASE_NOT_FOUND` or `LEASE_EXPIRED` distinctly;
- consume atomically using a lock;
- purge expired entries on create/get/consume.

`McpBridgeDiscoveryWriter` must write atomically to:

```text
%LOCALAPPDATA%\BIMBaoGui\Revit2020\bridges\<pid>.json
```

by writing `<pid>.json.tmp` and then replacing/moving it.

- [ ] **Step 4: Implement external session resolution**

`BridgeSessionLocator.ResolveAsync(int? processId, CancellationToken)` must:

- parse only valid discovery JSON;
- ignore records with wrong protocol or Revit version;
- attempt a short pipe `ping` before returning a session;
- delete stale records only after connection failure and process nonexistence;
- return `MULTIPLE_REVIT_SESSIONS` when more than one live session exists and no processId is supplied.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeDiscoveryWriter.cs \
  src/BIMBaoGui.RevitAddin/McpBridge/McpLeaseStore.cs \
  src/BIMBaoGui.McpServer/BridgeSessionLocator.cs \
  tests/BIMBaoGui.RevitAddin.Tests/McpLeaseStoreTests.cs \
  tests/BIMBaoGui.McpServer.Tests/BridgeSessionLocatorTests.cs
git commit -m "feat: add MCP session discovery and leases"
```

---

### Task 4: Add the current-user Named Pipe server to Revit

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeHost.cs`
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpNamedPipeServer.cs`
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs`
- Modify: `src/BIMBaoGui.RevitAddin/App.cs`
- Modify: `src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj`
- Create: `tests/test_revit_addin_mcp_contract.py`

**Interfaces:**
- Consumes: `BridgeFrameCodec`, `BridgeRequest`, `BridgeResponse`.
- Produces: a live per-process pipe and discovery record; `McpBridgeCommandRouter.RouteAsync`.

- [ ] **Step 1: Write static bridge contract tests**

```python
def test_revit_bridge_uses_current_user_acl_and_external_event():
    pipe = read("src/BIMBaoGui.RevitAddin/McpBridge/McpNamedPipeServer.cs")
    app = read("src/BIMBaoGui.RevitAddin/App.cs")
    assert "PipeSecurity" in pipe
    assert "WindowsIdentity.GetCurrent().User" in pipe
    assert "PipeAccessRule" in pipe
    assert "PipeAccessRights.FullControl" in pipe
    assert "McpBridgeHost.Start" in app
    assert "McpBridgeHost.Stop" in app
    assert "RevitExternalEventDispatcher" in read(
        "src/BIMBaoGui.RevitAddin/McpBridge/McpRevitCommandGateway.cs"
    )
```

- [ ] **Step 2: Run static tests and verify RED**

Expected: missing bridge files and startup calls.

- [ ] **Step 3: Implement the pipe listener**

`McpNamedPipeServer` must:

- create `NamedPipeServerStream` with current-user-only `PipeSecurity`;
- use `PipeTransmissionMode.Byte`;
- use `PipeOptions.Asynchronous`;
- accept one request frame per connection and write one response frame;
- validate protocol version, request ID, token and timeout range;
- map unexpected exceptions to `TECHNICAL_FATAL` without returning stack traces;
- continue accepting after one malformed connection;
- stop through a cancellation token and dispose the current listener.

- [ ] **Step 4: Integrate lifecycle without breaking existing startup**

`App.OnStartup` must keep existing Ribbon and DockablePane code. After registration:

```csharp
RevitExternalEventDispatcher.EnsureInitialized();
try
{
    McpBridgeHost.Start();
}
catch (Exception exception)
{
    McpBridgeHost.RecordStartupFailure(exception);
}
return Result.Succeeded;
```

`App.OnShutdown` must call:

```csharp
McpBridgeHost.Stop();
RevitExternalEventDispatcher.Dispose();
```

MCP startup failure must not change `Result.Succeeded` for the existing plugin.

- [ ] **Step 5: Add project reference and run existing tests**

```xml
<ProjectReference Include="..\BIMBaoGui.McpContracts\BIMBaoGui.McpContracts.csproj" />
```

Run all existing native tests and the new static contract. Expected: zero regression failures.

- [ ] **Step 6: Commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge \
  src/BIMBaoGui.RevitAddin/App.cs \
  src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj \
  tests/test_revit_addin_mcp_contract.py
git commit -m "feat: host authenticated MCP pipe bridge in Revit"
```

---

### Task 5: Build a Task-based gateway over the existing ExternalEvent dispatcher

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpRevitCommandGateway.cs`
- Modify: `src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/McpRevitCommandGatewayTests.cs`

**Interfaces:**
- Produces exact methods:

```csharp
Task<CurrentDocumentSnapshot> GetDocumentStatusAsync(CancellationToken token);
Task<NativeStage01ReadResult> ReadStage01Async(CancellationToken token);
Task<NativeStage01WriteResult> WriteStage01Async(
    NativeStage01WriteRequest request,
    CancellationToken token);
Task<NativeStage02RevitPreviewResult> PreviewStage02Async(
    NativeStage02PreviewRequest request,
    CancellationToken token);
Task<NativeStage02WriteResult> WriteStage02Async(
    NativeStage02WriteRequest request,
    CancellationToken token);
```

- [ ] **Step 1: Write gateway completion tests using a fake dispatcher seam**

The test must prove:

- callback success completes the Task once;
- callback failure faults the Task once;
- cancellation only cancels the wait and does not invoke Revit API from the caller thread;
- continuations run asynchronously.

- [ ] **Step 2: Verify RED**

Expected: gateway type missing.

- [ ] **Step 3: Implement the minimal adapter**

Use:

```csharp
new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously)
```

and call the existing callback-based methods. Do not replace or change the WPF calls. Add only a test seam or overload required to unit-test callback conversion.

- [ ] **Step 4: Run native tests and commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge/McpRevitCommandGateway.cs \
  src/BIMBaoGui.RevitAddin/RevitExternalEventDispatcher.cs \
  tests/BIMBaoGui.RevitAddin.Tests/McpRevitCommandGatewayTests.cs
git commit -m "feat: expose existing ExternalEvent operations to MCP bridge"
```

---

### Task 6: Implement Stage01 MCP adapter and validation lease

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage01Adapter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/McpStage01AdapterTests.cs`

**Interfaces:**
- Consumes: existing `NativeRuleCatalog`, `NativeStage01PayloadCodec`, `NativeStage01Validator`, `NativeStage01RevitReadService`, `NativeStage01RevitService`.
- Produces methods `GetFormSchema`, `ReadAsync`, `Validate`, `WriteAsync` used by router.

- [ ] **Step 1: Write adapter tests**

Tests must prove:

```csharp
[Fact]
public void ValidateReturnsCanonicalPayloadAndLeaseHashWithoutWriting()
{
    Stage01ValidationResponse result = adapter.Validate(validPayloadJson);
    Assert.True(result.Valid);
    Assert.Equal(
        NativeStage01Canonicalizer.Sha256(result.CanonicalPayloadJson),
        result.ValidationHash);
    Assert.Equal(0, fakeGateway.WriteCount);
}

[Fact]
public async Task WriteRequiresConfirmAndConsumesLeaseOnce()
{
    Stage01ValidationResponse validated = adapter.Validate(validPayloadJson);
    await Assert.ThrowsAsync<McpCommandException>(() =>
        adapter.WriteAsync(validated.ValidationHash, false, true, false, token));
    await adapter.WriteAsync(validated.ValidationHash, true, true, false, token);
    await Assert.ThrowsAsync<McpCommandException>(() =>
        adapter.WriteAsync(validated.ValidationHash, true, true, false, token));
}
```

- [ ] **Step 2: Verify RED**

Expected: missing adapter.

- [ ] **Step 3: Implement schema/read/validate/write projections**

Rules:

- form schema is projected from `NativeRuleCatalog.Current`;
- payload decode and validation use existing Stage01 code;
- validation hash is SHA-256 of existing canonical payload;
- the lease stores a cloned `NativeStage01Model`;
- write creates the existing `NativeStage01WriteRequest` without altering flags;
- `confirm=false` returns `CONFIRMATION_REQUIRED`;
- no business values are logged.

- [ ] **Step 4: Run tests and commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge/McpStage01Adapter.cs \
  tests/BIMBaoGui.RevitAddin.Tests/McpStage01AdapterTests.cs
git commit -m "feat: expose Stage01 through confirmed MCP leases"
```

---

### Task 7: Implement Stage02 MCP preview and write leases

**Files:**
- Create: `src/BIMBaoGui.RevitAddin/McpBridge/McpStage02Adapter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/McpStage02AdapterTests.cs`

**Interfaces:**
- Consumes: existing `NativeStage02RevitService.CreatePreview` and `NativeStage02RevitWriteService.Execute` through gateway.
- Produces: `PreviewAsync(scope)` and `WriteAsync(previewHash, confirm)`.

- [ ] **Step 1: Write adapter tests**

Tests must prove:

- `full_model` maps to `NativeStage02ScopeMode.FullModel`;
- `current_selection` maps to `CustomSelection` with no fabricated IDs;
- preview stores the exact `Preview` and `ResolvedRequest` returned by existing service;
- write only accepts the stored preview hash;
- write passes the same preview object to existing service;
- confirm false fails;
- lease is single-use;
- existing `STALE_RESULT` and `PARTIAL_SUCCESS` fields are preserved.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Implement adapter**

The lease value must be:

```csharp
internal sealed class Stage02PreviewLease
{
    internal NativeStage02Preview Preview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }
}
```

Never reconstruct a preview from client-supplied JSON.

- [ ] **Step 4: Run tests and commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge/McpStage02Adapter.cs \
  tests/BIMBaoGui.RevitAddin.Tests/McpStage02AdapterTests.cs
git commit -m "feat: expose Stage02 preview and write through MCP leases"
```

---

### Task 8: Route approved bridge methods and reject everything else

**Files:**
- Modify: `src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs`
- Create: `tests/BIMBaoGui.RevitAddin.Tests/McpBridgeCommandRouterTests.cs`

**Interfaces:**
- Consumes: Stage01/Stage02 adapters and document/rule identity services.
- Produces approved methods:

```text
ping
sessions.current
status.document
identity.rule_package
stage01.form_schema
stage01.read
stage01.validate
stage01.write
stage02.preview
stage02.write
```

- [ ] **Step 1: Write router tests**

Tests must assert the exact method set and that `execute_csharp`, `execute_revit_api`, `run_script`, `click_ui` and unknown names return `UNKNOWN_METHOD`.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Implement strict JSON request parsing**

Each method must deserialize a dedicated input DTO. Missing required fields return `INVALID_ARGUMENT`; predictable business errors return the adapter's error code; unexpected errors return `TECHNICAL_FATAL` without stack traces.

- [ ] **Step 4: Run tests and commit**

```bash
git add src/BIMBaoGui.RevitAddin/McpBridge/McpBridgeCommandRouter.cs \
  tests/BIMBaoGui.RevitAddin.Tests/McpBridgeCommandRouterTests.cs
git commit -m "feat: route only approved BIMBaoGui bridge commands"
```

---

### Task 9: Build the external official-SDK MCP stdio server

**Files:**
- Create: `src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj`
- Create: `src/BIMBaoGui.McpServer/Program.cs`
- Create: `src/BIMBaoGui.McpServer/NamedPipeBridgeClient.cs`
- Create: `src/BIMBaoGui.McpServer/BimBaoGuiTools.cs`
- Create: `src/BIMBaoGui.McpServer/McpToolErrors.cs`
- Create: `src/BIMBaoGui.McpServer/ProbeCommand.cs`
- Create: `tests/BIMBaoGui.McpServer.Tests/BIMBaoGui.McpServer.Tests.csproj`
- Create: `tests/BIMBaoGui.McpServer.Tests/BimBaoGuiToolsTests.cs`
- Create: `tests/BIMBaoGui.McpServer.Tests/NamedPipeBridgeClientTests.cs`

**Interfaces:**
- Consumes: `BridgeSessionLocator`, `BridgeFrameCodec` and approved bridge methods.
- Produces: self-contained stdio MCP Server and `--probe` command.

- [ ] **Step 1: Write tool-list tests**

```csharp
[Fact]
public void ToolCatalogContainsOnlyTheNineApprovedTools()
{
    Assert.Equal(new[]
    {
        "bimbaogui_get_document_status",
        "bimbaogui_get_rule_package_identity",
        "bimbaogui_list_revit_sessions",
        "bimbaogui_stage01_get_form_schema",
        "bimbaogui_stage01_read",
        "bimbaogui_stage01_validate",
        "bimbaogui_stage01_write",
        "bimbaogui_stage02_preview",
        "bimbaogui_stage02_write"
    }, BimBaoGuiToolCatalog.Names.OrderBy(x => x, StringComparer.Ordinal));
}
```

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Create the project with exact package versions**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>0.3.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.3.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <ProjectReference Include="..\BIMBaoGui.McpContracts\BIMBaoGui.McpContracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Configure stdio correctly**

`Program.cs` must use:

```csharp
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
```

No normal log or banner may be written to stdout.

- [ ] **Step 5: Implement the nine tool methods**

Use `[McpServerToolType]`, `[McpServerTool(Name = "...")]` and `[Description]`. Each method must accept `CancellationToken`; write methods require a non-optional `bool confirm` parameter.

- [ ] **Step 6: Implement fake-pipe integration tests**

The test fake server must:

- create a temporary discovery record;
- accept one framed request;
- assert token, method and request ID;
- return a framed response;
- prove the tool returns the structured payload;
- prove wrong token and timeout map to stable errors.

- [ ] **Step 7: Implement `--probe`**

`BIMBaoGui.McpServer.exe --probe` writes one JSON object to stdout and exits:

```json
{
  "connected": false,
  "status": "REVIT_NOT_CONNECTED",
  "sessions": []
}
```

Exit codes:

```text
0 = at least one live Bridge
2 = no live Revit Bridge
3 = invalid/multiple session state
4 = technical failure
```

- [ ] **Step 8: Run tests and commit**

```bash
git add src/BIMBaoGui.McpServer tests/BIMBaoGui.McpServer.Tests
git commit -m "feat: add official SDK BIMBaoGui MCP stdio server"
```

---

### Task 10: Package the bridge, server, probe and generic client config

**Files:**
- Modify: `installer/Install-Revit2020.ps1`
- Create: `installer/McpProbe.cmd`
- Create: `installer/mcp-server-config.example.json`
- Modify: `installer/Install.cmd`
- Modify: `installer/Uninstall.cmd`
- Modify: `docs/revit-addin/README.md`
- Create: `tests/test_revit_addin_mcp_installer_contract.py`

**Interfaces:**
- Produces: one ZIP installable by double-click, absolute-path MCP config, uninstall cleanup.

- [ ] **Step 1: Write installer contract tests**

Tests must require:

```text
BIMBaoGui.McpContracts.dll
BIMBaoGui.McpServer.exe
McpProbe.cmd
mcp-server-config.example.json
mcp-server-config.json
```

and the directories:

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMBaoGui.RevitAddin
%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.0
```

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Extend the installer**

Installation must:

1. require Revit closed unless `-Force`;
2. copy Revit DLLs to existing user add-in directory;
3. copy self-contained MCP EXE to LocalAppData version directory;
4. generate absolute-path `mcp-server-config.json`;
5. hash every installed binary;
6. write install evidence with plugin version, MCP version and hashes;
7. preserve existing Revit manifest behavior.

Uninstall must remove:

- Revit manifest and product DLL directory;
- MCP Server version directory and generated config;
- stale discovery JSON files;
- no unrelated client configuration.

- [ ] **Step 4: Add user probe**

`McpProbe.cmd` must run:

```cmd
"%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.0\BIMBaoGui.McpServer.exe" --probe
```

and preserve exit code after `pause`.

- [ ] **Step 5: Update README**

Document exact installation, MCP config, tool names, confirmation model, multiple Revit sessions and current Stage03 limitation.

- [ ] **Step 6: Run installer tests and commit**

```bash
git add installer docs/revit-addin/README.md \
  tests/test_revit_addin_mcp_installer_contract.py
git commit -m "feat: package MCP-enabled Revit installer"
```

---

### Task 11: Extend Windows CI and publish the installable artifact

**Files:**
- Modify: `.github/workflows/build-revit-addin.yml`

**Interfaces:**
- Produces artifact: `BIMBaoGui-Revit2020-Native-MCP-v0.3.0.zip`.

- [ ] **Step 1: Add restore/test/build stages**

CI must run:

```powershell
python -m pytest tests/test_revit_addin_mcp_non_regression.py -q
python -m pytest tests/test_revit_addin_mcp_contract.py -q
python -m pytest tests/test_revit_addin_mcp_installer_contract.py -q

dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.McpServer.Tests/BIMBaoGui.McpServer.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release

dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release -p:TreatWarningsAsErrors=true

dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

- [ ] **Step 2: Add no-Revit probe smoke**

```powershell
& src/BIMBaoGui.McpServer/bin/Release/net8.0/win-x64/publish/BIMBaoGui.McpServer.exe --probe
if ($LASTEXITCODE -ne 2) {
  throw "Expected REVIT_NOT_CONNECTED probe exit code 2."
}
```

- [ ] **Step 3: Extend installer smoke**

Verify:

- both Revit DLLs installed;
- MCP EXE installed;
- generated config uses an absolute existing command path;
- source and installed hashes match;
- uninstall removes both product roots;
- no discovery files remain.

- [ ] **Step 4: Stage the artifact**

Artifact root must contain:

```text
Install.cmd
Uninstall.cmd
McpProbe.cmd
Install-Revit2020.ps1
BIMBaoGui.RevitAddin.addin
mcp-server-config.example.json
README.md
SHA256SUMS.txt
BIMBaoGui.RevitAddin/
BIMBaoGui.McpServer/
```

- [ ] **Step 5: Upload and commit**

```yaml
- uses: actions/upload-artifact@v7
  with:
    name: BIMBaoGui-Revit2020-Native-MCP-v0.3.0
    path: artifacts
    if-no-files-found: error
    retention-days: 30
```

```bash
git add .github/workflows/build-revit-addin.yml
git commit -m "ci: build and package MCP-enabled Revit add-in"
```

---

### Task 12: Verification and acceptance evidence

**Files:**
- Create: `docs/revit-addin/acceptance/native-mcp-v0.3.0-checklist.md`
- Create: `docs/revit-addin/acceptance/native-mcp-v0.3.0-automation-evidence.json`

**Interfaces:**
- Produces: traceable automation evidence and explicit real-Revit pending items.

- [ ] **Step 1: Run the full verification matrix**

Required fresh commands:

```powershell
python -m pytest \
  tests/test_revit_addin_scaffold_contract.py \
  tests/test_revit_addin_installer_contract.py \
  tests/test_revit_addin_stage01_storage_contract.py \
  tests/test_revit_addin_stage01_revit_contract.py \
  tests/test_revit_addin_stage01_ui_contract.py \
  tests/test_revit_addin_stage02_revit_contract.py \
  tests/test_revit_addin_mcp_non_regression.py \
  tests/test_revit_addin_mcp_contract.py \
  tests/test_revit_addin_mcp_installer_contract.py -q

dotnet test tests/BIMBaoGui.McpContracts.Tests/BIMBaoGui.McpContracts.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.McpServer.Tests/BIMBaoGui.McpServer.Tests.csproj -c Release
dotnet test tests/BIMBaoGui.RevitAddin.Tests/BIMBaoGui.RevitAddin.Tests.csproj -c Release

dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj -c Release -p:TreatWarningsAsErrors=true

dotnet publish src/BIMBaoGui.McpServer/BIMBaoGui.McpServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Expected: zero failures, zero warnings, zero errors.

- [ ] **Step 2: Validate the artifact ZIP**

Record:

- branch and commit SHA;
- workflow run ID;
- artifact ID;
- ZIP SHA-256;
- Revit DLL SHA-256;
- contracts DLL SHA-256;
- MCP EXE SHA-256;
- rule package identity;
- exact test counts.

- [ ] **Step 3: Record honest real-Revit status**

The checklist must keep these items unchecked until performed in Revit 2020:

```text
[ ] MCP status works without opening DockablePane
[ ] Stage01 read/validate/write through a real MCP Client
[ ] Stage02 preview/write through a real MCP Client
[ ] stale preview rejection after model edit
[ ] multiple Revit session selection
[ ] bridge failure does not affect manual workspace
[ ] save-close-reopen persistence
```

- [ ] **Step 4: Commit evidence**

```bash
git add docs/revit-addin/acceptance
git commit -m "docs: record MCP v0.3 automation evidence"
```
