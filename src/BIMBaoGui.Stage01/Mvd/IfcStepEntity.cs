using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class IfcStepEntity
  {
    private readonly string _originalSegment;
    private readonly string _leadingWhitespace;
    private bool _modified;

    internal IfcStepEntity(
      int id,
      string type,
      IEnumerable<string> arguments,
      string originalSegment,
      string leadingWhitespace)
    {
      Id = id;
      Type = type ?? throw new ArgumentNullException(nameof(type));
      Arguments = (arguments ?? throw new ArgumentNullException(nameof(arguments)))
        .ToList();
      _originalSegment = originalSegment ?? throw new ArgumentNullException(nameof(originalSegment));
      _leadingWhitespace = leadingWhitespace ?? string.Empty;
    }

    public int Id { get; }
    public string Type { get; }
    public IList<string> Arguments { get; }
    public bool IsDeleted { get; private set; }

    public void SetArgument(int index, string value)
    {
      if (index < 0 || index >= Arguments.Count)
        throw new ArgumentOutOfRangeException(nameof(index));
      Arguments[index] = value ?? throw new ArgumentNullException(nameof(value));
      _modified = true;
    }

    public void Delete()
    {
      IsDeleted = true;
      _modified = true;
    }

    internal string Serialize()
    {
      if (IsDeleted) return string.Empty;
      if (!_modified) return _originalSegment;
      return _leadingWhitespace
        + "#"
        + Id
        + "="
        + Type
        + "("
        + string.Join(",", Arguments)
        + ");";
    }
  }
}
