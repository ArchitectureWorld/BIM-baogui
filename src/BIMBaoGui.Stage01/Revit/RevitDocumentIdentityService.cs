using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class RevitDocumentIdentity
  {
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string RevitVersion { get; set; } = string.Empty;
    internal bool IsFamilyDocument { get; set; }
    internal bool IsReadOnly { get; set; }
    internal StoredInitialization StoredInitialization { get; set; }
    internal Stage01StorageDecision StorageDecision { get; set; }
    internal Stage01StoredPayloadIntegrityDecision PayloadIntegrityDecision
    {
      get;
      set;
    }
  }

  internal static class RevitDocumentIdentityService
  {
    internal static RevitDocumentIdentity Read(
      UIApplication uiApplication,
      Document document)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (document == null) throw new ArgumentNullException(nameof(document));
      string version = uiApplication.Application.VersionNumber ?? string.Empty;
      StoredInitialization stored = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision =
        Stage01StorageStatePolicy.Evaluate(
          stored != null,
          stored == null ? string.Empty : stored.PayloadJson,
          stored == null ? string.Empty : stored.PayloadHash,
          stored == null ? string.Empty : stored.FileGuid,
          stored == null ? string.Empty : stored.WorkflowVersion,
          HBRContextVersions.FileContextSchema);
      Stage01StoredPayloadIntegrityDecision payloadIntegrity =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          stored == null ? string.Empty : stored.PayloadJson,
          stored == null ? string.Empty : stored.PayloadHash);
      return new RevitDocumentIdentity
      {
        DocumentTitle = document.Title ?? string.Empty,
        DocumentPath = document.PathName ?? string.Empty,
        RevitVersion = version,
        DocumentFingerprint = HBRDocumentFingerprint.Compute(
          document.PathName,
          document.Title,
          version),
        IsFamilyDocument = document.IsFamilyDocument,
        IsReadOnly = document.IsReadOnly,
        StoredInitialization = stored,
        StorageDecision = storageDecision,
        PayloadIntegrityDecision = payloadIntegrity
      };
    }

    internal static IReadOnlyList<string> Validate(
      HBRFileContext context,
      RevitDocumentIdentity identity)
    {
      var blockers = new List<string>();
      if (context == null)
      {
        blockers.Add("缺少 HBRFileContext，不能确认当前 Revit 文档身份。");
        return blockers;
      }
      if (identity == null)
      {
        blockers.Add("无法读取当前 Revit 文档身份。");
        return blockers;
      }
      if (!string.Equals(identity.RevitVersion, "2020", StringComparison.Ordinal))
        blockers.Add("Stage02 仅支持 Revit 2020。");
      if (identity.IsFamilyDocument)
        blockers.Add("族文档不支持 Stage02 项目参数绑定。");
      if (identity.IsReadOnly)
        blockers.Add("DOCUMENT_READ_ONLY：当前 Revit 文档为只读，不能生成可确认预览。");
      if (string.IsNullOrWhiteSpace(identity.DocumentPath))
        blockers.Add("当前 RVT 尚未保存，不能建立稳定文档身份。");
      if (!string.Equals(
        context.RevitDocumentFingerprint,
        identity.DocumentFingerprint,
        StringComparison.Ordinal))
      {
        blockers.Add("当前 Revit 文档指纹与文件上下文不一致。");
      }
      if (!string.Equals(
        context.RevitDocumentTitle,
        identity.DocumentTitle,
        StringComparison.Ordinal))
      {
        blockers.Add("当前 Revit 文档标题与文件上下文不一致。");
      }

      StoredInitialization stored = identity.StoredInitialization;
      if (identity.PayloadIntegrityDecision == null
        || !identity.PayloadIntegrityDecision.Success)
      {
        string code = identity.PayloadIntegrityDecision == null
          ? Stage01StoredPayloadIntegrityPolicy.CorruptStorageCode
          : identity.PayloadIntegrityDecision.ErrorCode;
        string message = identity.PayloadIntegrityDecision == null
          ? "Stage01 初始化载荷完整性状态不可用。"
          : identity.PayloadIntegrityDecision.Message;
        blockers.Add(code + "：" + message);
      }
      blockers.AddRange(HBRLiveContextPolicy.Validate(
        context.FileGuid,
        context.SourcePayloadHash,
        identity.StorageDecision != null
          && identity.StorageDecision.IsInitialized,
        stored == null ? string.Empty : stored.FileGuid,
        stored == null ? string.Empty : stored.PayloadHash,
        stored == null ? string.Empty : stored.WorkflowVersion));
      return blockers;
    }
  }
}
