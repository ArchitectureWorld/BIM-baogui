using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class PlanningTargetRequirementPolicyTests
  {
    [Theory]
    [InlineData(PlanningTargetCatalog.BuildingDensityCode)]
    [InlineData(PlanningTargetCatalog.FloorAreaRatioCode)]
    [InlineData(PlanningTargetCatalog.GreenRateCode)]
    public void SiteModel_RequiresCoreProjectTargets(string metricCode)
    {
      Assert.Equal(
        PlanningTargetRequirement.Required,
        PlanningTargetRequirementPolicy.GetRequirement(PlanningTargetRequirementPolicy.SiteModel, metricCode));
    }

    [Theory]
    [InlineData(PlanningTargetRequirementPolicy.AboveGroundModel)]
    [InlineData(PlanningTargetRequirementPolicy.UndergroundModel)]
    public void BuildingModels_InheritCoreProjectTargets(string modelFileType)
    {
      foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
      {
        Assert.Equal(
          PlanningTargetRequirement.Inherited,
          PlanningTargetRequirementPolicy.GetRequirement(modelFileType, definition.MetricCode));
      }
    }

    [Fact]
    public void UnknownMetric_IsNotApplicable()
    {
      Assert.Equal(
        PlanningTargetRequirement.NotApplicable,
        PlanningTargetRequirementPolicy.GetRequirement(PlanningTargetRequirementPolicy.SiteModel, "planning.unknown"));
    }
  }
}
