using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03SavedRoleAuditSnapshot
  {
    internal int StorageElementId { get; set; }
    internal string UniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string AuditUtc { get; set; } = string.Empty;
  }

  internal static class Stage03SavedRoleAuditPolicy
  {
    internal static IReadOnlyDictionary<string, string> Select(
      IEnumerable<Stage03SavedRoleAuditSnapshot> audits,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256)
    {
      var result = new Dictionary<string, string>(StringComparer.Ordinal);
      if (string.IsNullOrWhiteSpace(rulePackageId)
        || string.IsNullOrWhiteSpace(rulePackageVersion)
        || string.IsNullOrWhiteSpace(rulePackageSha256))
      {
        return result;
      }

      foreach (IGrouping<string, Stage03SavedRoleAuditSnapshot> group in
        (audits ?? Array.Empty<Stage03SavedRoleAuditSnapshot>())
          .Where(value => value != null)
          .Where(value => !string.IsNullOrWhiteSpace(value.UniqueId))
          .Where(value => !string.IsNullOrWhiteSpace(value.RoleId))
          .Where(value => string.Equals(
            value.RulePackageId,
            rulePackageId,
            StringComparison.Ordinal))
          .Where(value => string.Equals(
            value.RulePackageVersion,
            rulePackageVersion,
            StringComparison.Ordinal))
          .Where(value => string.Equals(
            value.RulePackageSha256,
            rulePackageSha256,
            StringComparison.OrdinalIgnoreCase))
          .GroupBy(value => value.UniqueId, StringComparer.Ordinal))
      {
        Stage03SavedRoleAuditSnapshot selected = group
          .OrderByDescending(value => value.AuditUtc, StringComparer.Ordinal)
          .ThenByDescending(value => value.StorageElementId)
          .First();
        result.Add(group.Key, selected.RoleId);
      }
      return result;
    }
  }
}
