using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class IfcStepEntity
  {
    private readonly List<string> _arguments;
    private readonly IReadOnlyList<string> _argumentsView;
    private string _originalSegment;
    private string _leadingWhitespace;
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
      _arguments = (arguments ?? throw new ArgumentNullException(nameof(arguments)))
        .ToList();
      _argumentsView = _arguments.AsReadOnly();
      _originalSegment = originalSegment ?? throw new ArgumentNullException(nameof(originalSegment));
      _leadingWhitespace = leadingWhitespace ?? string.Empty;
    }

    public int Id { get; }
    public string Type { get; }
    public IReadOnlyList<string> Arguments => _argumentsView;
    public bool IsDeleted { get; private set; }

    public void SetArgument(int index, string value)
    {
      if (index < 0 || index >= Arguments.Count)
        throw new ArgumentOutOfRangeException(nameof(index));
      string canonical = IfcStepSyntax.NormalizeSingleArgument(value);
      _arguments[index] = canonical;
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

    internal IfcStepEntity Clone()
    {
      var clone = new IfcStepEntity(
        Id,
        Type,
        Arguments,
        _originalSegment,
        _leadingWhitespace);
      clone._modified = _modified;
      clone.IsDeleted = IsDeleted;
      return clone;
    }

    internal void CopyMutableStateFrom(IfcStepEntity source)
    {
      _arguments.Clear();
      foreach (string argument in source.Arguments)
        _arguments.Add(argument);
      _originalSegment = source._originalSegment;
      _leadingWhitespace = source._leadingWhitespace;
      _modified = source._modified;
      IsDeleted = source.IsDeleted;
    }
  }
}
