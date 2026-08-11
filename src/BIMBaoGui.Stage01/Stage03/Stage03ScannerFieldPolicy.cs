using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03IfcOwnerStrategyDecision
  {
    internal Stage03IfcOwnerStrategyDecision(
      bool implemented,
      bool usesExportGuid,
      Stage03FieldStatus status,
      string message)
    {
      Implemented = implemented;
      UsesExportGuid = usesExportGuid;
      Status = status;
      Message = message ?? string.Empty;
    }

    internal bool Implemented { get; }
    internal bool UsesExportGuid { get; }
    internal Stage03FieldStatus Status { get; }
    internal string Message { get; }
  }

  internal static class Stage03IfcOwnerStrategyPolicy
  {
    internal static Stage03IfcOwnerStrategyDecision Evaluate(string strategy)
    {
      if (string.Equals(
        strategy,
        "BY_EXPORT_GUID",
        StringComparison.Ordinal))
      {
        return new Stage03IfcOwnerStrategyDecision(
          true,
          true,
          Stage03FieldStatus.Pass,
          string.Empty);
      }
      if (string.Equals(
        strategy,
        "SINGLE_ENTITY_BY_TYPE",
        StringComparison.Ordinal))
      {
        return new Stage03IfcOwnerStrategyDecision(
          true,
          false,
          Stage03FieldStatus.Pass,
          string.Empty);
      }
      return new Stage03IfcOwnerStrategyDecision(
        false,
        false,
        Stage03FieldStatus.RuleNotImplemented,
        "当前 IFC owner strategy 尚未完成可验证转译："
        + (strategy ?? string.Empty));
    }
  }

  internal sealed class Stage03RequirementApplicabilityDecision
  {
    internal Stage03RequirementApplicabilityDecision(
      bool active,
      string applicability,
      Stage03FieldStatus failureStatus,
      string message)
    {
      Active = active;
      Applicability = applicability ?? string.Empty;
      FailureStatus = failureStatus;
      Message = message ?? string.Empty;
    }

    internal bool Active { get; }
    internal string Applicability { get; }
    internal Stage03FieldStatus FailureStatus { get; }
    internal string Message { get; }
    internal IReadOnlyList<string> Messages => Message.Length == 0
      ? Array.Empty<string>()
      : new[] { Message };
  }

  internal static class Stage03RequirementApplicabilityPolicy
  {
    internal static Stage03RequirementApplicabilityDecision Evaluate(
      string requirementLevel,
      string conditionId,
      IReadOnlyDictionary<string, bool> projectConditions)
    {
      string level = (requirementLevel ?? string.Empty).Trim();
      if (string.Equals(level, "NOT_APPLICABLE", StringComparison.Ordinal))
        return Decision(false, "NOT_APPLICABLE", Stage03FieldStatus.NotApplicable);
      if (string.Equals(level, "CONDITIONAL", StringComparison.Ordinal))
      {
        string condition = (conditionId ?? string.Empty).Trim();
        bool active;
        if (condition.Length == 0
          || projectConditions == null
          || !projectConditions.TryGetValue(condition, out active))
        {
          return Decision(
            true,
            "UNKNOWN",
            Stage03FieldStatus.UnclassifiedRequirement,
            "CONDITIONAL 字段缺少明确的项目条件状态。");
        }
        return Decision(
          active,
          active ? "APPLICABLE" : "NOT_APPLICABLE",
          active ? Stage03FieldStatus.Pass : Stage03FieldStatus.NotApplicable);
      }
      if (string.Equals(level, "REQUIRED", StringComparison.Ordinal)
        || string.Equals(level, "OPTIONAL", StringComparison.Ordinal))
      {
        return Decision(true, "APPLICABLE", Stage03FieldStatus.Pass);
      }
      if (string.Equals(level, "UNCLASSIFIED", StringComparison.Ordinal))
      {
        return Decision(
          true,
          "APPLICABLE",
          Stage03FieldStatus.UnclassifiedRequirement,
          "字段 requirement.level 明确为 UNCLASSIFIED。");
      }
      return Decision(
        true,
        "UNKNOWN",
        Stage03FieldStatus.UnclassifiedRequirement,
        "字段 requirement.level 不在允许集合中。");
    }

    private static Stage03RequirementApplicabilityDecision Decision(
      bool active,
      string applicability,
      Stage03FieldStatus status,
      string message = "")
    {
      return new Stage03RequirementApplicabilityDecision(
        active,
        applicability,
        status,
        message);
    }
  }

  internal static class Stage03FieldStatusPolicy
  {
    internal static Stage03FieldStatus Resolve(
      bool active,
      string applicability,
      Stage03FieldStatus carrierStatus,
      Stage03FieldStatus parameterStatus,
      Stage03FieldStatus revitStatus,
      string requirementLevel)
    {
      if (!active) return Stage03FieldStatus.NotApplicable;
      if (carrierStatus != Stage03FieldStatus.Pass) return carrierStatus;
      if (revitStatus == Stage03FieldStatus.RuleNotImplemented
        || revitStatus == Stage03FieldStatus.IfcOwnerNotFound)
      {
        return revitStatus;
      }
      if (string.Equals(
        applicability,
        "UNKNOWN",
        StringComparison.Ordinal))
      {
        return Stage03FieldStatus.UnclassifiedRequirement;
      }
      if (string.Equals(
        requirementLevel,
        "UNCLASSIFIED",
        StringComparison.Ordinal))
      {
        return Stage03FieldStatus.UnclassifiedRequirement;
      }
      if (parameterStatus != Stage03FieldStatus.Pass) return parameterStatus;
      if (revitStatus != Stage03FieldStatus.Pass) return revitStatus;
      return Stage03FieldStatus.Pass;
    }
  }
}
