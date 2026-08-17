using System;
using System.Collections.Concurrent;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin
{
  internal sealed class CurrentDocumentSnapshot
  {
    internal bool HasDocument { get; set; }
    internal string RevitVersion { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string DocumentPath { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal bool IsFamilyDocument { get; set; }
    internal bool IsReadOnly { get; set; }
    internal bool IsSaved { get; set; }
  }

  internal sealed class RevitRequest
  {
    internal Action<UIApplication> ExecuteAction { get; set; }
    internal Action<Exception> Failed { get; set; }

    internal void Execute(UIApplication application)
    {
      try
      {
        if (ExecuteAction == null)
          throw new InvalidOperationException("Revit request 缺少执行委托。" );
        ExecuteAction(application);
      }
      catch (Exception exception)
      {
        Failed?.Invoke(exception);
      }
    }
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
      RevitExternalEventDispatcher.ObserveApplication(application);
      while (_queue.TryDequeue(out RevitRequest request))
        request.Execute(application);
    }

    public string GetName()
    {
      return "BIMBaoGui Revit request dispatcher";
    }
  }

  internal sealed class NativeDocumentBoundarySubscriptionRegistry
  {
    private readonly object _syncRoot = new object();
    private readonly Action<object> _attach;
    private readonly Action<object> _detach;
    private readonly System.Collections.Generic.List<
      Action<CurrentDocumentSnapshot>> _subscribers =
        new System.Collections.Generic.List<Action<CurrentDocumentSnapshot>>();
    private object _source;
    private bool _attached;

    internal NativeDocumentBoundarySubscriptionRegistry(
      Action<object> attach,
      Action<object> detach)
    {
      _attach = attach ?? throw new ArgumentNullException(nameof(attach));
      _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    internal int SubscriberCount
    {
      get
      {
        lock (_syncRoot) return _subscribers.Count;
      }
    }

    internal bool IsAttached
    {
      get
      {
        lock (_syncRoot) return _attached;
      }
    }

    internal object CurrentSource
    {
      get
      {
        lock (_syncRoot) return _source;
      }
    }

    internal void SetSource(object source)
    {
      lock (_syncRoot)
      {
        if (!ReferenceEquals(_source, source))
        {
          DetachIfNeeded();
          _source = source;
        }
        AttachIfNeeded();
      }
    }

    internal void Add(Action<CurrentDocumentSnapshot> subscriber)
    {
      if (subscriber == null) return;
      lock (_syncRoot)
      {
        _subscribers.Add(subscriber);
        AttachIfNeeded();
      }
    }

    internal void Remove(Action<CurrentDocumentSnapshot> subscriber)
    {
      if (subscriber == null) return;
      lock (_syncRoot)
      {
        int index = _subscribers.FindLastIndex(value => value == subscriber);
        if (index < 0) return;
        _subscribers.RemoveAt(index);
        if (_subscribers.Count == 0) DetachIfNeeded();
      }
    }

    internal void Publish(CurrentDocumentSnapshot snapshot)
    {
      Action<CurrentDocumentSnapshot>[] subscribers;
      lock (_syncRoot) subscribers = _subscribers.ToArray();
      foreach (Action<CurrentDocumentSnapshot> subscriber in subscribers)
      {
        try
        {
          subscriber(snapshot);
        }
        catch
        {
        }
      }
    }

    internal void Clear()
    {
      lock (_syncRoot)
      {
        DetachIfNeeded();
        _subscribers.Clear();
        _source = null;
      }
    }

    private void AttachIfNeeded()
    {
      if (_attached || _source == null || _subscribers.Count == 0) return;
      _attach(_source);
      _attached = true;
    }

    private void DetachIfNeeded()
    {
      if (!_attached) return;
      _detach(_source);
      _attached = false;
    }
  }

  internal static class RevitExternalEventDispatcher
  {
    private static readonly object SyncRoot = new object();
    private static readonly ConcurrentQueue<RevitRequest> Queue =
      new ConcurrentQueue<RevitRequest>();
    private static readonly NativeDocumentBoundarySubscriptionRegistry
      BoundarySubscriptions = new NativeDocumentBoundarySubscriptionRegistry(
        AttachObservedApplication,
        DetachObservedApplication);
    private static ExternalEvent _externalEvent;
    private static UIApplication _observedApplication;
    private static int _disposed;

    internal static event Action<CurrentDocumentSnapshot>
      DocumentBoundaryChanged
    {
      add { BoundarySubscriptions.Add(value); }
      remove { BoundarySubscriptions.Remove(value); }
    }

    internal static void ObserveApplication(UIApplication application)
    {
      if (application == null) return;
      BoundarySubscriptions.SetSource(application);
    }

    private static void AttachObservedApplication(object source)
    {
      UIApplication application = source as UIApplication;
      if (application == null) return;
      _observedApplication = application;
      _observedApplication.ViewActivated += OnViewActivated;
    }

    private static void DetachObservedApplication(object source)
    {
      UIApplication application = source as UIApplication;
      if (application != null)
        application.ViewActivated -= OnViewActivated;
      if (ReferenceEquals(_observedApplication, application))
        _observedApplication = null;
    }

    private static void OnViewActivated(
      object sender,
      ViewActivatedEventArgs args)
    {
      UIApplication application = sender as UIApplication;
      if (application == null)
        application = BoundarySubscriptions.CurrentSource as UIApplication;
      CurrentDocumentSnapshot snapshot;
      try
      {
        snapshot = RevitDocumentSnapshotService.Capture(application);
      }
      catch
      {
        snapshot = new CurrentDocumentSnapshot
        {
          HasDocument = application?.ActiveUIDocument?.Document != null,
          RevitVersion = application?.Application?.VersionNumber
            ?? string.Empty,
          DocumentFingerprint = string.Empty
        };
      }
      PublishDocumentBoundary(snapshot);
    }

    private static void PublishDocumentBoundary(
      CurrentDocumentSnapshot snapshot)
    {
      BoundarySubscriptions.Publish(snapshot);
    }

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
      Enqueue(
        application => completed?.Invoke(
          RevitDocumentSnapshotService.Capture(application)),
        failed);
    }

    internal static void RequestStage01Read(
      Action<NativeStage01ReadResult> completed,
      Action<Exception> failed)
    {
      Enqueue(
        application => completed?.Invoke(
          NativeStage01RevitReadService.Read(application)),
        failed);
    }

    internal static void RequestStage01Write(
      NativeStage01WriteRequest request,
      Action<NativeStage01WriteResult> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeStage01WriteRequest snapshot = new NativeStage01WriteRequest
      {
        Model = request.Model?.Clone(),
        ConfirmBlankProject = request.ConfirmBlankProject,
        AllowReinitialize = request.AllowReinitialize
      };
      Enqueue(
        application => completed?.Invoke(
          NativeStage01RevitService.Execute(application, snapshot)),
        failed);
    }

    internal static void RequestStage02Preview(
      NativeStage02PreviewRequest request,
      Action<NativeStage02RevitPreviewResult> completed,
      Action<Exception> failed)
    {
      NativeStage02PreviewRequest snapshot = request?.Clone()
        ?? new NativeStage02PreviewRequest();
      Enqueue(
        application => completed?.Invoke(
          NativeStage02RevitService.CreatePreview(application, snapshot)),
        failed);
    }

    internal static void RequestStage02Write(
      NativeStage02WriteRequest request,
      Action<NativeStage02WriteResult> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeStage02WriteRequest snapshot = request.Clone();
      Enqueue(
        application => completed?.Invoke(
          NativeStage02RevitWriteService.Execute(application, snapshot)),
        failed);
    }

    internal static void RequestStage02PickElements(
      Action<NativeStage02SelectionResult> completed,
      Action<Exception> failed)
    {
      Enqueue(
        application => completed?.Invoke(
          NativeStage02InteractionService.PickElements(application)),
        failed);
    }

    internal static void RequestStage02CurrentSelection(
      Action<NativeStage02SelectionResult> completed,
      Action<Exception> failed)
    {
      Enqueue(
        application => completed?.Invoke(
          NativeStage02InteractionService.ReadCurrentSelection(application)),
        failed);
    }

    internal static void RequestIssueNavigation(
      NativeIssueNavigationRequest request,
      Action<NativeIssueNavigationResult> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeIssueNavigationRequest snapshot = request.Clone();
      Enqueue(
        application => completed?.Invoke(
          NativeRevitIssueNavigationService.Execute(application, snapshot)),
        failed);
    }

    internal static void RequestStage03Scan(
      NativeStage03ScanRequest request,
      Action<NativeStage03ScanResult> completed,
      Action<Exception> failed)
    {
      NativeStage03ScanRequest snapshot = request?.Clone()
        ?? new NativeStage03ScanRequest();
      Enqueue(
        application => completed?.Invoke(
          NativeStage03WorkflowService.Scan(application, snapshot)),
        failed);
    }

    internal static void RequestStage03Export(
      NativeStage03ExportRequest request,
      Action<NativeStage03ExecutionResult> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeStage03ExportRequest snapshot = request.Clone();
      Enqueue(
        application => completed?.Invoke(
          NativeStage03WorkflowService.Execute(application, snapshot)),
        failed);
    }

    internal static void RequestStage03Revalidate(
      string ifcPath,
      Action<NativeStage03ExecutionResult> completed,
      Action<Exception> failed)
    {
      string snapshot = ifcPath ?? string.Empty;
      Enqueue(
        application => completed?.Invoke(
          NativeStage03WorkflowService.RevalidateFile(application, snapshot)),
        failed);
    }

    private static void Enqueue(
      Action<UIApplication> execute,
      Action<Exception> failed)
    {
      if (execute == null) throw new ArgumentNullException(nameof(execute));
      EnsureInitialized();
      Queue.Enqueue(new RevitRequest
      {
        ExecuteAction = execute,
        Failed = failed
      });
      _externalEvent.Raise();
    }

    internal static void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      lock (SyncRoot)
      {
        BoundarySubscriptions.Clear();
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
      string documentFingerprint;
      NativeRevitIssueNavigationService.TryCurrentDocumentFingerprint(
        application,
        out documentFingerprint);
      return new CurrentDocumentSnapshot
      {
        HasDocument = true,
        RevitVersion = application.Application.VersionNumber ?? string.Empty,
        DocumentTitle = document.Title ?? string.Empty,
        DocumentPath = path,
        DocumentFingerprint = documentFingerprint,
        IsFamilyDocument = document.IsFamilyDocument,
        IsReadOnly = document.IsReadOnly,
        IsSaved = !string.IsNullOrWhiteSpace(path)
      };
    }
  }
}
