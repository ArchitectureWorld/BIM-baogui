using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcNormalizationResult
  {
    public bool Success { get; set; }
    public int MatchingPropertyCount { get; set; }
    public int NormalizedPropertySetCount { get; set; }
    public int NormalizedPropertyNameCount { get; set; }
    public int NormalizedValueTypeCount { get; set; }
    public int RemovedDuplicatePropertyCount { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal sealed class MvdIfcValidationResult
  {
    public bool Success { get; set; }
    public int MatchingPropertyCount { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }
}
