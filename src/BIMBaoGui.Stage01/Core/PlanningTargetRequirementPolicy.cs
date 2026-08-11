using System;

namespace BIMBaoGui.Stage01.Core
{
  internal static class PlanningTargetRequirementPolicy
  {
    public const string SiteModel = "总平模型";
    public const string AboveGroundModel = "单体建筑—地上";
    public const string UndergroundModel = "单体建筑—地下";

    public static PlanningTargetRequirement GetRequirement(string modelFileType, string metricCode)
    {
      if (PlanningTargetCatalog.Get(metricCode) == null)
        return PlanningTargetRequirement.NotApplicable;

      if (string.Equals(modelFileType, SiteModel, StringComparison.Ordinal))
        return PlanningTargetRequirement.Required;

      if (string.Equals(modelFileType, AboveGroundModel, StringComparison.Ordinal)
        || string.Equals(modelFileType, UndergroundModel, StringComparison.Ordinal))
        return PlanningTargetRequirement.Inherited;

      return PlanningTargetRequirement.Optional;
    }

    public static bool RequiresManualValue(string modelFileType, string metricCode)
    {
      PlanningTargetRequirement requirement = GetRequirement(modelFileType, metricCode);
      return requirement == PlanningTargetRequirement.Required
        || requirement == PlanningTargetRequirement.Conditional;
    }
  }
}
