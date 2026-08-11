using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Core
{
  internal enum FieldKind
  {
    Text,
    Number,
    Integer,
    Boolean,
    DateTime,
    Enum,
    Guid
  }

  internal sealed class FieldDefinition
  {
    public string Key { get; set; }
    public string Label { get; set; }
    public string Group { get; set; }
    public FieldKind Kind { get; set; }
    public bool ReadOnly { get; set; }
    public bool Essential { get; set; }
    public bool Deferred { get; set; }
    public string Source { get; set; }
    public string Entity { get; set; }
    public string Pset { get; set; }
    public IReadOnlyList<string> AllowedValues { get; set; } = Array.Empty<string>();
  }

  internal sealed class ConditionDefinition
  {
    public ConditionDefinition(string key, string label, string group)
    {
      Key = key;
      Label = label;
      Group = group;
    }

    public string Key { get; }
    public string Label { get; }
    public string Group { get; }
  }
}
