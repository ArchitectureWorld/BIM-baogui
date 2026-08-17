using System;
using System.Collections.Generic;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal enum NativeIssueSeverity
  {
    Blocker,
    Warning
  }

  internal enum NativeIssueNavigationAction
  {
    None,
    Select,
    Zoom,
    Isolate,
    RestoreView,
    OpenStage01,
    OpenStage02A,
    OpenStage02B,
    StayStage03
  }

  internal sealed class NativeIssueElementReference
  {
    internal int ElementId { get; set; }
    internal string UniqueId { get; set; } = string.Empty;
    internal string ElementName { get; set; } = string.Empty;
    internal string CategoryName { get; set; } = string.Empty;
  }

  internal sealed class NativeIssueRecord
  {
    internal string IssueId { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal NativeIssueSeverity Severity { get; set; }
    internal string SourceFeature { get; set; } = string.Empty;
    internal string CheckId { get; set; } = string.Empty;
    internal string Code { get; set; } = string.Empty;
    internal string Missing { get; set; } = string.Empty;
    internal string Impact { get; set; } = string.Empty;
    internal string Remediation { get; set; } = string.Empty;
    internal string FieldKey { get; set; } = string.Empty;
    internal string PropertyId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
      Array.Empty<NativeIssueElementReference>();
    internal NativeIssueNavigationAction Route { get; set; }
  }
}
