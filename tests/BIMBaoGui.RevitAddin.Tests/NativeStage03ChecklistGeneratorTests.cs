using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03ChecklistGeneratorTests
  {
    [Fact]
    public void Total_plan_checklist_is_deterministic_and_has_no_not_applicable()
    {
      NativeStage03ChecklistGenerationResult first =
        NativeStage03ChecklistGenerator.Generate(
          "总平模型",
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            ["site.green"] = true
          },
          NativeReportingRuleCatalog.Current);
      NativeStage03ChecklistGenerationResult second =
        NativeStage03ChecklistGenerator.Generate(
          "总平模型",
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            ["site.green"] = true
          },
          NativeReportingRuleCatalog.Current);

      Assert.True(first.Supported);
      Assert.Equal(first.Definitions.Select(value => value.CheckId),
        second.Definitions.Select(value => value.CheckId));
      Assert.DoesNotContain(first.Definitions,
        value => value.DisplayName.Contains("不适用"));
      Assert.Equal(
        NativeReportingRuleCatalog.Current.OfficialAcceptancePropertyIds,
        first.OfficialAcceptanceManifest.Properties.Select(
          value => value.PropertyId));
      Assert.Equal(first.OfficialAcceptanceManifest.Sha256,
        second.OfficialAcceptanceManifest.Sha256);
      Assert.Equal(
        first.OfficialAcceptanceManifest.Sha256,
        OfficialAcceptanceManifestCanonicalizer.ComputeSha256(
          NativeStage03ChecklistGenerator.ToHifcManifest(
            first.OfficialAcceptanceManifest)));
    }

    [Fact]
    public void Conditional_tasks_are_excluded_instead_of_emitting_not_applicable()
    {
      NativeStage03ChecklistGenerationResult withoutGreen =
        NativeStage03ChecklistGenerator.Generate(
          "总平模型",
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            ["site.green"] = false
          },
          NativeReportingRuleCatalog.Current);

      Assert.DoesNotContain(withoutGreen.Definitions,
        value => string.Equals(value.TaskId, "SITE.GREEN",
          StringComparison.Ordinal));
      Assert.DoesNotContain(withoutGreen.Definitions,
        value => value.DisplayName.Contains("不适用"));
    }

    [Fact]
    public void Unsupported_profile_never_falls_back_to_total_plan()
    {
      NativeStage03ChecklistGenerationResult result =
        NativeStage03ChecklistGenerator.Generate(
          "单体建筑—地上",
          new Dictionary<string, bool>(StringComparer.Ordinal),
          NativeReportingRuleCatalog.Current);

      Assert.False(result.Supported);
      Assert.Equal("MODEL_PROFILE_NOT_IMPLEMENTED_PHASE1", result.Code);
      Assert.Empty(result.Definitions);
    }
  }
}
