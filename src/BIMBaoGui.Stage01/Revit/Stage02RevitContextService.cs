using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02RevitContextSnapshot
  {
    public bool HostAvailable { get; set; }
    public string RevitVersion { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public string DocumentFingerprint { get; set; } = string.Empty;
    public bool IsInitialized { get; set; }
    public string StoredFileGuid { get; set; } = string.Empty;
    public string StoredPayloadHash { get; set; } = string.Empty;
    public string StoredWorkflowVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal static class Stage02RevitContextService
  {
    public static Stage02RevitContextSnapshot ReadSnapshot()
    {
      if (RevitHost.RunReadInHostContext(ReadSnapshotCore, out Stage02RevitContextSnapshot snapshot, out string error))
        return snapshot;

      return new Stage02RevitContextSnapshot
      {
        Messages = new[] { error }
      };
    }

    private static Stage02RevitContextSnapshot ReadSnapshotCore()
    {
      var snapshot = new Stage02RevitContextSnapshot();
      var messages = new List<string>();
      if (!RevitHost.TryGetContext(out UIApplication uiapp, out _, out Document document, out string hostError))
      {
        snapshot.Messages = new[] { hostError };
        return snapshot;
      }

      snapshot.HostAvailable = true;
      snapshot.RevitVersion = uiapp.Application.VersionNumber ?? string.Empty;
      snapshot.DocumentTitle = document.Title ?? string.Empty;
      snapshot.DocumentPath = document.PathName ?? string.Empty;
      snapshot.DocumentFingerprint = HBRDocumentFingerprint.Compute(
        snapshot.DocumentPath,
        snapshot.DocumentTitle,
        snapshot.RevitVersion);
      StoredInitialization stored = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision = Stage01StorageStatePolicy.Evaluate(
        stored != null,
        stored?.PayloadJson,
        stored?.PayloadHash,
        stored?.FileGuid,
        stored?.WorkflowVersion,
        HBRContextVersions.FileContextSchema);
      snapshot.IsInitialized = storageDecision.IsInitialized;
      snapshot.StoredFileGuid = stored?.FileGuid ?? string.Empty;
      snapshot.StoredPayloadHash = stored?.PayloadHash ?? string.Empty;
      snapshot.StoredWorkflowVersion = stored?.WorkflowVersion ?? string.Empty;

      if (!string.Equals(snapshot.RevitVersion, "2020", StringComparison.Ordinal))
        messages.Add("当前 Revit 版本为 " + snapshot.RevitVersion + "，本组件仅支持 Revit 2020。");
      if (document.IsFamilyDocument)
        messages.Add("当前文档是族文件，不能编译报规模型任务。");
      if (string.IsNullOrWhiteSpace(snapshot.DocumentPath))
        messages.Add("请先保存当前 RVT 文件，再编译任务计划。");

      snapshot.Messages = messages;
      return snapshot;
    }
  }
}
