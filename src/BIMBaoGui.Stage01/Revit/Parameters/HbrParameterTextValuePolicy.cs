namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal sealed class HbrParameterTextValueDecision
  {
    internal string RawValue { get; set; } = string.Empty;
    internal string CanonicalValue { get; set; } = string.Empty;
    internal bool HasBusinessValue { get; set; }
  }

  internal static class HbrParameterTextValuePolicy
  {
    internal static HbrParameterTextValueDecision Evaluate(string value)
    {
      string text = value ?? string.Empty;
      return new HbrParameterTextValueDecision
      {
        RawValue = text,
        CanonicalValue = text,
        HasBusinessValue = !string.IsNullOrWhiteSpace(text)
      };
    }
  }
}
