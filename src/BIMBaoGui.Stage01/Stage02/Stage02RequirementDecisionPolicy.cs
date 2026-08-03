using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02RequirementDecision
  {
    internal bool Success { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal string Applicability { get; set; } = string.Empty;
    internal string ValueActionOverride { get; set; } = string.Empty;
    internal IReadOnlyList<Stage02Blocker> Blockers { get; set; } =
      Array.Empty<Stage02Blocker>();
  }

  internal static class Stage02RequirementDecisionPolicy
  {
    internal static Stage02RequirementDecision Resolve(
      string propertyId,
      string requirementLevel,
      string conditionId,
      IEnumerable<string> knownConditionIds,
      IReadOnlyDictionary<string, bool> projectConditions)
    {
      string level = (requirementLevel ?? string.Empty).Trim();
      string condition = (conditionId ?? string.Empty).Trim();
      bool conditional = string.Equals(
        level,
        "CONDITIONAL",
        StringComparison.Ordinal);
      if (!conditional && condition.Length > 0)
      {
        return Invalid(
          Stage02Codes.InvalidRequirementContract,
          "非 CONDITIONAL 属性不能声明 conditionId。");
      }
      if (!conditional)
      {
        switch (level)
        {
          case "REQUIRED":
          case "OPTIONAL":
          case "UNCLASSIFIED":
            return Valid("APPLICABLE", string.Empty, null);
          case "NOT_APPLICABLE":
            return Valid("NOT_APPLICABLE", "NO_WRITE", null);
          default:
            return Invalid(
              Stage02Codes.InvalidRequirementContract,
              "属性 requirement.level 无效。");
        }
      }
      if (condition.Length == 0)
      {
        return Invalid(
          Stage02Codes.InvalidRequirementContract,
          "CONDITIONAL 属性缺少 conditionId。");
      }
      if (!(knownConditionIds ?? Array.Empty<string>()).Contains(
        condition,
        StringComparer.Ordinal))
      {
        return Invalid(
          Stage02Codes.UnknownCondition,
          "CONDITIONAL 属性引用了未知 conditionId。");
      }
      bool active;
      if (projectConditions == null
        || !projectConditions.TryGetValue(condition, out active))
      {
        return Valid(
          "UNKNOWN",
          "NO_WRITE",
          new Stage02Blocker(
            Stage02Codes.ConditionStateMissing,
            "项目条件 " + condition + " 缺少 true/false 状态；属性 "
            + (propertyId ?? string.Empty) + " 禁止写入。"));
      }
      return active
        ? Valid("APPLICABLE", string.Empty, null)
        : Valid("NOT_APPLICABLE", "NO_WRITE", null);
    }

    private static Stage02RequirementDecision Valid(
      string applicability,
      string action,
      Stage02Blocker blocker)
    {
      return new Stage02RequirementDecision
      {
        Success = true,
        Applicability = applicability,
        ValueActionOverride = action,
        Blockers = new ReadOnlyCollection<Stage02Blocker>(
          blocker == null
            ? Array.Empty<Stage02Blocker>()
            : new[] { blocker })
      };
    }

    private static Stage02RequirementDecision Invalid(
      string code,
      string message)
    {
      return new Stage02RequirementDecision
      {
        Success = false,
        ErrorCode = code ?? string.Empty,
        Message = message ?? string.Empty
      };
    }
  }
}
