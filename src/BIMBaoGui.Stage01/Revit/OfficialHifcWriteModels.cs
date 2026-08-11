using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class OfficialHifcWriteRequest
  {
    public IReadOnlyList<int> ElementIds { get; set; } = Array.Empty<int>();
    public IReadOnlyList<string> PropertyKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Values { get; set; } = Array.Empty<string>();
  }

  internal sealed class OfficialHifcWriteResult
  {
    public bool Success { get; set; }
    public bool OfficialCompatibilityVerified { get; set; }
    public string Status { get; set; } = string.Empty;
    public int WriteCount { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }
}
