using System;
using System.Globalization;
using Autodesk.Revit.DB;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02ValueCodec
  {
    private const double DoubleTolerance = 1e-9;

    internal static string Read(
      Parameter parameter,
      NativeStage02PropertyDefinition property)
    {
      if (parameter == null) return string.Empty;
      if (property == null) throw new ArgumentNullException(nameof(property));
      EnsureStorageType(parameter, property.StorageType);
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return parameter.AsString() ?? string.Empty;
        case StorageType.Integer:
          if (IsYesNo(property))
            return parameter.AsInteger() == 0 ? "false" : "true";
          return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
        case StorageType.Double:
          return Format(FromInternalDouble(property, parameter.AsDouble()));
        default:
          throw new InvalidDataException(
            "Stage02 不支持 StorageType：" + parameter.StorageType);
      }
    }

    internal static void WriteAndVerify(
      Parameter parameter,
      NativeStage02PropertyDefinition property,
      string canonicalValue)
    {
      if (parameter == null)
        throw new InvalidOperationException("Stage02 写入目标参数不存在。" );
      if (property == null) throw new ArgumentNullException(nameof(property));
      if (parameter.IsReadOnly)
        throw new InvalidOperationException(
          "Stage02 参数为只读：" + property.ParameterName);
      EnsureStorageType(parameter, property.StorageType);

      bool written;
      switch (parameter.StorageType)
      {
        case StorageType.String:
          written = parameter.Set(canonicalValue ?? string.Empty);
          if (!written || !string.Equals(
            parameter.AsString() ?? string.Empty,
            canonicalValue ?? string.Empty,
            StringComparison.Ordinal))
            throw new InvalidOperationException(
              "Stage02 文本参数写入或回读失败：" + property.ParameterName);
          return;
        case StorageType.Integer:
          int expectedInteger = ToInteger(property, canonicalValue);
          written = parameter.Set(expectedInteger);
          if (!written || parameter.AsInteger() != expectedInteger)
            throw new InvalidOperationException(
              "Stage02 整数参数写入或回读失败：" + property.ParameterName);
          return;
        case StorageType.Double:
          double expectedDouble = ToInternalDouble(property, canonicalValue);
          written = parameter.Set(expectedDouble);
          if (!written
            || Math.Abs(parameter.AsDouble() - expectedDouble)
              > DoubleTolerance)
            throw new InvalidOperationException(
              "Stage02 数值参数写入或回读失败：" + property.ParameterName);
          return;
        default:
          throw new InvalidDataException(
            "Stage02 不支持 StorageType：" + parameter.StorageType);
      }
    }

    private static int ToInteger(
      NativeStage02PropertyDefinition property,
      string value)
    {
      string text = (value ?? string.Empty).Trim();
      if (IsYesNo(property))
      {
        if (bool.TryParse(text, out bool boolean)) return boolean ? 1 : 0;
        if (string.Equals(text, "是", StringComparison.Ordinal)) return 1;
        if (string.Equals(text, "否", StringComparison.Ordinal)) return 0;
        if (text == "1") return 1;
        if (text == "0") return 0;
        throw new FormatException(
          "YesNo 参数值必须为 true/false、是/否或 1/0。" );
      }
      if (int.TryParse(
        text,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int integer))
        return integer;
      throw new FormatException("Integer 参数值无效：" + value);
    }

    private static double ToInternalDouble(
      NativeStage02PropertyDefinition property,
      string value)
    {
      if (!double.TryParse(
        value,
        NumberStyles.Float | NumberStyles.AllowThousands,
        CultureInfo.InvariantCulture,
        out double number))
      {
        throw new FormatException("Double 参数值无效：" + value);
      }
      switch (NormalizeParameterType(property.ParameterType))
      {
        case "LENGTH":
          return LengthToInternal(number, property.CanonicalUnit);
        case "AREA":
          return UnitUtils.ConvertToInternalUnits(
            AreaToSquareMeters(number, property.CanonicalUnit),
            DisplayUnitType.DUT_SQUARE_METERS);
        case "VOLUME":
          return UnitUtils.ConvertToInternalUnits(
            VolumeToCubicMeters(number, property.CanonicalUnit),
            DisplayUnitType.DUT_CUBIC_METERS);
        case "ANGLE":
          return UnitUtils.ConvertToInternalUnits(
            number,
            DisplayUnitType.DUT_DECIMAL_DEGREES);
        case "NUMBER":
          return number;
        default:
          throw new InvalidDataException(
            "Double 参数使用了不支持的 ParameterType："
            + property.ParameterType);
      }
    }

    private static double FromInternalDouble(
      NativeStage02PropertyDefinition property,
      double value)
    {
      switch (NormalizeParameterType(property.ParameterType))
      {
        case "LENGTH":
          return LengthFromInternal(value, property.CanonicalUnit);
        case "AREA":
          return SquareMetersToCanonical(
            UnitUtils.ConvertFromInternalUnits(
              value,
              DisplayUnitType.DUT_SQUARE_METERS),
            property.CanonicalUnit);
        case "VOLUME":
          return CubicMetersToCanonical(
            UnitUtils.ConvertFromInternalUnits(
              value,
              DisplayUnitType.DUT_CUBIC_METERS),
            property.CanonicalUnit);
        case "ANGLE":
          return UnitUtils.ConvertFromInternalUnits(
            value,
            DisplayUnitType.DUT_DECIMAL_DEGREES);
        case "NUMBER":
          return value;
        default:
          throw new InvalidDataException(
            "Double 参数使用了不支持的 ParameterType："
            + property.ParameterType);
      }
    }

    private static double LengthToInternal(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM")
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_MILLIMETERS);
      if (normalized == "CM") value /= 100.0;
      return UnitUtils.ConvertToInternalUnits(
        value,
        DisplayUnitType.DUT_METERS);
    }

    private static double LengthFromInternal(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM")
        return UnitUtils.ConvertFromInternalUnits(
          value,
          DisplayUnitType.DUT_MILLIMETERS);
      double meters = UnitUtils.ConvertFromInternalUnits(
        value,
        DisplayUnitType.DUT_METERS);
      return normalized == "CM" ? meters * 100.0 : meters;
    }

    private static double AreaToSquareMeters(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM2") return value / 1000000.0;
      if (normalized == "CM2") return value / 10000.0;
      return value;
    }

    private static double SquareMetersToCanonical(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM2") return value * 1000000.0;
      if (normalized == "CM2") return value * 10000.0;
      return value;
    }

    private static double VolumeToCubicMeters(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM3") return value / 1000000000.0;
      if (normalized == "CM3") return value / 1000000.0;
      return value;
    }

    private static double CubicMetersToCanonical(double value, string unit)
    {
      string normalized = NormalizeUnit(unit);
      if (normalized == "MM3") return value * 1000000000.0;
      if (normalized == "CM3") return value * 1000000.0;
      return value;
    }

    private static bool IsYesNo(NativeStage02PropertyDefinition property)
    {
      return string.Equals(
        property.ParameterType,
        "YesNo",
        StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeParameterType(string value)
    {
      return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string NormalizeUnit(string value)
    {
      return (value ?? string.Empty)
        .Trim()
        .ToUpperInvariant()
        .Replace("²", "2")
        .Replace("³", "3")
        .Replace("^", string.Empty)
        .Replace(" ", string.Empty);
    }

    private static void EnsureStorageType(
      Parameter parameter,
      string expected)
    {
      if (!string.Equals(
        parameter.StorageType.ToString(),
        expected,
        StringComparison.Ordinal))
      {
        throw new InvalidDataException(
          "Stage02 参数 StorageType 与 HBR 数据库不一致。" );
      }
    }

    private static string Format(double value)
    {
      return value.ToString("G17", CultureInfo.InvariantCulture);
    }
  }
}
