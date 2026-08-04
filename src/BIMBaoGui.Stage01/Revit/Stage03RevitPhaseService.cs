using System;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage03RevitPhaseService
  {
    private static readonly TimeSpan CallbackStartTimeout =
      TimeSpan.FromMinutes(2);

    private readonly HbrRuleDatabase _database;
    private readonly Stage03ModelScanService _scanner;
    private readonly AutodeskIfcExportService _exporter;

    internal Stage03RevitPhaseService(HbrRuleDatabase database)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
      _scanner = new Stage03ModelScanService(database);
      _exporter = new AutodeskIfcExportService();
    }

    internal Task<Stage03ModelScanResult> ScanInHostContext(
      Stage03RevitScanRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return RunInHostContext(
        "Stage03 Revit 扫描请求",
        uiApplication =>
      {
        Document document = RequireActiveDocument(uiApplication);
        ValidateRequestIdentity(
          uiApplication,
          document,
          request.DocumentFingerprint,
          request.DocumentTitle,
          request.RulePackageId,
          request.RulePackageVersion,
          request.RulePackageSha256);
        return _scanner.Scan(
          uiApplication,
          document,
          request.Context);
      });
    }

    internal Task<AutodeskIfcExportResult> ExportInHostContext(
      Stage03RevitExportRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return RunInHostContext(
        "Stage03 Autodesk IFC4 导出请求",
        uiApplication =>
      {
        Document document = RequireActiveDocument(uiApplication);
        ValidateRequestIdentity(
          uiApplication,
          document,
          request.DocumentFingerprint,
          request.DocumentTitle,
          request.RulePackageId,
          request.RulePackageVersion,
          request.RulePackageSha256);
        return _exporter.Export(document, request.RawIfcPath);
      });
    }

    private Task<TResult> RunInHostContext<TResult>(
      string operationName,
      Func<UIApplication, TResult> operation)
    {
      if (operation == null) throw new ArgumentNullException(nameof(operation));

      var completion = new TaskCompletionSource<TResult>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      var startGate = new Stage03HostCallbackStartGate();
      Timer callbackStartTimer = null;

      Action disposeCallbackStartTimer = () =>
        Interlocked.Exchange(ref callbackStartTimer, null)?.Dispose();
      Action<Exception> hostFailure = exception =>
      {
        if (!startGate.TryAbandon()) return;
        disposeCallbackStartTimer();
        completion.TrySetException(exception);
      };

      callbackStartTimer = new Timer(
        _ =>
        {
          if (!startGate.TryAbandon()) return;
          disposeCallbackStartTimer();
          completion.TrySetException(new TimeoutException(
            operationName + "已提交，但 host callback 在超时前未开始。"));
        },
        null,
        CallbackStartTimeout,
        Timeout.InfiniteTimeSpan);

      try
      {
        bool enqueued = RevitHost.EnqueueAction(
          uiApplication =>
          {
            if (!startGate.TryStart()) return;
            disposeCallbackStartTimer();
            try
            {
              completion.TrySetResult(operation(uiApplication));
            }
            catch (Exception exception)
            {
              completion.TrySetException(exception);
            }
          },
          hostFailure,
          out string enqueueError);
        if (!enqueued)
        {
          hostFailure(new InvalidOperationException(
            string.IsNullOrWhiteSpace(enqueueError)
              ? operationName + "未能进入 host callback。"
              : enqueueError));
        }
      }
      catch (Exception exception)
      {
        hostFailure(exception);
      }

      return completion.Task;
    }

    private void ValidateRequestIdentity(
      UIApplication uiApplication,
      Document document,
      string expectedFingerprint,
      string expectedTitle,
      string expectedRulePackageId,
      string expectedRulePackageVersion,
      string expectedRulePackageSha256)
    {
      RevitDocumentIdentity identity = RevitDocumentIdentityService.Read(
        uiApplication,
        document);
      Stage03RevitRequestIdentityDecision documentDecision =
        Stage03RevitRequestIdentityPolicy.Evaluate(
          expectedFingerprint,
          expectedTitle,
          identity.DocumentFingerprint,
          identity.DocumentTitle);
      if (!documentDecision.Success)
        throw new InvalidOperationException(documentDecision.Message);

      HbrRulePackage package = _database.Package;
      Stage03RevitRequestIdentityDecision packageDecision =
        Stage03RevitRequestRulePackagePolicy.Evaluate(
          expectedRulePackageId,
          expectedRulePackageVersion,
          expectedRulePackageSha256,
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256);
      if (!packageDecision.Success)
        throw new InvalidOperationException(packageDecision.Message);
    }

    private static Document RequireActiveDocument(
      UIApplication uiApplication)
    {
      if (uiApplication == null)
        throw new InvalidOperationException(
          "Revit host callback 未提供 UIApplication。");
      UIDocument uiDocument = uiApplication.ActiveUIDocument;
      Document document = uiDocument == null ? null : uiDocument.Document;
      if (document == null)
        throw new InvalidOperationException(
          "Revit host callback 当前没有活动 Document。");
      return document;
    }
  }
}
