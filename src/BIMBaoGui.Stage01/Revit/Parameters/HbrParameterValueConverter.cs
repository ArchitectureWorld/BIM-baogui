using System;
using System.Globalization;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrParameterConversionDecision
  {
    internal bool Success { get; set; }
    internal string InternalRawValue { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal sealed class HbrParameterReadDecision
  {
    internal bool Success { get; set; }
    internal bool HasValue { get; set; }
    internal string RawValue { get; set; } = string.Empty;
    internal string CanonicalValue { get; set; } = string.Empty;
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal sealed class HbrParameterValueConverter
  {
    internal static HbrParameterReadDecision TryReadCanonicalValue(
      Parameter parameter,
      HbrRuleProperty property)
    {
      if (property == null) throw new ArgumentNullException(nameof(property));
      if (parameter == null)
      {
        return new HbrParameterReadDecision
        {
          Success = false,
          ErrorCode = "MISSING_PARAMETER",
          Message = "固定 GUID HBR 参数不存在。"
        };
      }

      try
      {
        if (!parameter.IsShared
          || parameter.GUID != property.Revit.ParameterGuid)
        {
          return InvalidRead("读取到的参数不是规则指定的固定 GUID 共享参数。");
        }
        EnsureStorage(parameter, property.Revit.StorageType);
        if (!parameter.HasValue)
        {
          return new HbrParameterReadDecision
          {
            Success = true,
            HasValue = false
          };
        }

        switch (parameter.StorageType)
        {
          case StorageType.String:
            HbrParameterTextValueDecision textDecision =
              HbrParameterTextValuePolicy.Evaluate(parameter.AsString());
            return SuccessfulRead(
              textDecision.RawValue,
              textDecision.CanonicalValue,
              textDecision.HasBusinessValue);
          case StorageType.Integer:
            string integer = parameter.AsInteger().ToString(
              CultureInfo.InvariantCulture);
            HbrCanonicalUnitConversionDecision integerConversion =
              HbrCanonicalUnitConverter.TryFromInternalInteger(
                property.Revit.ParameterType,
                parameter.AsInteger());
            if (!integerConversion.Success)
            {
              return new HbrParameterReadDecision
              {
                Success = false,
                HasValue = true,
                RawValue = integer,
                ErrorCode = integerConversion.ErrorCode,
                Message = integerConversion.Message
              };
            }
            return SuccessfulRead(
              integer,
              integerConversion.Value,
              true);
          case StorageType.Double:
            double internalValue = parameter.AsDouble();
            string raw = internalValue.ToString(
              "R",
              CultureInfo.InvariantCulture);
            HbrCanonicalUnitConversionDecision conversion =
              HbrCanonicalUnitConverter.TryFromInternalDouble(
                property.Revit.ParameterType,
                property.Ifc.CanonicalUnit,
                internalValue);
            if (!conversion.Success)
            {
              return new HbrParameterReadDecision
              {
                Success = false,
                HasValue = true,
                RawValue = raw,
                ErrorCode = conversion.ErrorCode,
                Message = conversion.Message
              };
            }
            return SuccessfulRead(raw, conversion.Value, true);
          default:
            return InvalidRead("固定 GUID HBR 参数使用了不支持的 StorageType。");
        }
      }
      catch (Exception exception)
      {
        return InvalidRead(exception.Message);
      }
    }

    internal static HbrParameterConversionDecision TryToInternalRawString(
      HbrRuleProperty property,
      string value,
      bool sourceAlreadyUsesInternalUnits)
    {
      if (property == null) throw new ArgumentNullException(nameof(property));
      HbrInvariantValueParseDecision parsed = HbrInvariantValueParser
        .TryNormalize(
          property.Revit.StorageType,
          property.Revit.ParameterType,
          value,
          sourceAlreadyUsesInternalUnits);
      if (!parsed.Success)
      {
        return new HbrParameterConversionDecision
        {
          Success = false,
          ErrorCode = parsed.ErrorCode,
          Message = parsed.Message
        };
      }
      if (parsed.NormalizedValue.Length == 0)
      {
        return new HbrParameterConversionDecision { Success = true };
      }
      try
      {
        string internalRaw = parsed.NormalizedValue;
        if (string.Equals(
          property.Revit.StorageType,
          "Double",
          StringComparison.Ordinal))
        {
          double number = double.Parse(
            parsed.NormalizedValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
          if (!sourceAlreadyUsesInternalUnits)
            number = ToInternalUnits(property.Revit.ParameterType, number);
          internalRaw = number.ToString("R", CultureInfo.InvariantCulture);
        }
        return new HbrParameterConversionDecision
        {
          Success = true,
          InternalRawValue = internalRaw
        };
      }
      catch (Exception exception)
      {
        return new HbrParameterConversionDecision
        {
          Success = false,
          ErrorCode = "INVALID_VALUE",
          Message = exception.Message
        };
      }
    }

    internal void WriteNonBlankSuggestions(
      Document document,
      Stage02Preview preview,
      HbrRuleDatabase database)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      if (database == null) throw new ArgumentNullException(nameof(database));
      foreach (Stage02MatchedElement matched in preview.Elements)
      {
        foreach (Stage02WriteOperation operation in matched.Operations)
        {
          if (string.IsNullOrWhiteSpace(operation.SuggestedValue)
            || !string.Equals(
              operation.ValueAction,
              "SET",
              StringComparison.Ordinal))
          {
            continue;
          }
          HbrRuleProperty property;
          if (!database.PropertiesById.TryGetValue(
            operation.PropertyId,
            out property))
          {
            throw new InvalidOperationException(
              "写入操作引用了未知 HBR 属性规则。");
          }
          Element target = document.GetElement(operation.TargetUniqueId);
          if (target == null)
            throw new InvalidOperationException(
              "无法按 TargetUniqueId 解析 HBR 写入目标。");
          Parameter parameter = target.get_Parameter(operation.ParameterGuid);
          if (parameter == null)
            throw new InvalidOperationException(
              "固定 GUID HBR 参数在绑定后仍不可用。");
          if (parameter.IsReadOnly || !parameter.UserModifiable)
            throw new InvalidOperationException(
              "固定 GUID HBR 参数不可由用户编辑。");
          EnsureStorage(parameter, property.Revit.StorageType);
          bool written;
          switch (parameter.StorageType)
          {
            case StorageType.String:
              written = parameter.Set(operation.SuggestedValue);
              break;
            case StorageType.Integer:
              written = parameter.Set(int.Parse(
                operation.SuggestedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture));
              break;
            case StorageType.Double:
              written = parameter.Set(double.Parse(
                operation.SuggestedValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture));
              break;
            default:
              throw new InvalidOperationException(
                "固定 GUID HBR 参数使用了不支持的 StorageType。");
          }
          if (!written)
            throw new InvalidOperationException("Parameter.Set 返回 false。");
        }
      }
    }

    internal static bool TypedValueMatches(
      Parameter parameter,
      HbrRuleProperty property,
      string expectedInternalRaw)
    {
      if (parameter == null || property == null) return false;
      EnsureStorage(parameter, property.Revit.StorageType);
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return string.Equals(
            parameter.AsString() ?? string.Empty,
            expectedInternalRaw ?? string.Empty,
            StringComparison.Ordinal);
        case StorageType.Integer:
          return parameter.AsInteger() == int.Parse(
            expectedInternalRaw,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
        case StorageType.Double:
          return Math.Abs(parameter.AsDouble() - double.Parse(
            expectedInternalRaw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture)) <= 1e-9;
        default:
          return false;
      }
    }

    private static double ToInternalUnits(string parameterType, double value)
    {
      switch ((parameterType ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "LENGTH":
          return UnitUtils.ConvertToInternalUnits(
            value,
            DisplayUnitType.DUT_METERS);
        case "AREA":
          return UnitUtils.ConvertToInternalUnits(
            value,
            DisplayUnitType.DUT_SQUARE_METERS);
        case "VOLUME":
          return UnitUtils.ConvertToInternalUnits(
            value,
            DisplayUnitType.DUT_CUBIC_METERS);
        case "ANGLE":
          return UnitUtils.ConvertToInternalUnits(
            value,
            DisplayUnitType.DUT_DECIMAL_DEGREES);
        case "NUMBER":
          return value;
        default:
          throw new InvalidOperationException(
            "Double 参数使用了不支持的 ParameterType。");
      }
    }

    private static void EnsureStorage(
      Parameter parameter,
      string expectedStorage)
    {
      if (!string.Equals(
        parameter.StorageType.ToString(),
        expectedStorage,
        StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "固定 GUID HBR 参数的 StorageType 与规则不一致。");
      }
    }

    private static HbrParameterReadDecision SuccessfulRead(
      string rawValue,
      string canonicalValue,
      bool hasValue)
    {
      return new HbrParameterReadDecision
      {
        Success = true,
        HasValue = hasValue,
        RawValue = rawValue ?? string.Empty,
        CanonicalValue = canonicalValue ?? string.Empty
      };
    }

    private static HbrParameterReadDecision InvalidRead(string message)
    {
      return new HbrParameterReadDecision
      {
        Success = false,
        ErrorCode = "INVALID_VALUE",
        Message = message ?? string.Empty
      };
    }
  }
}
