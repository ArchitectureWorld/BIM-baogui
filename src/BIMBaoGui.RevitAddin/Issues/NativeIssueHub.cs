using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal sealed class NativeIssueHub
  {
    private readonly object _syncRoot = new object();
    private readonly List<NativeIssueRecord> _issues =
      new List<NativeIssueRecord>();

    internal event Action IssuesChanged;

    internal string DocumentFingerprint { get; private set; } = string.Empty;

    internal void ResetForDocument(string documentFingerprint)
    {
      string next = Clean(documentFingerprint);
      bool changed;
      lock (_syncRoot)
      {
        changed = !string.Equals(
          DocumentFingerprint,
          next,
          StringComparison.Ordinal);
        if (!changed) return;
        DocumentFingerprint = next;
        _issues.Clear();
      }
      IssuesChanged?.Invoke();
    }

    internal void ResetForDocument(CurrentDocumentSnapshot snapshot)
    {
      ResetForDocument(snapshot != null && snapshot.HasDocument
        ? snapshot.DocumentFingerprint
        : string.Empty);
    }

    internal void Replace(
      string sourceFeature,
      IEnumerable<NativeIssueRecord> issues)
    {
      string source = Clean(sourceFeature);
      if (source.Length == 0)
        throw new ArgumentException(
          "Issue source feature is required.",
          nameof(sourceFeature));
      NativeIssueRecord[] incoming = (issues ?? Array.Empty<NativeIssueRecord>())
        .Where(value => value != null)
        .Select(CloneIssue)
        .ToArray();
      if (incoming.Any(value => !string.Equals(
        Clean(value.DocumentFingerprint),
        DocumentFingerprint,
        StringComparison.Ordinal)))
        throw new ArgumentException(
          "Issue document fingerprint does not match the current hub document.",
          nameof(issues));
      if (incoming.Any(value => !string.Equals(
        Clean(value.SourceFeature),
        source,
        StringComparison.Ordinal)))
        throw new ArgumentException(
          "Issue source feature does not match the replacement source.",
          nameof(issues));

      lock (_syncRoot)
      {
        _issues.RemoveAll(value => string.Equals(
          Clean(value.SourceFeature),
          source,
          StringComparison.Ordinal));
        _issues.AddRange(incoming);
        SortInPlace();
      }
      IssuesChanged?.Invoke();
    }

    internal IReadOnlyList<NativeIssueRecord> Snapshot()
    {
      lock (_syncRoot)
      {
        return new ReadOnlyCollection<NativeIssueRecord>(
          _issues.Select(CloneIssue).ToArray());
      }
    }

    private void SortInPlace()
    {
      NativeIssueRecord[] sorted = _issues
        .OrderBy(value => value.Severity)
        .ThenBy(value => Clean(value.SourceFeature), StringComparer.Ordinal)
        .ThenBy(value => Clean(value.CheckId), StringComparer.Ordinal)
        .ThenBy(value => Clean(value.IssueId), StringComparer.Ordinal)
        .ToArray();
      _issues.Clear();
      _issues.AddRange(sorted);
    }

    internal static NativeIssueRecord CloneIssue(NativeIssueRecord value)
    {
      if (value == null) return null;
      return new NativeIssueRecord
      {
        IssueId = value.IssueId ?? string.Empty,
        DocumentFingerprint = value.DocumentFingerprint ?? string.Empty,
        Severity = value.Severity,
        SourceFeature = value.SourceFeature ?? string.Empty,
        CheckId = value.CheckId ?? string.Empty,
        Code = value.Code ?? string.Empty,
        Missing = value.Missing ?? string.Empty,
        Impact = value.Impact ?? string.Empty,
        Remediation = value.Remediation ?? string.Empty,
        FieldKey = value.FieldKey ?? string.Empty,
        PropertyId = value.PropertyId ?? string.Empty,
        RoleId = value.RoleId ?? string.Empty,
        Elements = new ReadOnlyCollection<NativeIssueElementReference>(
          (value.Elements ?? Array.Empty<NativeIssueElementReference>())
            .Where(element => element != null)
            .Select(NativeIssueNavigationRequest.CloneElement)
            .ToArray()),
        Route = value.Route
      };
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
