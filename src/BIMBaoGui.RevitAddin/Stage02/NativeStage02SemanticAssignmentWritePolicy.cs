using System;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02SemanticAssignmentReadbackDecision
  {
    internal bool Success { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class NativeStage02SemanticAssignmentWritePolicy
  {
    internal const string ReadbackFailed =
      "SEMANTIC_ASSIGNMENT_READBACK_FAILED";

    internal static NativeStage02SemanticAssignmentPayload Apply(
      NativeStage02SemanticAssignmentPayload committed,
      NativeStage02ElementPlan plan)
    {
      if (committed == null) throw new ArgumentNullException(nameof(committed));
      if (plan?.Element == null) throw new ArgumentNullException(nameof(plan));

      NativeStage02SemanticAssignmentPayload normalized =
        NativeStage02SemanticAssignmentCanonicalizer.Normalize(committed);
      if (string.Equals(
        plan.AssignmentAction,
        NativeStage02AssignmentActions.SaveManualAssignment,
        StringComparison.Ordinal))
      {
        if (plan.AssignmentMode != NativeStage02AssignmentMode.Manual
          || string.IsNullOrWhiteSpace(plan.EffectiveRoleId))
          throw new InvalidOperationException(
            "SEMANTIC_ASSIGNMENT_SAVE_PLAN_INVALID");
        return NativeStage02SemanticAssignmentCanonicalizer.Upsert(
          normalized,
          new NativeStage02SemanticAssignmentRecord
          {
            ElementUniqueId = plan.Element.UniqueId,
            RoleId = plan.EffectiveRoleId,
            AssignmentMode = NativeStage02AssignmentMode.Manual,
            CarrierCategory = plan.Element.Category,
            CarrierElementKind = plan.Element.ElementKind
          });
      }
      if (string.Equals(
        plan.AssignmentAction,
        NativeStage02AssignmentActions.RemoveManualAssignment,
        StringComparison.Ordinal))
      {
        return NativeStage02SemanticAssignmentCanonicalizer.Remove(
          normalized,
          plan.Element.UniqueId);
      }
      if (string.Equals(
          plan.AssignmentAction,
          NativeStage02AssignmentActions.KeepManualAssignment,
          StringComparison.Ordinal)
        || string.Equals(
          plan.AssignmentAction,
          NativeStage02AssignmentActions.None,
          StringComparison.Ordinal))
        return normalized;

      throw new InvalidOperationException(
        "SEMANTIC_ASSIGNMENT_ACTION_UNSUPPORTED:" + plan.AssignmentAction);
    }

    internal static NativeStage02SemanticAssignmentReadbackDecision Verify(
      NativeStage02SemanticAssignmentPayload actual,
      NativeStage02ElementPlan plan)
    {
      if (actual == null) return Failure("Assignment 回读 Payload 缺失。");
      if (plan?.Element == null) return Failure("Assignment 回读计划缺失。");

      NativeStage02SemanticAssignmentPayload normalized;
      try
      {
        normalized = NativeStage02SemanticAssignmentCanonicalizer.Normalize(actual);
      }
      catch (Exception exception)
      {
        return Failure("Assignment 回读 Payload 非法：" + exception.Message);
      }

      NativeStage02SemanticAssignmentRecord record = normalized.Assignments
        .SingleOrDefault(value => string.Equals(
          value.ElementUniqueId,
          plan.Element.UniqueId,
          StringComparison.Ordinal));
      if (string.Equals(
        plan.AssignmentAction,
        NativeStage02AssignmentActions.RemoveManualAssignment,
        StringComparison.Ordinal))
      {
        return record == null
          ? Success()
          : Failure("恢复自动识别后仍存在人工 Assignment 记录。");
      }
      if (string.Equals(
          plan.AssignmentAction,
          NativeStage02AssignmentActions.SaveManualAssignment,
          StringComparison.Ordinal)
        || string.Equals(
          plan.AssignmentAction,
          NativeStage02AssignmentActions.KeepManualAssignment,
          StringComparison.Ordinal))
      {
        if (record == null) return Failure("人工 Assignment 记录缺失。");
        if (!string.Equals(
            record.RoleId,
            plan.EffectiveRoleId,
            StringComparison.Ordinal)
          || record.AssignmentMode != NativeStage02AssignmentMode.Manual
          || !string.Equals(
            record.CarrierCategory,
            plan.Element.Category,
            StringComparison.Ordinal)
          || !string.Equals(
            record.CarrierElementKind,
            plan.Element.ElementKind,
            StringComparison.Ordinal))
          return Failure("人工 Assignment 记录与预览计划不一致。");
      }
      return Success();
    }

    private static NativeStage02SemanticAssignmentReadbackDecision Success()
    {
      return new NativeStage02SemanticAssignmentReadbackDecision
      {
        Success = true
      };
    }

    private static NativeStage02SemanticAssignmentReadbackDecision Failure(
      string message)
    {
      return new NativeStage02SemanticAssignmentReadbackDecision
      {
        Success = false,
        ErrorCode = ReadbackFailed,
        Message = message ?? string.Empty
      };
    }
  }
}
