using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02RoleMatchStatus
  {
    Matched,
    NameNotMatched,
    NameAmbiguous,
    AssignedRoleConflict,
    NotApplicable
  }

  internal sealed class NativeStage02RoleMatchResult
  {
    internal NativeStage02RoleMatchResult(
      NativeStage02RoleMatchStatus status,
      string roleId,
      string matchSource,
      IEnumerable<string> candidateRoleIds,
      string message)
    {
      Status = status;
      RoleId = roleId ?? string.Empty;
      MatchSource = matchSource ?? string.Empty;
      CandidateRoleIds = new ReadOnlyCollection<string>(
        (candidateRoleIds ?? Array.Empty<string>())
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray());
      Message = message ?? string.Empty;
    }

    internal NativeStage02RoleMatchStatus Status { get; }
    internal string RoleId { get; }
    internal string MatchSource { get; }
    internal IReadOnlyList<string> CandidateRoleIds { get; }
    internal string Message { get; }
  }

  internal static class NativeStage02RoleMatcher
  {
    private static readonly Regex ConsecutiveWhitespace = new Regex(
      @"\s+",
      RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> ProjectInformationRoleIds =
      new HashSet<string>(
        new[] { "BUILDING", "PROJECT", "SITE" },
        StringComparer.Ordinal);

    internal static NativeStage02RoleMatchResult Match(
      NativeStage02ElementSnapshot candidate,
      IEnumerable<NativeCarrierRoleDefinition> roles,
      string modelProfile)
    {
      if (candidate == null) return NotApplicable("候选元素为空。" );
      NativeCarrierRoleDefinition[] allRoles = (roles
          ?? Array.Empty<NativeCarrierRoleDefinition>())
        .Where(role => role != null)
        .GroupBy(role => role.RoleId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(role => role.RoleId, StringComparer.Ordinal)
        .ToArray();

      if (!string.IsNullOrWhiteSpace(candidate.AssignedRoleId))
      {
        string assignedRoleId = candidate.AssignedRoleId.Trim();
        NativeCarrierRoleDefinition manualRole = allRoles.FirstOrDefault(role =>
          string.Equals(role.RoleId, assignedRoleId, StringComparison.Ordinal)
          && role.ModelFileTypes.Contains(
            modelProfile ?? string.Empty,
            StringComparer.Ordinal)
          && string.Equals(
            role.SelectionPolicy,
            "MANUAL_SEMANTIC_ASSIGNMENT",
            StringComparison.Ordinal));
        if (manualRole != null)
          return Matched(manualRole.RoleId, "VERIFIED_MANUAL_ASSIGNMENT");
      }

      NativeCarrierRoleDefinition[] compatible = allRoles
        .Where(role => role.ModelFileTypes.Contains(
          modelProfile ?? string.Empty,
          StringComparer.Ordinal))
        .Where(role => role.RevitCategories.Contains(
          candidate.Category ?? string.Empty,
          StringComparer.Ordinal))
        .Where(role => role.AllowedElementKinds.Contains(
          candidate.ElementKind ?? string.Empty,
          StringComparer.Ordinal))
        .ToArray();
      if (compatible.Length == 0)
        return NotApplicable("模型类型、Revit 类别或 ElementKind 不适用。" );

      if (!string.IsNullOrWhiteSpace(candidate.AssignedRoleId))
      {
        NativeCarrierRoleDefinition assigned = compatible.FirstOrDefault(role =>
          string.Equals(
            role.RoleId,
            candidate.AssignedRoleId.Trim(),
            StringComparison.Ordinal));
        if (assigned == null)
        {
          return new NativeStage02RoleMatchResult(
            NativeStage02RoleMatchStatus.AssignedRoleConflict,
            string.Empty,
            string.Empty,
            compatible.Select(role => role.RoleId),
            "显式角色不属于当前元素的兼容角色集合。" );
        }
        return Matched(assigned.RoleId, "ASSIGNED_ROLE");
      }

      if (IsProjectInformationSingleEntitySet(candidate, compatible))
      {
        string[] roleIds = compatible
          .Select(role => role.RoleId)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        return new NativeStage02RoleMatchResult(
          NativeStage02RoleMatchStatus.Matched,
          string.Join("+", roleIds),
          "SINGLE_ENTITY_BY_TYPE",
          roleIds,
          string.Empty);
      }

      bool requiresAlias = compatible.Length > 1
        || string.Equals(candidate.Category, "OST_GenericModel", StringComparison.Ordinal)
        || compatible.Any(role => string.Equals(
          role.SelectionPolicy,
          "USER_SELECTED_EXPORTABLE_GENERIC_MODEL",
          StringComparison.Ordinal));
      if (!requiresAlias) return Matched(compatible[0].RoleId, "CATEGORY_KIND");

      NativeCarrierRoleDefinition[] aliasMatches = compatible
        .Where(role => MatchesExactAlias(role, candidate))
        .ToArray();
      if (aliasMatches.Length == 1)
        return Matched(aliasMatches[0].RoleId, "EXACT_ALIAS");
      if (aliasMatches.Length > 1)
      {
        return new NativeStage02RoleMatchResult(
          NativeStage02RoleMatchStatus.NameAmbiguous,
          string.Empty,
          string.Empty,
          aliasMatches.Select(role => role.RoleId),
          "元素名称、族名或类型名同时精确匹配多个角色。" );
      }
      return new NativeStage02RoleMatchResult(
        NativeStage02RoleMatchStatus.NameNotMatched,
        string.Empty,
        string.Empty,
        compatible.Select(role => role.RoleId),
        "共享类别必须通过数据库批准的精确别名或显式角色识别。" );
    }

    internal static string NormalizeAlias(string value)
    {
      string normalized = (value ?? string.Empty)
        .Normalize(NormalizationForm.FormKC)
        .Trim();
      return ConsecutiveWhitespace.Replace(normalized, " ");
    }

    private static bool IsProjectInformationSingleEntitySet(
      NativeStage02ElementSnapshot candidate,
      IReadOnlyCollection<NativeCarrierRoleDefinition> roles)
    {
      return string.Equals(candidate.Category, "OST_ProjectInformation", StringComparison.Ordinal)
        && string.Equals(candidate.ElementKind, "ProjectInformation", StringComparison.Ordinal)
        && roles.Count > 0
        && roles.All(role => ProjectInformationRoleIds.Contains(role.RoleId)
          && string.Equals(role.IfcOwnerStrategy, "SINGLE_ENTITY_BY_TYPE", StringComparison.Ordinal));
    }

    private static bool MatchesExactAlias(
      NativeCarrierRoleDefinition role,
      NativeStage02ElementSnapshot candidate)
    {
      string[] values =
      {
        NormalizeAlias(candidate.ElementName),
        NormalizeAlias(candidate.FamilyName),
        NormalizeAlias(candidate.TypeName)
      };
      return role.NameAliases
        .Concat(role.FamilyAliases)
        .Concat(role.TypeAliases)
        .Select(NormalizeAlias)
        .Where(value => value.Length > 0)
        .Any(alias => values.Any(value => string.Equals(
          alias,
          value,
          StringComparison.OrdinalIgnoreCase)));
    }

    private static NativeStage02RoleMatchResult Matched(string roleId, string source)
    {
      return new NativeStage02RoleMatchResult(
        NativeStage02RoleMatchStatus.Matched,
        roleId,
        source,
        new[] { roleId },
        string.Empty);
    }

    private static NativeStage02RoleMatchResult NotApplicable(string message)
    {
      return new NativeStage02RoleMatchResult(
        NativeStage02RoleMatchStatus.NotApplicable,
        string.Empty,
        string.Empty,
        Array.Empty<string>(),
        message);
    }
  }
}
