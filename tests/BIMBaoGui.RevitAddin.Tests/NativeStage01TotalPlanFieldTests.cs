using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01TotalPlanFieldTests
  {
    [Fact]
    public void PlanningAndActualRatioIdentitiesRemainDistinct()
    {
      Assert.NotEqual(
        "IfcProject|Pset_项目控制指标信息属性集|容积率",
        NativeStage02BMetricCatalog.Current.MetricsFor("总平模型")[2].Identity);
    }

    [Fact]
    public void TotalPlanStage01CatalogContainsLocationAndReferenceFields()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;

      Assert.Contains(
        "IfcProject|Pset_申报信息属性集|经度",
        catalog.Stage01FieldsByKey.Keys);
      Assert.Contains(
        "IfcProject|Pset_申报信息属性集|纬度",
        catalog.Stage01FieldsByKey.Keys);
      Assert.True(catalog.Stage01FieldsByKey[
        "IfcProject|Pset_登记信息属性集|总建筑面积"].Deferred);
      Assert.Contains(catalog.Stage01Fields, field => string.Equals(
        field.SourceKind,
        "GH_planning_condition_input",
        StringComparison.Ordinal));
    }

    [Fact]
    public void PlanningTargetEditorWritesPlanningTargetsNotActualMetricValues()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01FieldDefinition target = catalog.Stage01Fields.First(field =>
        string.Equals(
          field.SourceKind,
          "GH_planning_condition_input",
          StringComparison.Ordinal));
      var viewModel = new NativeStage01ViewModel(catalog);

      viewModel.SetPlanningTarget(
        target,
        "LessOrEqual",
        "2.0",
        string.Empty,
        target.CanonicalUnit);

      Assert.True(viewModel.Model.PlanningTargets.ContainsKey(target.PropertyId));
      Assert.False(viewModel.Model.Values.ContainsKey(target.FieldKey));
      Assert.Equal("≤2.0", viewModel.GetFieldValue(target));
      Assert.Equal("0.9.1", NativeStage01Canonicalizer.PayloadSchemaVersion);
    }
  }
}
