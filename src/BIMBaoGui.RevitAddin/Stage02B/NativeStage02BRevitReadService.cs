using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BRevitReadService
  {
    internal static NativeStage02BReadResult Read(UIApplication application)
    {
      NativeStage02BRevitContext context = NativeStage02BRevitContext.Create(
        application);
      NativeStage02BStorageSnapshot snapshot = NativeStage02BStorage.Read(
        context.Document);
      NativeWorkflowResultEnvelope workflow = null;
      try
      {
        NativeWorkflowResultEnvelope stored = NativeWorkflowResultStorage.Read(
          context.Document, "STAGE02B");
        if (stored != null && NativeWorkflowFreshnessPolicy.Evaluate(
          stored, context.Identity, snapshot.SnapshotHash).State
          == NativeWorkflowFreshnessState.Current)
          workflow = stored;
      }
      catch
      {
        workflow = null;
      }
      return new NativeStage02BReadResult
      {
        Identity = context.Identity,
        Records = snapshot.Records,
        WorkflowResult = workflow,
        Issues = Array.Empty<NativeIssueRecord>()
      };
    }
  }

  internal sealed class NativeStage02BRevitContext
  {
    internal Document Document { get; private set; }
    internal NativeWorkflowIdentity Identity { get; private set; }

    internal static NativeStage02BRevitContext Create(UIApplication application)
    {
      if (application == null) throw new ArgumentNullException(nameof(application));
      Document document = application.ActiveUIDocument?.Document
        ?? throw new InvalidOperationException("STAGE02B_DOCUMENT_REQUIRED");
      if (!string.Equals(application.Application.VersionNumber,
          "2020", StringComparison.Ordinal)
        || document.IsFamilyDocument
        || document.IsReadOnly
        || string.IsNullOrWhiteSpace(document.PathName))
        throw new InvalidOperationException("STAGE02B_DOCUMENT_NOT_WRITABLE");
      NativeStage01ReadResult stage01 = NativeStage01RevitReadService.Read(
        application);
      if (stage01?.StorageDecision?.State != NativeStage01StorageState.Current
        || stage01.Model == null)
        throw new InvalidOperationException("STAGE02B_STAGE01_NOT_CURRENT");
      string modelFileType = stage01.Model.GetValue(
        NativeStage01Keys.ModelFileType);
      if (!string.Equals(modelFileType, "总平模型", StringComparison.Ordinal))
        throw new InvalidOperationException("STAGE02B_PROFILE_UNSUPPORTED");
      NativeStoredInitialization stored = NativeStage01Storage.Read(document);
      if (stored == null || !stored.HasRecord
        || string.IsNullOrWhiteSpace(stored.FileGuid)
        || string.IsNullOrWhiteSpace(stage01.StorageDecision.ActualPayloadHash))
        throw new InvalidOperationException("STAGE02B_STAGE01_IDENTITY_MISSING");
      NativeWorkflowIdentity identity = NativeWorkflowIdentityFactory.Create(
        application,
        modelFileType,
        stored.FileGuid,
        stage01.StorageDecision.ActualPayloadHash,
        NativeRuleCatalog.Current.Identity);
      return new NativeStage02BRevitContext
      {
        Document = document,
        Identity = identity
      };
    }
  }
}
