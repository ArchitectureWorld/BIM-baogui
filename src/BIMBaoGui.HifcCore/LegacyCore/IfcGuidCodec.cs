using System;
using System.Globalization;
using System.Text;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Mvd
{
  internal static class IfcGuidCodec
  {
    private const string Alphabet =
      "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    public static string Encode(Guid value)
    {
      byte[] bytes = ReadRfc4122Bytes(value);
      var builder = new StringBuilder(22);
      AppendBase64(builder, bytes[0], 2);
      for (int offset = 1; offset < bytes.Length; offset += 3)
      {
        uint group = (uint)(bytes[offset] << 16)
          | (uint)(bytes[offset + 1] << 8)
          | bytes[offset + 2];
        AppendBase64(builder, group, 4);
      }
      return builder.ToString();
    }

    public static Guid Decode(string globalId)
    {
      if (!IsValid(globalId))
        throw new FormatException("IFC GlobalId 必须是规范的 22 字符压缩 GUID。");

      var bytes = new byte[16];
      bytes[0] = (byte)ReadBase64(globalId, 0, 2);
      int byteOffset = 1;
      for (int textOffset = 2; textOffset < globalId.Length; textOffset += 4)
      {
        uint group = ReadBase64(globalId, textOffset, 4);
        bytes[byteOffset++] = (byte)(group >> 16);
        bytes[byteOffset++] = (byte)(group >> 8);
        bytes[byteOffset++] = (byte)group;
      }

      var hexadecimal = new StringBuilder(32);
      foreach (byte value in bytes)
        hexadecimal.Append(value.ToString("x2", CultureInfo.InvariantCulture));
      return Guid.ParseExact(hexadecimal.ToString(), "N");
    }

    public static bool IsValid(string globalId)
    {
      if (globalId == null || globalId.Length != 22) return false;
      int first = Alphabet.IndexOf(globalId[0]);
      if (first < 0 || first > 3) return false;
      for (int index = 1; index < globalId.Length; index++)
      {
        if (Alphabet.IndexOf(globalId[index]) < 0) return false;
      }
      return true;
    }

    public static string CreateDeterministic(
      Guid namespaceId,
      string semanticKey)
    {
      if (namespaceId == Guid.Empty)
        throw new ArgumentException(
          "IFC 确定性 GlobalId namespace 不能为空。",
          nameof(namespaceId));
      if (string.IsNullOrWhiteSpace(semanticKey))
        throw new ArgumentException(
          "IFC 确定性 GlobalId 语义键不能为空。",
          nameof(semanticKey));
      return Encode(DeterministicGuidV5.Create(namespaceId, semanticKey));
    }

    private static byte[] ReadRfc4122Bytes(Guid value)
    {
      string hexadecimal = value.ToString("N");
      var bytes = new byte[16];
      for (int index = 0; index < bytes.Length; index++)
      {
        bytes[index] = byte.Parse(
          hexadecimal.Substring(index * 2, 2),
          NumberStyles.AllowHexSpecifier,
          CultureInfo.InvariantCulture);
      }
      return bytes;
    }

    private static void AppendBase64(
      StringBuilder builder,
      uint value,
      int width)
    {
      var characters = new char[width];
      for (int index = width - 1; index >= 0; index--)
      {
        characters[index] = Alphabet[(int)(value & 0x3f)];
        value >>= 6;
      }
      if (value != 0)
        throw new ArgumentOutOfRangeException(nameof(value));
      builder.Append(characters);
    }

    private static uint ReadBase64(string value, int offset, int width)
    {
      uint result = 0;
      for (int index = 0; index < width; index++)
      {
        result = (result << 6)
          | (uint)Alphabet.IndexOf(value[offset + index]);
      }
      return result;
    }
  }
}
