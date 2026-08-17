using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal enum NativeStage03Mode
  {
    Strict,
    ForcedTest
  }

  internal static class NativeStage03Codes
  {
    internal const string UnsupportedRevit = "UNSUPPORTED_REVIT";
    internal const string DocumentUnavailable = "DOCUMENT_UNAVAILABLE";
    internal const string Stage01NotInitialized = "STAGE01_NOT_INITIALIZED";
    internal const string Stage01Invalid = "STAGE01_INVALID";
    internal const string Stage01BusinessInvalid =
      "STAGE01_BUSINESS_INVALID";
    internal const string ProjectConditionsUndeclared =
      "PROJECT_CONDITIONS_UNDECLARED";
    internal const string Stage02ScanFailed = "STAGE02_SCAN_FAILED";
    internal const string CarrierBlocked = "CARRIER_BLOCKED";
    internal const string FieldNotReady = "FIELD_NOT_READY";
    internal const string RuntimeUnclassified = "RUNTIME_UNCLASSIFIED";
    internal const string OwnerNotResolvable = "OWNER_NOT_RESOLVABLE";
    internal const string ForceReasonRequired = "FORCE_REASON_REQUIRED";
    internal const string NoExportableFields = "NO_EXPORTABLE_FIELDS";
    internal const string ScanExpired = "STAGE03_SCAN_EXPIRED";
    internal const string InvalidOutputDirectory = "INVALID_OUTPUT_DIRECTORY";
    internal const string ModelProfileNotImplemented =
      "MODEL_PROFILE_NOT_IMPLEMENTED_PHASE1";
    internal const string OutputDirectoryNotWritable =
      "OUTPUT_DIRECTORY_NOT_WRITABLE";
    internal const string IfcExporterUnavailable = "IFC_EXPORTER_UNAVAILABLE";
    internal const string TranslatorDependencyUnavailable =
      "TRANSLATOR_DEPENDENCY_UNAVAILABLE";
    internal const string ReportWriterUnavailable = "REPORT_WRITER_UNAVAILABLE";
    internal const string UnsavedDocument = "UNSAVED_DOCUMENT";
    internal const string RuntimeArtifactChanged = "RUNTIME_ARTIFACT_CHANGED";
    internal const string ScanEvidenceCollision = "SCAN_EVIDENCE_COLLISION";
  }

  internal sealed class NativeStage03GateDecision
  {
    internal bool AllowExport { get; set; }
    internal bool Forced { get; set; }
    internal IReadOnlyList<string> Blockers { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> BypassedBusinessBlockers { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage03GatePolicy
  {
    internal static NativeStage03GateDecision Evaluate(
      NativeStage03Mode mode,
      string forceReason,
      IEnumerable<string> technicalFatalCodes,
      IEnumerable<string> businessBlockers,
      int exportableFieldCount)
    {
      string[] technical = Normalize(technicalFatalCodes);
      string[] business = Normalize(businessBlockers);
      var blockers = new SortedSet<string>(StringComparer.Ordinal);
      foreach (string code in technical) blockers.Add(code);
      if (exportableFieldCount <= 0)
        blockers.Add(NativeStage03Codes.NoExportableFields);

      if (mode == NativeStage03Mode.Strict)
      {
        foreach (string code in business) blockers.Add(code);
        return new NativeStage03GateDecision
        {
          AllowExport = blockers.Count == 0,
          Forced = false,
          Blockers = Freeze(blockers),
          BypassedBusinessBlockers = Array.Empty<string>()
        };
      }

      if (string.IsNullOrWhiteSpace(forceReason))
        blockers.Add(NativeStage03Codes.ForceReasonRequired);

      return new NativeStage03GateDecision
      {
        AllowExport = blockers.Count == 0,
        Forced = blockers.Count == 0,
        Blockers = Freeze(blockers),
        BypassedBusinessBlockers = blockers.Count == 0
          ? Freeze(business)
          : Array.Empty<string>()
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

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values
        ?? Array.Empty<string>()).ToArray());
    }
  }

  internal sealed class NativeStage03ScanRequest
  {
    internal NativeStage03Mode Mode { get; set; } = NativeStage03Mode.Strict;
    internal string ForceReason { get; set; } = string.Empty;
    internal string OutputDirectory { get; set; } = string.Empty;
    internal string FocusCheckId { get; set; } = string.Empty;

    internal NativeStage03ScanRequest Clone()
    {
      return new NativeStage03ScanRequest
      {
        Mode = Mode,
        ForceReason = ForceReason ?? string.Empty,
        OutputDirectory = OutputDirectory ?? string.Empty,
        FocusCheckId = FocusCheckId ?? string.Empty
      };
    }
  }

  internal sealed class NativePluginRuntimeIdentity
  {
    internal string ProductVersion { get; set; } = string.Empty;
    internal string AssemblyVersion { get; set; } = string.Empty;
    internal string InformationalVersion { get; set; } = string.Empty;
    internal string CommitSha { get; set; } = string.Empty;
    internal string AddinDllPath { get; set; } = string.Empty;
    internal string AddinDllSha256 { get; set; } = string.Empty;
  }

  internal sealed class NativeStage03FieldEvidence
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string Entity { get; set; } = string.Empty;
    internal string PropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal string Requirement { get; set; } = string.Empty;
    internal string RuntimeStatus { get; set; } = string.Empty;
    internal int ElementId { get; set; }
    internal string OwnerUniqueId { get; set; } = string.Empty;
    internal string OwnerStrategy { get; set; } = string.Empty;
    internal string OwnerExportGuid { get; set; } = string.Empty;
    internal string OwnerGlobalId { get; set; } = string.Empty;
    internal string OwnerResolutionStatus { get; set; } = string.Empty;
    internal string CanonicalValue { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal bool Active { get; set; }
    internal bool StrictExportReady { get; set; }
    internal bool ExportableInForcedMode { get; set; }
    internal HifcFieldRequest HifcField { get; set; }
  }

  internal sealed class NativeStage03ScanResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal NativeStage03Mode Mode { get; set; }
    internal string ForceReason { get; set; } = string.Empty;
    internal bool AllowExport { get; set; }
    internal bool Forced { get; set; }
    internal string ScanHash { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string ModelFileType { get; set; } = string.Empty;
    internal string RevitVersion { get; set; } = string.Empty;
    internal string NormalizedOutputDirectory { get; set; } = string.Empty;
    internal string PreflightHash { get; set; } = string.Empty;
    internal string Stage02ACurrentInputSnapshotHash { get; set; } =
      string.Empty;
    internal string Stage01PayloadSha256 { get; set; } = string.Empty;
    internal NativeWorkflowResultEnvelope Stage01WorkflowResult { get; set; }
    internal NativeWorkflowResultEnvelope Stage02AWorkflowResult { get; set; }
    internal NativeWorkflowResultEnvelope Stage02BWorkflowResult { get; set; }
    internal NativePluginRuntimeIdentity PluginRuntime { get; set; }
    internal NativeOfficialAcceptanceManifest OfficialAcceptanceManifest
    {
      get;
      set;
    }
    internal IReadOnlyList<NativeOfficialAcceptancePropertyReadback>
      OfficialAcceptanceRevitReadbacks { get; set; } =
        Array.Empty<NativeOfficialAcceptancePropertyReadback>();
    internal IReadOnlyList<NativeStage03ChecklistItem> Checklist { get; set; } =
      Array.Empty<NativeStage03ChecklistItem>();
    internal int PassedCount { get; set; }
    internal int FailedCount { get; set; }
    internal int WarningCount { get; set; }
    internal int NotCheckedCount { get; set; }
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> BusinessBlockers { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<NativeStage03FieldEvidence> Fields { get; set; } =
      Array.Empty<NativeStage03FieldEvidence>();
    internal IReadOnlyList<HifcFieldRequest> ExportFields { get; set; } =
      Array.Empty<HifcFieldRequest>();
  }

  internal sealed class NativeStage03ExportRequest
  {
    internal NativeStage03ScanResult ConfirmedScan { get; set; }
    internal string OutputDirectory { get; set; } = string.Empty;

    internal NativeStage03ExportRequest Clone()
    {
      return new NativeStage03ExportRequest
      {
        ConfirmedScan = ConfirmedScan,
        OutputDirectory = OutputDirectory ?? string.Empty
      };
    }
  }

  internal sealed class NativeStage03RunPaths
  {
    internal string RunId { get; set; } = string.Empty;
    internal string RunDirectory { get; set; } = string.Empty;
    internal string QuarantineDirectory { get; set; } = string.Empty;
    internal string RawIfcPath { get; set; } = string.Empty;
    internal string FinalIfcPath { get; set; } = string.Empty;
    internal string FieldsReportPath { get; set; } = string.Empty;
    internal string ValidationReportPath { get; set; } = string.Empty;
    internal string FailureReportPath { get; set; } = string.Empty;
    internal string IfcFluxChecklistPath { get; set; } = string.Empty;
  }

  internal static class NativeStage03OutputPathPolicy
  {
    internal static NativeStage03RunPaths Create(
      string outputDirectory,
      string documentPath,
      string runId,
      DateTimeOffset timestamp,
      NativeStage03Mode mode)
    {
      if (string.IsNullOrWhiteSpace(outputDirectory)
        || !Path.IsPathRooted(outputDirectory))
        throw new ArgumentException("Stage03 输出目录必须是绝对路径。" );
      string normalizedRunId = Sanitize(
        string.IsNullOrWhiteSpace(runId)
          ? Guid.NewGuid().ToString("N")
          : runId.Trim());
      string sourceName = Path.GetFileNameWithoutExtension(
        documentPath ?? string.Empty);
      if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "RevitModel";
      string stem = Sanitize(sourceName);
      string stamp = timestamp.ToString("yyyyMMdd-HHmmss");
      string runDirectory = Path.Combine(
        Path.GetFullPath(outputDirectory),
        stem + "_" + stamp + "_" + normalizedRunId);
      string outputStem = stem + "_" + normalizedRunId;
      string finalSuffix = mode == NativeStage03Mode.ForcedTest
        ? "_FORCED_TEST_HIFC.ifc"
        : "_HIFC.ifc";
      return new NativeStage03RunPaths
      {
        RunId = normalizedRunId,
        RunDirectory = runDirectory,
        QuarantineDirectory = Path.Combine(runDirectory, "quarantine"),
        RawIfcPath = Path.Combine(runDirectory, outputStem + "_RAW.ifc"),
        FinalIfcPath = Path.Combine(runDirectory, outputStem + finalSuffix),
        FieldsReportPath = Path.Combine(
          runDirectory,
          outputStem + "_fields.json"),
        ValidationReportPath = Path.Combine(
          runDirectory,
          outputStem + "_validation.json"),
        FailureReportPath = Path.Combine(
          runDirectory,
          outputStem + "_failure.json"),
        IfcFluxChecklistPath = Path.Combine(
          runDirectory,
          outputStem + "_IFCFlux_checklist.md")
      };
    }

    private static string Sanitize(string value)
    {
      char[] invalid = Path.GetInvalidFileNameChars();
      return new string((value ?? string.Empty)
        .Select(character => invalid.Contains(character) ? '_' : character)
        .ToArray());
    }
  }

  internal sealed class NativeStage03ExecutionResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal string InternalValidationStatus { get; set; } =
      HifcCoreStatus.InternalFailed;
    internal string IfcFluxStatus { get; set; } =
      HifcCoreStatus.IfcFluxManualPending;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal NativeStage03RunPaths Paths { get; set; }
    internal string RawIfcSha256 { get; set; } = string.Empty;
    internal string FinalIfcSha256 { get; set; } = string.Empty;
    internal bool IsTestExport { get; set; }
    internal bool CountsAsNormalExportPass { get; set; }
    internal string OfficialAcceptanceStatus { get; set; } = "PENDING";
    internal NativeOfficialAcceptanceManifest OfficialAcceptanceManifest
    {
      get;
      set;
    }
    internal IReadOnlyList<NativeOfficialAcceptancePropertyReadback>
      OfficialAcceptanceRevitReadbacks { get; set; } =
        Array.Empty<NativeOfficialAcceptancePropertyReadback>();
    internal IReadOnlyList<NativeStage03ChecklistItem> Checklist { get; set; } =
      Array.Empty<NativeStage03ChecklistItem>();
    internal IReadOnlyList<NativeStage03FieldEvidence> Fields { get; set; } =
      Array.Empty<NativeStage03FieldEvidence>();
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage03ExecutionIdentityPolicy
  {
    internal static void Apply(
      NativeStage03ScanResult scan,
      NativeStage03ExecutionResult execution)
    {
      if (scan == null) throw new ArgumentNullException(nameof(scan));
      if (execution == null)
        throw new ArgumentNullException(nameof(execution));
      bool isTest = scan.Mode == NativeStage03Mode.ForcedTest;
      execution.IsTestExport = isTest;
      execution.CountsAsNormalExportPass =
        NativeStage03StrictSuccessPolicy.IsSatisfied(
          scan,
          execution.Success,
          execution.InternalValidationStatus);
      execution.OfficialAcceptanceStatus = "PENDING";
      execution.OfficialAcceptanceManifest = scan.OfficialAcceptanceManifest;
      execution.OfficialAcceptanceRevitReadbacks =
        scan.OfficialAcceptanceRevitReadbacks
        ?? Array.Empty<NativeOfficialAcceptancePropertyReadback>();
      execution.Checklist = scan.Checklist
        ?? Array.Empty<NativeStage03ChecklistItem>();
    }
  }

  internal static class NativeStage03StrictSuccessPolicy
  {
    internal static bool IsSatisfied(
      NativeStage03ScanResult scan,
      bool exportSucceeded,
      string internalValidationStatus)
    {
      if (scan == null
        || scan.Mode != NativeStage03Mode.Strict
        || !exportSucceeded
        || !scan.AllowExport
        || (scan.ExportFields?.Count ?? 0) == 0
        || (scan.TechnicalFatalCodes?.Count ?? 0) != 0
        || !string.Equals(internalValidationStatus,
          HifcCoreStatus.InternalValidated, StringComparison.Ordinal))
      {
        return false;
      }

      NativeStage03ChecklistItem[] checklist = (scan.Checklist
        ?? Array.Empty<NativeStage03ChecklistItem>())
        .Where(value => value != null)
        .ToArray();
      int passed = checklist.Count(value =>
        value.Status == NativeStage03ChecklistStatus.Passed);
      int failed = checklist.Count(value =>
        value.Status == NativeStage03ChecklistStatus.Failed);
      int warning = checklist.Count(value =>
        value.Status == NativeStage03ChecklistStatus.Warning);
      int notChecked = checklist.Count(value =>
        value.Status == NativeStage03ChecklistStatus.NotChecked);
      return scan.PassedCount == passed
        && scan.FailedCount == failed
        && scan.WarningCount == warning
        && scan.NotCheckedCount == notChecked
        && failed == 0
        && notChecked == 0;
    }
  }
}
