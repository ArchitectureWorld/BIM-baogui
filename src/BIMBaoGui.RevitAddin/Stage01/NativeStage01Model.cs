using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01Keys
  {
    internal const string SubitemCode = "HBR|FileIdentity|SubitemCode";
    internal const string SubitemName = "HBR|FileIdentity|SubitemName";
    internal const string ModelFileType = "HBR|FileIdentity|ModelFileType";
    internal const string ModelScope = "HBR|FileIdentity|ModelScope";
    internal const string FileGuid = "HBR|FileIdentity|FileGuid";
    internal const string WorkflowVersion = "HBR|Workflow|Version";
    internal const string InitializationStatus =
      "HBR|Workflow|InitializationStatus";
    internal const string TrueNorthAngle =
      "HBR|SpatialReference|TrueNorthAngle";
    internal const string LengthUnit = "HBR|ProjectUnits|Length";
    internal const string AreaUnit = "HBR|ProjectUnits|Area";
    internal const string AngleUnit = "HBR|ProjectUnits|Angle";

    internal const string ProjectNumber =
      "IfcProject|Pset_申报信息属性集|项目编号";
    internal const string ProjectName =
      "IfcProject|Pset_申报信息属性集|项目名称";
    internal const string BaseX =
      "IfcProject|Pset_申报信息属性集|基点坐标X";
    internal const string BaseY =
      "IfcProject|Pset_申报信息属性集|基点坐标Y";
    internal const string BaseElevation =
      "IfcProject|Pset_申报信息属性集|基点高程";
    internal const string CoordinateSystem =
      "IfcProject|Pset_申报信息属性集|坐标系名称";
    internal const string ElevationSystem =
      "IfcProject|Pset_申报信息属性集|高程系名称";
    internal const string Longitude =
      "IfcProject|Pset_申报信息属性集|经度";
    internal const string Latitude =
      "IfcProject|Pset_申报信息属性集|纬度";
  }

  internal sealed class NativePlanningTargetValue
  {
    internal NativePlanningTargetValue(
      string @operator,
      string value1,
      string value2,
      string unit,
      string source,
      string mvdText)
    {
      Operator = @operator ?? string.Empty;
      Value1 = value1 ?? string.Empty;
      Value2 = value2 ?? string.Empty;
      Unit = unit ?? string.Empty;
      Source = source ?? string.Empty;
      MvdText = mvdText ?? string.Empty;
    }

    internal string Operator { get; }
    internal string Value1 { get; }
    internal string Value2 { get; }
    internal string Unit { get; }
    internal string Source { get; }
    internal string MvdText { get; }
  }

  internal sealed class NativeStage01Model
  {
    internal NativeStage01Model()
    {
      Values = new Dictionary<string, string>(StringComparer.Ordinal);
      Conditions = new Dictionary<string, bool>(StringComparer.Ordinal);
      PlanningTargets =
        new Dictionary<string, NativePlanningTargetValue>(StringComparer.Ordinal);
      Organizations = new List<Dictionary<string, string>>
      {
        new Dictionary<string, string>(StringComparer.Ordinal)
      };
    }

    internal Dictionary<string, string> Values { get; }
    internal Dictionary<string, bool> Conditions { get; }
    internal Dictionary<string, NativePlanningTargetValue> PlanningTargets
    {
      get;
    }
    internal List<Dictionary<string, string>> Organizations { get; }
    internal bool ConfirmBlankProject { get; set; }
    internal bool AllowReinitialize { get; set; }
    internal string ActiveGroup { get; set; } =
      "01_文件与项目身份";

    internal string GetValue(string key)
    {
      return key != null && Values.TryGetValue(key, out string value)
        ? value ?? string.Empty
        : string.Empty;
    }

    internal void SetValue(string key, string value)
    {
      if (string.IsNullOrWhiteSpace(key))
        throw new ArgumentException("Stage01 value key 不能为空。", nameof(key));
      Values[key] = value ?? string.Empty;
    }

    internal bool GetCondition(string conditionId)
    {
      return conditionId != null
        && Conditions.TryGetValue(conditionId, out bool value)
        && value;
    }

    internal void SetCondition(string conditionId, bool value)
    {
      if (string.IsNullOrWhiteSpace(conditionId))
        throw new ArgumentException(
          "Stage01 conditionId 不能为空。",
          nameof(conditionId));
      Conditions[conditionId] = value;
    }

    internal string GetOrganizationValue(int index, string key)
    {
      if (index < 0 || index >= Organizations.Count || key == null)
        return string.Empty;
      return Organizations[index].TryGetValue(key, out string value)
        ? value ?? string.Empty
        : string.Empty;
    }

    internal void SetOrganizationValue(int index, string key, string value)
    {
      if (index < 0)
        throw new ArgumentOutOfRangeException(nameof(index));
      if (string.IsNullOrWhiteSpace(key))
        throw new ArgumentException(
          "Organization field key 不能为空。",
          nameof(key));
      while (Organizations.Count <= index)
        Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
      Organizations[index][key] = value ?? string.Empty;
    }

    internal NativeStage01Model Clone()
    {
      var clone = new NativeStage01Model
      {
        ConfirmBlankProject = ConfirmBlankProject,
        AllowReinitialize = AllowReinitialize,
        ActiveGroup = ActiveGroup
      };
      clone.Values.Clear();
      foreach (KeyValuePair<string, string> pair in Values)
        clone.Values.Add(pair.Key, pair.Value);
      clone.Conditions.Clear();
      foreach (KeyValuePair<string, bool> pair in Conditions)
        clone.Conditions.Add(pair.Key, pair.Value);
      clone.PlanningTargets.Clear();
      foreach (KeyValuePair<string, NativePlanningTargetValue> pair in
        PlanningTargets)
      {
        clone.PlanningTargets.Add(pair.Key, pair.Value);
      }
      clone.Organizations.Clear();
      foreach (Dictionary<string, string> organization in Organizations)
      {
        clone.Organizations.Add(organization.ToDictionary(
          pair => pair.Key,
          pair => pair.Value,
          StringComparer.Ordinal));
      }
      return clone;
    }
  }
}
