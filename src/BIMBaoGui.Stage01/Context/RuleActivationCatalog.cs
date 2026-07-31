using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Context
{
  internal sealed class RuleActivationResult
  {
    public RuleActivationResult(IEnumerable<string> activated, IEnumerable<string> notApplicable)
    {
      Activated = (activated ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      NotApplicable = (notApplicable ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string> Activated { get; }
    public IReadOnlyList<string> NotApplicable { get; }
  }

  internal static class RuleActivationCatalog
  {
    private static readonly IReadOnlyDictionary<string, string> ConditionRules =
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["site.other_land"] = "HBR.SITE.OTHER_LAND",
        ["site.road_redline"] = "HBR.SITE.ROAD_REDLINE",
        ["site.road_centerline"] = "HBR.SITE.ROAD_CENTERLINE",
        ["site.internal_roads"] = "HBR.SITE.INTERNAL_ROADS",
        ["site.fire_lane"] = "HBR.SITE.FIRE_LANE",
        ["site.fire_field"] = "HBR.SITE.FIRE_FIELD",
        ["site.green"] = "HBR.SITE.GREEN",
        ["site.outdoor_parking"] = "HBR.SITE.OUTDOOR_PARKING",
        ["site.civil_defense"] = "HBR.COMMON.CIVIL_DEFENSE",
        ["site.structures"] = "HBR.SITE.STRUCTURES"
      };

    public static RuleActivationResult Compile(string modelFileType, IDictionary<string, bool> conditions)
    {
      var activated = new List<string>();
      var notApplicable = new List<string>();

      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.SiteModel, StringComparison.Ordinal))
      {
        activated.Add("HBR.SITE.BASE");
        activated.Add("HBR.SITE.TOTAL_LAND");
        activated.Add("HBR.SITE.NET_LAND");
        activated.Add("HBR.SITE.BUILDING_FOOTPRINT");
      }
      else if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.AboveGroundModel, StringComparison.Ordinal))
      {
        activated.Add("HBR.BUILDING.ABOVE.BASE");
        activated.Add("HBR.BUILDING.ABOVE.LEVELS");
        activated.Add("HBR.BUILDING.ABOVE.BODY");
      }
      else if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.UndergroundModel, StringComparison.Ordinal))
      {
        activated.Add("HBR.BUILDING.UNDERGROUND.BASE");
        activated.Add("HBR.BUILDING.UNDERGROUND.LEVELS");
        activated.Add("HBR.BUILDING.UNDERGROUND.BODY");
      }

      foreach (KeyValuePair<string, string> rule in ConditionRules)
      {
        bool applies = conditions != null && conditions.TryGetValue(rule.Key, out bool enabled) && enabled;
        if (applies) activated.Add(rule.Value);
        else notApplicable.Add(rule.Value);
      }

      foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
      {
        PlanningTargetRequirement requirement = PlanningTargetRequirementPolicy.GetRequirement(modelFileType, definition.MetricCode);
        string ruleId = "HBR.TARGET." + definition.MetricCode.Substring("planning.".Length).ToUpperInvariant();
        if (requirement == PlanningTargetRequirement.NotApplicable)
          notApplicable.Add(ruleId);
        else
          activated.Add(ruleId);
      }

      return new RuleActivationResult(activated, notApplicable);
    }
  }
}
