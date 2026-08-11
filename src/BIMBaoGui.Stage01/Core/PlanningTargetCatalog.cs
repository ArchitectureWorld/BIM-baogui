using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Core
{
  internal sealed class PlanningTargetDefinition
  {
    public PlanningTargetDefinition(
      string metricCode,
      string label,
      string mvdFieldKey,
      PlanningTargetUnit unit,
      PlanningTargetOperator defaultOperator,
      string example)
    {
      MetricCode = metricCode ?? string.Empty;
      Label = label ?? string.Empty;
      MvdFieldKey = mvdFieldKey ?? string.Empty;
      Unit = unit;
      DefaultOperator = defaultOperator;
      Example = example ?? string.Empty;
    }

    public string MetricCode { get; }
    public string Label { get; }
    public string MvdFieldKey { get; }
    public PlanningTargetUnit Unit { get; }
    public PlanningTargetOperator DefaultOperator { get; }
    public string Example { get; }
  }

  internal static class PlanningTargetCatalog
  {
    public const string BuildingDensityCode = "planning.building_density";
    public const string FloorAreaRatioCode = "planning.floor_area_ratio";
    public const string GreenRateCode = "planning.green_rate";

    private const string Prefix = "IfcProject|Pset_项目控制指标信息属性集|";

    private static readonly IReadOnlyList<PlanningTargetDefinition> Definitions =
      new[]
      {
        new PlanningTargetDefinition(
          BuildingDensityCode,
          "建筑密度",
          Prefix + "建筑密度",
          PlanningTargetUnit.Percent,
          PlanningTargetOperator.LessOrEqual,
          "≤30%"),
        new PlanningTargetDefinition(
          FloorAreaRatioCode,
          "容积率",
          Prefix + "容积率",
          PlanningTargetUnit.Ratio,
          PlanningTargetOperator.LessOrEqual,
          "≤2.00"),
        new PlanningTargetDefinition(
          GreenRateCode,
          "绿地率",
          Prefix + "绿地率",
          PlanningTargetUnit.Percent,
          PlanningTargetOperator.GreaterOrEqual,
          "≥35%")
      };

    public static IReadOnlyList<PlanningTargetDefinition> All => Definitions;

    public static PlanningTargetDefinition Get(string metricCode)
    {
      return Definitions.FirstOrDefault(x =>
        string.Equals(x.MetricCode, metricCode, StringComparison.Ordinal));
    }

    public static PlanningTargetDefinition GetByMvdFieldKey(string fieldKey)
    {
      return Definitions.FirstOrDefault(x =>
        string.Equals(x.MvdFieldKey, fieldKey, StringComparison.Ordinal));
    }

    public static bool IsManagedMvdField(string fieldKey)
    {
      return GetByMvdFieldKey(fieldKey) != null;
    }
  }
}
