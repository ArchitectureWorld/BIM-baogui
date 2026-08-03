using System;
using System.Globalization;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrInvariantValueParseDecision
  {
    internal bool Success { get; set; }
    internal string NormalizedValue { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class HbrInvariantValueParser
  {
    internal static HbrInvariantValueParseDecision TryNormalize(
      string storageType,
      string parameterType,
      string value,
      bool sourceAlreadyUsesInternalUnits = false)
    {
      string raw = value ?? string.Empty;
      if (raw.Trim().Length == 0) return Success(string.Empty);
      switch ((storageType ?? string.Empty).Trim())
      {
        case "String":
          return Success(raw);
        case "Integer":
          if (string.Equals(
            parameterType,
            "YesNo",
            StringComparison.OrdinalIgnoreCase))
          {
            string normalized = raw.Trim();
            if (sourceAlreadyUsesInternalUnits
              && (string.Equals(normalized, "0", StringComparison.Ordinal)
                || string.Equals(normalized, "1", StringComparison.Ordinal)))
            {
              return Success(normalized);
            }
            if (TryBoolean(raw, out bool boolean))
              return Success(boolean ? "1" : "0");
            return Invalid("YesNo 仅接受 true/false 或 是/否。");
          }
          if (TryInteger(raw, out int integer))
            return Success(integer.ToString(CultureInfo.InvariantCulture));
          return Invalid("整数格式不符合 Stage01 输入合同。");
        case "Double":
          if (FieldInputRules.TryDouble(raw, out double number)
            && !double.IsNaN(number)
            && !double.IsInfinity(number))
            return Success(number.ToString("R", CultureInfo.InvariantCulture));
          return Invalid("数值必须有限且符合 Stage01 数值输入合同。");
        default:
          return Invalid("规则包含不支持的 Revit StorageType。");
      }
    }

    private static HbrInvariantValueParseDecision Success(string value)
    {
      return new HbrInvariantValueParseDecision
      {
        Success = true,
        NormalizedValue = value ?? string.Empty
      };
    }

    private static bool TryInteger(string value, out int result)
    {
      string normalized = (value ?? string.Empty).Trim();
      return int.TryParse(
          normalized,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out result)
        || int.TryParse(
          normalized,
          NumberStyles.Integer,
          CultureInfo.CurrentCulture,
          out result);
    }

    private static bool TryBoolean(string value, out bool result)
    {
      string normalized = (value ?? string.Empty).Trim();
      if (bool.TryParse(normalized, out result)) return true;
      if (string.Equals(normalized, "是", StringComparison.Ordinal))
      {
        result = true;
        return true;
      }
      if (string.Equals(normalized, "否", StringComparison.Ordinal))
      {
        result = false;
        return true;
      }
      result = false;
      return false;
    }

    private static HbrInvariantValueParseDecision Invalid(string message)
    {
      return new HbrInvariantValueParseDecision
      {
        Success = false,
        ErrorCode = "INVALID_VALUE",
        Message = message ?? string.Empty
      };
    }
  }
}
