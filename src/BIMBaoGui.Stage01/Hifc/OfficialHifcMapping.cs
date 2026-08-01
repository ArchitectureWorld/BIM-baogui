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
    public string PersistenceMode { get; set; } = string.Empty;
    public string IfcEntity { get; set; } = string.Empty;
    public string PropertySet { get; set; } = string.Empty;
    public string IfcProperty { get; set; } = string.Empty;
    public string IfcDataType { get; set; } = string.Empty;
    public string SharedParameterType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public bool IsTypeBinding => string.Equals(BindingScope, "TYPE", StringComparison.OrdinalIgnoreCase);
    public OfficialPluginEntityPolicy EntityPolicy =>
      OfficialPluginCompatibilityCatalog.Instance.GetEntityPolicy(IfcEntity);
  }
}
