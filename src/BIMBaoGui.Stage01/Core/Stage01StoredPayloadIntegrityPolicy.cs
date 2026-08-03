using System;
using System.Globalization;

namespace BIMBaoGui.Stage01.Core
{
  internal sealed class Stage01StoredPayloadIntegrityDecision
  {
    internal bool Success { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal string CanonicalPayload { get; set; } = string.Empty;
    internal string ActualPayloadHash { get; set; } = string.Empty;
  }

  internal static class Stage01StoredPayloadIntegrityPolicy
  {
    internal const string CorruptStorageCode = "CORRUPT_STAGE01_STORAGE";
    internal const string PayloadHashMismatchCode = "PAYLOAD_HASH_MISMATCH";

    internal static Stage01StoredPayloadIntegrityDecision Evaluate(
      string payloadJson,
      string storedPayloadHash)
    {
      if (string.IsNullOrWhiteSpace(payloadJson))
        return Corrupt("Stage01 初始化载荷为空。");

      var model = new Stage01Model();
      if (!Stage01PayloadCodec.TryApply(
        payloadJson,
        model,
        out string parseError))
      {
        return Corrupt(parseError);
      }

      string canonical;
      try
      {
        canonical = CanonicalPayload.Build(model);
      }
      catch (Exception exception)
      {
        return Corrupt(
          "Stage01 初始化载荷无法规范化：" + exception.Message);
      }
      if (!string.Equals(payloadJson, canonical, StringComparison.Ordinal))
      {
        return Corrupt(
          "Stage01 初始化载荷不是当前 schema 的规范 JSON。");
      }

      string actualHash = CanonicalPayload.Sha256(canonical);
      if (!FixedTimeHexEquals(actualHash, storedPayloadHash))
      {
        return new Stage01StoredPayloadIntegrityDecision
        {
          Success = false,
          ErrorCode = PayloadHashMismatchCode,
          Message = "Stage01 初始化载荷的实际 SHA-256 与存储哈希不一致。",
          CanonicalPayload = canonical,
          ActualPayloadHash = actualHash
        };
      }
      return new Stage01StoredPayloadIntegrityDecision
      {
        Success = true,
        CanonicalPayload = canonical,
        ActualPayloadHash = actualHash
      };
    }

    private static Stage01StoredPayloadIntegrityDecision Corrupt(
      string message)
    {
      return new Stage01StoredPayloadIntegrityDecision
      {
        Success = false,
        ErrorCode = CorruptStorageCode,
        Message = message ?? string.Empty
      };
    }

    private static bool FixedTimeHexEquals(string left, string right)
    {
      byte[] leftBytes = ParseSha256(left);
      byte[] rightBytes = ParseSha256(right);
      int difference = leftBytes.Length ^ rightBytes.Length;
      int length = Math.Max(leftBytes.Length, rightBytes.Length);
      for (int index = 0; index < length; index++)
      {
        byte leftValue = index < leftBytes.Length ? leftBytes[index] : (byte)0;
        byte rightValue = index < rightBytes.Length ? rightBytes[index] : (byte)0;
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
