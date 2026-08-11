using System;
using System.Globalization;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrCanonicalUnitConversionDecision
  {
    internal bool Success { get; set; }
    internal string Value { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class HbrCanonicalUnitConverter
  {
    private const string InvalidValueCode = "INVALID_VALUE";

    internal static HbrCanonicalUnitConversionDecision
      TryFromInternalDouble(
        string parameterType,
        string canonicalUnit,
        double internalValue)
    {
      return TryConvert(
        parameterType,
        canonicalUnit,
        internalValue,
        fromInternal: true);
    }

    internal static HbrCanonicalUnitConversionDecision TryToInternalDouble(
      string parameterType,
      string canonicalUnit,
      double canonicalValue)
    {
      return TryConvert(
        parameterType,
        canonicalUnit,
        canonicalValue,
        fromInternal: false);
    }

    internal static HbrCanonicalUnitConversionDecision
      TryFromInternalInteger(
        string parameterType,
        int internalValue)
    {
      string type = (parameterType ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      switch (type)
      {
        case "YESNO":
          if (internalValue == 0 || internalValue == 1)
          {
            return new HbrCanonicalUnitConversionDecision
            {
              Success = true,
              Value = internalValue == 1 ? ".T." : ".F."
            };
          }
          return Invalid("YesNo 参数内部值必须是 0 或 1。");
        case "INTEGER":
          return new HbrCanonicalUnitConversionDecision
          {
            Success = true,
            Value = internalValue.ToString(CultureInfo.InvariantCulture)
          };
        default:
          return Invalid("Integer 参数使用了不支持的 ParameterType。");
      }
    }

    private static HbrCanonicalUnitConversionDecision TryConvert(
      string parameterType,
      string canonicalUnit,
      double value,
      bool fromInternal)
    {
      if (double.IsNaN(value) || double.IsInfinity(value))
        return Invalid("数值必须是有限数字。");

      double canonicalPerInternal;
      string error;
      if (!TryResolveCanonicalPerInternal(
        parameterType,
        canonicalUnit,
        out canonicalPerInternal,
        out error))
      {
        return Invalid(error);
      }

      double converted = fromInternal
        ? value * canonicalPerInternal
        : value / canonicalPerInternal;
      if (double.IsNaN(converted) || double.IsInfinity(converted))
        return Invalid("单位转换结果不是有限数字。");

      return new HbrCanonicalUnitConversionDecision
      {
        Success = true,
        Value = converted.ToString("R", CultureInfo.InvariantCulture)
      };
    }

    private static bool TryResolveCanonicalPerInternal(
      string parameterType,
      string canonicalUnit,
      out double factor,
      out string error)
    {
      factor = 0d;
      error = string.Empty;
      string type = (parameterType ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      string unit = (canonicalUnit ?? string.Empty).Trim();
      switch (type)
      {
        case "LENGTH":
          if (string.Equals(unit, "m", StringComparison.OrdinalIgnoreCase))
            factor = 0.3048d;
          else if (string.Equals(
            unit,
            "mm",
            StringComparison.OrdinalIgnoreCase))
          {
            factor = 304.8d;
          }
          else
            error = "Length 参数仅支持 canonical unit m 或 mm。";
          break;
        case "AREA":
          if (IsAny(unit, "m2", "m²", "m^2"))
            factor = 0.09290304d;
          else
            error = "Area 参数仅支持 canonical unit m2。";
          break;
        case "VOLUME":
          if (IsAny(unit, "m3", "m³", "m^3"))
            factor = 0.028316846592d;
          else
            error = "Volume 参数仅支持 canonical unit m3。";
          break;
        case "ANGLE":
          if (IsAny(unit, "deg", "°"))
            factor = 180d / Math.PI;
          else
            error = "Angle 参数仅支持 canonical unit deg。";
          break;
        case "NUMBER":
          if (unit.Length == 0)
            factor = 1d;
          else
            error = "Number 参数不能声明物理 canonical unit。";
          break;
        default:
          error = "Double 参数使用了不支持的 ParameterType。";
          break;
      }
      return error.Length == 0;
    }

    private static bool IsAny(string value, params string[] candidates)
    {
      foreach (string candidate in candidates)
      {
        if (string.Equals(
          value,
          candidate,
          StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
      return false;
    }

    private static HbrCanonicalUnitConversionDecision Invalid(string message)
    {
      return new HbrCanonicalUnitConversionDecision
      {
        Success = false,
        ErrorCode = InvalidValueCode,
        Message = message ?? string.Empty
      };
    }
  }
}
