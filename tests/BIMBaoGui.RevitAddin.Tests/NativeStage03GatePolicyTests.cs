using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.McpBridge;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03GatePolicyTests
  {
    [Fact]
    public void Strict_blocks_any_business_blocker()
    {
      NativeStage03GateDecision decision = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.Strict,
        string.Empty,
        Array.Empty<string>(),
        new[] { "EMPTY_REQUIRED_VALUE" },
        12);

      Assert.False(decision.AllowExport);
      Assert.False(decision.Forced);
      Assert.Contains("EMPTY_REQUIRED_VALUE", decision.Blockers);
    }

    [Fact]
    public void Forced_test_requires_reason_and_never_bypasses_technical_fatal()
    {
      NativeStage03GateDecision noReason = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        " ",
        Array.Empty<string>(),
        new[] { "MISSING_DATA" },
        1);
      NativeStage03GateDecision businessOnly =
        NativeStage03GatePolicy.Evaluate(
          NativeStage03Mode.ForcedTest,
          "开发测试缺项导出",
          Array.Empty<string>(),
          new[] { "MISSING_DATA" },
          1);
      NativeStage03GateDecision technical = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        "开发测试",
        new[] { "DOCUMENT_UNAVAILABLE" },
        Array.Empty<string>(),
        1);

      Assert.False(noReason.AllowExport);
      Assert.Contains(NativeStage03Codes.ForceReasonRequired,
        noReason.Blockers);
      Assert.True(businessOnly.AllowExport);
      Assert.Contains("MISSING_DATA",
        businessOnly.BypassedBusinessBlockers);
      Assert.False(technical.AllowExport);
    }

    [Fact]
    public void Forced_allows_business_blockers_but_never_technical_fatals()
    {
      NativeStage03GateDecision forced = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        "IFCFlux 定位属性映射",
        Array.Empty<string>(),
        new[] { "EMPTY_REQUIRED_VALUE" },
        8);
      NativeStage03GateDecision fatal = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        "IFCFlux 定位属性映射",
        new[] { "UNSUPPORTED_REVIT" },
        new[] { "EMPTY_REQUIRED_VALUE" },
        8);

      Assert.True(forced.AllowExport);
      Assert.True(forced.Forced);
      Assert.Contains("EMPTY_REQUIRED_VALUE", forced.BypassedBusinessBlockers);
      Assert.False(fatal.AllowExport);
      Assert.Contains("UNSUPPORTED_REVIT", fatal.Blockers);
    }

    [Fact]
    public void Successful_forced_test_execution_never_becomes_a_normal_pass()
    {
      var failedChecklistItem = new NativeStage03ChecklistItem
      {
        CheckId = "CHECK-RED",
        Status = NativeStage03ChecklistStatus.Failed,
        IssueCode = "MISSING_DATA"
      };
      var scan = new NativeStage03ScanResult
      {
        Mode = NativeStage03Mode.ForcedTest,
        OfficialAcceptanceManifest = new NativeOfficialAcceptanceManifest
        {
          SchemaVersion = "1.0.0",
          Sha256 = new string('a', 64)
        },
        OfficialAcceptanceRevitReadbacks = Array.Empty<
          NativeOfficialAcceptancePropertyReadback>(),
        Checklist = new[] { failedChecklistItem }
      };
      var execution = new NativeStage03ExecutionResult { Success = true };

      NativeStage03ExecutionIdentityPolicy.Apply(scan, execution);

      Assert.True(execution.IsTestExport);
      Assert.False(execution.CountsAsNormalExportPass);
      Assert.Equal("PENDING", execution.OfficialAcceptanceStatus);
      Assert.Same(scan.OfficialAcceptanceManifest,
        execution.OfficialAcceptanceManifest);
      Assert.Same(failedChecklistItem, Assert.Single(execution.Checklist));
      Assert.Equal(NativeStage03ChecklistStatus.Failed,
        execution.Checklist[0].Status);
    }

    [Fact]
    public void Strict_execution_identity_recomputes_checklist_counts_before_normal_pass()
    {
      var scan = new NativeStage03ScanResult
      {
        Mode = NativeStage03Mode.Strict,
        AllowExport = true,
        ExportFields = new[] { new HifcFieldRequest() },
        Checklist = new[]
        {
          new NativeStage03ChecklistItem
          {
            CheckId = "CHECK-RED-BUT-STORED-ZERO",
            Status = NativeStage03ChecklistStatus.Failed,
            IssueCode = "MISSING_DATA"
          }
        },
        FailedCount = 0,
        NotCheckedCount = 0,
        TechnicalFatalCodes = Array.Empty<string>()
      };
      var execution = new NativeStage03ExecutionResult
      {
        Success = true,
        InternalValidationStatus = HifcCoreStatus.InternalValidated
      };

      NativeStage03ExecutionIdentityPolicy.Apply(scan, execution);

      Assert.False(execution.CountsAsNormalExportPass);
    }

    [Fact]
    public void Output_paths_are_unique_and_mark_forced_test()
    {
      DateTimeOffset timestamp = DateTimeOffset.Parse(
        "2026-08-12T08:09:10+08:00");
      NativeStage03RunPaths strict = NativeStage03OutputPathPolicy.Create(
        @"C:\HBR",
        "测试模型.rvt",
        "RUN001",
        timestamp,
        NativeStage03Mode.Strict);
      NativeStage03RunPaths forced = NativeStage03OutputPathPolicy.Create(
        @"C:\HBR",
        "测试模型.rvt",
        "RUN002",
        timestamp,
        NativeStage03Mode.ForcedTest);

      Assert.EndsWith("_RAW.ifc", strict.RawIfcPath);
      Assert.EndsWith("_HIFC.ifc", strict.FinalIfcPath);
      Assert.DoesNotContain("FORCED_TEST", strict.FinalIfcPath);
      Assert.Contains("FORCED_TEST", forced.FinalIfcPath);
      Assert.NotEqual(strict.RunDirectory, forced.RunDirectory);
      Assert.EndsWith("_fields.json", strict.FieldsReportPath);
      Assert.EndsWith("_validation.json", strict.ValidationReportPath);
      Assert.EndsWith("_IFCFlux_checklist.md", strict.IfcFluxChecklistPath);
    }

    [Fact]
    public void Forced_test_ui_requires_reason_only_for_export_not_for_scan()
    {
      Assert.False(NativeStage03UiStatePolicy.CanExport(
        NativeStage03Mode.ForcedTest, " ", false, true));
      Assert.True(NativeStage03UiStatePolicy.CanExport(
        NativeStage03Mode.ForcedTest, "开发定位", false, true));
      Assert.True(NativeStage03UiStatePolicy.CanExport(
        NativeStage03Mode.Strict, string.Empty, false, true));
      Assert.True(NativeStage03UiStatePolicy.CanScan(false));
      Assert.False(NativeStage03UiStatePolicy.CanScan(true));
    }

    [Fact]
    public async Task Mcp_scan_propagates_reason_and_directory_and_leases_only_exportable_scans()
    {
      var gateway = new FakeStage03Gateway();
      var leases = new McpLeaseStore<NativeStage03ScanResult>(
        new FixedClock(), TimeSpan.FromMinutes(30));
      var adapter = new McpStage03Adapter(gateway, leases);

      string strictJson = await adapter.ScanAsync("strict", string.Empty,
        @"C:\strict-output", CancellationToken.None);
      Assert.Same(gateway.LastScan, leases.Get("strict-hash"));
      Dictionary<string, object> strict = Json(strictJson);
      Assert.Equal(@"C:\strict-output",
        strict["normalized_output_directory"]);
      Assert.True((bool)strict["allow_export"]);

      string forcedJson = await adapter.ScanAsync("forced_test",
        "开发定位", @"C:\forced-output", CancellationToken.None);
      Assert.Same(gateway.LastScan, leases.Get("forced-hash"));
      Dictionary<string, object> forced = Json(forcedJson);
      Assert.Equal("PENDING", forced["official_acceptance_status"]);
      Assert.Single((object[])forced["checklist"]);
      Assert.Equal(1,
        ((Dictionary<string, object>)forced["checklist_counts"])["failed"]);
      Assert.Single((object[])forced["official_acceptance_revit_readbacks"]);
      object[] projectedOwners = (object[])
        ((Dictionary<string, object>)
          ((object[])forced["official_acceptance_revit_readbacks"])[0])
          ["values"];
      Assert.Equal("owner-b",
        ((Dictionary<string, object>)projectedOwners[0])["revit_unique_id"]);

      string executionJson = await adapter.ExportAsync("forced-hash", true,
        @"C:\forced-output", CancellationToken.None);
      Dictionary<string, object> execution = Json(executionJson);
      Assert.True((bool)execution["is_test_export"]);
      Assert.False((bool)execution["counts_as_normal_export_pass"]);
      Assert.Equal("PENDING", execution["official_acceptance_status"]);
      Assert.Equal("Failed",
        ((Dictionary<string, object>)((object[])execution["checklist"])[0])
          ["status"]);

      foreach (Tuple<string, string, string> blocked in new[]
      {
        Tuple.Create("forced_test", " ", @"C:\empty-reason"),
        Tuple.Create("forced_test", "开发定位", @"C:\not-writable"),
        Tuple.Create("forced_test", "开发定位", @"C:\translator-missing")
      })
      {
        string json = await adapter.ScanAsync(blocked.Item1, blocked.Item2,
          blocked.Item3, CancellationToken.None);
        Dictionary<string, object> projected = Json(json);
        Assert.False((bool)projected["allow_export"]);
        Assert.Throws<McpLeaseException>(() =>
          leases.Get((string)projected["scan_hash"]));
      }
    }

    private static Dictionary<string, object> Json(string json)
    {
      return (Dictionary<string, object>)new JavaScriptSerializer()
        .DeserializeObject(json);
    }

    private sealed class FixedClock : IMcpClock
    {
      public DateTimeOffset UtcNow =>
        DateTimeOffset.Parse("2026-08-17T12:00:00+08:00");
    }

    private sealed class FakeStage03Gateway : IMcpStage03Gateway
    {
      internal NativeStage03ScanResult LastScan { get; private set; }

      public Task<NativeStage03ScanResult> ScanStage03Async(
        NativeStage03ScanRequest request,
        CancellationToken cancellationToken)
      {
        string technical = request.OutputDirectory.Contains("not-writable")
          ? NativeStage03Codes.OutputDirectoryNotWritable
          : request.OutputDirectory.Contains("translator-missing")
            ? NativeStage03Codes.TranslatorDependencyUnavailable
            : string.Empty;
        bool forced = request.Mode == NativeStage03Mode.ForcedTest;
        NativeStage03GateDecision gate = NativeStage03GatePolicy.Evaluate(
          request.Mode,
          request.ForceReason,
          technical.Length == 0
            ? Array.Empty<string>()
            : new[] { technical },
          forced ? new[] { "MISSING_DATA" } : Array.Empty<string>(),
          1);
        string hash = forced ? "forced-hash" : "strict-hash";
        if (request.OutputDirectory.Contains("empty-reason"))
          hash = "empty-reason-hash";
        if (request.OutputDirectory.Contains("not-writable"))
          hash = "not-writable-hash";
        if (request.OutputDirectory.Contains("translator-missing"))
          hash = "translator-missing-hash";
        LastScan = new NativeStage03ScanResult
        {
          Success = technical.Length == 0,
          Mode = request.Mode,
          ForceReason = request.ForceReason,
          AllowExport = gate.AllowExport,
          Forced = gate.Forced,
          ScanHash = hash,
          NormalizedOutputDirectory = request.OutputDirectory,
          PreflightHash = "preflight-hash",
          TechnicalFatalCodes = technical.Length == 0
            ? Array.Empty<string>()
            : new[] { technical },
          BusinessBlockers = forced
            ? new[] { "MISSING_DATA" }
            : Array.Empty<string>(),
          FailedCount = forced ? 1 : 0,
          OfficialAcceptanceManifest = new NativeOfficialAcceptanceManifest
          {
            SchemaVersion = "1.0.0",
            Sha256 = "manifest-sha",
            Properties = Array.Empty<
              NativeOfficialAcceptanceManifestEntry>()
          },
          OfficialAcceptanceRevitReadbacks = new[]
          {
            new NativeOfficialAcceptancePropertyReadback
            {
              PropertyId = "property-id",
              SourceStage = NativeReportingSourceStage.Stage02B,
              SourceResultHash = "result-hash",
              Values = new[]
              {
                new NativeOfficialAcceptanceOwnerReadback
                {
                  RevitUniqueId = "owner-a",
                  ExpectedIfcGlobalId = "gid-b",
                  CanonicalValue = "A"
                },
                new NativeOfficialAcceptanceOwnerReadback
                {
                  RevitUniqueId = "owner-b",
                  ExpectedIfcGlobalId = "gid-a",
                  CanonicalValue = "B"
                }
              }
            }
          },
          Checklist = forced
            ? new[]
            {
              new NativeStage03ChecklistItem
              {
                CheckId = "CHECK-RED",
                Status = NativeStage03ChecklistStatus.Failed,
                IssueCode = "MISSING_DATA",
                SourceStage = NativeReportingSourceStage.Stage02B
              }
            }
            : Array.Empty<NativeStage03ChecklistItem>()
        };
        return Task.FromResult(LastScan);
      }

      public Task<NativeStage03ExecutionResult> ExportStage03Async(
        NativeStage03ExportRequest request,
        CancellationToken cancellationToken)
      {
        var result = new NativeStage03ExecutionResult { Success = true };
        NativeStage03ExecutionIdentityPolicy.Apply(
          request.ConfirmedScan, result);
        return Task.FromResult(result);
      }

      public Task<CurrentDocumentSnapshot> GetDocumentStatusAsync(
        CancellationToken cancellationToken)
      {
        return Task.FromResult<CurrentDocumentSnapshot>(null);
      }

      public Task<NativeStage03ExecutionResult> RevalidateStage03Async(
        string ifcPath,
        CancellationToken cancellationToken)
      {
        return Task.FromResult<NativeStage03ExecutionResult>(null);
      }
    }
  }
}
