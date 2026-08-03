using System;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal static class HbrBindingScopePolicy
  {
    internal static bool RequiresTypeBinding(string bindingScope)
    {
      if (string.Equals(bindingScope, "TYPE", StringComparison.Ordinal))
        return true;
      if (string.Equals(bindingScope, "INSTANCE", StringComparison.Ordinal))
        return false;
      throw new InvalidOperationException(
        "未知 HBR Revit bindingScope：" + (bindingScope ?? string.Empty));
    }
  }
}
