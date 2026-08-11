using System;
using System.Collections.Generic;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02ConditionStatus
  {
    Applicable,
    NotApplicable,
    Missing
  }

  internal sealed class NativeStage02ConditionDecision
  {
    internal NativeStage02ConditionStatus Status { get; set; }
    internal string Message { get; set; } = string.Empty;
  }

  internal static class NativeStage02ConditionPolicy
  {
    internal static NativeStage02ConditionDecision Evaluate(
      string conditionId,
      IReadOnlyDictionary<string, bool> conditions)
    {
      if (string.IsNullOrWhiteSpace(conditionId))
      {
        return new NativeStage02ConditionDecision
        {
          Status = NativeStage02ConditionStatus.Applicable
        };
      }
      if (conditions == null
        || !conditions.TryGetValue(conditionId, out bool active))
      {
        return new NativeStage02ConditionDecision
        {
          Status = NativeStage02ConditionStatus.Missing,
          Message = "CONDITION_MISSING：项目条件键缺失，不得静默按 false 处理。"
        };
      }
      return new NativeStage02ConditionDecision
      {
        Status = active
          ? NativeStage02ConditionStatus.Applicable
          : NativeStage02ConditionStatus.NotApplicable,
        Message = active
          ? string.Empty
          : "CONDITION_INACTIVE：当前项目条件未启用。"
      };
    }
  }
}
