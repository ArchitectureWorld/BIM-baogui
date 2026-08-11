# Stage01 JSON Failure Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate a complete JSON failure report beside the active GHA whenever a Stage01 Revit commit throws, while keeping the Grasshopper UI concise.

**Architecture:** Add a Revit-independent report writer that accepts explicit plugin, host, document, stage, rollback, and exception context. `Stage01RevitService` tracks the active commit stage and delegates failure persistence to the writer; the writer uses the existing `JavaScriptSerializer`, UTF-8 without BOM, and a same-directory temporary file followed by rename.

**Tech Stack:** C#/.NET Framework 4.8, `System.Web.Script.Serialization.JavaScriptSerializer`, xUnit, Revit 2020 API, pytest source-contract tests.

---

### Task 1: JSON Report Writer

**Files:**
- Create: `src/BIMBaoGui.Stage01/Diagnostics/Stage01FailureReportWriter.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage01FailureReportWriterTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing report-writer tests**

Add tests that create a temporary fake GHA path, pass a nested exception and explicit context, then assert:

```csharp
Stage01FailureReportWriteResult result = Stage01FailureReportWriter.TryWrite(context);
Assert.True(result.Success);
Assert.EndsWith(".json", result.ReportPath);
Assert.Equal(Path.GetDirectoryName(context.AssemblyPath), Path.GetDirectoryName(result.ReportPath));
Assert.Equal("1.0", root["schemaVersion"]);
Assert.Equal("OFFICIAL_PROJECTION", root["operationStage"]);
Assert.Equal(2, ((object[])root["exceptionChain"]).Length);
Assert.DoesNotContain("payload-secret", File.ReadAllText(result.ReportPath));
```

Add a second test using a deleted output directory and assert `Success == false`, `ErrorCode == "REPORT_WRITE_FAILED"`, and the original exception remains available in the result summary.

- [ ] **Step 2: Run the new tests and verify RED**

Run:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release --filter Stage01FailureReportWriterTests
```

Expected: compilation/test failure because `Stage01FailureReportWriter` does not exist.

- [ ] **Step 3: Implement the minimal writer**

Create DTOs for the explicit context and write result. Build a dictionary with:

```csharp
new Dictionary<string, object>
{
  ["schemaVersion"] = "1.0",
  ["reportId"] = Guid.NewGuid().ToString("D"),
  ["occurredUtc"] = context.OccurredUtc.ToString("O", CultureInfo.InvariantCulture),
  ["occurredLocal"] = context.OccurredLocal.ToString("O", CultureInfo.InvariantCulture),
  ["diagnosticCode"] = "DIAG_STAGE01_COMMIT_FAILED",
  ["operationStage"] = context.OperationStage,
  ["transactionRolledBack"] = context.TransactionRolledBack,
  ["plugin"] = pluginDictionary,
  ["host"] = hostDictionary,
  ["document"] = documentDictionary,
  ["exceptionChain"] = exceptionEntries
};
```

Serialize with `JavaScriptSerializer`, format deterministically for readable JSON, write with `new UTF8Encoding(false)` to a unique temporary file in the GHA directory, then `File.Move` to the final timestamped `.json` path. On failure, delete the temporary file best-effort and return `REPORT_WRITE_FAILED` without throwing.

- [ ] **Step 4: Run report-writer tests and verify GREEN**

Run the command from Step 2. Expected: all `Stage01FailureReportWriterTests` pass.

- [ ] **Step 5: Commit the writer**

```powershell
git add src/BIMBaoGui.Stage01/Diagnostics/Stage01FailureReportWriter.cs tests/BIMBaoGui.Stage01.Core.Tests/Stage01FailureReportWriterTests.cs tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
git commit -m "feat: write Stage01 JSON failure reports"
```

### Task 2: Stage01 Commit Integration

**Files:**
- Modify: `src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs`
- Modify: `tests/test_stage01_failure_ui_contract.py`

- [ ] **Step 1: Write failing integration contract assertions**

Require the service source to contain:

```python
assert "Stage01FailureReportWriter.TryWrite" in service
assert 'operationStage = "OFFICIAL_PROJECTION"' in service
assert 'operationStage = "READBACK_VERIFICATION"' in service
assert "错误报告=" in service
assert "FormatExceptionChain" not in service
```

- [ ] **Step 2: Run the contract test and verify RED**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
& 'C:\ProgramData\Anaconda3\python.exe' -m pytest tests/test_stage01_failure_ui_contract.py -q
```

Expected: failure because Stage01 still renders the exception chain directly and does not invoke the writer.

- [ ] **Step 3: Track commit stages and invoke the report writer**

Initialize `operationStage` before the transaction group and assign it before each boundary:

```csharp
operationStage = "APPLY_UNITS";
operationStage = "PROJECT_POSITION";
operationStage = "PROJECT_INFORMATION";
operationStage = "INTERNAL_STORAGE";
operationStage = "OFFICIAL_PROJECTION";
operationStage = "TRANSACTION_COMMIT";
operationStage = "READBACK_VERIFICATION";
```

In the outer catch, roll back first, build a `Stage01FailureReportContext` from `uiapp.Application`, `document`, `typeof(Stage01RevitService).Assembly.Location`, the stage, and the original exception, then call `Stage01FailureReportWriter.TryWrite`.

If writing succeeds, return only:

```text
DIAG_STAGE01_COMMIT_FAILED：初始化失败，事务已回滚；异常类型=<type>；错误报告=<full path>
```

If writing fails, return `REPORT_WRITE_FAILED` plus the original exception type/message and report-write error. Remove `FormatExceptionChain` and the direct stack trace from the UI path.

- [ ] **Step 4: Run contract and .NET tests**

Run the command from Step 2, then:

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
```

Expected: both suites pass.

- [ ] **Step 5: Commit integration**

```powershell
git add src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs tests/test_stage01_failure_ui_contract.py
git commit -m "fix: persist Stage01 failure diagnostics as JSON"
```

### Task 3: Full Verification and Revit Deployment

**Files:**
- Modify only if verification exposes a defect.
- Runtime output: `C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui\BIMBaoGui.Stage01.failure-*.json`

- [ ] **Step 1: Run all Python tests**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
& 'C:\ProgramData\Anaconda3\python.exe' -m pytest tests -q
```

Expected: zero failures.

- [ ] **Step 2: Run all .NET tests and Release build**

```powershell
dotnet test tests\BIMBaoGui.Stage01.Core.Tests\BIMBaoGui.Stage01.Core.Tests.csproj -c Release
dotnet build src\BIMBaoGui.Stage01\BIMBaoGui.Stage01.csproj -c Release --no-restore
```

Expected: zero test failures, zero build warnings, and zero build errors.

- [ ] **Step 3: Deploy without backup**

After Revit and Rhino have fully exited, directly copy `src\BIMBaoGui.Stage01\bin\Release\net48\BIMBaoGui.Stage01.dll` over the active `.gha`. Do not create `BIMbaogui-backups`. Verify source and target SHA-256 match and the active directory contains exactly one GHA.

- [ ] **Step 4: Reproduce and inspect the report**

Open `20260731test02-v090-validation.rvt`, start Rhino.Inside/Grasshopper, open `BIMBaoGui-HIFC-debug-20260801.gh`, and click `写入并回读` once. Verify the UI shows a JSON report path. Read the report, validate it parses, confirm it contains no payload values, and use `operationStage` plus `exceptionChain` to identify the actual Revit failure boundary.

- [ ] **Step 5: Continue the root-cause fix**

Write a failing regression test for the failure revealed by the JSON report, implement the smallest root-cause fix, rerun the full suites, redeploy without backup, and repeat the Revit reproduction until Stage01 succeeds.
