using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Mvd;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03WorkflowRequest
  {
    internal HBRFileContext Context { get; set; }
    internal string OutputDirectory { get; set; } = string.Empty;
    internal string RvtStem { get; set; } = string.Empty;
    internal string RunId { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string PluginVersion { get; set; } = string.Empty;
    internal Stage03GateMode Mode { get; set; } = Stage03GateMode.Strict;
    internal string ForceReason { get; set; } = string.Empty;
  }

  internal sealed class Stage03WorkflowScanResult
  {
    internal string FileGuid { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string RevitVersion { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<Stage03CarrierResult> Carriers { get; set; } =
      Array.Empty<Stage03CarrierResult>();
    internal IReadOnlyList<Stage03FieldResult> Fields { get; set; } =
      Array.Empty<Stage03FieldResult>();
    internal IReadOnlyList<HbrIfcEnrichmentValue> EnrichmentValues
    {
      get;
      set;
    } = Array.Empty<HbrIfcEnrichmentValue>();
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<Stage03Diagnostic> Diagnostics { get; set; } =
      Array.Empty<Stage03Diagnostic>();
  }

  internal sealed class Stage03WorkflowRawExportResult
  {
    internal string RawIfcPath { get; set; } = string.Empty;
    internal long RawIfcLength { get; set; }
    internal string RawIfcSha256 { get; set; } = string.Empty;
  }

  internal sealed class Stage03WorkflowTranslationResult
  {
    internal bool Success { get; set; }
    internal Exception FailureException { get; set; }
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
    internal HbrIfcBatchInspectionResult RawInspection { get; set; }
    internal HbrIfcBatchInspectionResult FinalInspection { get; set; }
    internal string FinalIfcPath { get; set; } = string.Empty;
    internal long FinalIfcLength { get; set; }
    internal string FinalIfcSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<Stage03FieldResult> Fields { get; set; } =
      Array.Empty<Stage03FieldResult>();
    internal IReadOnlyList<Stage03Diagnostic> Diagnostics { get; set; } =
      Array.Empty<Stage03Diagnostic>();
  }

  internal sealed class Stage03WorkflowExportRequest
  {
    internal Stage03WorkflowExportRequest(
      HBRFileContext context,
      string rawIfcPath)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      RawIfcPath = rawIfcPath ?? string.Empty;
    }

    internal HBRFileContext Context { get; }
    internal string RawIfcPath { get; }
  }

  internal sealed class Stage03WorkflowTranslationRequest
  {
    internal Stage03WorkflowTranslationRequest(
      string rawIfcPath,
      string finalIfcPath,
      IReadOnlyList<Stage03FieldResult> fields,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues)
    {
      RawIfcPath = rawIfcPath ?? string.Empty;
      FinalIfcPath = finalIfcPath ?? string.Empty;
      if (fields == null) throw new ArgumentNullException(nameof(fields));
      if (enrichmentValues == null)
        throw new ArgumentNullException(nameof(enrichmentValues));
      Fields = new ReadOnlyCollection<Stage03FieldResult>(fields
        .Select(Stage03WorkflowCoordinator.CloneField)
        .ToArray());
      EnrichmentValues = new ReadOnlyCollection<HbrIfcEnrichmentValue>(
        enrichmentValues
          .Select(Stage03WorkflowCoordinator.CloneEnrichmentValue)
          .ToArray());
    }

    internal string RawIfcPath { get; }
    internal string FinalIfcPath { get; }
    internal IReadOnlyList<Stage03FieldResult> Fields { get; }
    internal IReadOnlyList<HbrIfcEnrichmentValue> EnrichmentValues { get; }
  }

  internal sealed class Stage03WorkflowServices
  {
    internal Func<Stage03WorkflowRequest, Task<Stage03WorkflowScanResult>>
      ScanAsync { get; set; }
    internal Func<Stage03WorkflowExportRequest,
      Task<Stage03WorkflowRawExportResult>> ExportRawAsync { get; set; }
    internal Func<Stage03WorkflowTranslationRequest,
      Task<Stage03WorkflowTranslationResult>> TranslateAsync { get; set; }
    internal Func<Stage03FieldReportContext, Stage03FieldReportWriteResult>
      WriteFieldReport { get; set; }
    internal Func<Stage03FailureReportContext, Stage03FailureReportWriteResult>
      WriteFailureReport { get; set; }
    internal Func<DateTimeOffset> UtcNow { get; set; }
  }

  internal sealed class Stage03RunResult
  {
    internal Stage03RunResult(
      string runId,
      bool success,
      string status,
      Stage03GateDecision gateDecision,
      Stage03OutputPaths paths,
      string rawIfcSha256,
      string finalIfcSha256,
      string fieldReportPath,
      string fieldReportSha256,
      string failureReportPath,
      string rulePackageSha256,
      IEnumerable<Stage03CarrierResult> carriers,
      IEnumerable<Stage03FieldResult> fields,
      IEnumerable<Stage03Diagnostic> diagnostics,
      IEnumerable<string> technicalFatalCodes,
      IEnumerable<string> messages)
    {
      RunId = runId ?? string.Empty;
      Success = success;
      Status = status ?? string.Empty;
      GateDecision = gateDecision;
      AllowExport = gateDecision != null && gateDecision.AllowExport;
      Forced = gateDecision != null && gateDecision.Forced;
      RawIfcPath = paths == null ? string.Empty : paths.RawIfc;
      FinalIfcPath = paths == null ? string.Empty : paths.FinalIfc;
      FieldReportPath = fieldReportPath ?? string.Empty;
      RawIfcSha256 = rawIfcSha256 ?? string.Empty;
      FinalIfcSha256 = finalIfcSha256 ?? string.Empty;
      FieldReportSha256 = fieldReportSha256 ?? string.Empty;
      FailureReportPath = failureReportPath ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      Carriers = Freeze((carriers ?? Array.Empty<Stage03CarrierResult>())
        .Select(Stage03WorkflowCoordinator.CloneCarrier));
      Fields = Freeze((fields ?? Array.Empty<Stage03FieldResult>())
        .Select(Stage03WorkflowCoordinator.CloneField));
      Diagnostics = Freeze((diagnostics ?? Array.Empty<Stage03Diagnostic>())
        .Select(Stage03WorkflowCoordinator.CloneDiagnostic));
      TechnicalFatalCodes = Freeze((technicalFatalCodes
        ?? Array.Empty<string>()).Select(value => value ?? string.Empty));
      Messages = Freeze((messages ?? Array.Empty<string>())
        .Select(value => value ?? string.Empty));
    }

    internal string RunId { get; }
    internal bool Success { get; }
    internal bool AllowExport { get; }
    internal bool Forced { get; }
    internal string Status { get; }
    internal Stage03GateDecision GateDecision { get; }
    internal string RawIfcPath { get; }
    internal string FinalIfcPath { get; }
    internal string FieldReportPath { get; }
    internal string RawIfcSha256 { get; }
    internal string FinalIfcSha256 { get; }
    internal string FieldReportSha256 { get; }
    internal string FailureReportPath { get; }
    internal string RulePackageSha256 { get; }
    internal IReadOnlyList<Stage03CarrierResult> Carriers { get; }
    internal IReadOnlyList<Stage03FieldResult> Fields { get; }
    internal IReadOnlyList<Stage03Diagnostic> Diagnostics { get; }
    internal IReadOnlyList<string> TechnicalFatalCodes { get; }
    internal IReadOnlyList<string> Messages { get; }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }
  }

  internal sealed partial class Stage03WorkflowCoordinator
  {
    private const string InvalidOutputPathCode = "INVALID_OUTPUT_PATH";
    private readonly Func<Stage03WorkflowRequest,
      Task<Stage03WorkflowScanResult>> _scanAsync;
    private readonly Func<Stage03WorkflowExportRequest,
      Task<Stage03WorkflowRawExportResult>> _exportRawAsync;
    private readonly Func<Stage03WorkflowTranslationRequest,
      Task<Stage03WorkflowTranslationResult>> _translateAsync;
    private readonly Func<Stage03FieldReportContext,
      Stage03FieldReportWriteResult> _writeFieldReport;
    private readonly Func<Stage03FailureReportContext,
      Stage03FailureReportWriteResult> _writeFailureReport;
    private readonly Func<DateTimeOffset> _utcNow;

    internal Stage03WorkflowCoordinator(Stage03WorkflowServices services)
    {
      if (services == null) throw new ArgumentNullException(nameof(services));
      _scanAsync = RequireDelegate(services.ScanAsync, nameof(services.ScanAsync));
      _exportRawAsync = RequireDelegate(
        services.ExportRawAsync,
        nameof(services.ExportRawAsync));
      _translateAsync = RequireDelegate(
        services.TranslateAsync,
        nameof(services.TranslateAsync));
      _writeFieldReport = RequireDelegate(
        services.WriteFieldReport,
        nameof(services.WriteFieldReport));
      _writeFailureReport = RequireDelegate(
        services.WriteFailureReport,
        nameof(services.WriteFailureReport));
      _utcNow = RequireDelegate(services.UtcNow, nameof(services.UtcNow));
    }

    internal async Task<Stage03RunResult> RunAsync(
      Stage03WorkflowRequest request)
    {
      DateTimeOffset startedUtc = DateTimeOffset.MinValue;
      Stage03WorkflowRequest input = null;
      Stage03OutputPaths paths = null;
      ScanSnapshot scan = null;
      Stage03GateDecision gate = null;
      IReadOnlyList<Stage03CarrierResult> carriers =
        Array.Empty<Stage03CarrierResult>();
      IReadOnlyList<Stage03FieldResult> fields =
        Array.Empty<Stage03FieldResult>();
      IReadOnlyList<Stage03Diagnostic> diagnostics =
        Array.Empty<Stage03Diagnostic>();
      IReadOnlyList<string> technicalCodes = Array.Empty<string>();
      string rawHash = string.Empty;
      string finalHash = string.Empty;
      string fieldReportPath = string.Empty;
      string fieldReportHash = string.Empty;
      string failureCode = Stage03TechnicalFatalCodes.DocumentUnavailable;
      string failureStage = "capture-start-time";

      try
      {
        startedUtc = SafeUtcNow();
        failureCode = Stage03TechnicalFatalCodes.WrongDocument;
        failureStage = "preflight-identity";
        input = SnapshotAndValidateRequest(request);
        failureCode = InvalidOutputPathCode;
        failureStage = "create-output-paths";
        try
        {
          paths = Stage03OutputPathPolicy.Create(
            input.OutputDirectory,
            input.RvtStem,
            input.RunId);
        }
        catch (Exception exception)
        {
          throw Failure(failureCode, failureStage, exception);
        }
        failureCode = Stage03TechnicalFatalCodes.OutputExists;
        failureStage = "preflight-paths";
        try
        {
          Stage03OutputPathPolicy.ValidateUnused(paths);
        }
        catch (Exception exception)
        {
          throw Failure(failureCode, failureStage, exception);
        }

        failureCode = Stage03TechnicalFatalCodes.DocumentUnavailable;
        failureStage = "scan-revit-host";
        Stage03WorkflowScanResult scanResult = await _scanAsync(
          CloneRequest(input)).ConfigureAwait(false);
        scan = SnapshotScan(scanResult);
        ValidateScanEnrichment(scan.Fields, scan.EnrichmentValues);
        ValidateScanIdentity(input, scan);
        carriers = scan.Carriers;
        fields = scan.Fields;
        diagnostics = scan.Diagnostics;
        technicalCodes = AddUnsupportedVersionIfNeeded(
          scan.TechnicalFatalCodes,
          scan.RevitVersion);

        gate = Stage03ExportGatePolicy.Decide(
          input.Mode,
          input.ForceReason,
          fields,
          technicalCodes);
        technicalCodes = gate.TechnicalFatalCodes;
        if (!gate.AllowExport)
        {
          failureCode = Stage03TechnicalFatalCodes.ReportFailed;
          failureStage = "write-fields-report";
          Artifact report = WriteFieldsReport(
            input,
            paths,
            scan,
            gate,
            carriers,
            fields,
            diagnostics,
            startedUtc,
            string.Empty,
            string.Empty);
          fieldReportPath = report.Path;
          fieldReportHash = report.Sha256;
          failureCode = Stage03TechnicalFatalCodes.OutputExists;
          failureStage = "post-report-blocked-paths";
          EnsureAbsent(paths.RawIfc, failureCode, failureStage);
          EnsureAbsent(paths.FinalIfc, failureCode, failureStage);
          bool strictBusinessBlock = input.Mode == Stage03GateMode.Strict
            && technicalCodes.Count == 0
            && gate.BusinessBlockers.Count > 0;
          return CreateResult(
            input,
            strictBusinessBlock,
            strictBusinessBlock
              ? "检测完成｜Strict 业务阻断｜未导出"
              : "检测失败｜门禁阻断｜未导出",
            gate,
            paths,
            rawHash,
            finalHash,
            fieldReportPath,
            fieldReportHash,
            string.Empty,
            scan.RulePackageSha256,
            carriers,
            fields,
            diagnostics,
            technicalCodes,
            gate.Messages);
        }

        failureCode = Stage03TechnicalFatalCodes.OutputExists;
        failureStage = "pre-export-paths";
        try
        {
          Stage03OutputPathPolicy.ValidateUnused(paths);
        }
        catch (Exception exception)
        {
          throw Failure(failureCode, failureStage, exception);
        }

        failureCode = Stage03TechnicalFatalCodes.ExportFailed;
        failureStage = "export-raw-ifc";
        Stage03WorkflowRawExportResult exportResult =
          await _exportRawAsync(new Stage03WorkflowExportRequest(
            input.Context,
            paths.RawIfc)).ConfigureAwait(false);
        Artifact raw = ValidateArtifact(
          paths.RawIfc,
          exportResult == null ? null : exportResult.RawIfcPath,
          exportResult == null ? 0L : exportResult.RawIfcLength,
          exportResult == null ? null : exportResult.RawIfcSha256,
          failureCode,
          failureStage);
        rawHash = raw.Sha256;
        failureCode = Stage03TechnicalFatalCodes.OutputExists;
        failureStage = "post-export-paths";
        EnsureAbsent(paths.FinalIfc, failureCode, failureStage);
        EnsureAbsent(paths.FieldReport, failureCode, failureStage);

        failureCode = Stage03TechnicalFatalCodes.InvalidIfc;
        failureStage = "translate-ifc";
        Stage03WorkflowTranslationResult translationResult =
          await _translateAsync(new Stage03WorkflowTranslationRequest(
            paths.RawIfc,
            paths.FinalIfc,
            fields,
            scan.EnrichmentValues)).ConfigureAwait(false);
        ValidateArtifactUnchangedForResult(
          raw,
          ref rawHash,
          failureCode,
          failureStage);
        if (translationResult != null
          && translationResult.Diagnostics != null)
        {
          diagnostics = Freeze(scan.Diagnostics.Concat(SnapshotDiagnostics(
            translationResult.Diagnostics,
            failureCode,
            failureStage)));
        }
        ValidateTranslationEnvelope(translationResult);
        Artifact final = ValidateArtifact(
          paths.FinalIfc,
          translationResult == null ? null : translationResult.FinalIfcPath,
          translationResult == null ? 0L : translationResult.FinalIfcLength,
          translationResult == null ? null : translationResult.FinalIfcSha256,
          failureCode,
          failureStage);
        finalHash = final.Sha256;
        fields = SnapshotFields(
          translationResult == null ? null : translationResult.Fields,
          failureCode,
          failureStage);
        ValidateTranslatedFieldIdentity(scan.Fields, fields);
        ValidateTranslatedFieldEvidence(
          scan.Fields,
          scan.EnrichmentValues,
          fields);
        ValidateTranslationInspections(
          scan.Fields,
          scan.EnrichmentValues,
          fields,
          translationResult.RawInspection,
          translationResult.FinalInspection);
        failureCode = Stage03TechnicalFatalCodes.OutputExists;
        failureStage = "post-translation-paths";
        EnsureAbsent(paths.FieldReport, failureCode, failureStage);

        failureCode = Stage03TechnicalFatalCodes.ReportFailed;
        failureStage = "write-fields-report";
        Artifact finalReport = WriteFieldsReport(
          input,
          paths,
          scan,
          gate,
          carriers,
          fields,
          diagnostics,
          startedUtc,
          rawHash,
          finalHash);
        fieldReportPath = finalReport.Path;
        fieldReportHash = finalReport.Sha256;
        failureCode = Stage03TechnicalFatalCodes.InvalidIfc;
        failureStage = "post-report-artifacts";
        Exception postReportFailure = null;
        try
        {
          ValidateArtifactUnchangedForResult(
            raw,
            ref rawHash,
            failureCode,
            failureStage);
        }
        catch (Exception exception)
        {
          postReportFailure = exception;
        }
        try
        {
          ValidateArtifactUnchangedForResult(
            final,
            ref finalHash,
            failureCode,
            failureStage);
        }
        catch (Exception exception)
        {
          if (postReportFailure == null) postReportFailure = exception;
        }
        if (postReportFailure != null) throw postReportFailure;
        return CreateResult(
          input,
          true,
          "Stage03 三件套生成成功",
          gate,
          paths,
          rawHash,
          finalHash,
          fieldReportPath,
          fieldReportHash,
          string.Empty,
          scan.RulePackageSha256,
          carriers,
          fields,
          diagnostics,
          technicalCodes,
          gate.Messages.Concat(new[] { "RAW、HIFC-MVD 与 fields JSON 已生成。" }));
      }
      catch (Exception exception)
      {
        WorkflowFailure workflowFailure = exception as WorkflowFailure
          ?? Failure(failureCode, failureStage, exception);
        return CreateFailureResult(
          request,
          input,
          paths,
          scan,
          gate,
          carriers,
          fields,
          diagnostics,
          technicalCodes,
          startedUtc,
          rawHash,
          finalHash,
          fieldReportPath,
          fieldReportHash,
          workflowFailure);
      }
    }

    private Artifact WriteFieldsReport(
      Stage03WorkflowRequest input,
      Stage03OutputPaths paths,
      ScanSnapshot scan,
      Stage03GateDecision gate,
      IReadOnlyList<Stage03CarrierResult> carriers,
      IReadOnlyList<Stage03FieldResult> fields,
      IReadOnlyList<Stage03Diagnostic> diagnostics,
      DateTimeOffset startedUtc,
      string rawHash,
      string finalHash)
    {
      IReadOnlyList<Stage03CarrierResult> writerCarriers = SnapshotCarriers(
        carriers,
        Stage03TechnicalFatalCodes.ReportFailed,
        "write-fields-report");
      IReadOnlyList<Stage03FieldResult> writerFields = SnapshotFields(
        fields,
        Stage03TechnicalFatalCodes.ReportFailed,
        "write-fields-report");
      IReadOnlyList<Stage03Diagnostic> writerDiagnostics = SnapshotDiagnostics(
        diagnostics,
        Stage03TechnicalFatalCodes.ReportFailed,
        "write-fields-report");
      Stage03FieldReportWriteResult result;
      try
      {
        result = _writeFieldReport(new Stage03FieldReportContext
        {
          RunId = input.RunId,
          StartedUtc = startedUtc,
          CompletedUtc = SafeUtcNow(),
          PluginVersion = input.PluginVersion,
          RevitVersion = scan.RevitVersion,
          DocumentTitle = scan.DocumentTitle,
          DocumentPath = Path.GetFullPath(scan.DocumentPath),
          DocumentFingerprint = scan.DocumentFingerprint,
          FileGuid = scan.FileGuid,
          FileContextHash = input.Context.FileContextHash,
          RulePackageId = scan.RulePackageId,
          RulePackageVersion = scan.RulePackageVersion,
          RulePackageSha256 = scan.RulePackageSha256,
          GateDecision = gate,
          OutputPaths = paths,
          RawIfcSha256 = rawHash ?? string.Empty,
          FinalIfcSha256 = finalHash ?? string.Empty,
          Carriers = writerCarriers,
          Fields = writerFields,
          Diagnostics = writerDiagnostics
        });
        ValidateReportSnapshotUnchanged(
          carriers,
          fields,
          diagnostics,
          writerCarriers,
          writerFields,
          writerDiagnostics);
      }
      catch (Exception exception)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.ReportFailed,
          "write-fields-report",
          exception);
      }
      if (result == null)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.ReportFailed,
          "write-fields-report",
          new InvalidOperationException("字段报告 writer 未返回结果。"));
      }
      if (!IsSha256(result.PayloadSha256))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.ReportFailed,
          "write-fields-report",
          new InvalidDataException("字段报告 payload SHA-256 无效。"));
      }
      return ValidateArtifact(
        paths.FieldReport,
        result.ReportPath,
        File.Exists(paths.FieldReport)
          ? new FileInfo(paths.FieldReport).Length
          : 0L,
        result.PublishedSha256,
        Stage03TechnicalFatalCodes.ReportFailed,
        "write-fields-report");
    }

    private static void ValidateReportSnapshotUnchanged(
      IReadOnlyList<Stage03CarrierResult> carriers,
      IReadOnlyList<Stage03FieldResult> fields,
      IReadOnlyList<Stage03Diagnostic> diagnostics,
      IReadOnlyList<Stage03CarrierResult> writerCarriers,
      IReadOnlyList<Stage03FieldResult> writerFields,
      IReadOnlyList<Stage03Diagnostic> writerDiagnostics)
    {
      bool unchanged = carriers.Count == writerCarriers.Count
        && fields.Count == writerFields.Count
        && diagnostics.Count == writerDiagnostics.Count
        && carriers.Zip(writerCarriers, SameCarrier).All(value => value)
        && fields.Zip(writerFields, SameWorkflowField).All(value => value)
        && diagnostics.Zip(writerDiagnostics, SameDiagnostic).All(value => value);
      if (!unchanged)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.ReportFailed,
          "write-fields-report",
          new InvalidDataException(
            "字段报告 writer 改写了协调器提供的报告快照。"));
      }
    }

    private static bool SameCarrier(
      Stage03CarrierResult expected,
      Stage03CarrierResult actual)
    {
      return expected != null
        && actual != null
        && string.Equals(expected.Entity, actual.Entity, StringComparison.Ordinal)
        && string.Equals(expected.Role, actual.Role, StringComparison.Ordinal)
        && expected.ElementId == actual.ElementId
        && string.Equals(expected.UniqueId, actual.UniqueId,
          StringComparison.Ordinal)
        && string.Equals(expected.Category, actual.Category,
          StringComparison.Ordinal)
        && string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
        && expected.Status == actual.Status
        && expected.Active == actual.Active
        && expected.IsBusinessBlocker == actual.IsBusinessBlocker
        && SameMessages(expected.Messages, actual.Messages);
    }

    private static bool SameWorkflowField(
      Stage03FieldResult expected,
      Stage03FieldResult actual)
    {
      return SameScanOwnedField(expected, actual)
        && string.Equals(expected.RawIfcOwner, actual.RawIfcOwner,
          StringComparison.Ordinal)
        && string.Equals(expected.RawIfcPropertySet, actual.RawIfcPropertySet,
          StringComparison.Ordinal)
        && string.Equals(expected.RawIfcProperty, actual.RawIfcProperty,
          StringComparison.Ordinal)
        && string.Equals(expected.RawIfcType, actual.RawIfcType,
          StringComparison.Ordinal)
        && string.Equals(expected.RawIfcValue, actual.RawIfcValue,
          StringComparison.Ordinal)
        && expected.RawIfcStatus == actual.RawIfcStatus
        && string.Equals(expected.FinalIfcOwner, actual.FinalIfcOwner,
          StringComparison.Ordinal)
        && string.Equals(expected.FinalIfcPropertySet, actual.FinalIfcPropertySet,
          StringComparison.Ordinal)
        && string.Equals(expected.FinalIfcProperty, actual.FinalIfcProperty,
          StringComparison.Ordinal)
        && string.Equals(expected.FinalIfcType, actual.FinalIfcType,
          StringComparison.Ordinal)
        && string.Equals(expected.FinalIfcValue, actual.FinalIfcValue,
          StringComparison.Ordinal)
        && expected.FinalIfcStatus == actual.FinalIfcStatus
        && SameMessages(expected.Messages, actual.Messages);
    }

    private static bool SameDiagnostic(
      Stage03Diagnostic expected,
      Stage03Diagnostic actual)
    {
      return expected != null
        && actual != null
        && string.Equals(expected.Code, actual.Code, StringComparison.Ordinal)
        && string.Equals(expected.Stage, actual.Stage, StringComparison.Ordinal)
        && string.Equals(expected.Severity, actual.Severity,
          StringComparison.Ordinal)
        && string.Equals(expected.Message, actual.Message,
          StringComparison.Ordinal);
    }

    private static bool SameMessages(
      IReadOnlyList<string> expected,
      IReadOnlyList<string> actual)
    {
      return (expected ?? Array.Empty<string>()).SequenceEqual(
        actual ?? Array.Empty<string>(),
        StringComparer.Ordinal);
    }

    private Stage03RunResult CreateFailureResult(
      Stage03WorkflowRequest originalRequest,
      Stage03WorkflowRequest input,
      Stage03OutputPaths paths,
      ScanSnapshot scan,
      Stage03GateDecision gate,
      IReadOnlyList<Stage03CarrierResult> carriers,
      IReadOnlyList<Stage03FieldResult> fields,
      IReadOnlyList<Stage03Diagnostic> diagnostics,
      IReadOnlyList<string> existingTechnicalCodes,
      DateTimeOffset startedUtc,
      string rawHash,
      string finalHash,
      string fieldReportPath,
      string fieldReportHash,
      WorkflowFailure failure)
    {
      var codes = new SortedSet<string>(StringComparer.Ordinal);
      foreach (string code in existingTechnicalCodes ?? Array.Empty<string>())
      {
        string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length > 0) codes.Add(normalized);
      }
      codes.Add(failure.TechnicalCode);
      Stage03WorkflowRequest reportInput = input ?? originalRequest;
      string runId = reportInput == null ? string.Empty : reportInput.RunId;
      string pluginVersion = reportInput == null
        ? string.Empty
        : reportInput.PluginVersion;
      string revitVersion = scan == null ? string.Empty : scan.RevitVersion;
      var messages = new List<string>
      {
        failure.TechnicalCode + "：" + failure.Message
      };
      string failureReportPath = string.Empty;
      try
      {
        DateTimeOffset occurredUtc = FailureUtcNow();
        Stage03FailureReportWriteResult report = _writeFailureReport(
          new Stage03FailureReportContext
          {
            RunId = runId,
            PluginVersion = pluginVersion,
            RevitVersion = revitVersion,
            TechnicalCode = failure.TechnicalCode,
            RootCauseStage = failure.RootCauseStage,
            SafeDiagnosticCodes = Freeze(codes),
            Exception = failure.InnerException ?? failure,
            OccurredUtc = occurredUtc,
            OccurredLocal = occurredUtc.ToLocalTime()
          });
        if (report != null
          && report.Success
          && !string.IsNullOrWhiteSpace(report.ReportPath)
          && Path.IsPathRooted(report.ReportPath)
          && File.Exists(report.ReportPath))
        {
          failureReportPath = Path.GetFullPath(report.ReportPath);
          messages.Add("失败报告：" + failureReportPath);
        }
        else
        {
          codes.Add(Stage03TechnicalFatalCodes.ReportFailed);
          messages.Add(
            Stage03TechnicalFatalCodes.ReportFailed
            + "：Stage03 失败报告写入失败。"
            + (report == null || string.IsNullOrWhiteSpace(
                report.ReportWriteErrorSummary)
              ? string.Empty
              : " " + report.ReportWriteErrorSummary));
        }
      }
      catch (Exception reportFailure)
      {
        codes.Add(Stage03TechnicalFatalCodes.ReportFailed);
        messages.Add(
          Stage03TechnicalFatalCodes.ReportFailed
          + "：Stage03 失败报告写入异常。 "
          + reportFailure.GetType().Name);
      }

      Stage03GateMode mode = reportInput == null
        ? Stage03GateMode.Strict
        : reportInput.Mode;
      string forceReason = reportInput == null
        ? string.Empty
        : reportInput.ForceReason;
      Stage03GateDecision failureGate = Stage03ExportGatePolicy.Decide(
        mode,
        forceReason,
        fields,
        codes);
      messages.AddRange(failureGate.Messages);
      return new Stage03RunResult(
        runId,
        false,
        "Stage03 失败｜" + failure.TechnicalCode,
        failureGate,
        paths,
        rawHash,
        finalHash,
        fieldReportPath,
        fieldReportHash,
        failureReportPath,
        scan == null
          ? (reportInput?.Context?.RulePackageSha256 ?? string.Empty)
          : scan.RulePackageSha256,
        carriers,
        fields,
        diagnostics,
        codes,
        messages.Distinct(StringComparer.Ordinal));
    }

    private static Stage03RunResult CreateResult(
      Stage03WorkflowRequest input,
      bool success,
      string status,
      Stage03GateDecision gate,
      Stage03OutputPaths paths,
      string rawHash,
      string finalHash,
      string fieldReportPath,
      string fieldReportHash,
      string failureReportPath,
      string rulePackageSha256,
      IEnumerable<Stage03CarrierResult> carriers,
      IEnumerable<Stage03FieldResult> fields,
      IEnumerable<Stage03Diagnostic> diagnostics,
      IEnumerable<string> technicalCodes,
      IEnumerable<string> messages)
    {
      return new Stage03RunResult(
        input.RunId,
        success,
        status,
        gate,
        paths,
        rawHash,
        finalHash,
        fieldReportPath,
        fieldReportHash,
        failureReportPath,
        rulePackageSha256,
        carriers,
        fields,
        diagnostics,
        technicalCodes,
        messages);
    }

    private static Stage03WorkflowRequest SnapshotAndValidateRequest(
      Stage03WorkflowRequest request)
    {
      if (request == null)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new ArgumentNullException(nameof(request)));
      }
      HBRFileContext context = request.Context;
      Stage03ContextIdentityDecision identity =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          context == null ? string.Empty : context.RulePackageId,
          context == null ? string.Empty : context.RulePackageVersion,
          context == null ? string.Empty : context.RulePackageSha256,
          context == null ? string.Empty : context.RevitDocumentFingerprint,
          context == null ? string.Empty : context.RevitDocumentTitle);
      if (!identity.Success)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new InvalidOperationException(string.Join("；", identity.Messages)));
      }
      if (string.IsNullOrWhiteSpace(context.RevitDocumentTitle))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new InvalidOperationException("HBRFileContext 缺少 Revit 文档标题。"));
      }
      string documentPath = (request.DocumentPath ?? string.Empty).Trim();
      if (documentPath.Length == 0
        || !Path.IsPathRooted(documentPath)
        || !string.Equals(
          Path.GetExtension(documentPath),
          ".rvt",
          StringComparison.OrdinalIgnoreCase))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new InvalidOperationException("Stage03 文档路径必须是绝对 RVT 路径。"));
      }
      string fullDocumentPath = Path.GetFullPath(documentPath);
      if (!File.Exists(fullDocumentPath))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new FileNotFoundException(
            "Stage03 RVT 文档路径不存在。",
            fullDocumentPath));
      }
      string pluginVersion = (request.PluginVersion ?? string.Empty).Trim();
      if (pluginVersion.Length == 0)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "preflight-identity",
          new InvalidOperationException("Stage03 缺少插件版本。"));
      }
      return new Stage03WorkflowRequest
      {
        Context = context,
        OutputDirectory = request.OutputDirectory ?? string.Empty,
        RvtStem = request.RvtStem ?? string.Empty,
        RunId = request.RunId ?? string.Empty,
        DocumentPath = fullDocumentPath,
        PluginVersion = pluginVersion,
        Mode = request.Mode,
        ForceReason = request.ForceReason ?? string.Empty
      };
    }

    private static Stage03WorkflowRequest CloneRequest(
      Stage03WorkflowRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return new Stage03WorkflowRequest
      {
        Context = request.Context,
        OutputDirectory = request.OutputDirectory,
        RvtStem = request.RvtStem,
        RunId = request.RunId,
        DocumentPath = request.DocumentPath,
        PluginVersion = request.PluginVersion,
        Mode = request.Mode,
        ForceReason = request.ForceReason
      };
    }

    private static ScanSnapshot SnapshotScan(
      Stage03WorkflowScanResult result)
    {
      if (result == null)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.DocumentUnavailable,
          "scan-revit-host",
          new InvalidOperationException("Revit 扫描未返回结果。"));
      }
      return new ScanSnapshot(
        result.FileGuid,
        result.DocumentFingerprint,
        result.DocumentTitle,
        result.DocumentPath,
        result.RevitVersion,
        result.RulePackageId,
        result.RulePackageVersion,
        result.RulePackageSha256,
        SnapshotCarriers(
          result.Carriers,
          Stage03TechnicalFatalCodes.DocumentUnavailable,
          "scan-revit-host"),
        SnapshotFields(
          result.Fields,
          Stage03TechnicalFatalCodes.InvalidFieldStatus,
          "scan-revit-host"),
        SnapshotEnrichmentValues(result.EnrichmentValues),
        Freeze((result.TechnicalFatalCodes ?? Array.Empty<string>())
          .Select(value => value ?? string.Empty)),
        SnapshotDiagnostics(
          result.Diagnostics,
          Stage03TechnicalFatalCodes.DocumentUnavailable,
          "scan-revit-host"));
    }

    private static void ValidateScanIdentity(
      Stage03WorkflowRequest input,
      ScanSnapshot scan)
    {
      HBRFileContext context = input.Context;
      Stage03ContextIdentityDecision decision =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          scan.RulePackageId,
          scan.RulePackageVersion,
          scan.RulePackageSha256,
          scan.DocumentFingerprint,
          scan.DocumentTitle);
      string liveDocumentPath;
      try
      {
        liveDocumentPath = Path.GetFullPath(scan.DocumentPath);
      }
      catch (Exception exception)
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "scan-identity",
          exception);
      }
      string expectedFingerprint = HBRDocumentFingerprint.Compute(
        liveDocumentPath,
        scan.DocumentTitle,
        scan.RevitVersion);
      bool validLivePath = Path.IsPathRooted(scan.DocumentPath)
        && string.Equals(
          Path.GetExtension(liveDocumentPath),
          ".rvt",
          StringComparison.OrdinalIgnoreCase)
        && string.Equals(
          input.DocumentPath,
          liveDocumentPath,
          StringComparison.OrdinalIgnoreCase)
        && string.Equals(
          input.RvtStem,
          Path.GetFileNameWithoutExtension(liveDocumentPath),
          StringComparison.OrdinalIgnoreCase)
        && string.Equals(
          scan.DocumentFingerprint,
          expectedFingerprint,
          StringComparison.OrdinalIgnoreCase);
      if (!decision.Success
        || !validLivePath
        || !string.Equals(
          context.FileGuid,
          scan.FileGuid,
          StringComparison.OrdinalIgnoreCase))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.WrongDocument,
          "scan-identity",
          new InvalidOperationException(
            decision.Success
              ? "Revit 扫描文档路径、文件名主体、fingerprint 或 fileGuid"
                + " 与文件上下文不一致。"
              : string.Join("；", decision.Messages)));
      }
    }

    private static IReadOnlyList<string> AddUnsupportedVersionIfNeeded(
      IEnumerable<string> technicalCodes,
      string revitVersion)
    {
      var values = new SortedSet<string>(StringComparer.Ordinal);
      foreach (string code in technicalCodes ?? Array.Empty<string>())
      {
        string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length > 0) values.Add(normalized);
      }
      if (!string.Equals(revitVersion, "2020", StringComparison.Ordinal))
        values.Add(Stage03TechnicalFatalCodes.UnsupportedRevit);
      return Freeze(values);
    }

    private static Artifact ValidateArtifact(
      string expectedPath,
      string returnedPath,
      long returnedLength,
      string returnedSha256,
      string technicalCode,
      string stage)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(returnedPath)
          || !Path.IsPathRooted(returnedPath)
          || !string.Equals(
            Path.GetFullPath(returnedPath),
            Path.GetFullPath(expectedPath),
            StringComparison.OrdinalIgnoreCase))
        {
          throw new InvalidDataException("服务返回路径与正式目标不一致。");
        }
        if (!File.Exists(expectedPath))
          throw new FileNotFoundException("服务未生成正式目标文件。", expectedPath);
        var file = new FileInfo(expectedPath);
        if (file.Length <= 0L)
          throw new InvalidDataException("正式目标文件为空。");
        if (returnedLength <= 0L || returnedLength != file.Length)
          throw new InvalidDataException("服务返回长度与正式目标不一致。");
        if (!IsSha256(returnedSha256))
          throw new InvalidDataException("服务返回 SHA-256 格式无效。");
        string actualHash = ComputeSha256(expectedPath);
        if (!string.Equals(
          returnedSha256,
          actualHash,
          StringComparison.OrdinalIgnoreCase))
        {
          throw new InvalidDataException("服务返回 SHA-256 与正式目标不一致。");
        }
        return new Artifact(
          Path.GetFullPath(expectedPath),
          file.Length,
          actualHash);
      }
      catch (WorkflowFailure)
      {
        throw;
      }
      catch (Exception exception)
      {
        throw Failure(technicalCode, stage, exception);
      }
    }

    private static void ValidateArtifactUnchanged(
      Artifact artifact,
      string technicalCode,
      string stage)
    {
      ValidateArtifact(
        artifact.Path,
        artifact.Path,
        artifact.Length,
        artifact.Sha256,
        technicalCode,
        stage);
    }

    private static void EnsureAbsent(
      string path,
      string technicalCode,
      string stage)
    {
      if (File.Exists(path) || Directory.Exists(path))
      {
        throw Failure(
          technicalCode,
          stage,
          new IOException("非当前阶段正式目标已被占用：" + path));
      }
    }

    private static void ValidateArtifactUnchangedForResult(
      Artifact artifact,
      ref string verifiedHash,
      string technicalCode,
      string stage)
    {
      try
      {
        ValidateArtifactUnchanged(artifact, technicalCode, stage);
      }
      catch
      {
        verifiedHash = string.Empty;
        throw;
      }
    }

    private static void ValidateTranslatedFieldIdentity(
      IReadOnlyList<Stage03FieldResult> scanned,
      IReadOnlyList<Stage03FieldResult> translated)
    {
      string[] expected = (scanned ?? Array.Empty<Stage03FieldResult>())
        .Select(FieldIdentity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] actual = (translated ?? Array.Empty<Stage03FieldResult>())
        .Select(FieldIdentity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (expected.Length != expected.Distinct(StringComparer.Ordinal).Count()
        || actual.Length != actual.Distinct(StringComparer.Ordinal).Count()
        || !expected.SequenceEqual(actual, StringComparer.Ordinal))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.InvalidIfc,
          "translate-ifc",
          new InvalidDataException("转译字段身份与 Revit 扫描快照不一致。"));
      }
      var translatedByIdentity = translated.ToDictionary(
        FieldIdentity,
        StringComparer.Ordinal);
      if (scanned.Any(field => !SameScanOwnedField(
        field,
        translatedByIdentity[FieldIdentity(field)])))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.InvalidIfc,
          "translate-ifc",
          new InvalidDataException("转译器改写了 Revit 扫描拥有的字段值。"));
      }
    }

    private static void ValidateTranslationEnvelope(
      Stage03WorkflowTranslationResult result)
    {
      if (result == null
        || !result.Success
        || result.FailureException != null
        || result.TechnicalFatalCodes == null
        || result.TechnicalFatalCodes.Any(code =>
          !string.IsNullOrWhiteSpace(code))
        || result.RawInspection == null
        || result.FinalInspection == null
        || result.Diagnostics == null
        || result.Diagnostics.Any(IsFatalTranslationDiagnostic))
      {
        Exception failureException = result != null
            && !result.Success
            && result.FailureException != null
          ? result.FailureException
          : new InvalidDataException(
            "IFC translator 返回失败、技术致命码或缺少检查证据。");
        throw Failure(
          Stage03TechnicalFatalCodes.InvalidIfc,
          "translate-ifc",
          failureException);
      }
    }

    private static void ValidateTranslationInspections(
      IReadOnlyList<Stage03FieldResult> scanned,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues,
      IReadOnlyList<Stage03FieldResult> translated,
      HbrIfcBatchInspectionResult rawInspection,
      HbrIfcBatchInspectionResult finalInspection)
    {
      string[] expected = enrichmentValues
        .Select(value => value.PropertyIdentity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] raw = rawInspection.Fields
        .Select(value => value.PropertyIdentity ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] final = finalInspection.Fields
        .Select(value => value.PropertyIdentity ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      bool identitiesMatch = expected.Length
          == expected.Distinct(StringComparer.Ordinal).Count()
        && raw.Length == raw.Distinct(StringComparer.Ordinal).Count()
        && final.Length == final.Distinct(StringComparer.Ordinal).Count()
        && expected.SequenceEqual(raw, StringComparer.Ordinal)
        && expected.SequenceEqual(final, StringComparer.Ordinal);
      if (!identitiesMatch
        || !rawInspection.Success
        || !finalInspection.Success)
        throw InvalidTranslationEvidence();
      var valuesByIdentity = enrichmentValues.ToDictionary(
        value => value.PropertyIdentity,
        StringComparer.Ordinal);
      var scannedByEnrichmentIdentity = scanned
        .Where(RequiresEnrichment)
        .ToDictionary(EnrichmentIdentity, StringComparer.Ordinal);
      var translatedByFieldIdentity = translated.ToDictionary(
        FieldIdentity,
        StringComparer.Ordinal);
      foreach (HbrIfcFieldInspectionResult inspection in rawInspection.Fields)
      {
        HbrIfcEnrichmentValue expectedValue =
          valuesByIdentity[inspection.PropertyIdentity];
        Stage03FieldResult scannedField =
          scannedByEnrichmentIdentity[inspection.PropertyIdentity];
        Stage03FieldResult translatedField =
          translatedByFieldIdentity[FieldIdentity(scannedField)];
        string expectedOwner = inspection.OwnerId.HasValue
          ? "#" + inspection.OwnerId.Value.ToString(
            CultureInfo.InvariantCulture)
          : string.Empty;
        if (!inspection.Success
          || inspection.OwnerId.GetValueOrDefault() <= 0
          || inspection.PropertyId.GetValueOrDefault() <= 0
          || inspection.PropertySetId.GetValueOrDefault() <= 0
          || inspection.RelationshipId.GetValueOrDefault() <= 0
          || translatedField.RawIfcStatus != Stage03FieldStatus.Pass
          || !string.Equals(
            translatedField.RawIfcOwner,
            expectedOwner,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.RawIfcPropertySet,
            expectedValue.PropertySetName,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.RawIfcProperty,
            expectedValue.PropertyName,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.RawIfcType,
            expectedValue.DeclaredIfcType,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.RawIfcValue,
            expectedValue.CanonicalValue,
            StringComparison.Ordinal)
          || !string.Equals(
            inspection.ActualIfcType,
            expectedValue.DeclaredIfcType,
            StringComparison.Ordinal)
          || !string.Equals(
            inspection.TypedToken,
            ExpectedTypedToken(expectedValue),
            StringComparison.Ordinal))
        {
          throw InvalidTranslationEvidence();
        }
      }
      foreach (HbrIfcFieldInspectionResult inspection in
        finalInspection.Fields)
      {
        HbrIfcEnrichmentValue expectedValue =
          valuesByIdentity[inspection.PropertyIdentity];
        Stage03FieldResult scannedField =
          scannedByEnrichmentIdentity[inspection.PropertyIdentity];
        Stage03FieldResult translatedField =
          translatedByFieldIdentity[FieldIdentity(scannedField)];
        string expectedOwner = inspection.OwnerId.HasValue
          ? "#" + inspection.OwnerId.Value.ToString(
            CultureInfo.InvariantCulture)
          : string.Empty;
        if (!inspection.Success
          || inspection.OwnerId.GetValueOrDefault() <= 0
          || inspection.PropertyId.GetValueOrDefault() <= 0
          || inspection.PropertySetId.GetValueOrDefault() <= 0
          || inspection.RelationshipId.GetValueOrDefault() <= 0
          || !string.Equals(
            translatedField.FinalIfcOwner,
            expectedOwner,
            StringComparison.Ordinal)
          || !string.Equals(
            inspection.ActualIfcType,
            expectedValue.DeclaredIfcType,
            StringComparison.Ordinal)
          || !string.Equals(
            inspection.TypedToken,
            ExpectedTypedToken(expectedValue),
            StringComparison.Ordinal))
        {
          throw InvalidTranslationEvidence();
        }
      }
    }

    private static string ExpectedTypedToken(HbrIfcEnrichmentValue value)
    {
      string type = (value.DeclaredIfcType ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate(type, value.CanonicalValue);
      if (!decision.Success) throw InvalidTranslationEvidence();
      string inner = decision.RequiresStringEncoding
        ? IfcStepSyntax.EncodeString(decision.NormalizedValue)
        : decision.NormalizedValue;
      return IfcStepSyntax.FormatTypedValue(type, inner);
    }

    private static bool IsFatalTranslationDiagnostic(
      Stage03Diagnostic diagnostic)
    {
      if (diagnostic == null) return true;
      string severity = (diagnostic.Severity ?? string.Empty).Trim();
      if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(severity, "FATAL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(
          severity,
          "CRITICAL",
          StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      string code = (diagnostic.Code ?? string.Empty).Trim();
      return string.Equals(code, Stage03TechnicalFatalCodes.WrongDocument,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.UnsupportedRevit,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.DocumentUnavailable,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.OutputExists,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.ExportFailed,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.InvalidIfc,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.ReportFailed,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.InvalidFieldStatus,
          StringComparison.Ordinal)
        || code.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool SameScanOwnedField(
      Stage03FieldResult expected,
      Stage03FieldResult actual)
    {
      return expected != null
        && actual != null
        && string.Equals(expected.PropertyId, actual.PropertyId,
          StringComparison.Ordinal)
        && string.Equals(expected.ContractKind, actual.ContractKind,
          StringComparison.Ordinal)
        && string.Equals(expected.Requirement, actual.Requirement,
          StringComparison.Ordinal)
        && string.Equals(expected.Applicability, actual.Applicability,
          StringComparison.Ordinal)
        && string.Equals(expected.Entity, actual.Entity,
          StringComparison.Ordinal)
        && string.Equals(expected.PropertySet, actual.PropertySet,
          StringComparison.Ordinal)
        && string.Equals(expected.IfcProperty, actual.IfcProperty,
          StringComparison.Ordinal)
        && string.Equals(expected.Role, actual.Role, StringComparison.Ordinal)
        && expected.ElementId == actual.ElementId
        && string.Equals(expected.OwnerUniqueId, actual.OwnerUniqueId,
          StringComparison.Ordinal)
        && string.Equals(expected.ParameterGuid, actual.ParameterGuid,
          StringComparison.Ordinal)
        && string.Equals(expected.ParameterName, actual.ParameterName,
          StringComparison.Ordinal)
        && string.Equals(expected.ParameterScope, actual.ParameterScope,
          StringComparison.Ordinal)
        && expected.CarrierStatus == actual.CarrierStatus
        && expected.ParameterStatus == actual.ParameterStatus
        && expected.RevitStatus == actual.RevitStatus
        && string.Equals(expected.RevitRawValue, actual.RevitRawValue,
          StringComparison.Ordinal)
        && string.Equals(
          expected.RevitNormalizedValue,
          actual.RevitNormalizedValue,
          StringComparison.Ordinal)
        && string.Equals(expected.RevitValueSource, actual.RevitValueSource,
          StringComparison.Ordinal)
        && expected.Status == actual.Status
        && expected.Active == actual.Active
        && expected.IsBusinessBlocker == actual.IsBusinessBlocker;
    }

    private static void ValidateScanEnrichment(
      IReadOnlyList<Stage03FieldResult> fields,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues)
    {
      if ((fields ?? Array.Empty<Stage03FieldResult>()).Any(field =>
        field == null
        || field.RawIfcStatus != Stage03FieldStatus.NotEvaluated
        || field.FinalIfcStatus != Stage03FieldStatus.NotEvaluated))
      {
        throw InvalidScanEnrichment();
      }
      Stage03FieldResult[] expectedFields = (fields
        ?? Array.Empty<Stage03FieldResult>())
        .Where(RequiresEnrichment)
        .ToArray();
      HbrIfcEnrichmentValue[] actualValues = (enrichmentValues
        ?? Array.Empty<HbrIfcEnrichmentValue>()).ToArray();
      var expected = new Dictionary<string, Stage03FieldResult>(
        StringComparer.Ordinal);
      foreach (Stage03FieldResult field in expectedFields)
      {
        string identity = EnrichmentIdentity(field);
        if (identity.Length == 0 || expected.ContainsKey(identity))
          throw InvalidScanEnrichment();
        expected.Add(identity, field);
      }
      if (expected.Count != actualValues.Length)
        throw InvalidScanEnrichment();
      foreach (HbrIfcEnrichmentValue value in actualValues)
      {
        if (value == null
          || !expected.TryGetValue(
            value.PropertyIdentity ?? string.Empty,
            out Stage03FieldResult field)
          || string.IsNullOrWhiteSpace(field.Entity)
          || string.IsNullOrWhiteSpace(field.PropertySet)
          || string.IsNullOrWhiteSpace(field.IfcProperty)
          || !string.Equals(
            value.OwnerEntityType,
            field.Entity,
            StringComparison.Ordinal)
          || !string.Equals(
            value.PropertySetName,
            field.PropertySet,
            StringComparison.Ordinal)
          || !string.Equals(
            value.PropertyName,
            field.IfcProperty,
            StringComparison.Ordinal)
          || !string.Equals(
            value.CanonicalValue,
            field.RevitNormalizedValue,
            StringComparison.Ordinal)
          || string.IsNullOrWhiteSpace(value.DeclaredIfcType)
          || string.IsNullOrWhiteSpace(value.SemanticKey)
          || !SupportedOwnerStrategy(value))
        {
          throw InvalidScanEnrichment();
        }
        HbrIfcCanonicalValueDecision canonical =
          HbrIfcCanonicalValuePolicy.Validate(
            value.DeclaredIfcType,
            value.CanonicalValue);
        if (!canonical.Success
          || !string.Equals(
            canonical.NormalizedValue,
            value.CanonicalValue,
            StringComparison.Ordinal))
        {
          throw InvalidScanEnrichment();
        }
      }
    }

    private static void ValidateTranslatedFieldEvidence(
      IReadOnlyList<Stage03FieldResult> scanned,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues,
      IReadOnlyList<Stage03FieldResult> translated)
    {
      var translatedByIdentity = translated.ToDictionary(
        FieldIdentity,
        StringComparer.Ordinal);
      var enrichmentByIdentity = enrichmentValues.ToDictionary(
        value => value.PropertyIdentity,
        StringComparer.Ordinal);
      foreach (Stage03FieldResult scannedField in scanned)
      {
        Stage03FieldResult translatedField =
          translatedByIdentity[FieldIdentity(scannedField)];
        bool hasEnrichment = enrichmentByIdentity.TryGetValue(
          EnrichmentIdentity(scannedField),
          out HbrIfcEnrichmentValue enrichment);
        if (!hasEnrichment)
        {
          if (!CanOmitTranslationEvidence(scannedField)
            || !HasNoTranslationEvidence(translatedField))
          {
            throw InvalidTranslationEvidence();
          }
          continue;
        }
        if (translatedField.RawIfcStatus == Stage03FieldStatus.NotEvaluated
          || translatedField.FinalIfcStatus != Stage03FieldStatus.Pass
          || string.IsNullOrWhiteSpace(translatedField.FinalIfcOwner)
          || !string.Equals(
            translatedField.FinalIfcPropertySet,
            enrichment.PropertySetName,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.FinalIfcProperty,
            enrichment.PropertyName,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.FinalIfcType,
            enrichment.DeclaredIfcType,
            StringComparison.Ordinal)
          || !string.Equals(
            translatedField.FinalIfcValue,
            enrichment.CanonicalValue,
            StringComparison.Ordinal))
        {
          throw InvalidTranslationEvidence();
        }
        if (translatedField.RawIfcStatus == Stage03FieldStatus.Pass
          && (string.IsNullOrWhiteSpace(translatedField.RawIfcOwner)
            || !string.Equals(
              translatedField.RawIfcPropertySet,
              enrichment.PropertySetName,
              StringComparison.Ordinal)
            || !string.Equals(
              translatedField.RawIfcProperty,
              enrichment.PropertyName,
              StringComparison.Ordinal)
            || !string.Equals(
              translatedField.RawIfcType,
              enrichment.DeclaredIfcType,
              StringComparison.Ordinal)
            || !string.Equals(
              translatedField.RawIfcValue,
              enrichment.CanonicalValue,
              StringComparison.Ordinal)))
        {
          throw InvalidTranslationEvidence();
        }
      }
    }

    private static bool CanOmitTranslationEvidence(Stage03FieldResult field)
    {
      if (field == null || RequiresEnrichment(field)) return false;
      if (!field.Active)
        return field.Status == Stage03FieldStatus.NotApplicable;
      return (field.IsBusinessBlocker
          || field.Status == Stage03FieldStatus.UnclassifiedRequirement)
        && field.Status != Stage03FieldStatus.Pass
        && field.Status != Stage03FieldStatus.NotApplicable;
    }

    private static bool HasNoTranslationEvidence(Stage03FieldResult field)
    {
      return field != null
        && field.RawIfcStatus == Stage03FieldStatus.NotEvaluated
        && field.FinalIfcStatus == Stage03FieldStatus.NotEvaluated
        && string.IsNullOrEmpty(field.RawIfcOwner)
        && string.IsNullOrEmpty(field.RawIfcPropertySet)
        && string.IsNullOrEmpty(field.RawIfcProperty)
        && string.IsNullOrEmpty(field.RawIfcType)
        && string.IsNullOrEmpty(field.RawIfcValue)
        && string.IsNullOrEmpty(field.FinalIfcOwner)
        && string.IsNullOrEmpty(field.FinalIfcPropertySet)
        && string.IsNullOrEmpty(field.FinalIfcProperty)
        && string.IsNullOrEmpty(field.FinalIfcType)
        && string.IsNullOrEmpty(field.FinalIfcValue);
    }

    private static Exception InvalidTranslationEvidence()
    {
      return Failure(
        Stage03TechnicalFatalCodes.InvalidIfc,
        "translate-ifc",
        new InvalidDataException("转译字段缺少完整且一致的 IFC 检查证据。"));
    }

    private static bool RequiresEnrichment(Stage03FieldResult field)
    {
      return field != null
        && field.Active
        && field.CarrierStatus == Stage03FieldStatus.Pass
        && field.ParameterStatus == Stage03FieldStatus.Pass
        && field.RevitStatus == Stage03FieldStatus.Pass
        && !string.IsNullOrEmpty(field.RevitNormalizedValue)
        && (field.Status == Stage03FieldStatus.Pass
          || field.Status == Stage03FieldStatus.UnclassifiedRequirement);
    }

    private static string EnrichmentIdentity(Stage03FieldResult field)
    {
      if (field == null) return string.Empty;
      return (field.PropertyId ?? string.Empty) + "|"
        + (field.Role ?? string.Empty) + "|"
        + (field.OwnerUniqueId ?? string.Empty);
    }

    private static bool SupportedOwnerStrategy(HbrIfcEnrichmentValue value)
    {
      if (value == null) return false;
      if (string.Equals(
        value.OwnerStrategy,
        HbrIfcOwnerStrategies.GlobalId,
        StringComparison.Ordinal))
      {
        return !string.IsNullOrWhiteSpace(value.OwnerGlobalId);
      }
      return string.Equals(
        value.OwnerStrategy,
        HbrIfcOwnerStrategies.SingleEntityByType,
        StringComparison.Ordinal);
    }

    private static Exception InvalidScanEnrichment()
    {
      return Failure(
        Stage03TechnicalFatalCodes.InvalidFieldStatus,
        "scan-enrichment",
        new InvalidDataException(
          "扫描字段与 IFC enrichment 快照不一致。"));
    }

    private static string FieldIdentity(Stage03FieldResult field)
    {
      if (field == null) return "<null>";
      return IdentitySegment(field.PropertyId)
        + IdentitySegment(field.Role)
        + IdentitySegment(field.OwnerUniqueId)
        + IdentitySegment(field.ElementId.ToString(CultureInfo.InvariantCulture));
    }

    private static string IdentitySegment(string value)
    {
      string text = value ?? string.Empty;
      return text.Length.ToString(CultureInfo.InvariantCulture)
        + ":"
        + text;
    }

    private static IReadOnlyList<Stage03CarrierResult> SnapshotCarriers(
      IEnumerable<Stage03CarrierResult> values,
      string technicalCode,
      string stage)
    {
      if (values == null || values.Any(value => value == null))
      {
        throw Failure(
          technicalCode,
          stage,
          new InvalidDataException("载体快照不能为 null 或包含 null。"));
      }
      return Freeze(values.Select(CloneCarrier));
    }

    private static IReadOnlyList<Stage03FieldResult> SnapshotFields(
      IEnumerable<Stage03FieldResult> values,
      string technicalCode,
      string stage)
    {
      if (values == null || values.Any(value => value == null))
      {
        throw Failure(
          technicalCode,
          stage,
          new InvalidDataException("字段快照不能为 null 或包含 null。"));
      }
      return Freeze(values.Select(CloneField));
    }

    private static IReadOnlyList<Stage03Diagnostic> SnapshotDiagnostics(
      IEnumerable<Stage03Diagnostic> values,
      string technicalCode,
      string stage)
    {
      if (values == null || values.Any(value => value == null))
      {
        throw Failure(
          technicalCode,
          stage,
          new InvalidDataException("诊断快照不能为 null 或包含 null。"));
      }
      return Freeze(values.Select(CloneDiagnostic));
    }

    private static IReadOnlyList<HbrIfcEnrichmentValue>
      SnapshotEnrichmentValues(IEnumerable<HbrIfcEnrichmentValue> values)
    {
      if (values == null || values.Any(value => value == null))
      {
        throw Failure(
          Stage03TechnicalFatalCodes.InvalidFieldStatus,
          "scan-revit-host",
          new InvalidDataException("IFC enrichment 快照不能为 null 或包含 null。"));
      }
      HbrIfcEnrichmentValue[] snapshot = values
        .Select(CloneEnrichmentValue)
        .ToArray();
      string[] identities = snapshot
        .Select(value => value.PropertyIdentity ?? string.Empty)
        .ToArray();
      if (identities.Any(string.IsNullOrWhiteSpace)
        || identities.Length != identities.Distinct(StringComparer.Ordinal).Count())
      {
        throw Failure(
          Stage03TechnicalFatalCodes.InvalidFieldStatus,
          "scan-revit-host",
          new InvalidDataException("IFC enrichment PropertyIdentity 必须非空且唯一。"));
      }
      return Freeze(snapshot);
    }

    internal static Stage03CarrierResult CloneCarrier(
      Stage03CarrierResult value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));
      return new Stage03CarrierResult
      {
        Entity = value.Entity ?? string.Empty,
        Role = value.Role ?? string.Empty,
        ElementId = value.ElementId,
        UniqueId = value.UniqueId ?? string.Empty,
        Category = value.Category ?? string.Empty,
        Name = value.Name ?? string.Empty,
        Status = value.Status,
        Active = value.Active,
        IsBusinessBlocker = value.IsBusinessBlocker,
        Messages = Freeze((value.Messages ?? Array.Empty<string>())
          .Select(message => message ?? string.Empty))
      };
    }

    internal static Stage03FieldResult CloneField(Stage03FieldResult value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));
      return new Stage03FieldResult
      {
        PropertyId = value.PropertyId ?? string.Empty,
        ContractKind = value.ContractKind ?? string.Empty,
        Requirement = value.Requirement ?? string.Empty,
        Applicability = value.Applicability ?? string.Empty,
        Entity = value.Entity ?? string.Empty,
        PropertySet = value.PropertySet ?? string.Empty,
        IfcProperty = value.IfcProperty ?? string.Empty,
        Role = value.Role ?? string.Empty,
        ElementId = value.ElementId,
        OwnerUniqueId = value.OwnerUniqueId ?? string.Empty,
        ParameterGuid = value.ParameterGuid ?? string.Empty,
        ParameterName = value.ParameterName ?? string.Empty,
        ParameterScope = value.ParameterScope ?? string.Empty,
        CarrierStatus = value.CarrierStatus,
        ParameterStatus = value.ParameterStatus,
        RevitStatus = value.RevitStatus,
        RevitRawValue = value.RevitRawValue ?? string.Empty,
        RevitNormalizedValue = value.RevitNormalizedValue ?? string.Empty,
        RevitValueSource = value.RevitValueSource ?? string.Empty,
        RawIfcOwner = value.RawIfcOwner ?? string.Empty,
        RawIfcPropertySet = value.RawIfcPropertySet ?? string.Empty,
        RawIfcProperty = value.RawIfcProperty ?? string.Empty,
        RawIfcType = value.RawIfcType ?? string.Empty,
        RawIfcValue = value.RawIfcValue ?? string.Empty,
        RawIfcStatus = value.RawIfcStatus,
        FinalIfcOwner = value.FinalIfcOwner ?? string.Empty,
        FinalIfcPropertySet = value.FinalIfcPropertySet ?? string.Empty,
        FinalIfcProperty = value.FinalIfcProperty ?? string.Empty,
        FinalIfcType = value.FinalIfcType ?? string.Empty,
        FinalIfcValue = value.FinalIfcValue ?? string.Empty,
        FinalIfcStatus = value.FinalIfcStatus,
        Status = value.Status,
        Active = value.Active,
        IsBusinessBlocker = value.IsBusinessBlocker,
        Messages = Freeze((value.Messages ?? Array.Empty<string>())
          .Select(message => message ?? string.Empty))
      };
    }

    internal static Stage03Diagnostic CloneDiagnostic(Stage03Diagnostic value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));
      return new Stage03Diagnostic
      {
        Code = value.Code ?? string.Empty,
        Stage = value.Stage ?? string.Empty,
        Severity = value.Severity ?? string.Empty,
        Message = value.Message ?? string.Empty
      };
    }

    internal static HbrIfcEnrichmentValue CloneEnrichmentValue(
      HbrIfcEnrichmentValue value)
    {
      return new HbrIfcEnrichmentValue
      {
        OwnerEntityType = value.OwnerEntityType ?? string.Empty,
        OwnerGlobalId = value.OwnerGlobalId ?? string.Empty,
        OwnerStrategy = value.OwnerStrategy ?? string.Empty,
        PropertySetName = value.PropertySetName ?? string.Empty,
        PropertyName = value.PropertyName ?? string.Empty,
        DeclaredIfcType = value.DeclaredIfcType ?? string.Empty,
        CanonicalValue = value.CanonicalValue ?? string.Empty,
        PropertyIdentity = value.PropertyIdentity ?? string.Empty,
        SemanticKey = value.SemanticKey ?? string.Empty
      };
    }

    private static string ComputeSha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read))
      {
        return string.Concat(algorithm.ComputeHash(stream)
          .Select(value => value.ToString("x2")));
      }
    }

    private static bool IsSha256(string value)
    {
      return value != null
        && value.Length == 64
        && value.All(Uri.IsHexDigit);
    }

    private DateTimeOffset SafeUtcNow()
    {
      DateTimeOffset value = _utcNow();
      return value.ToUniversalTime();
    }

    private DateTimeOffset FailureUtcNow()
    {
      try
      {
        return SafeUtcNow();
      }
      catch
      {
        return DateTimeOffset.UtcNow;
      }
    }

    private static T RequireDelegate<T>(T value, string name)
      where T : class
    {
      if (value == null) throw new ArgumentNullException(name);
      return value;
    }

    private static WorkflowFailure Failure(
      string technicalCode,
      string rootCauseStage,
      Exception exception)
    {
      return new WorkflowFailure(
        technicalCode,
        rootCauseStage,
        exception ?? new InvalidOperationException("Stage03 未知失败。"));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }

    private sealed class Artifact
    {
      internal Artifact(string path, long length, string sha256)
      {
        Path = path;
        Length = length;
        Sha256 = sha256;
      }

      internal string Path { get; }
      internal long Length { get; }
      internal string Sha256 { get; }
    }

    private sealed class WorkflowFailure : Exception
    {
      internal WorkflowFailure(
        string technicalCode,
        string rootCauseStage,
        Exception innerException)
        : base(
          innerException == null ? "Stage03 未知失败。" : innerException.Message,
          innerException)
      {
        TechnicalCode = string.IsNullOrWhiteSpace(technicalCode)
          ? Stage03TechnicalFatalCodes.DocumentUnavailable
          : technicalCode.Trim().ToUpperInvariant();
        RootCauseStage = string.IsNullOrWhiteSpace(rootCauseStage)
          ? "unknown-stage"
          : rootCauseStage.Trim();
      }

      internal string TechnicalCode { get; }
      internal string RootCauseStage { get; }
    }

    private sealed class ScanSnapshot
    {
      internal ScanSnapshot(
        string fileGuid,
        string documentFingerprint,
        string documentTitle,
        string documentPath,
        string revitVersion,
        string rulePackageId,
        string rulePackageVersion,
        string rulePackageSha256,
        IReadOnlyList<Stage03CarrierResult> carriers,
        IReadOnlyList<Stage03FieldResult> fields,
        IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues,
        IReadOnlyList<string> technicalFatalCodes,
        IReadOnlyList<Stage03Diagnostic> diagnostics)
      {
        FileGuid = fileGuid ?? string.Empty;
        DocumentFingerprint = documentFingerprint ?? string.Empty;
        DocumentTitle = documentTitle ?? string.Empty;
        DocumentPath = documentPath ?? string.Empty;
        RevitVersion = revitVersion ?? string.Empty;
        RulePackageId = rulePackageId ?? string.Empty;
        RulePackageVersion = rulePackageVersion ?? string.Empty;
        RulePackageSha256 = rulePackageSha256 ?? string.Empty;
        Carriers = carriers;
        Fields = fields;
        EnrichmentValues = enrichmentValues;
        TechnicalFatalCodes = technicalFatalCodes;
        Diagnostics = diagnostics;
      }

      internal string FileGuid { get; }
      internal string DocumentFingerprint { get; }
      internal string DocumentTitle { get; }
      internal string DocumentPath { get; }
      internal string RevitVersion { get; }
      internal string RulePackageId { get; }
      internal string RulePackageVersion { get; }
      internal string RulePackageSha256 { get; }
      internal IReadOnlyList<Stage03CarrierResult> Carriers { get; }
      internal IReadOnlyList<Stage03FieldResult> Fields { get; }
      internal IReadOnlyList<HbrIfcEnrichmentValue> EnrichmentValues { get; }
      internal IReadOnlyList<string> TechnicalFatalCodes { get; }
      internal IReadOnlyList<Stage03Diagnostic> Diagnostics { get; }
    }
  }
}
