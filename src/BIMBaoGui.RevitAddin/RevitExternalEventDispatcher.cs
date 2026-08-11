using System;
using System.Collections.Concurrent;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMBaoGui.RevitAddin
{
  internal sealed class CurrentDocumentSnapshot
  {
    internal bool HasDocument { get; set; }
    internal string RevitVersion { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal bool IsFamilyDocument { get; set; }
    internal bool IsReadOnly { get; set; }
    internal bool IsSaved { get; set; }
  }

  internal sealed class RevitRequest
  {
    internal Action<CurrentDocumentSnapshot> Completed { get; set; }
    internal Action<Exception> Failed { get; set; }
  }

  internal sealed class RevitExternalEventHandler : IExternalEventHandler
  {
    private readonly ConcurrentQueue<RevitRequest> _queue;

    internal RevitExternalEventHandler(ConcurrentQueue<RevitRequest> queue)
    {
      _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Execute(UIApplication application)
    {
      while (_queue.TryDequeue(out RevitRequest request))
      {
        try
        {
          CurrentDocumentSnapshot snapshot =
            RevitDocumentSnapshotService.Capture(application);
          request.Completed?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
          request.Failed?.Invoke(exception);
        }
      }
    }

    public string GetName()
    {
      return "BIMBaoGui Revit request dispatcher";
    }
  }

  internal static class RevitExternalEventDispatcher
  {
    private static readonly object SyncRoot = new object();
    private static readonly ConcurrentQueue<RevitRequest> Queue =
      new ConcurrentQueue<RevitRequest>();
    private static ExternalEvent _externalEvent;
    private static int _disposed;

    internal static void EnsureInitialized()
    {
      if (Volatile.Read(ref _disposed) != 0)
        throw new ObjectDisposedException(nameof(RevitExternalEventDispatcher));
      if (_externalEvent != null) return;

      lock (SyncRoot)
      {
        if (_externalEvent != null) return;
        _externalEvent = ExternalEvent.Create(
          new RevitExternalEventHandler(Queue));
      }
    }

    internal static void RequestDocumentSnapshot(
      Action<CurrentDocumentSnapshot> completed,
      Action<Exception> failed)
    {
      EnsureInitialized();
      Queue.Enqueue(new RevitRequest
      {
        Completed = completed,
        Failed = failed
      });
      _externalEvent.Raise();
    }

    internal static void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      lock (SyncRoot)
      {
        _externalEvent?.Dispose();
        _externalEvent = null;
        while (Queue.TryDequeue(out _))
        {
        }
      }
    }
  }

  internal static class RevitDocumentSnapshotService
  {
    internal static CurrentDocumentSnapshot Capture(UIApplication application)
    {
      if (application == null)
        throw new ArgumentNullException(nameof(application));
      Document document = application.ActiveUIDocument?.Document;
      if (document == null)
      {
        return new CurrentDocumentSnapshot
        {
          HasDocument = false,
          RevitVersion = application.Application.VersionNumber
            ?? string.Empty
        };
      }

      string path = document.PathName ?? string.Empty;
      return new CurrentDocumentSnapshot
      {
        HasDocument = true,
        RevitVersion = application.Application.VersionNumber ?? string.Empty,
        DocumentTitle = document.Title ?? string.Empty,
        DocumentPath = path,
        IsFamilyDocument = document.IsFamilyDocument,
        IsReadOnly = document.IsReadOnly,
        IsSaved = !string.IsNullOrWhiteSpace(path)
      };
    }
  }
}
