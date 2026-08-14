using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin
{
  internal sealed class RulePackageIdentity
  {
    internal string PackageId { get; set; } = string.Empty;
    internal string PackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
  }

  internal sealed class RulePackageEnvelope
  {
    internal RulePackageIdentity Identity { get; set; }
    internal string PayloadJson { get; set; } = string.Empty;
  }

  internal static class RulePackageIdentityReader
  {
    internal const string ResourceName =
      "BIMBaoGui.RevitAddin.Resources.HBR_RulePack.hbrpack";

    private static readonly byte[] ExpectedMagic =
    {
      (byte)'H', (byte)'B', (byte)'R', (byte)'P'
    };

    internal static RulePackageIdentity ReadEmbedded()
    {
      return ReadEmbeddedEnvelope().Identity;
    }

    internal static RulePackageEnvelope ReadEmbeddedEnvelope()
    {
      Assembly assembly = typeof(RulePackageIdentityReader).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null)
          throw new InvalidDataException(
            "缺少嵌入式 HBR 规则包：" + ResourceName);
        return ReadEnvelope(stream);
      }
    }

    internal static RulePackageIdentity Read(Stream stream)
    {
      return ReadEnvelope(stream).Identity;
    }

    internal static RulePackageEnvelope ReadEnvelope(Stream stream)
    {
      if (stream == null || !stream.CanRead)
        throw new InvalidDataException("HBR 规则包不可读。");

      byte[] magic = ReadExactly(stream, 4);
      if (!EqualBytes(magic, ExpectedMagic))
        throw new InvalidDataException("HBR 规则包 magic 不是 HBRP。");

      int formatVersion = ReadInt32BigEndian(ReadExactly(stream, 4));
      if (formatVersion != 1)
        throw new InvalidDataException(
          "不支持的 HBR 规则包格式版本：" + formatVersion);

      long payloadLength = ReadInt64BigEndian(ReadExactly(stream, 8));
      if (payloadLength < 0 || payloadLength > int.MaxValue)
        throw new InvalidDataException("HBR 规则包 payloadLength 无效。");

      byte[] expectedHash = ReadExactly(stream, 32);
      byte[] payload = ReadExactly(stream, (int)payloadLength);
      if (stream.ReadByte() != -1)
        throw new InvalidDataException("HBR 规则包包含尾随字节。");

      byte[] actualHash;
      using (SHA256 algorithm = SHA256.Create())
        actualHash = algorithm.ComputeHash(payload);
      if (!EqualBytes(expectedHash, actualHash))
        throw new InvalidDataException("HBR 规则包 SHA-256 校验失败。");

      string json;
      try
      {
        json = new UTF8Encoding(false, true).GetString(payload);
      }
      catch (DecoderFallbackException exception)
      {
        throw new InvalidDataException(
          "HBR 规则包 payload 不是严格 UTF-8。",
          exception);
      }

      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      RulePackageIdentityDto dto;
      try
      {
        dto = serializer.Deserialize<RulePackageIdentityDto>(json);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is InvalidOperationException)
      {
        throw new InvalidDataException(
          "HBR 规则包 payload JSON 无法反序列化。",
          exception);
      }
      if (dto == null
        || string.IsNullOrWhiteSpace(dto.packageId)
        || string.IsNullOrWhiteSpace(dto.packageVersion))
      {
        throw new InvalidDataException("HBR 规则包缺少 package identity。");
      }

      return new RulePackageEnvelope
      {
        Identity = new RulePackageIdentity
        {
          PackageId = dto.packageId.Trim(),
          PackageVersion = dto.packageVersion.Trim(),
          RulePackageSha256 = ToLowerHex(actualHash)
        },
        PayloadJson = json
      };
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
      var result = new byte[count];
      int offset = 0;
      while (offset < count)
      {
        int read = stream.Read(result, offset, count - offset);
        if (read <= 0)
          throw new InvalidDataException("HBR 规则包已截断。");
        offset += read;
      }
      return result;
    }

    private static int ReadInt32BigEndian(byte[] bytes)
    {
      uint value = 0;
      foreach (byte item in bytes) value = (value << 8) | item;
      return unchecked((int)value);
    }

    private static long ReadInt64BigEndian(byte[] bytes)
    {
      ulong value = 0;
      foreach (byte item in bytes) value = (value << 8) | item;
      return unchecked((long)value);
    }

    private static bool EqualBytes(byte[] left, byte[] right)
    {
      if (left == null || right == null || left.Length != right.Length)
        return false;
      int difference = 0;
      for (int index = 0; index < left.Length; index++)
        difference |= left[index] ^ right[index];
      return difference == 0;
    }

    private static string ToLowerHex(byte[] bytes)
    {
      var builder = new StringBuilder(bytes.Length * 2);
      foreach (byte item in bytes) builder.Append(item.ToString("x2"));
      return builder.ToString();
    }

    private sealed class RulePackageIdentityDto
    {
      public string packageId { get; set; }
      public string packageVersion { get; set; }
    }
  }
}
