using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02WorkbenchRequestPolicy
  {
    internal static NativeStage02PreviewRequest Build(
      NativeStage02ScopeMode scope,
      NativeStage02IdentificationMode identificationMode,
      string bulkRoleId,
      IReadOnlyDictionary<string, string> overrides,
      IReadOnlyList<NativeStage02RoleConfirmation> confirmations = null)
    {
      bool manual = scope != NativeStage02ScopeMode.FullModel
        && identificationMode == NativeStage02IdentificationMode.Manual;
      NativeStage02RoleOverride[] canonicalOverrides = manual
        ? (overrides ?? new Dictionary<string, string>(StringComparer.Ordinal))
          .Select(value => new NativeStage02RoleOverride
          {
            ElementUniqueId = Clean(value.Key),
            RoleId = Clean(value.Value)
          })
          .Where(value => value.ElementUniqueId.Length > 0
            && value.RoleId.Length > 0)
          .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
          .ThenBy(value => value.RoleId, StringComparer.Ordinal)
          .ToArray()
        : Array.Empty<NativeStage02RoleOverride>();
      return new NativeStage02PreviewRequest
      {
        ScopeMode = scope,
        IdentificationMode = manual
          ? NativeStage02IdentificationMode.Manual
          : NativeStage02IdentificationMode.Automatic,
        CustomUniqueIds = Array.Empty<string>(),
        BulkRoleId = manual ? Clean(bulkRoleId) : string.Empty,
        RoleOverrides = new ReadOnlyCollection<NativeStage02RoleOverride>(
          canonicalOverrides),
        Confirmations = new ReadOnlyCollection<NativeStage02RoleConfirmation>(
          (confirmations ?? Array.Empty<NativeStage02RoleConfirmation>())
            .Where(value => value != null)
            .Select(value => value.Clone())
            .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
            .ThenBy(value => value.RoleId, StringComparer.Ordinal)
            .ToArray())
      }.Clone();
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
