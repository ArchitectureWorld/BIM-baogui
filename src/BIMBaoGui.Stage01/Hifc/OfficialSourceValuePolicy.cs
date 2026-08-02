using System;

namespace BIMBaoGui.Stage01.Hifc
{
  internal static class OfficialSourceValuePolicy
  {
    public static string Normalize(string ifcDataType, string rawValue)
    {
      if (!string.Equals(
        (ifcDataType ?? string.Empty).Trim(),
        "IfcBoolean",
        StringComparison.OrdinalIgnoreCase))
        return rawValue ?? string.Empty;

      string normalized = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
      if (normalized == "true" || normalized == "1"
        || normalized == "是" || normalized == "yes")
        return "True";
      if (normalized == "false" || normalized == "0"
        || normalized == "否" || normalized == "no")
        return "False";
      throw new FormatException(
        "官方 IfcBoolean 值只接受 true/false、是/否、1/0。");
    }
  }
}
