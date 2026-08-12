using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class HbrIfcCanonicalValueDecision
  {
    internal bool Success { get; set; }
    internal string NormalizedValue { get; set; } = string.Empty;
    internal bool RequiresStringEncoding { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class HbrIfcCanonicalValuePolicy
  {
    private static readonly Regex IfcDateTimePattern = new Regex(
      @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$",
      RegexOptions.CultureInvariant);
    private static readonly Regex IfcRealLiteralPattern = new Regex(
      @"^[+-]?[0-9]+\.[0-9]*(?:E[+-]?[0-9]+)?$",
      RegexOptions.CultureInvariant);

    internal static HbrIfcCanonicalValueDecision Validate(
      string declaredType,
      string canonicalValue)
    {
      string type = (declaredType ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      if (canonicalValue == null)
        return Invalid("IFC canonical value 不能为空。");

      switch (type)
      {
        case "IFCBOOLEAN":
          return canonicalValue == ".T." || canonicalValue == ".F."
            ? Valid(canonicalValue, false)
            : Invalid("IfcBoolean 仅接受 .T. 或 .F.。");
        case "IFCDATE":
          return DateTime.TryParseExact(
            canonicalValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _)
              ? Valid(canonicalValue, true)
              : Invalid("IfcDate 必须是有效 yyyy-MM-dd 日期。");
        case "IFCDATETIME":
          return IfcDateTimePattern.IsMatch(canonicalValue)
            && DateTimeOffset.TryParse(
              canonicalValue,
              CultureInfo.InvariantCulture,
              DateTimeStyles.None,
              out _)
                ? Valid(canonicalValue, true)
                : Invalid("IfcDateTime 必须包含 T、秒和有效时区。");
        case "IFCINTEGER":
          return int.TryParse(
            canonicalValue,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out _)
              ? Valid(canonicalValue, false)
              : Invalid("IfcInteger 必须是 Int32 invariant 整数。");
        case "IFCLABEL":
          return !string.IsNullOrWhiteSpace(canonicalValue)
            && canonicalValue.Length <= 255
            ? Valid(canonicalValue, true)
            : Invalid("IfcLabel 必须包含 1 到 255 个字符。");
        case "IFCREAL":
          return NormalizeReal(canonicalValue);
        case "IFCTEXT":
          return !string.IsNullOrWhiteSpace(canonicalValue)
            ? Valid(canonicalValue, true)
            : Invalid("IfcText 不能为空。");
        default:
          return Invalid(
            "当前 declared IFC type 不在 enrichment allowlist：" + type);
      }
    }

    private static HbrIfcCanonicalValueDecision NormalizeReal(string value)
    {
      double number;
      if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
        || !double.TryParse(
          value,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out number)
        || double.IsNaN(number)
        || double.IsInfinity(number))
      {
        return Invalid("IfcReal 必须是有限 invariant 数字。");
      }
      if (IfcRealLiteralPattern.IsMatch(value)) return Valid(value, false);

      string normalized = number.ToString("R", CultureInfo.InvariantCulture)
        .ToUpperInvariant();
      int exponentIndex = normalized.IndexOf('E');
      if (exponentIndex < 0)
      {
        normalized = normalized.IndexOf('.') >= 0
          ? normalized
          : normalized + ".0";
      }
      else
      {
        string mantissa = normalized.Substring(0, exponentIndex);
        if (mantissa.IndexOf('.') < 0) mantissa += ".0";
        normalized = mantissa + normalized.Substring(exponentIndex);
      }
      return Valid(normalized, false);
    }

    private static HbrIfcCanonicalValueDecision Valid(
      string value,
      bool requiresStringEncoding)
    {
      return new HbrIfcCanonicalValueDecision
      {
        Success = true,
        NormalizedValue = value ?? string.Empty,
        RequiresStringEncoding = requiresStringEncoding
      };
    }

    private static HbrIfcCanonicalValueDecision Invalid(string message)
    {
      return new HbrIfcCanonicalValueDecision
      {
        Success = false,
        ErrorCode = "INVALID_VALUE",
        Message = message ?? string.Empty
      };
    }
  }
}
