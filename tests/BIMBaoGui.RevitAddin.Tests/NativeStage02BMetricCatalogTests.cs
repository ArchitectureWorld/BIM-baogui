using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BMetricCatalogTests
  {
    [Fact]
    public void TotalPlanMetricsAreTheExactSixManualActualValues()
    {
      IReadOnlyList<NativeStage02BMetricDefinition> metrics =
        NativeStage02BMetricCatalog.Current.MetricsFor("总平模型");

      Assert.Equal(6, metrics.Count);
      Assert.Equal(
        new[]
        {
          "ca21e324-046b-5bfd-84c8-0d3470082303",
          "93e51676-237e-56a8-8f28-2da845422e2e",
          "201a00ac-3672-5ded-83d2-ed96f81bfabf",
          "f630ad47-b006-5127-badd-b1660cf996c3",
          "c62cfd5f-2a50-5230-9c5d-4037c39061bf",
          "84df74c2-a7e5-5a98-a5e0-4458e49a3973"
        },
        metrics.Select(value => value.PropertyId));
      Assert.Equal(
        "IfcProject|Pset_登记信息属性集|总建筑面积",
        metrics[0].Identity);
      Assert.All(metrics, value => Assert.Equal("MANUAL_INPUT", value.Source));
      Assert.All(metrics, value => Assert.False(value.OfficialExportVerified));
      Assert.All(metrics, value => Assert.Equal(
        NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
        value.OfficialCarrierStatus));
      Assert.All(metrics, value => Assert.Same(
        NativeStage02RuleCatalog.Current.PropertiesById[value.PropertyId],
        value.Property));
    }

    [Fact]
    public void PlanningTargetsAreNeverExposedAsActualMetrics()
    {
      string[] actual = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")
        .Select(value => value.PropertyId)
        .ToArray();

      Assert.DoesNotContain("c94f1ae2-0a02-5479-aae4-c8f59af71fe0", actual);
      Assert.DoesNotContain("35675fd2-c3d2-5553-8db6-855980a201a4", actual);
      Assert.DoesNotContain("5d5f3dba-3ae9-59c6-9aee-aa24e88f312c", actual);
    }

    [Fact]
    public void UnsupportedProfilesDoNotFallBackToTotalPlanMetrics()
    {
      Assert.Empty(NativeStage02BMetricCatalog.Current.MetricsFor("单体建筑—地上"));
      Assert.Empty(NativeStage02BMetricCatalog.Current.MetricsFor("单体建筑—地下"));
      Assert.Empty(NativeStage02BMetricCatalog.Current.MetricsFor("未知模型"));
      Assert.Empty(NativeReportingRuleCatalog.Current.GetTaskIds("未知模型"));
      Assert.Empty(NativeReportingRuleCatalog.Current.GetChecks("未知模型"));
      Assert.Empty(NativeReportingRuleCatalog.Current.GetSemanticRoles("未知模型"));
    }
  }
}
