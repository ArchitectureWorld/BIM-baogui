using System;
using BIMBaoGui.Stage01.Mvd;

namespace BIMBaoGui.HifcCore
{
  public static class IfcGlobalId
  {
    public static string Encode(Guid value)
    {
      if (value == Guid.Empty)
        throw new ArgumentException("IFC GlobalId 源 GUID 不能为空。", nameof(value));
      return IfcGuidCodec.Encode(value);
    }

    public static Guid Decode(string globalId)
    {
      return IfcGuidCodec.Decode(globalId);
    }

    public static bool IsValid(string globalId)
    {
      return IfcGuidCodec.IsValid(globalId);
    }
  }
}
