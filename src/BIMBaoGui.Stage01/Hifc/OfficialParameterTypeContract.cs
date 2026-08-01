using System;

namespace BIMBaoGui.Stage01.Hifc
{
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

    public static bool IsCompatible(
      string expectedSharedParameterType,
      string actualRevitParameterType)
    {
      return string.Equals(
        Normalize(expectedSharedParameterType),
        Normalize(actualRevitParameterType),
        StringComparison.Ordinal);
    }
  }
}
