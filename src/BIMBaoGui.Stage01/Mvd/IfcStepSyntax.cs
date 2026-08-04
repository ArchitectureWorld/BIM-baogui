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
      var current = new StringBuilder(value.Length);
      int depth = 0;
      bool insideString = false;
      bool insideComment = false;

      for (int index = 0; index < value.Length; index++)
      {
        char character = value[index];
        if (insideString)
        {
          current.Append(character);
          if (character == '\'' && index + 1 < value.Length
            && value[index + 1] == '\'')
          {
            current.Append(value[index + 1]);
            index++;
            continue;
          }
          if (character == '\'') insideString = false;
          continue;
        }

        if (insideComment)
        {
          if (character == '*' && index + 1 < value.Length
            && value[index + 1] == '/')
          {
            insideComment = false;
            index++;
            if (current.Length > 0
              && !char.IsWhiteSpace(current[current.Length - 1]))
              current.Append(' ');
          }
          continue;
        }

        if (character == '\'')
        {
          insideString = true;
          current.Append(character);
          continue;
        }
        if (character == '/' && index + 1 < value.Length
          && value[index + 1] == '*')
        {
          insideComment = true;
          index++;
          continue;
        }
        if (character == '(')
        {
          depth++;
          current.Append(character);
          continue;
        }
        if (character == ')')
        {
          depth--;
          if (depth < 0)
            throw new InvalidDataException("IFC STEP 参数括号不平衡。");
          current.Append(character);
          continue;
        }
        if (character == ',' && depth == 0)
        {
          result.Add(current.ToString().Trim());
          current.Clear();
          continue;
        }
        current.Append(character);
      }

      if (insideString)
        throw new InvalidDataException("IFC STEP 字符串未闭合。");
      if (insideComment)
        throw new InvalidDataException("IFC STEP 块注释未闭合。");
      if (depth != 0)
        throw new InvalidDataException("IFC STEP 参数括号不平衡。");

      result.Add(current.ToString().Trim());
      return result;
    }

    public static string RemoveCommentsOutsideStrings(string value)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));

      var builder = new StringBuilder(value.Length);
      bool insideString = false;
      bool insideComment = false;
      for (int index = 0; index < value.Length; index++)
      {
        char character = value[index];
        if (insideString)
        {
          builder.Append(character);
          if (character == '\'' && index + 1 < value.Length
            && value[index + 1] == '\'')
          {
            builder.Append(value[index + 1]);
            index++;
            continue;
          }
          if (character == '\'') insideString = false;
          continue;
        }

        if (insideComment)
        {
          if (character == '*' && index + 1 < value.Length
            && value[index + 1] == '/')
          {
            insideComment = false;
            index++;
            if (builder.Length > 0
              && !char.IsWhiteSpace(builder[builder.Length - 1]))
              builder.Append(' ');
          }
          continue;
        }

        if (character == '\'')
        {
          insideString = true;
          builder.Append(character);
          continue;
        }
        if (character == '/' && index + 1 < value.Length
          && value[index + 1] == '*')
        {
          insideComment = true;
          index++;
          continue;
        }
        builder.Append(character);
      }

      if (insideString)
        throw new InvalidDataException("IFC STEP 文件包含未闭合字符串。");
      if (insideComment)
        throw new InvalidDataException("IFC STEP 文件包含未闭合块注释。");
      return builder.ToString();
    }

    public static string NormalizeSingleArgument(string argument)
    {
      if (string.IsNullOrWhiteSpace(argument))
        throw new ArgumentException(
          "IFC STEP 实体参数不能为空。",
          nameof(argument));
      string canonical;
      try
      {
        IReadOnlyList<string> tokens = SplitTopLevelArguments(argument.Trim());
        if (tokens.Count != 1 || string.IsNullOrWhiteSpace(tokens[0]))
          throw new ArgumentException(
            "IFC STEP 实体参数必须是单个值：" + argument,
            nameof(argument));
        canonical = tokens[0];
      }
      catch (InvalidDataException exception)
      {
        throw new ArgumentException(
          "IFC STEP 实体参数语法无效：" + argument,
          nameof(argument),
          exception);
      }

      bool insideString = false;
      for (int index = 0; index < canonical.Length; index++)
      {
        char character = canonical[index];
        if (insideString)
        {
          if (character == '\'' && index + 1 < canonical.Length
            && canonical[index + 1] == '\'')
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
        if (character == ';' || character == '=')
          throw new ArgumentException(
            "IFC STEP 实体参数包含非法 statement token：" + argument,
            nameof(argument));
      }
      return canonical;
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

        if (char.IsHighSurrogate(character))
        {
          builder.Append("\\X4\\");
          while (index < value.Length && char.IsHighSurrogate(value[index]))
          {
            if (index + 1 >= value.Length
              || !char.IsLowSurrogate(value[index + 1]))
              throw new InvalidDataException(
                "IFC STEP 字符串包含孤立 UTF-16 高代理项。");
            int codePoint = char.ConvertToUtf32(
              value[index],
              value[index + 1]);
            builder.Append(codePoint.ToString(
              "X8",
              CultureInfo.InvariantCulture));
            index += 2;
          }
          builder.Append("\\X0\\");
          continue;
        }
        if (char.IsLowSurrogate(character))
          throw new InvalidDataException(
            "IFC STEP 字符串包含孤立 UTF-16 低代理项。");

        builder.Append("\\X2\\");
        while (index < value.Length)
        {
          character = value[index];
          if (character >= 0x20 && character <= 0x7e
            || char.IsSurrogate(character))
            break;
          builder.Append(((int)character).ToString(
            "X4",
            CultureInfo.InvariantCulture));
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
            if (codeUnit >= 0xd800 && codeUnit <= 0xdfff)
              throw new InvalidDataException(
                "IFC STEP X2 字符串包含无效 Unicode 码元。");
            builder.Append((char)codeUnit);
          }
          index = end + 3;
          continue;
        }
        if (StartsWith(value, index, "\\X4\\"))
        {
          int end = value.IndexOf("\\X0\\", index + 4, StringComparison.Ordinal);
          if (end < 0)
            throw new InvalidDataException("IFC STEP X4 字符串未闭合。");
          string hexadecimal = value.Substring(index + 4, end - index - 4);
          if (hexadecimal.Length == 0 || hexadecimal.Length % 8 != 0)
            throw new InvalidDataException("IFC STEP X4 字符串长度无效。");
          for (int offset = 0; offset < hexadecimal.Length; offset += 8)
          {
            if (!uint.TryParse(
              hexadecimal.Substring(offset, 8),
              NumberStyles.AllowHexSpecifier,
              CultureInfo.InvariantCulture,
              out uint codePoint))
              throw new InvalidDataException(
                "IFC STEP X4 字符串包含无效十六进制字符。");
            if (codePoint > 0x10ffff
              || codePoint >= 0xd800 && codePoint <= 0xdfff)
              throw new InvalidDataException(
                "IFC STEP X4 字符串包含无效 Unicode 码点。");
            builder.Append(char.ConvertFromUtf32((int)codePoint));
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
