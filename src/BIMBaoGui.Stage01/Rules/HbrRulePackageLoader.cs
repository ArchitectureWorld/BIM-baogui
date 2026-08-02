using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Rules
{
  public static class HbrRulePackageLoader
  {
    private static readonly byte[] ExpectedMagic =
    {
      (byte)'H',
      (byte)'B',
      (byte)'R',
      (byte)'P',
    };

    private const int SupportedFormatVersion = 1;
    private const int Sha256Length = 32;

    public static HbrRulePackage Load(Stream stream)
    {
      if (stream == null)
        throw new InvalidDataException("HBRP input stream is null.");
      if (!stream.CanRead)
        throw new InvalidDataException("HBRP input stream is not readable.");

      byte[] magic = ReadExactly(stream, 4, "header.magic");
      if (!EqualBytes(magic, ExpectedMagic))
        throw new InvalidDataException(
          "HBRP header.magic must be the four bytes HBRP.");

      int formatVersion = ReadInt32BigEndian(
        ReadExactly(stream, 4, "header.version"));
      if (formatVersion != SupportedFormatVersion)
        throw new InvalidDataException(
          "HBRP header.version is unsupported: " + formatVersion + ".");

      long payloadLength = ReadInt64BigEndian(
        ReadExactly(stream, 8, "header.payloadLength"));
      if (payloadLength < 0)
        throw new InvalidDataException(
          "HBRP header.payloadLength is negative: " + payloadLength + ".");
      if (payloadLength > int.MaxValue)
        throw new InvalidDataException(
          "HBRP header.payloadLength exceeds the net48 array length limit: "
          + payloadLength
          + ".");

      byte[] expectedHash = ReadExactly(
        stream,
        Sha256Length,
        "header.sha256");
      ValidateSeekableBoundary(stream, payloadLength);

      byte[] payload = ReadExactly(
        stream,
        (int)payloadLength,
        "payload");
      if (stream.ReadByte() != -1)
        throw new InvalidDataException(
          "HBRP contains trailing bytes after the declared payload.");

      byte[] actualHash;
      using (SHA256 algorithm = SHA256.Create())
        actualHash = algorithm.ComputeHash(payload);
      if (!EqualBytes(actualHash, expectedHash))
        throw new InvalidDataException(
          "HBRP payload SHA256 does not match header.sha256.");

      string json;
      try
      {
        json = new UTF8Encoding(false, true).GetString(payload);
      }
      catch (DecoderFallbackException exception)
      {
        throw new InvalidDataException(
          "HBRP payload is not strict UTF-8.",
          exception);
      }

      HbrRulePackageDto dto;
      try
      {
        var serializer = new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue,
          RecursionLimit = 512,
        };
        dto = serializer.Deserialize<HbrRulePackageDto>(json);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is InvalidOperationException)
      {
        throw new InvalidDataException(
          "HBRP payload JSON cannot be deserialized.",
          exception);
      }

      if (dto == null)
        throw new InvalidDataException("HBRP payload JSON is null.");

      return new HbrRulePackage(
        dto,
        formatVersion,
        ToLowerHex(actualHash));
    }

    private static void ValidateSeekableBoundary(
      Stream stream,
      long payloadLength)
    {
      if (!stream.CanSeek)
        return;

      long remaining;
      try
      {
        remaining = stream.Length - stream.Position;
      }
      catch (NotSupportedException)
      {
        return;
      }

      if (remaining < payloadLength)
        throw new InvalidDataException(
          "HBRP payload is shorter than header.payloadLength: expected "
          + payloadLength
          + ", available "
          + remaining
          + ".");
      if (remaining > payloadLength)
        throw new InvalidDataException(
          "HBRP contains trailing bytes after the declared payload: "
          + (remaining - payloadLength)
          + ".");
    }

    private static byte[] ReadExactly(Stream stream, int count, string path)
    {
      var bytes = new byte[count];
      int offset = 0;
      while (offset < count)
      {
        int read = stream.Read(bytes, offset, count - offset);
        if (read <= 0)
          throw new InvalidDataException(
            "HBRP "
            + path
            + " is truncated: expected "
            + count
            + " bytes, read "
            + offset
            + ".");
        offset += read;
      }
      return bytes;
    }

    private static int ReadInt32BigEndian(byte[] bytes)
    {
      uint value = 0;
      for (int index = 0; index < bytes.Length; index++)
        value = (value << 8) | bytes[index];
      return unchecked((int)value);
    }

    private static long ReadInt64BigEndian(byte[] bytes)
    {
      ulong value = 0;
      for (int index = 0; index < bytes.Length; index++)
        value = (value << 8) | bytes[index];
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
      for (int index = 0; index < bytes.Length; index++)
        builder.Append(bytes[index].ToString("x2"));
      return builder.ToString();
    }
  }
}
