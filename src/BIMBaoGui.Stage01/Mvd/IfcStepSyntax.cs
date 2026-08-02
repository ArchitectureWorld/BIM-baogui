using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal static class IfcStepSyntax
  {
    public static IReadOnlyList<string> SplitTopLevelArguments(string value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));

      var result = new List<string>();
      int depth = 0;
      int start = 0;
      bool insideString = false;

      for (int index = 0; index < value.Length; index++)
      {
        char character = value[index];
        if (insideString)
        {
          if (character == '\'' && index + 1 < value.Length
            && value[index + 1] == '\'')
          {
            index++;
            continue;
          }
          if (character == '\'') insideString = false;
          continue;
        }

        if (character == '\'')
        {
          insideString = true;
          continue;
        }
        if (character == '(')
        {
          depth++;
          continue;
        }
        if (character == ')')
        {
          depth--;
          if (depth < 0)
            throw new InvalidDataException("IFC STEP 参数括号不平衡。");
          continue;
        }
        if (character == ',' && depth == 0)
        {
          result.Add(value.Substring(start, index - start).Trim());
          start = index + 1;
        }
      }

      if (insideString)
        throw new InvalidDataException("IFC STEP 字符串未闭合。");
      if (depth != 0)
        throw new InvalidDataException("IFC STEP 参数括号不平衡。");

      result.Add(value.Substring(start).Trim());
      return result;
    }

    public static string EncodeString(string value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));

      var builder = new StringBuilder(value.Length + 16);
      builder.Append('\'');
      int index = 0;
      while (index < value.Length)
      {
        char character = value[index];
        if (character >= 0x20 && character <= 0x7e)
        {
          if (character == '\'') builder.Append("''");
          else if (character == '\\') builder.Append("\\\\");
          else builder.Append(character);
          index++;
          continue;
        }

        builder.Append("\\X2\\");
        while (index < value.Length)
        {
          character = value[index];
          if (character >= 0x20 && character <= 0x7e) break;
          builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
          index++;
        }
        builder.Append("\\X0\\");
      }
      builder.Append('\'');
      return builder.ToString();
    }

    public static string DecodeString(string token)
    {
      if (string.IsNullOrWhiteSpace(token)
        || token.Length < 2
        || token[0] != '\''
        || token[token.Length - 1] != '\'')
        throw new InvalidDataException("IFC STEP 值不是字符串。");

      string value = token.Substring(1, token.Length - 2);
      var builder = new StringBuilder(value.Length);
      for (int index = 0; index < value.Length; index++)
      {
        if (value[index] == '\'' && index + 1 < value.Length
          && value[index + 1] == '\'')
        {
          builder.Append('\'');
          index++;
          continue;
        }
        if (value[index] == '\\' && index + 1 < value.Length
          && value[index + 1] == '\\')
        {
          builder.Append('\\');
          index++;
          continue;
        }
        if (StartsWith(value, index, "\\X2\\"))
        {
          int end = value.IndexOf("\\X0\\", index + 4, StringComparison.Ordinal);
          if (end < 0)
            throw new InvalidDataException("IFC STEP X2 字符串未闭合。");
          string hexadecimal = value.Substring(index + 4, end - index - 4);
          if (hexadecimal.Length == 0 || hexadecimal.Length % 4 != 0)
            throw new InvalidDataException("IFC STEP X2 字符串长度无效。");
          for (int offset = 0; offset < hexadecimal.Length; offset += 4)
          {
            if (!ushort.TryParse(
              hexadecimal.Substring(offset, 4),
              NumberStyles.AllowHexSpecifier,
              CultureInfo.InvariantCulture,
              out ushort codeUnit))
              throw new InvalidDataException("IFC STEP X2 字符串包含无效十六进制字符。");
            builder.Append((char)codeUnit);
          }
          index = end + 3;
          continue;
        }
        builder.Append(value[index]);
      }
      return builder.ToString();
    }

    public static int ParseReference(string token)
    {
      if (string.IsNullOrWhiteSpace(token)
        || token[0] != '#'
        || !int.TryParse(token.Substring(1), out int id)
        || id <= 0)
        throw new InvalidDataException("IFC STEP 引用无效：" + token);
      return id;
    }

    public static IReadOnlyList<int> ParseReferenceList(string token)
    {
      if (string.IsNullOrWhiteSpace(token)
        || token.Length < 2
        || token[0] != '('
        || token[token.Length - 1] != ')')
        throw new InvalidDataException("IFC STEP 引用列表无效：" + token);
      string inner = token.Substring(1, token.Length - 2).Trim();
      if (inner.Length == 0) return Array.Empty<int>();
      return SplitTopLevelArguments(inner)
        .Select(ParseReference)
        .ToArray();
    }

    public static string FormatReferenceList(IEnumerable<int> references)
    {
      if (references == null) throw new ArgumentNullException(nameof(references));
      return "(" + string.Join(",", references.Select(id => "#" + id)) + ")";
    }

    public static bool TryParseTypedValue(
      string token,
      out string type,
      out string inner)
    {
      type = null;
      inner = null;
      if (string.IsNullOrWhiteSpace(token)) return false;
      int open = token.IndexOf('(');
      if (open <= 0 || token[token.Length - 1] != ')') return false;
      string candidateType = token.Substring(0, open).Trim();
      string candidateInner = token.Substring(
        open + 1,
        token.Length - open - 2).Trim();
      if (candidateType.Length == 0 || candidateInner.Length == 0) return false;
      SplitTopLevelArguments(candidateInner);
      type = candidateType.ToUpperInvariant();
      inner = candidateInner;
      return true;
    }

    public static string FormatTypedValue(string type, string inner)
    {
      if (string.IsNullOrWhiteSpace(type))
        throw new ArgumentException("IFC 值类型不能为空。", nameof(type));
      if (string.IsNullOrWhiteSpace(inner))
        throw new ArgumentException("IFC 值不能为空。", nameof(inner));
      return type.Trim().ToUpperInvariant() + "(" + inner.Trim() + ")";
    }

    public static bool IsFiniteNumber(string token)
    {
      return double.TryParse(
          token,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double value)
        && !double.IsNaN(value)
        && !double.IsInfinity(value);
    }

    private static bool StartsWith(string value, int index, string prefix)
    {
      return index + prefix.Length <= value.Length
        && string.CompareOrdinal(value, index, prefix, 0, prefix.Length) == 0;
    }
  }
}
