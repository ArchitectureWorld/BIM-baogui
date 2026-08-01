using System;

namespace BIMBaoGui.Stage01.Hifc
{
  internal enum OfficialParameterStorageKind
  {
    String,
    Integer,
    Double
  }

  internal enum OfficialParameterValueRoute
  {
    Text,
    Integer,
    YesNo,
    Double
  }

  internal enum OfficialParameterUnitRoute
  {
    None,
    Meters,
    SquareMeters,
    CubicMeters,
    Degrees
  }

  internal readonly struct OfficialParameterTypeDecision
  {
    public OfficialParameterTypeDecision(
      string semanticType,
      OfficialParameterStorageKind storageKind,
      OfficialParameterValueRoute valueRoute,
      OfficialParameterUnitRoute unitRoute)
    {
      SemanticType = semanticType;
      StorageKind = storageKind;
      ValueRoute = valueRoute;
      UnitRoute = unitRoute;
    }

    public string SemanticType { get; }
    public OfficialParameterStorageKind StorageKind { get; }
    public OfficialParameterValueRoute ValueRoute { get; }
    public OfficialParameterUnitRoute UnitRoute { get; }
  }

  internal static class OfficialParameterTypeContract
  {
    public static string Normalize(string sharedParameterType)
    {
      string normalized = (sharedParameterType ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      switch (normalized)
      {
        case "TEXT":
        case "INTEGER":
        case "YESNO":
        case "LENGTH":
        case "AREA":
        case "VOLUME":
        case "ANGLE":
        case "NUMBER":
          return normalized;
        default:
          throw new InvalidOperationException(
            "不支持的共享参数类型：" + sharedParameterType);
      }
    }

    public static OfficialParameterTypeDecision Resolve(string sharedParameterType)
    {
      switch (Normalize(sharedParameterType))
      {
        case "TEXT":
          return new OfficialParameterTypeDecision(
            "TEXT",
            OfficialParameterStorageKind.String,
            OfficialParameterValueRoute.Text,
            OfficialParameterUnitRoute.None);
        case "INTEGER":
          return new OfficialParameterTypeDecision(
            "INTEGER",
            OfficialParameterStorageKind.Integer,
            OfficialParameterValueRoute.Integer,
            OfficialParameterUnitRoute.None);
        case "YESNO":
          return new OfficialParameterTypeDecision(
            "YESNO",
            OfficialParameterStorageKind.Integer,
            OfficialParameterValueRoute.YesNo,
            OfficialParameterUnitRoute.None);
        case "LENGTH":
          return DoubleDecision("LENGTH", OfficialParameterUnitRoute.Meters);
        case "AREA":
          return DoubleDecision("AREA", OfficialParameterUnitRoute.SquareMeters);
        case "VOLUME":
          return DoubleDecision("VOLUME", OfficialParameterUnitRoute.CubicMeters);
        case "ANGLE":
          return DoubleDecision("ANGLE", OfficialParameterUnitRoute.Degrees);
        default:
          return DoubleDecision("NUMBER", OfficialParameterUnitRoute.None);
      }
    }

    public static bool IsCompatible(
      string expectedSharedParameterType,
      string actualRevitParameterType)
    {
      return string.Equals(
        Normalize(expectedSharedParameterType),
        Normalize(actualRevitParameterType),
        StringComparison.Ordinal);
    }

    private static OfficialParameterTypeDecision DoubleDecision(
      string semanticType,
      OfficialParameterUnitRoute unitRoute)
    {
      return new OfficialParameterTypeDecision(
        semanticType,
        OfficialParameterStorageKind.Double,
        OfficialParameterValueRoute.Double,
        unitRoute);
    }
  }
}
