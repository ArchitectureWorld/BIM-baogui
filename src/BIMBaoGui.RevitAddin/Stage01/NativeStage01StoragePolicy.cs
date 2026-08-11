using System;
using System.Globalization;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStoredInitialization
  {
    internal bool HasRecord { get; set; }
    internal string PayloadJson { get; set; } = string.Empty;
    internal string PayloadHash { get; set; } = string.Empty;
    internal string FileGuid { get; set; } = string.Empty;
    internal string WorkflowVersion { get; set; } = string.Empty;
    internal string InitializedUtc { get; set; } = string.Empty;
  }

  internal enum NativeStage01StorageState
  {
    NoRecord,
    Current,
    MigratableLegacy,
    Corrupt,
    UnsupportedFuture
  }

  internal static class NativeStage01StorageCodes
  {
    internal const string CorruptStorage = "CORRUPT_STAGE01_STORAGE";
    internal const string PayloadHashMismatch = "PAYLOAD_HASH_MISMATCH";
    internal const string NonCanonicalCurrentPayload =
      "NON_CANONICAL_CURRENT_PAYLOAD";
    internal const string FileGuidMismatch = "FILE_GUID_MISMATCH";
    internal const string WorkflowVersionMismatch =
      "WORKFLOW_VERSION_MISMATCH";
    internal const string UnsupportedFutureVersion =
      "UNSUPPORTED_FUTURE_VERSION";
  }

  internal sealed class NativeStage01StorageDecision
  {
    internal NativeStage01StorageState State { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal NativeStage01Payload Payload { get; set; }
    internal string ActualPayloadHash { get; set; } = string.Empty;
    internal bool IsInitialized => State == NativeStage01StorageState.Current
      || State == NativeStage01StorageState.MigratableLegacy;
    internal bool RequiresMigration =>
      State == NativeStage01StorageState.MigratableLegacy;
    internal bool RequiresBlankModelGate =>
      State == NativeStage01StorageState.NoRecord;
    internal bool RequiresReinitializePermission =>
      State == NativeStage01StorageState.Current;
  }

  internal static class NativeStage01StoragePolicy
  {
    internal static NativeStage01StorageDecision Evaluate(
      NativeStoredInitialization record,
      string currentWorkflowVersion)
    {
      if (record == null || !record.HasRecord)
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.NoRecord,
          Message = "当前 RVT 没有 Stage01 初始化记录。"
        };
      }

      if (string.IsNullOrWhiteSpace(record.PayloadJson)
        || string.IsNullOrWhiteSpace(record.PayloadHash)
        || string.IsNullOrWhiteSpace(record.FileGuid)
        || string.IsNullOrWhiteSpace(record.WorkflowVersion)
        || string.IsNullOrWhiteSpace(record.InitializedUtc))
      {
        return Corrupt("Stage01 初始化存储字段不完整。");
      }
      if (!Guid.TryParse(record.FileGuid, out Guid storedFileGuid)
        || storedFileGuid == Guid.Empty)
      {
        return Corrupt("Stage01 初始化存储 FileGuid 无效。");
      }
      if (!DateTimeOffset.TryParse(
        record.InitializedUtc,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind,
        out _))
      {
        return Corrupt("Stage01 初始化存储 InitializedUtc 无效。");
      }

      string actualHash = NativeStage01Canonicalizer.Sha256(
        record.PayloadJson);
      if (!FixedTimeSha256Equals(actualHash, record.PayloadHash))
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode = NativeStage01StorageCodes.PayloadHashMismatch,
          Message = "Stage01 Payload 实际 SHA-256 与存储哈希不一致。",
          ActualPayloadHash = actualHash
        };
      }

      if (!NativeStage01PayloadCodec.TryDecode(
        record.PayloadJson,
        out NativeStage01Payload payload,
        out string payloadError))
      {
        return Corrupt(payloadError, actualHash);
      }

      string modelWorkflowVersion = payload.Model.GetValue(
        NativeStage01Keys.WorkflowVersion);
      if (!string.Equals(
          payload.SchemaVersion,
          record.WorkflowVersion,
          StringComparison.Ordinal)
        || !string.Equals(
          payload.WorkflowVersion,
          record.WorkflowVersion,
          StringComparison.Ordinal)
        || !string.Equals(
          modelWorkflowVersion,
          record.WorkflowVersion,
          StringComparison.Ordinal))
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode = NativeStage01StorageCodes.WorkflowVersionMismatch,
          Message = "Stage01 Storage、Payload envelope 与 values 中的工作流版本不一致。",
          Payload = payload,
          ActualPayloadHash = actualHash
        };
      }

      string payloadFileGuid = payload.Model.GetValue(
        NativeStage01Keys.FileGuid);
      if (!Guid.TryParse(payloadFileGuid, out Guid decodedFileGuid)
        || decodedFileGuid == Guid.Empty
        || !string.Equals(
          storedFileGuid.ToString("D"),
          decodedFileGuid.ToString("D"),
          StringComparison.OrdinalIgnoreCase))
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode = NativeStage01StorageCodes.FileGuidMismatch,
          Message = "Stage01 Storage FileGuid 与 Payload FileGuid 不一致。",
          Payload = payload,
          ActualPayloadHash = actualHash
        };
      }

      if (!TryParseVersion(
          record.WorkflowVersion,
          out Version storedVersion)
        || !TryParseVersion(
          currentWorkflowVersion,
          out Version currentVersion))
      {
        return Corrupt(
          "Stage01 工作流版本不是可比较的语义版本。",
          actualHash,
          payload);
      }

      int versionComparison = storedVersion.CompareTo(currentVersion);
      if (versionComparison > 0)
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.UnsupportedFuture,
          ErrorCode = NativeStage01StorageCodes.UnsupportedFutureVersion,
          Message = "RVT 使用了比当前原生插件更新的 Stage01 工作流版本，已拒绝覆盖。",
          Payload = payload,
          ActualPayloadHash = actualHash
        };
      }
      if (versionComparison < 0)
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.MigratableLegacy,
          Message = "检测到哈希有效的旧版 Stage01 Payload；再次提交时迁移到当前版本。",
          Payload = payload,
          ActualPayloadHash = actualHash
        };
      }

      string canonicalPayload = NativeStage01Canonicalizer.ToJson(
        payload.Model);
      if (!string.Equals(
        record.PayloadJson,
        canonicalPayload,
        StringComparison.Ordinal))
      {
        return new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode =
            NativeStage01StorageCodes.NonCanonicalCurrentPayload,
          Message = "当前版本 Stage01 Payload 不是确定性规范 JSON。",
          Payload = payload,
          ActualPayloadHash = actualHash
        };
      }

      return new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.Current,
        Message = "Stage01 初始化存储、Payload、FileGuid 和哈希均有效。",
        Payload = payload,
        ActualPayloadHash = actualHash
      };
    }

    private static NativeStage01StorageDecision Corrupt(
      string message,
      string actualHash = null,
      NativeStage01Payload payload = null)
    {
      return new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.Corrupt,
        ErrorCode = NativeStage01StorageCodes.CorruptStorage,
        Message = message ?? string.Empty,
        Payload = payload,
        ActualPayloadHash = actualHash ?? string.Empty
      };
    }

    private static bool TryParseVersion(string value, out Version version)
    {
      version = null;
      string text = (value ?? string.Empty).Trim();
      return text.Length > 0 && Version.TryParse(text, out version);
    }

    private static bool FixedTimeSha256Equals(string left, string right)
    {
      byte[] leftBytes = ParseSha256(left);
      byte[] rightBytes = ParseSha256(right);
      int difference = leftBytes.Length ^ rightBytes.Length;
      int length = Math.Max(leftBytes.Length, rightBytes.Length);
      for (int index = 0; index < length; index++)
      {
        byte leftValue = index < leftBytes.Length
          ? leftBytes[index]
          : (byte)0;
        byte rightValue = index < rightBytes.Length
          ? rightBytes[index]
          : (byte)0;
        difference |= leftValue ^ rightValue;
      }
      return difference == 0 && leftBytes.Length == 32;
    }

    private static byte[] ParseSha256(string value)
    {
      string text = (value ?? string.Empty).Trim();
      if (text.Length != 64) return Array.Empty<byte>();
      var bytes = new byte[32];
      for (int index = 0; index < bytes.Length; index++)
      {
        if (!byte.TryParse(
          text.Substring(index * 2, 2),
          NumberStyles.AllowHexSpecifier,
          CultureInfo.InvariantCulture,
          out bytes[index]))
        {
          return Array.Empty<byte>();
        }
      }
      return bytes;
    }
  }
}
