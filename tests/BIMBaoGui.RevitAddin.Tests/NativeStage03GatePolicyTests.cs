using System;
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
    public void Forced_requires_a_reason_and_at_least_one_exportable_field()
    {
      NativeStage03GateDecision missingReason =
        NativeStage03GatePolicy.Evaluate(
          NativeStage03Mode.ForcedTest,
          " ",
          Array.Empty<string>(),
          new[] { "EMPTY_REQUIRED_VALUE" },
          1);
      NativeStage03GateDecision noFields = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        "IFCFlux 定位",
        Array.Empty<string>(),
        new[] { "EMPTY_REQUIRED_VALUE" },
        0);

      Assert.False(missingReason.AllowExport);
      Assert.Contains("FORCE_REASON_REQUIRED", missingReason.Blockers);
      Assert.False(noFields.AllowExport);
      Assert.Contains("NO_EXPORTABLE_FIELDS", noFields.Blockers);
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
  }
}
