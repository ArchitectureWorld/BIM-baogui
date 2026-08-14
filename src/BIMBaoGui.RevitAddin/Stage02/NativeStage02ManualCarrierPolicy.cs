using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02ManualCarrierDefinition
  {
    internal string Category { get; set; } = string.Empty;
    internal IReadOnlyList<string> ElementKinds { get; set; } =
      Array.Empty<string>();
  }

  internal sealed class NativeStage02ManualRoleContract
  {
    internal string RoleId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal IReadOnlyList<string> ModelFileTypes { get; set; } =
      Array.Empty<string>();
    internal string ConditionId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02ManualCarrierDefinition> ManualCarriers
    {
      get;
      set;
    } = Array.Empty<NativeStage02ManualCarrierDefinition>();
    internal bool HasPropertyTemplate { get; set; }
    internal string IfcOwnerStrategy { get; set; } = string.Empty;
  }

  internal static class NativeStage02ManualCarrierCodes
  {
    internal const string RoleUnknown = "MANUAL_ROLE_UNKNOWN";
    internal const string ModelTypeNotAllowed =
      "MANUAL_ROLE_MODEL_TYPE_NOT_ALLOWED";
    internal const string ConditionInactive = "STAGE01_CONDITION_INACTIVE";
    internal const string CarrierNotAllowed =
      "MANUAL_ROLE_NOT_ALLOWED_FOR_CARRIER";
    internal const string TemplateUnavailable = "ROLE_TEMPLATE_UNAVAILABLE";
    internal const string OwnerStrategyUnavailable =
      "ROLE_OWNER_STRATEGY_UNAVAILABLE";
  }

  internal sealed class NativeStage02ManualCarrierDecision
  {
    private NativeStage02ManualCarrierDecision(
      bool accepted,
      string errorCode,
      string message,
      NativeStage02ManualRoleContract role)
    {
      Accepted = accepted;
      ErrorCode = errorCode ?? string.Empty;
      Message = message ?? string.Empty;
      Role = role;
    }

    internal bool Accepted { get; }
    internal string ErrorCode { get; }
    internal string Message { get; }
    internal NativeStage02ManualRoleContract Role { get; }

    internal static NativeStage02ManualCarrierDecision Success(
      NativeStage02ManualRoleContract role)
    {
      return new NativeStage02ManualCarrierDecision(
        true,
        string.Empty,
        string.Empty,
        role);
    }

    internal static NativeStage02ManualCarrierDecision Failure(
      string errorCode,
      string message)
    {
      return new NativeStage02ManualCarrierDecision(
        false,
        errorCode,
        message,
        null);
    }
  }

  internal static class NativeStage02ManualCarrierPolicy
  {
    internal static NativeStage02ManualCarrierDecision Evaluate(
      string roleId,
      string modelFileType,
      IReadOnlyDictionary<string, bool> conditions,
      NativeStage02ElementSnapshot element,
      IEnumerable<NativeStage02ManualRoleContract> roles)
    {
      string normalizedRoleId = Normalize(roleId);
      NativeStage02ManualRoleContract role = (roles
          ?? Array.Empty<NativeStage02ManualRoleContract>())
        .FirstOrDefault(value => value != null
          && string.Equals(
            Normalize(value.RoleId),
            normalizedRoleId,
            StringComparison.Ordinal));
      if (role == null)
      {
        return NativeStage02ManualCarrierDecision.Failure(
          NativeStage02ManualCarrierCodes.RoleUnknown,
          "规则库不存在手动语义角色：" + normalizedRoleId);
      }

      string normalizedModel = Normalize(modelFileType);
      if (!(role.ModelFileTypes ?? Array.Empty<string>())
        .Any(value => string.Equals(
          Normalize(value),
          normalizedModel,
          StringComparison.Ordinal)))
      {
        return NativeStage02ManualCarrierDecision.Failure(
          NativeStage02ManualCarrierCodes.ModelTypeNotAllowed,
          "当前模型文件类型不允许使用该语义角色。" );
      }

      string conditionId = Normalize(role.ConditionId);
      if (conditionId.Length > 0)
      {
        bool active;
        if (conditions == null
          || !conditions.TryGetValue(conditionId, out active)
          || !active)
        {
          return NativeStage02ManualCarrierDecision.Failure(
            NativeStage02ManualCarrierCodes.ConditionInactive,
            "Stage01 项目条件未启用：" + conditionId);
        }
      }

      if (element == null
        || !CarrierMatches(
          element.Category,
          element.ElementKind,
          role.ManualCarriers))
      {
        return NativeStage02ManualCarrierDecision.Failure(
          NativeStage02ManualCarrierCodes.CarrierNotAllowed,
          "当前 Revit 构件类别与 ElementKind 未被该语义角色批准。" );
      }

      if (!role.HasPropertyTemplate)
      {
        return NativeStage02ManualCarrierDecision.Failure(
          NativeStage02ManualCarrierCodes.TemplateUnavailable,
          "规则包没有提供该语义角色对应的属性模板。" );
      }

      if (Normalize(role.IfcOwnerStrategy).Length == 0)
      {
        return NativeStage02ManualCarrierDecision.Failure(
          NativeStage02ManualCarrierCodes.OwnerStrategyUnavailable,
          "规则包没有声明该语义角色的 H-IFC Owner 策略。" );
      }

      return NativeStage02ManualCarrierDecision.Success(role);
    }

    internal static IReadOnlyList<NativeStage02ManualCarrierDefinition>
      CanonicalizeCarriers(
        IEnumerable<NativeStage02ManualCarrierDefinition> carriers)
    {
      var output = new List<NativeStage02ManualCarrierDefinition>();
      foreach (IGrouping<string, NativeStage02ManualCarrierDefinition> group in
        (carriers ?? Array.Empty<NativeStage02ManualCarrierDefinition>())
          .Where(value => value != null)
          .GroupBy(value => Normalize(value.Category), StringComparer.Ordinal)
          .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        if (group.Key.Length == 0) continue;
        string[] kinds = group
          .SelectMany(value => value.ElementKinds ?? Array.Empty<string>())
          .Select(Normalize)
          .Where(value => value.Length > 0)
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        if (kinds.Length == 0) continue;
        output.Add(new NativeStage02ManualCarrierDefinition
        {
          Category = group.Key,
          ElementKinds = new ReadOnlyCollection<string>(kinds)
        });
      }
      return new ReadOnlyCollection<NativeStage02ManualCarrierDefinition>(
        output);
    }

    private static bool CarrierMatches(
      string category,
      string elementKind,
      IEnumerable<NativeStage02ManualCarrierDefinition> carriers)
    {
      string normalizedCategory = Normalize(category);
      string normalizedKind = Normalize(elementKind);
      return (carriers ?? Array.Empty<NativeStage02ManualCarrierDefinition>())
        .Where(value => value != null)
        .Any(value => string.Equals(
            Normalize(value.Category),
            normalizedCategory,
            StringComparison.Ordinal)
          && (value.ElementKinds ?? Array.Empty<string>()).Any(kind =>
            string.Equals(
              Normalize(kind),
              normalizedKind,
              StringComparison.Ordinal)));
    }

    private static string Normalize(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
