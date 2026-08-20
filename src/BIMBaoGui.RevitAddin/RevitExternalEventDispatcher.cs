using System;
using System.Diagnostics;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
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
    private readonly RevitExternalEventRequestGate _gate =
      new RevitExternalEventRequestGate();

    internal Action<UIApplication> ExecuteAction { get; set; }
    internal Action<Exception> Failed { get; set; }

    internal void Execute(UIApplication application)
    {
      _gate.Execute(() =>
      {
        if (ExecuteAction == null)
          throw new InvalidOperationException("Revit request 缺少执行委托。" );
        ExecuteAction(application);
      });
    }

    internal void Reject(
      Exception exception,
      Action<Exception> reportCallbackFailure)
    {
      _gate.Reject(Failed, exception, reportCallbackFailure);
    }
  }

  internal sealed class RevitExternalEventRequestGate
  {
    private int _state;

    internal void Execute(Action execute)
    {
      if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
      execute();
    }

    internal void Reject(
      Action<Exception> failed,
      Exception exception,
      Action<Exception> reportCallbackFailure)
    {
      if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return;
      try
      {
        failed?.Invoke(exception);
      }
      catch (Exception callbackFailure)
      {
        RevitExternalEventExecutionBoundary.ReportFailureCallback(
          reportCallbackFailure,
          exception,
          callbackFailure);
      }
    }
  }

  internal sealed class RevitExternalEventRequestQueue<TRequest>
  {
    private readonly object _syncRoot = new object();
    private readonly System.Collections.Generic.Queue<TRequest> _queue =
      new System.Collections.Generic.Queue<TRequest>();

    internal bool IsEmpty
    {
      get
      {
        lock (_syncRoot) return _queue.Count == 0;
      }
    }

    internal void Enqueue(TRequest request)
    {
      lock (_syncRoot) _queue.Enqueue(request);
    }

    internal bool TryDequeue(out TRequest request)
    {
      lock (_syncRoot)
      {
        if (_queue.Count == 0)
        {
          request = default(TRequest);
          return false;
        }
        request = _queue.Dequeue();
        return true;
      }
    }

    internal bool Remove(TRequest request)
    {
      lock (_syncRoot)
      {
        bool removed = false;
        int count = _queue.Count;
        for (int index = 0; index < count; index++)
        {
          TRequest current = _queue.Dequeue();
          if (!removed && ReferenceEquals(current, request))
          {
            removed = true;
            continue;
          }
          _queue.Enqueue(current);
        }
        return removed;
      }
    }

    internal void Synchronize(Action action)
    {
      lock (_syncRoot) action();
    }
  }

  internal static class RevitExternalEventExecutionBoundary
  {
    internal static void Execute<TApplication, TRequest>(
      RevitExternalEventRequestQueue<TRequest> queue,
      TApplication application,
      Action<TApplication> observeApplication,
      Action<TRequest, TApplication> executeRequest,
      Action<TRequest, Exception> failRequest,
      Action<Exception> reportCallbackFailure)
    {
      try
      {
        observeApplication(application);
      }
      catch (Exception exception)
      {
        while (queue.TryDequeue(out TRequest request))
          FailRequest(
            request,
            exception,
            failRequest,
            reportCallbackFailure);
        return;
      }

      while (queue.TryDequeue(out TRequest request))
      {
        try
        {
          executeRequest(request, application);
        }
        catch (Exception exception)
        {
          FailRequest(
            request,
            exception,
            failRequest,
            reportCallbackFailure);
        }
      }
    }

    private static void FailRequest<TRequest>(
      TRequest request,
      Exception exception,
      Action<TRequest, Exception> failRequest,
      Action<Exception> reportCallbackFailure)
    {
      try
      {
        failRequest(request, exception);
      }
      catch (Exception callbackFailure)
      {
        ReportFailureCallback(
          reportCallbackFailure,
          exception,
          callbackFailure);
      }
    }

    internal static void ReportFailureCallback(
      Action<Exception> reportCallbackFailure,
      Exception requestFailure,
      Exception callbackFailure)
    {
      ReportCallbackFailure(
        reportCallbackFailure,
        new AggregateException(
          "BIMBaoGui request and failure callback both failed.",
          requestFailure,
          callbackFailure));
    }

    private static void ReportCallbackFailure(
      Action<Exception> reportCallbackFailure,
      Exception exception)
    {
      try
      {
        reportCallbackFailure?.Invoke(exception);
      }
      catch
      {
      }
    }
  }

  internal enum RevitExternalEventRaiseStatus
  {
    Accepted,
    Pending,
    Denied,
    TimedOut,
    Unknown
  }

  internal static class RevitExternalEventRaiseBoundary
  {
    internal static void EnqueueAndRaise<TRequest>(
      RevitExternalEventRequestQueue<TRequest> queue,
      TRequest request,
      Func<RevitExternalEventRaiseStatus> raise,
      Action<TRequest, Exception, Action<Exception>> rejectRequest,
      Action<Exception> reportCallbackFailure)
      where TRequest : class
    {
      if (queue == null) throw new ArgumentNullException(nameof(queue));
      if (ReferenceEquals(request, null))
        throw new ArgumentNullException(nameof(request));
      if (raise == null) throw new ArgumentNullException(nameof(raise));
      if (rejectRequest == null)
        throw new ArgumentNullException(nameof(rejectRequest));
      Exception rejection = null;
      queue.Synchronize(() =>
      {
        queue.Enqueue(request);
        try
        {
          RevitExternalEventRaiseStatus result = raise();
          if (result != RevitExternalEventRaiseStatus.Accepted &&
              result != RevitExternalEventRaiseStatus.Pending)
          {
            rejection = new InvalidOperationException(
              "Revit ExternalEvent request was not accepted: " +
              result + ".");
          }
        }
        catch (Exception exception)
        {
          rejection = exception;
        }
        if (rejection != null && !queue.Remove(request))
        {
          rejection = new AggregateException(
            "Rejected Revit ExternalEvent request could not be removed.",
            rejection,
            new InvalidOperationException(
              "The rejected request was not present in the request queue."));
        }
      });
      if (rejection != null)
        rejectRequest(request, rejection, reportCallbackFailure);
    }
  }

  internal sealed class RevitExternalEventHandler : IExternalEventHandler
  {
    private readonly RevitExternalEventRequestQueue<RevitRequest> _queue;

    internal RevitExternalEventHandler(
      RevitExternalEventRequestQueue<RevitRequest> queue)
    {
      _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Execute(UIApplication application)
    {
      RevitExternalEventExecutionBoundary.Execute(
        _queue,
        application,
        RevitExternalEventDispatcher.ObserveApplication,
        (request, currentApplication) => request.Execute(currentApplication),
        (request, exception) => request.Failed?.Invoke(exception),
        exception => Trace.TraceError(
          "BIMBaoGui ExternalEvent failure callback threw: {0}",
          exception));
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
    private bool _attachmentUncertain;
    private Exception _attachFailure;

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
        lock (_syncRoot) return _attached || _attachmentUncertain;
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
      object source = _source;
      RecoverUncertainAttachment(source);
      try
      {
        _attach(source);
        _attached = true;
      }
      catch (Exception attachFailure)
      {
        _attached = false;
        try
        {
          _detach(source);
        }
        catch (Exception compensationFailure)
        {
          _attachmentUncertain = true;
          _attachFailure = attachFailure;
          throw new AggregateException(
            "BIMBaoGui document-boundary attachment state is uncertain.",
            attachFailure,
            compensationFailure);
        }
        _attachmentUncertain = false;
        _attachFailure = null;
        throw;
      }
    }

    private void RecoverUncertainAttachment(object source)
    {
      if (!_attachmentUncertain) return;
      try
      {
        _detach(source);
      }
      catch (Exception recoveryFailure)
      {
        throw new AggregateException(
          "BIMBaoGui document-boundary attachment cleanup failed.",
          _attachFailure ?? new InvalidOperationException(
            "The original attachment failure was not recorded."),
          recoveryFailure);
      }
      _attachmentUncertain = false;
      _attachFailure = null;
    }

    private void DetachIfNeeded()
    {
      if (!_attached && !_attachmentUncertain) return;
      _detach(_source);
      _attached = false;
      _attachmentUncertain = false;
      _attachFailure = null;
    }
  }

  internal static class RevitExternalEventDispatcher
  {
    private static readonly object SyncRoot = new object();
    private static readonly RevitExternalEventRequestQueue<RevitRequest> Queue =
      new RevitExternalEventRequestQueue<RevitRequest>();
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
      application.ViewActivated += OnViewActivated;
      _observedApplication = application;
    }

    private static void DetachObservedApplication(object source)
    {
      UIApplication application = source as UIApplication;
      try
      {
        if (application != null)
          application.ViewActivated -= OnViewActivated;
      }
      finally
      {
        if (ReferenceEquals(_observedApplication, application))
          _observedApplication = null;
      }
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

    internal static void RequestStage02BRead(
      Action<NativeStage02BReadResult> completed,
      Action<Exception> failed)
    {
      Enqueue(
        application => completed?.Invoke(
          NativeStage02BRevitReadService.Read(application)),
        failed);
    }

    internal static void RequestStage02BWrite(
      NativeStage02BWriteRequest request,
      Action<NativeStage02BWriteResult> completed,
      Action<Exception> failed)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeStage02BWriteRequest snapshot = request.Clone();
      Enqueue(
        application => completed?.Invoke(
          NativeStage02BRevitWriteService.Execute(application, snapshot)),
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
      var request = new RevitRequest
      {
        ExecuteAction = execute,
        Failed = failed
      };
      RevitExternalEventRaiseBoundary.EnqueueAndRaise(
        Queue,
        request,
        RaiseExternalEvent,
        (currentRequest, exception, report) =>
          currentRequest.Reject(exception, report),
        exception => Trace.TraceError(
          "BIMBaoGui ExternalEvent failure callback threw: {0}",
          exception));
    }

    private static RevitExternalEventRaiseStatus RaiseExternalEvent()
    {
      switch (_externalEvent.Raise())
      {
        case ExternalEventRequest.Accepted:
          return RevitExternalEventRaiseStatus.Accepted;
        case ExternalEventRequest.Pending:
          return RevitExternalEventRaiseStatus.Pending;
        case ExternalEventRequest.Denied:
          return RevitExternalEventRaiseStatus.Denied;
        case ExternalEventRequest.TimedOut:
          return RevitExternalEventRaiseStatus.TimedOut;
        default:
          return RevitExternalEventRaiseStatus.Unknown;
      }
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
