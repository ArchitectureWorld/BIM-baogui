using System;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Stage03
{
  internal static class Stage03ProductionWorkflowServices
  {
    internal static Stage03WorkflowServices Create()
    {
      var revit = new Stage03RevitPhaseService(HbrRuleDatabase.Current);
      var translator = new Stage03IfcTranslationService();
      return new Stage03WorkflowServices
      {
        ScanAsync = request => ScanAsync(revit, request),
        ExportRawAsync = request => ExportRawAsync(revit, request),
        TranslateAsync = translator.TranslateAsync,
        WriteFieldReport = Stage03FieldReportWriter.Write,
        WriteFailureReport = Stage03FailureReportWriter.TryWrite,
        UtcNow = () => DateTimeOffset.UtcNow
      };
    }

    private static async Task<Stage03WorkflowScanResult> ScanAsync(
      Stage03RevitPhaseService revit,
      Stage03WorkflowRequest request)
    {
      if (revit == null) throw new ArgumentNullException(nameof(revit));
      if (request == null) throw new ArgumentNullException(nameof(request));
      Stage03ModelScanResult scan = await revit.ScanInHostContext(
        new Stage03RevitScanRequest(request.Context)).ConfigureAwait(false);
      if (scan == null)
        throw new InvalidOperationException(
          "Stage03 Revit 扫描未返回结果。");
      return new Stage03WorkflowScanResult
      {
        FileGuid = scan.FileGuid,
        DocumentFingerprint = scan.DocumentFingerprint,
        DocumentTitle = scan.DocumentTitle,
        DocumentPath = scan.DocumentPath,
        RevitVersion = scan.RevitVersion,
        RulePackageId = scan.RulePackageId,
        RulePackageVersion = scan.RulePackageVersion,
        RulePackageSha256 = scan.RulePackageSha256,
        Carriers = scan.Carriers,
        Fields = scan.Fields,
        EnrichmentValues = scan.EnrichmentValues,
        TechnicalFatalCodes = scan.TechnicalFatalCodes,
        Diagnostics = scan.Diagnostics
      };
    }

    private static async Task<Stage03WorkflowRawExportResult> ExportRawAsync(
      Stage03RevitPhaseService revit,
      Stage03WorkflowExportRequest request)
    {
      if (revit == null) throw new ArgumentNullException(nameof(revit));
      if (request == null) throw new ArgumentNullException(nameof(request));
      AutodeskIfcExportResult export = await revit.ExportInHostContext(
        new Stage03RevitExportRequest(
          request.Context.RevitDocumentFingerprint,
          request.Context.RevitDocumentTitle,
          request.Context.RulePackageId,
          request.Context.RulePackageVersion,
          request.Context.RulePackageSha256,
          request.RawIfcPath)).ConfigureAwait(false);
      if (export == null)
        throw new InvalidOperationException(
          "Stage03 Autodesk IFC4 导出未返回结果。");
      return new Stage03WorkflowRawExportResult
      {
        RawIfcPath = export.RawIfcPath,
        RawIfcLength = export.RawIfcLength,
        RawIfcSha256 = export.RawIfcSha256
      };
    }
  }
}
