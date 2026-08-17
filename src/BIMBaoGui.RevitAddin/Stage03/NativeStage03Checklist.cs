using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03ChecklistGenerationResult
  {
    internal bool Supported { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal string ModelFileType { get; set; } = string.Empty;
    internal NativeOfficialAcceptanceManifest OfficialAcceptanceManifest
    {
      get;
      set;
    }
    internal IReadOnlyList<NativeReportingCheckDefinition> Definitions
    {
      get;
      set;
    } = Array.Empty<NativeReportingCheckDefinition>();
  }

  internal enum NativeStage03ChecklistStatus
  {
    NotChecked,
    Passed,
    Failed,
    Warning
  }

  internal sealed class NativeStage03ChecklistItem
  {
    internal string CheckId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal NativeReportingSourceStage SourceStage { get; set; }
    internal NativeReportingCheckKind CheckKind { get; set; }
    internal string ApplicableBasis { get; set; } = string.Empty;
    internal string CurrentValue { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal NativeStage03ChecklistStatus Status { get; set; }
    internal string IssueCode { get; set; } = string.Empty;
    internal string IssueMessage { get; set; } = string.Empty;
    internal string RemediationTarget { get; set; } = string.Empty;
    internal int? ElementId { get; set; }
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
      Array.Empty<NativeIssueElementReference>();
    internal string FieldKey { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string RuleText { get; set; } = string.Empty;
    internal string TargetKey { get; set; } = string.Empty;
    internal NativeOfficialCarrierEvidenceStatus OfficialCarrierStatus
    {
      get;
      set;
    }
    internal string OfficialProjectionCarrierId { get; set; } = string.Empty;
    internal string OfficialCarrierProbeRef { get; set; } = string.Empty;
    internal string OfficialEvidenceRef { get; set; } = string.Empty;
    internal bool InternalValidationPassed { get; set; }
    internal bool OfficialAcceptancePassed { get; set; }
  }

  internal sealed class NativeOfficialAcceptanceManifestEntry
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal string ParameterGuid { get; set; } = string.Empty;
    internal string BindingScope { get; set; } = string.Empty;
    internal NativeReportingSourceStage SourceStage { get; set; }
  }

  internal sealed class NativeOfficialAcceptanceManifest
  {
    internal string SchemaVersion { get; set; } = "1.0.0";
    internal string Sha256 { get; set; } = string.Empty;
    internal IReadOnlyList<NativeOfficialAcceptanceManifestEntry> Properties
    {
      get;
      set;
    } = Array.Empty<NativeOfficialAcceptanceManifestEntry>();
  }

  internal sealed class NativeOfficialAcceptanceOwnerReadback
  {
    internal string RevitUniqueId { get; set; } = string.Empty;
    internal string ExpectedIfcGlobalId { get; set; } = string.Empty;
    internal string CanonicalValue { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialAcceptancePropertyReadback
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal NativeReportingSourceStage SourceStage { get; set; }
    internal string SourceResultHash { get; set; } = string.Empty;
    internal IReadOnlyList<NativeOfficialAcceptanceOwnerReadback> Values
    {
      get;
      set;
    } = Array.Empty<NativeOfficialAcceptanceOwnerReadback>();
  }

  internal sealed class NativeStage03BlockerClassification
  {
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> BusinessBlockers { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage03BlockerPolicy
  {
    internal static NativeStage03BlockerClassification Classify(
      IEnumerable<string> technicalFatalCodes,
      IEnumerable<NativeStage03ChecklistItem> checklist)
    {
      string[] technical = Normalize(technicalFatalCodes);
      var technicalSet = new HashSet<string>(technical, StringComparer.Ordinal);
      string[] business = Normalize((checklist
          ?? Array.Empty<NativeStage03ChecklistItem>())
        .Where(value => value != null
          && value.Status == NativeStage03ChecklistStatus.Failed)
        .Select(value => value.IssueCode))
        .Where(value => !technicalSet.Contains(value))
        .ToArray();
      return new NativeStage03BlockerClassification
      {
        TechnicalFatalCodes = new ReadOnlyCollection<string>(technical),
        BusinessBlockers = new ReadOnlyCollection<string>(business)
      };
    }

    private static string[] Normalize(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }
  }
}
