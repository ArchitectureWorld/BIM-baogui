using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class Stage01OfficialCompatibilityDecision
  {
    public Stage01OfficialCompatibilityDecision(
      IReadOnlyList<string> blockers)
    {
      Blockers = blockers ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Blockers { get; }
    public bool IsCompatible => Blockers.Count == 0;
  }

  internal static class Stage01OfficialCompatibilityPolicy
  {
    public static Stage01OfficialCompatibilityDecision Evaluate(
      IEnumerable<Dictionary<string, string>> organizations)
    {
      return new Stage01OfficialCompatibilityDecision(
        Array.Empty<string>());
    }
  }
}
