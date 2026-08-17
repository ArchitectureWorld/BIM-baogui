using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal static class NativeRevitIssueNavigationService
  {
    internal static NativeIssueNavigationResult Execute(
      UIApplication application,
      NativeIssueNavigationRequest request)
    {
      if (application == null) throw new ArgumentNullException(nameof(application));
      NativeIssueNavigationRequest safeRequest = request?.Clone()
        ?? new NativeIssueNavigationRequest();
      UIDocument uiDocument = application.ActiveUIDocument;
      Document document = uiDocument?.Document;
      if (document == null)
        return Failure(safeRequest.Action, "ISSUE_DOCUMENT_MISSING");

      string currentDocumentFingerprint;
      try
      {
        currentDocumentFingerprint = CurrentDocumentFingerprint(
          application,
          document);
      }
      catch
      {
        return Failure(
          safeRequest.Action,
          "ISSUE_DOCUMENT_FINGERPRINT_UNAVAILABLE");
      }
      NativeIssueNavigationDecision decision =
        NativeIssueNavigationPolicy.Evaluate(
          safeRequest,
          currentDocumentFingerprint);
      if (!decision.Allowed)
        return Failure(safeRequest.Action, decision.Code);

      var elementIds = new List<ElementId>();
      foreach (NativeIssueElementReference reference in
        decision.ResolvedElements)
      {
        Element live = document.GetElement(reference.UniqueId);
        if (live == null || live.Id.IntegerValue != reference.ElementId)
          return Failure(safeRequest.Action, "ISSUE_ELEMENT_STALE");
        elementIds.Add(live.Id);
      }

      try
      {
        switch (safeRequest.Action)
        {
          case NativeIssueNavigationAction.Select:
            uiDocument.Selection.SetElementIds(elementIds);
            break;
          case NativeIssueNavigationAction.Zoom:
            uiDocument.ShowElements(elementIds);
            break;
          case NativeIssueNavigationAction.Isolate:
            using (Transaction transaction = new Transaction(
              document,
              "HBR 问题中心隔离构件"))
            {
              if (transaction.Start() != TransactionStatus.Started)
                return Failure(safeRequest.Action, "ISSUE_TRANSACTION_FAILED");
              document.ActiveView.IsolateElementsTemporary(elementIds);
              if (transaction.Commit() != TransactionStatus.Committed)
                return Failure(safeRequest.Action, "ISSUE_TRANSACTION_FAILED");
            }
            break;
          case NativeIssueNavigationAction.RestoreView:
            using (Transaction transaction = new Transaction(
              document,
              "HBR 问题中心恢复视图"))
            {
              if (transaction.Start() != TransactionStatus.Started)
                return Failure(safeRequest.Action, "ISSUE_TRANSACTION_FAILED");
              document.ActiveView.DisableTemporaryViewMode(
                TemporaryViewMode.TemporaryHideIsolate);
              if (transaction.Commit() != TransactionStatus.Committed)
                return Failure(safeRequest.Action, "ISSUE_TRANSACTION_FAILED");
            }
            break;
          default:
            return Failure(safeRequest.Action, "ISSUE_ACTION_UNSUPPORTED");
        }
      }
      catch
      {
        return Failure(safeRequest.Action, "ISSUE_NAVIGATION_FAILED");
      }

      return new NativeIssueNavigationResult
      {
        Succeeded = true,
        Code = "OK",
        Action = safeRequest.Action,
        AffectedElementIds = new ReadOnlyCollection<int>(
          elementIds.Select(value => value.IntegerValue).ToArray())
      };
    }

    private static string CurrentDocumentFingerprint(
      UIApplication application,
      Document document)
    {
      NativeStage01ReadResult stage01 = NativeStage01RevitReadService.Read(
        application);
      if (stage01?.Model == null || stage01.StorageDecision == null)
        throw new InvalidOperationException(
          "当前文档没有可用于问题定位的 Stage01 身份。" );
      return NativeWorkflowIdentityFactory.ComputeDocumentFingerprint(
        document.PathName,
        document.Title,
        application.Application.VersionNumber,
        stage01.Model.GetValue(NativeStage01Keys.FileGuid),
        stage01.StorageDecision.ActualPayloadHash);
    }

    internal static bool TryCurrentDocumentFingerprint(
      UIApplication application,
      out string documentFingerprint)
    {
      documentFingerprint = string.Empty;
      Document document = application?.ActiveUIDocument?.Document;
      if (document == null) return false;
      try
      {
        documentFingerprint = CurrentDocumentFingerprint(
          application,
          document);
        return !string.IsNullOrWhiteSpace(documentFingerprint);
      }
      catch
      {
        documentFingerprint = string.Empty;
        return false;
      }
    }

    private static NativeIssueNavigationResult Failure(
      NativeIssueNavigationAction action,
      string code)
    {
      return new NativeIssueNavigationResult
      {
        Succeeded = false,
        Code = code ?? string.Empty,
        Action = action,
        AffectedElementIds = Array.Empty<int>()
      };
    }
  }
}
