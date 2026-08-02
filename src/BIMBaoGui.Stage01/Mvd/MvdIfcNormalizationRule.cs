using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcNormalizationRule
  {
    public string Entity { get; set; } = string.Empty;
    public string CanonicalPropertySet { get; set; } = string.Empty;
    public IReadOnlyCollection<string> PropertySetAliases { get; set; } =
      Array.Empty<string>();
    public string CanonicalProperty { get; set; } = string.Empty;
    public IReadOnlyCollection<string> PropertyAliases { get; set; } =
      Array.Empty<string>();
    public string TargetType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public IReadOnlyCollection<string> InternalAliases { get; set; } =
      Array.Empty<string>();
  }
}
