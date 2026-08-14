using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02RoleAssignmentPolicy
  {
    internal const string AutoOverrideRoleId = "__AUTO__";

    internal static NativeStage02RoleAssignmentDecision Resolve(
      NativeStage02ScopeMode scopeMode,
      NativeStage02IdentificationMode identificationMode,
      IEnumerable<string> selectedUniqueIds,
      string bulkRoleId,
      IEnumerable<NativeStage02RoleOverride> roleOverrides)
    {
      string[] selected = CanonicalizeIds(selectedUniqueIds);
      string normalizedBulkRole = Normalize(bulkRoleId);
      NativeStage02RoleOverride[] overrides = (roleOverrides
          ?? Array.Empty<NativeStage02RoleOverride>())
        .Where(value => value != null)
        .Select(value => new NativeStage02RoleOverride
        {
          ElementUniqueId = Normalize(value.ElementUniqueId),
          RoleId = Normalize(value.RoleId)
        })
        .Where(value => value.ElementUniqueId.Length > 0
          || value.RoleId.Length > 0)
        .ToArray();

      bool hasManualInput = normalizedBulkRole.Length > 0
        || overrides.Length > 0;
      if (scopeMode == NativeStage02ScopeMode.FullModel && hasManualInput)
      {
        return NativeStage02RoleAssignmentDecision.Failure(
          NativeStage02RoleAssignmentCodes.ScopeInputConflict,
          "全模型模式不得携带当前选择专用的批量角色或逐项改写。",
          selected);
      }

      if (identificationMode == NativeStage02IdentificationMode.Automatic)
      {
        if (hasManualInput)
        {
          return NativeStage02RoleAssignmentDecision.Failure(
            NativeStage02RoleAssignmentCodes.AutomaticModeInputConflict,
            "自动识别模式不得携带手动语义角色。",
            selected);
        }
        return NativeStage02RoleAssignmentDecision.Success(
          selected,
          Array.Empty<NativeStage02ResolvedAssignment>());
      }

      if (identificationMode != NativeStage02IdentificationMode.Manual)
      {
        return NativeStage02RoleAssignmentDecision.Failure(
          NativeStage02RoleAssignmentCodes.ManualRoleRequired,
          "未知识别方式，无法建立语义角色分配。",
          selected);
      }
      if (!hasManualInput)
      {
        return NativeStage02RoleAssignmentDecision.Failure(
          NativeStage02RoleAssignmentCodes.ManualRoleRequired,
          "手动指定模式必须提供批量角色或至少一个逐项角色。",
          selected);
      }
      if (string.Equals(
        normalizedBulkRole,
        AutoOverrideRoleId,
        StringComparison.Ordinal))
      {
        return NativeStage02RoleAssignmentDecision.Failure(
          NativeStage02RoleAssignmentCodes.RoleIdRequired,
          "批量语义类型不能使用“恢复自动识别”；该动作仅允许逐构件设置。",
          selected);
      }

      var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);
      var overrideByElement = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (NativeStage02RoleOverride item in overrides)
      {
        if (item.ElementUniqueId.Length == 0 || item.RoleId.Length == 0)
        {
          return NativeStage02RoleAssignmentDecision.Failure(
            NativeStage02RoleAssignmentCodes.RoleIdRequired,
            "逐项角色必须同时包含 ElementUniqueId 与 RoleId。",
            selected);
        }
        if (!selectedSet.Contains(item.ElementUniqueId))
        {
          return NativeStage02RoleAssignmentDecision.Failure(
            NativeStage02RoleAssignmentCodes.OverrideElementNotSelected,
            "逐项改写指向了当前选择范围之外的构件：" + item.ElementUniqueId,
            selected);
        }
        string existing;
        if (overrideByElement.TryGetValue(item.ElementUniqueId, out existing))
        {
          if (!string.Equals(existing, item.RoleId, StringComparison.Ordinal))
          {
            return NativeStage02RoleAssignmentDecision.Failure(
              NativeStage02RoleAssignmentCodes.RoleAssignmentConflict,
              "同一构件存在互相冲突的手动角色：" + item.ElementUniqueId,
              selected);
          }
          continue;
        }
        overrideByElement[item.ElementUniqueId] = item.RoleId;
      }

      var assignments = new List<NativeStage02ResolvedAssignment>();
      foreach (string uniqueId in selected)
      {
        string overrideRole;
        if (overrideByElement.TryGetValue(uniqueId, out overrideRole))
        {
          bool restoreAutomatic = string.Equals(
            overrideRole,
            AutoOverrideRoleId,
            StringComparison.Ordinal);
          assignments.Add(new NativeStage02ResolvedAssignment
          {
            ElementUniqueId = uniqueId,
            RoleId = restoreAutomatic ? string.Empty : overrideRole,
            AssignmentMode = restoreAutomatic
              ? NativeStage02AssignmentMode.Auto
              : NativeStage02AssignmentMode.Manual,
            Source = restoreAutomatic ? "OverrideAuto" : "Override"
          });
          continue;
        }
        if (normalizedBulkRole.Length > 0)
        {
          assignments.Add(new NativeStage02ResolvedAssignment
          {
            ElementUniqueId = uniqueId,
            RoleId = normalizedBulkRole,
            AssignmentMode = NativeStage02AssignmentMode.Manual,
            Source = "Bulk"
          });
        }
      }

      return NativeStage02RoleAssignmentDecision.Success(
        selected,
        assignments.OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal));
    }

    private static string[] CanonicalizeIds(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Select(Normalize)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static string Normalize(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
