using System;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialHifcMapping
  {
    public string PropertyId { get; set; } = string.Empty;
    public Guid ParameterGuid { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string BindingScope { get; set; } = "INSTANCE";
    public string Category { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;

    public bool IsTypeBinding => string.Equals(BindingScope, "TYPE", StringComparison.OrdinalIgnoreCase);
  }
}
