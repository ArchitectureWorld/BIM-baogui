using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03CarrierCandidateSnapshot
  {
    internal string UniqueId { get; set; } = string.Empty;
    internal string Category { get; set; } = string.Empty;
    internal string ElementKind { get; set; } = string.Empty;
    internal string ElementName { get; set; } = string.Empty;
    internal string FamilyName { get; set; } = string.Empty;
    internal string TypeName { get; set; } = string.Empty;
    internal string SavedRoleId { get; set; } = string.Empty;
  }

  internal sealed class Stage03CarrierMatchDecision
  {
    internal Stage03CarrierMatchDecision(
      bool accepted,
      Stage03FieldStatus status,
      string matchSource,
      string message)
    {
      Accepted = accepted;
      Status = status;
      MatchSource = matchSource ?? string.Empty;
      Message = message ?? string.Empty;
    }

    internal bool Accepted { get; }
    internal Stage03FieldStatus Status { get; }
    internal string MatchSource { get; }
    internal string Message { get; }
  }

  internal static class Stage03CarrierMatchPolicy
  {
    private const string UserSelectedPolicy =
      "USER_SELECTED_EXPORTABLE_GENERIC_MODEL";

    internal static Stage03CarrierMatchDecision Evaluate(
      HbrCarrierRole role,
      Stage03CarrierCandidateSnapshot candidate,
      IEnumerable<HbrCarrierRole> activeRoles)
    {
      if (role == null) throw new ArgumentNullException(nameof(role));
      if (candidate == null)
        throw new ArgumentNullException(nameof(candidate));
      if (!role.RevitCategories.Contains(
        candidate.Category,
        StringComparer.Ordinal)
        || !role.AllowedElementKinds.Contains(
          candidate.ElementKind,
          StringComparer.Ordinal))
      {
        return Rejected(
          Stage03FieldStatus.CarrierCategoryMismatch,
          "元素类别或 ElementKind 不符合载体角色合同。");
      }
      if (string.Equals(
        role.SelectionPolicy,
        UserSelectedPolicy,
        StringComparison.Ordinal))
      {
        if (!string.Equals(
          candidate.SavedRoleId,
          role.RoleId,
          StringComparison.Ordinal))
        {
          return Rejected(
            Stage03FieldStatus.CarrierNameMismatch,
            "用户选择型载体必须具有 Stage02 保存的匹配角色。");
        }
        return Accepted("SAVED_ROLE");
      }
      if (IsProjectInformationSingleEntityRole(role, candidate))
        return Accepted("SINGLE_ENTITY_BY_TYPE");

      HbrCarrierRole[] competingRoles = (activeRoles
          ?? Array.Empty<HbrCarrierRole>())
        .Where(value => value != null)
        .Where(value => value.RevitCategories.Contains(
          candidate.Category,
          StringComparer.Ordinal))
        .Where(value => value.AllowedElementKinds.Contains(
          candidate.ElementKind,
          StringComparer.Ordinal))
        .GroupBy(value => value.RoleId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(value => value.RoleId, StringComparer.Ordinal)
        .ToArray();
      if (competingRoles.Length <= 1) return Accepted("CATEGORY");

      if (!string.IsNullOrWhiteSpace(candidate.SavedRoleId))
      {
        return string.Equals(
          candidate.SavedRoleId,
          role.RoleId,
          StringComparison.Ordinal)
          ? Accepted("SAVED_ROLE")
          : Rejected(
            Stage03FieldStatus.CarrierNameMismatch,
            "Stage02 保存角色指向同类别的另一个载体角色。");
      }

      HbrCarrierRole[] aliasMatches = competingRoles
        .Where(value => MatchesAlias(value, candidate))
        .ToArray();
      if (aliasMatches.Length == 1)
      {
        return string.Equals(
          aliasMatches[0].RoleId,
          role.RoleId,
          StringComparison.Ordinal)
          ? Accepted("NAME_ALIAS")
          : Rejected(
            Stage03FieldStatus.CarrierNameMismatch,
            "元素名称、族或类型别名匹配同类别的另一个载体角色。");
      }
      if (aliasMatches.Length == 0)
      {
        return Rejected(
          Stage03FieldStatus.CarrierNameMismatch,
          "共享类别缺少可唯一识别载体角色的名称、族或类型别名。");
      }
      return Rejected(
        Stage03FieldStatus.AmbiguousCarrier,
        "共享类别的名称、族或类型别名同时匹配多个载体角色。");
    }

    private static bool IsProjectInformationSingleEntityRole(
      HbrCarrierRole role,
      Stage03CarrierCandidateSnapshot candidate)
    {
      return string.Equals(
          candidate.Category,
          "OST_ProjectInformation",
          StringComparison.Ordinal)
        && string.Equals(
          candidate.ElementKind,
          "ProjectInformation",
          StringComparison.Ordinal)
        && string.Equals(
          role.IfcOwnerStrategy,
          "SINGLE_ENTITY_BY_TYPE",
          StringComparison.Ordinal)
        && (string.Equals(role.RoleId, "PROJECT", StringComparison.Ordinal)
          || string.Equals(role.RoleId, "SITE", StringComparison.Ordinal)
          || string.Equals(
            role.RoleId,
            "BUILDING",
            StringComparison.Ordinal));
    }

    private static bool MatchesAlias(
      HbrCarrierRole role,
      Stage03CarrierCandidateSnapshot candidate)
    {
      return MatchesAlias(role.NameAliases, candidate.ElementName)
        || MatchesAlias(role.FamilyAliases, candidate.FamilyName)
        || MatchesAlias(role.TypeAliases, candidate.TypeName);
    }

    private static bool MatchesAlias(
      IEnumerable<string> aliases,
      string value)
    {
      string normalized = NormalizeAlias(value);
      return normalized.Length > 0 && (aliases ?? Array.Empty<string>())
        .Any(alias => string.Equals(
          NormalizeAlias(alias),
          normalized,
          StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAlias(string value)
    {
      return string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Normalize(NormalizationForm.FormKC);
    }

    private static Stage03CarrierMatchDecision Accepted(string source)
    {
      return new Stage03CarrierMatchDecision(
        true,
        Stage03FieldStatus.Pass,
        source,
        string.Empty);
    }

    private static Stage03CarrierMatchDecision Rejected(
      Stage03FieldStatus status,
      string message)
    {
      return new Stage03CarrierMatchDecision(
        false,
        status,
        string.Empty,
        message);
    }
  }

  internal static class Stage03CarrierScanAggregationPolicy
  {
    internal static bool ShouldReportAlongsideAccepted(
      Stage03CarrierMatchDecision decision)
    {
      return decision != null
        && !decision.Accepted
        && decision.Status == Stage03FieldStatus.AmbiguousCarrier;
    }
  }
}
