using System;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpRevitCommandGateway
  {
    internal Task<CurrentDocumentSnapshot> GetDocumentStatusAsync(
      CancellationToken cancellationToken)
    {
      return Schedule<CurrentDocumentSnapshot>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestDocumentSnapshot(
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage01ReadResult> ReadStage01Async(
      CancellationToken cancellationToken)
    {
      return Schedule<NativeStage01ReadResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage01Read(
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage01WriteResult> WriteStage01Async(
      NativeStage01WriteRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return Schedule<NativeStage01WriteResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage01Write(
            request,
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage02RevitPreviewResult> PreviewStage02Async(
      NativeStage02PreviewRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return Schedule<NativeStage02RevitPreviewResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage02Preview(
            request,
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage02WriteResult> WriteStage02Async(
      NativeStage02WriteRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return Schedule<NativeStage02WriteResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage02Write(
            request,
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage03ScanResult> ScanStage03Async(
      NativeStage03ScanRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return Schedule<NativeStage03ScanResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage03Scan(
            request,
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage03ExecutionResult> ExportStage03Async(
      NativeStage03ExportRequest request,
      CancellationToken cancellationToken)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      return Schedule<NativeStage03ExecutionResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage03Export(
            request,
            completed,
            failed),
        cancellationToken);
    }

    internal Task<NativeStage03ExecutionResult> RevalidateStage03Async(
      string ifcPath,
      CancellationToken cancellationToken)
    {
      return Schedule<NativeStage03ExecutionResult>(
        (completed, failed) =>
          RevitExternalEventDispatcher.RequestStage03Revalidate(
            ifcPath,
            completed,
            failed),
        cancellationToken);
    }

    private static Task<T> Schedule<T>(
      Action<Action<T>, Action<Exception>> schedule,
      CancellationToken cancellationToken)
    {
      if (schedule == null) throw new ArgumentNullException(nameof(schedule));
      var completion = new TaskCompletionSource<T>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      CancellationTokenRegistration registration = default(
        CancellationTokenRegistration);
      if (cancellationToken.CanBeCanceled)
      {
        registration = cancellationToken.Register(
          () => completion.TrySetCanceled());
      }
      try
      {
        schedule(
          result =>
          {
            registration.Dispose();
            completion.TrySetResult(result);
          },
          exception =>
          {
            registration.Dispose();
            completion.TrySetException(
              exception ?? new InvalidOperationException(
                "Revit ExternalEvent 返回未知错误。" ));
          });
      }
      catch (Exception exception)
      {
        registration.Dispose();
        completion.TrySetException(exception);
      }
      return completion.Task;
    }
  }
}
