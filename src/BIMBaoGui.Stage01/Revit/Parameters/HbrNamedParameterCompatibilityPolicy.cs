using System;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  public sealed class HbrNamedParameterCompatibilityDecision
  {
    internal HbrNamedParameterCompatibilityDecision(
      bool compatible,
      bool sourceAlreadyUsesInternalUnits)
    {
      Compatible = compatible;
      SourceAlreadyUsesInternalUnits = sourceAlreadyUsesInternalUnits;
    }

    public bool Compatible { get; }
    public bool SourceAlreadyUsesInternalUnits { get; }
  }

  public static class HbrNamedParameterCompatibilityPolicy
  {
    public static HbrNamedParameterCompatibilityDecision Evaluate(
      string targetStorageType,
      string targetParameterType,
      string sourceStorageType,
      string sourceParameterType)
    {
      bool compatible = Equal(targetStorageType, sourceStorageType)
        && Equal(targetParameterType, sourceParameterType);
      return new HbrNamedParameterCompatibilityDecision(
        compatible,
        compatible);
    }

    private static bool Equal(string left, string right)
    {
      return !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
          left.Trim(),
          right.Trim(),
          StringComparison.OrdinalIgnoreCase);
    }
  }
}
