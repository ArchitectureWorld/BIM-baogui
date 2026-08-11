using System;

namespace BIMBaoGui.Stage01.Hifc
{
  internal static class OfficialSourceParameterTypePolicy
  {
    public static string Resolve(string ifcDataType)
    {
      switch ((ifcDataType ?? string.Empty).Trim().ToUpperInvariant())
      {
      case "IFCREAL":
      case "REAL":
      case "DOUBLE":
        return "NUMBER";
      case "IFCINTEGER":
      case "INT":
        return "INTEGER";
      case "IFCBOOLEAN":
      case "BOOLEAN":
        return "TEXT";
      case "IFCTEXT":
      case "IFCLABEL":
      case "IFCDATETIME":
      case "IFCDATE":
        return "TEXT";
      default:
        throw new InvalidOperationException(
          "不支持的官方 IFC 数据类型：" + ifcDataType);
      }
    }
  }
}
