using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02CandidatePolicy
  {
    internal static IReadOnlyList<NativeStage02SemanticCandidate> Suggest(
      NativeStage02ElementSnapshot element,
      IReadOnlyList<NativeReportingSemanticRole> roles)
    {
      if (element == null) throw new ArgumentNullException(nameof(element));
      if (!element.IsModelElement) return Array.Empty<NativeStage02SemanticCandidate>();
      var candidates = new List<NativeStage02SemanticCandidate>();
      foreach (NativeReportingSemanticRole reportingRole in roles
        ?? Array.Empty<NativeReportingSemanticRole>())
      {
        if (reportingRole == null) continue;
        NativeCarrierRoleDefinition carrier;
        if (!NativeStage02RuleCatalog.Current.CarrierRolesById.TryGetValue(
          reportingRole.RoleId,
          out carrier))
          continue;
        bool approvedRuntimeCarrier =
          carrier.RevitCategories.Contains(element.Category, StringComparer.Ordinal)
          && carrier.AllowedElementKinds.Contains(
            element.ElementKind,
            StringComparer.Ordinal);
        NativeStage02ManualRoleContract manualRole;
        bool approvedManualCarrier =
          NativeStage02ManualRoleCatalog.Current.RolesById.TryGetValue(
            reportingRole.RoleId,
            out manualRole)
          && manualRole.ManualCarriers.Any(value =>
            value.Category == element.Category
            && value.ElementKinds.Contains(
              element.ElementKind,
              StringComparer.Ordinal));
        if (!approvedRuntimeCarrier && !approvedManualCarrier)
          continue;

        string[] values =
        {
          element.ElementName,
          element.FamilyName,
          element.TypeName
        };
        var approvedAliases = new HashSet<string>(
          reportingRole.CandidateAliases
            .Concat(carrier.NameAliases)
            .Concat(carrier.FamilyAliases)
            .Concat(carrier.TypeAliases)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NativeStage02RoleMatcher.NormalizeAlias),
          StringComparer.OrdinalIgnoreCase);
        string[] hits = values
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(value => new
          {
            Raw = value.Trim(),
            Normalized = NativeStage02RoleMatcher.NormalizeAlias(value)
          })
          .Where(value => approvedAliases.Contains(value.Normalized))
          .Select(value => "ALIAS:" + value.Raw)
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        if (hits.Length == 0) continue;
        candidates.Add(new NativeStage02SemanticCandidate
        {
          RoleId = reportingRole.RoleId,
          Confidence = hits.Length > 1 ? "HIGH" : "LOW",
          Evidence = hits
        });
      }
      return candidates
        .OrderBy(value => string.Equals(
          value.Confidence,
          "HIGH",
          StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(value => value.RoleId, StringComparer.Ordinal)
        .ToArray();
    }
  }
}
