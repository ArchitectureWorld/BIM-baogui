using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal interface INativeDocumentBoundarySource
  {
    event Action<CurrentDocumentSnapshot> DocumentBoundaryChanged;
  }

  internal sealed class NativeRevitDocumentBoundarySource
    : INativeDocumentBoundarySource
  {
    internal static NativeRevitDocumentBoundarySource Instance { get; } =
      new NativeRevitDocumentBoundarySource();

    private NativeRevitDocumentBoundarySource()
    {
    }

    public event Action<CurrentDocumentSnapshot> DocumentBoundaryChanged
    {
      add
      {
        RevitExternalEventDispatcher.DocumentBoundaryChanged += value;
      }
      remove
      {
        RevitExternalEventDispatcher.DocumentBoundaryChanged -= value;
      }
    }
  }

  internal sealed class NativeIssueHubLifecycle
  {
    private readonly object _syncRoot = new object();
    private readonly NativeIssueHub _hub;
    private readonly INativeDocumentBoundarySource _source;
    private readonly Action _refresh;
    private readonly Action<Action> _dispatch;
    private bool _active;

    internal NativeIssueHubLifecycle(
      NativeIssueHub hub,
      INativeDocumentBoundarySource source,
      Action refresh,
      Action<Action> dispatch = null)
    {
      _hub = hub ?? throw new ArgumentNullException(nameof(hub));
      _source = source ?? throw new ArgumentNullException(nameof(source));
      _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
      _dispatch = dispatch ?? (action => action());
    }

    internal void Activate()
    {
      lock (_syncRoot)
      {
        if (_active) return;
        _source.DocumentBoundaryChanged += ApplySnapshot;
        _active = true;
      }
    }

    internal void Deactivate()
    {
      lock (_syncRoot)
      {
        if (!_active) return;
        _source.DocumentBoundaryChanged -= ApplySnapshot;
        _active = false;
      }
    }

    internal void ApplySnapshot(CurrentDocumentSnapshot snapshot)
    {
      ApplySnapshot(snapshot, null);
    }

    internal void ApplySnapshot(
      CurrentDocumentSnapshot snapshot,
      Action completed)
    {
      _dispatch(() =>
      {
        _hub.ResetForDocument(snapshot);
        _refresh();
        completed?.Invoke();
      });
    }

    internal void ApplySnapshotFailure(
      Exception exception,
      Action<Exception> failed)
    {
      _dispatch(() =>
      {
        _hub.ResetForDocument(string.Empty);
        _refresh();
        failed?.Invoke(exception);
      });
    }
  }

  internal static class NativeIssueSnapshotRequest
  {
    internal static void Execute(
      Action<Action<CurrentDocumentSnapshot>, Action<Exception>> request,
      NativeIssueHubLifecycle lifecycle,
      Action<CurrentDocumentSnapshot> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
      Action<Exception> failClosed = exception =>
        lifecycle.ApplySnapshotFailure(exception, failed);
      try
      {
        request(completed, failClosed);
      }
      catch (Exception exception)
      {
        failClosed(exception);
      }
    }
  }

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
