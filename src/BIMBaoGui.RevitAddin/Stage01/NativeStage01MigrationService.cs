using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01MigrationCodes
  {
    internal const string InvalidPayload = "INVALID_LEGACY_PAYLOAD";
    internal const string VersionIdentityMismatch =
      "LEGACY_VERSION_IDENTITY_MISMATCH";
    internal const string UnsupportedSourceVersion =
      "UNSUPPORTED_LEGACY_SOURCE_VERSION";
    internal const string UnsupportedTargetVersion =
      "UNSUPPORTED_MIGRATION_TARGET_VERSION";
    internal const string CandidateSerializationFailed =
      "MIGRATION_CANDIDATE_SERIALIZATION_FAILED";
  }

  internal sealed class NativeStage01MigrationResult
  {
    internal bool Success { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal string SourceVersion { get; set; } = string.Empty;
    internal string TargetVersion { get; set; } = string.Empty;
    internal string SourcePayloadHash { get; set; } = string.Empty;
    internal string TargetPayloadJson { get; set; } = string.Empty;
    internal string TargetPayloadHash { get; set; } = string.Empty;
    internal NativeStage01Model Model { get; set; }
    internal IReadOnlyList<string> AddedConditionIds { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage01MigrationService
  {
    internal const string SupportedSourceVersion = "0.9.0";

    internal static NativeStage01MigrationResult CreateCandidate(
      NativeStage01Payload payload,
      NativeRuleCatalog catalog)
    {
      return Migrate(
        payload,
        catalog,
        NativeStage01Canonicalizer.PayloadSchemaVersion);
    }

    internal static NativeStage01MigrationResult Migrate(
      NativeStage01Payload payload,
      NativeRuleCatalog catalog,
      string targetVersion)
    {
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      string target = targetVersion ?? string.Empty;
      if (!string.Equals(
        target,
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        StringComparison.Ordinal))
      {
        return Failure(
          NativeStage01MigrationCodes.UnsupportedTargetVersion,
          "Stage01 迁移目标必须是当前 Payload 协议版本。",
          payload?.SchemaVersion,
          target);
      }
      if (payload?.Model == null)
      {
        return Failure(
          NativeStage01MigrationCodes.InvalidPayload,
          "旧版 Stage01 Payload 缺少模型。",
          payload?.SchemaVersion,
          target);
      }

      string source = payload.SchemaVersion ?? string.Empty;
      string envelopeVersion = payload.WorkflowVersion ?? string.Empty;
      string modelVersion = payload.Model.GetValue(
        NativeStage01Keys.WorkflowVersion);
      if (!string.Equals(source, envelopeVersion, StringComparison.Ordinal)
        || !string.Equals(source, modelVersion, StringComparison.Ordinal))
      {
        return Failure(
          NativeStage01MigrationCodes.VersionIdentityMismatch,
          "旧版 Stage01 Payload 的 schema、envelope 与模型版本不一致。",
          source,
          target);
      }
      if (!string.Equals(
        source,
        SupportedSourceVersion,
        StringComparison.Ordinal))
      {
        return Failure(
          NativeStage01MigrationCodes.UnsupportedSourceVersion,
          "当前仅支持 Stage01 Payload 0.9.0 → 0.9.1 迁移。",
          source,
          target);
      }

      string sourceCanonical;
      string sourceHash;
      try
      {
        sourceCanonical = NativeStage01Canonicalizer.ToJson(
          payload.Model,
          source);
        sourceHash = NativeStage01Canonicalizer.Sha256(sourceCanonical);
      }
      catch (Exception exception)
      {
        return Failure(
          NativeStage01MigrationCodes.InvalidPayload,
          "旧版 Stage01 Payload 无法按其原协议规范化："
            + exception.Message,
          source,
          target);
      }

      NativeStage01Model candidate = payload.Model.Clone();
      if (!candidate.Conditions.ContainsKey(
        NativeProjectConditionDeclarationPolicy.NoneConditionId))
      {
        candidate.SetCondition(
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
          false);
      }
      NativeStage01ConditionSchemaReconciliation conditions =
        NativeStage01ConditionSchemaPolicy.Reconcile(candidate, catalog);
      candidate.SetValue(NativeStage01Keys.WorkflowVersion, target);
      string targetJson;
      string targetHash;
      try
      {
        targetJson = NativeStage01Canonicalizer.ToJson(candidate);
        targetHash = NativeStage01Canonicalizer.Sha256(targetJson);
      }
      catch (Exception exception)
      {
        return Failure(
          NativeStage01MigrationCodes.CandidateSerializationFailed,
          "Stage01 0.9.1 迁移候选无法确定性序列化："
            + exception.Message,
          source,
          target);
      }

      string[] added = conditions.AddedConditionIds
        .Concat(payload.Model.Conditions.ContainsKey(
          NativeProjectConditionDeclarationPolicy.NoneConditionId)
          ? Array.Empty<string>()
          : new[] { NativeProjectConditionDeclarationPolicy.NoneConditionId })
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      var messages = new List<string>
      {
        "Stage01 Payload 已在内存中从 " + source + " 迁移到 " + target + "。",
        "迁移候选保留既有非空业务值，且尚未写回 RVT。"
      };
      if (added.Length > 0)
      {
        messages.Add(
          "迁移候选补齐条件键为 false：" + string.Join("、", added) + "。" );
      }
      return new NativeStage01MigrationResult
      {
        Success = true,
        Message = "已生成 0.9.1 内存迁移候选；尚未写回 RVT。",
        SourceVersion = source,
        TargetVersion = target,
        SourcePayloadHash = sourceHash,
        TargetPayloadJson = targetJson,
        TargetPayloadHash = targetHash,
        Model = candidate,
        AddedConditionIds = new ReadOnlyCollection<string>(added),
        Messages = new ReadOnlyCollection<string>(messages)
      };
    }

    private static NativeStage01MigrationResult Failure(
      string errorCode,
      string message,
      string sourceVersion,
      string targetVersion)
    {
      string text = message ?? string.Empty;
      return new NativeStage01MigrationResult
      {
        Success = false,
        ErrorCode = errorCode ?? NativeStage01MigrationCodes.InvalidPayload,
        Message = text,
        SourceVersion = sourceVersion ?? string.Empty,
        TargetVersion = targetVersion ?? string.Empty,
        Model = null,
        Messages = new ReadOnlyCollection<string>(new[] { text })
      };
    }
  }
}
