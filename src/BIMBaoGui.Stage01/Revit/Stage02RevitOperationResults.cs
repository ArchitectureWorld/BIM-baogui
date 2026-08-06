using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit
{
  internal enum Stage02RevitSelectionDisposition
  {
    Success,
    BusinessBlocked,
    Cancelled,
    TechnicalFailure
  }

  internal sealed class Stage02RevitSelectionItem
  {
    internal Stage02RevitSelectionItem(
      Stage02ElementReference element,
      string roleHint)
      : this(element, roleHint, string.Empty)
    {
    }

    internal Stage02RevitSelectionItem(
      Stage02ElementReference element,
      string roleHint,
      string stage01RecordIdentity)
    {
      Element = element ?? throw new ArgumentNullException(nameof(element));
      RoleHint = roleHint ?? string.Empty;
      Stage01RecordIdentity = stage01RecordIdentity ?? string.Empty;
    }

    internal Stage02ElementReference Element { get; }
    internal string DocumentFingerprint => Element.DocumentFingerprint;
    internal string UniqueId => Element.UniqueId;
    internal int ElementId => Element.ElementId;
    internal string RoleHint { get; }
    internal string Stage01RecordIdentity { get; }
  }

  internal sealed class Stage02RevitSelectionResult
  {
    internal Stage02RevitSelectionResult(
      bool cancelled,
      IEnumerable<Stage02RevitSelectionItem> items,
      IEnumerable<string> messages)
      : this(
        Stage02SelectionModes.Legacy,
        cancelled,
        items,
        messages)
    {
    }

    internal Stage02RevitSelectionResult(
      string selectionMode,
      bool cancelled,
      IEnumerable<Stage02RevitSelectionItem> items,
      IEnumerable<string> messages)
      : this(
        selectionMode,
        ResolveDisposition(cancelled, messages),
        items,
        messages,
        null)
    {
    }

    private Stage02RevitSelectionResult(
      string selectionMode,
      Stage02RevitSelectionDisposition disposition,
      IEnumerable<Stage02RevitSelectionItem> items,
      IEnumerable<string> messages,
      Exception exception)
    {
      SelectionMode = selectionMode ?? string.Empty;
      Disposition = disposition;
      Items = new ReadOnlyCollection<Stage02RevitSelectionItem>(
        (items ?? Array.Empty<Stage02RevitSelectionItem>()).ToArray());
      Messages = new ReadOnlyCollection<string>(
        (messages ?? Array.Empty<string>()).ToArray());
      Exception = exception;
    }

    internal Stage02RevitSelectionDisposition Disposition { get; }
    internal bool Cancelled =>
      Disposition == Stage02RevitSelectionDisposition.Cancelled;
    internal string SelectionMode { get; }
    internal IReadOnlyList<Stage02RevitSelectionItem> Items { get; }
    internal IReadOnlyList<string> Messages { get; }
    internal Exception Exception { get; }
    internal bool Success =>
      Disposition == Stage02RevitSelectionDisposition.Success;

    internal static Stage02RevitSelectionResult BusinessBlocked(
      string selectionMode,
      IEnumerable<string> messages)
    {
      return new Stage02RevitSelectionResult(
        selectionMode,
        Stage02RevitSelectionDisposition.BusinessBlocked,
        Array.Empty<Stage02RevitSelectionItem>(),
        messages,
        null);
    }

    internal static Stage02RevitSelectionResult CancelledResult(
      string selectionMode)
    {
      return new Stage02RevitSelectionResult(
        selectionMode,
        Stage02RevitSelectionDisposition.Cancelled,
        Array.Empty<Stage02RevitSelectionItem>(),
        Array.Empty<string>(),
        null);
    }

    internal static Stage02RevitSelectionResult TechnicalFailure(
      string selectionMode,
      string message,
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitSelectionResult(
        selectionMode,
        Stage02RevitSelectionDisposition.TechnicalFailure,
        Array.Empty<Stage02RevitSelectionItem>(),
        new[] { message ?? string.Empty },
        exception);
    }

    private static Stage02RevitSelectionDisposition ResolveDisposition(
      bool cancelled,
      IEnumerable<string> messages)
    {
      if (cancelled) return Stage02RevitSelectionDisposition.Cancelled;
      return (messages ?? Array.Empty<string>()).Any()
        ? Stage02RevitSelectionDisposition.BusinessBlocked
        : Stage02RevitSelectionDisposition.Success;
    }
  }

  internal enum Stage02RevitPreviewDisposition
  {
    Success,
    BusinessBlocked,
    TechnicalFailure,
    NoResult
  }

  internal sealed class Stage02RevitPreviewResult
  {
    private const string NoResultDiagnostic =
      "Stage02 preview service returned no result.";

    internal Stage02RevitPreviewResult(
      Stage02Preview preview,
      IEnumerable<Stage02Blocker> blockers)
    {
      Stage02Blocker[] materializedBlockers =
        (blockers ?? Array.Empty<Stage02Blocker>()).ToArray();
      Disposition = ResolveDisposition(preview, materializedBlockers);
      Preview = preview;
      Blockers = new ReadOnlyCollection<Stage02Blocker>(materializedBlockers);
      Exception = Disposition == Stage02RevitPreviewDisposition.NoResult
        ? new InvalidOperationException(NoResultDiagnostic)
        : null;
    }

    private Stage02RevitPreviewResult(
      Stage02RevitPreviewDisposition disposition,
      Stage02Preview preview,
      IEnumerable<Stage02Blocker> blockers,
      Exception exception)
    {
      Disposition = disposition;
      Preview = preview;
      Blockers = new ReadOnlyCollection<Stage02Blocker>(
        (blockers ?? Array.Empty<Stage02Blocker>()).ToArray());
      Exception = exception;
    }

    internal Stage02RevitPreviewDisposition Disposition { get; }
    internal Stage02Preview Preview { get; }
    internal IReadOnlyList<Stage02Blocker> Blockers { get; }
    internal Exception Exception { get; }
    internal bool Success =>
      Disposition == Stage02RevitPreviewDisposition.Success;

    internal static Stage02RevitPreviewResult BusinessBlocked(
      string code,
      string message)
    {
      return new Stage02RevitPreviewResult(
        Stage02RevitPreviewDisposition.BusinessBlocked,
        null,
        new[] { new Stage02Blocker(code, message) },
        null);
    }

    internal static Stage02RevitPreviewResult TechnicalFailure(
      string message,
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitPreviewResult(
        Stage02RevitPreviewDisposition.TechnicalFailure,
        null,
        new[] { new Stage02Blocker("REVIT_PREVIEW_FAILED", message) },
        exception);
    }

    internal static Stage02RevitPreviewResult NoResult()
    {
      return new Stage02RevitPreviewResult(
        Stage02RevitPreviewDisposition.NoResult,
        null,
        Array.Empty<Stage02Blocker>(),
        new InvalidOperationException(NoResultDiagnostic));
    }

    internal static Stage02RevitPreviewResult FromException(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      if (exception is Stage02ContractException contract)
        return BusinessBlocked(contract.Code, contract.Message);
      return TechnicalFailure(exception.Message, exception);
    }

    private static Stage02RevitPreviewDisposition ResolveDisposition(
      Stage02Preview preview,
      IEnumerable<Stage02Blocker> blockers)
    {
      if ((blockers ?? Array.Empty<Stage02Blocker>()).Any())
        return Stage02RevitPreviewDisposition.BusinessBlocked;
      return preview != null
        ? Stage02RevitPreviewDisposition.Success
        : Stage02RevitPreviewDisposition.NoResult;
    }
  }

  internal sealed class Stage02RevitFailureReportDecision
  {
    private Stage02RevitFailureReportDecision(
      bool shouldWrite,
      string operationStage,
      string errorCode,
      string diagnosticMessage,
      Exception exception)
    {
      ShouldWrite = shouldWrite;
      OperationStage = operationStage ?? string.Empty;
      ErrorCode = errorCode ?? string.Empty;
      DiagnosticMessage = diagnosticMessage ?? string.Empty;
      Exception = exception;
    }

    internal bool ShouldWrite { get; }
    internal string OperationStage { get; }
    internal string ErrorCode { get; }
    internal string DiagnosticMessage { get; }
    internal Exception Exception { get; }

    internal static Stage02RevitFailureReportDecision None()
    {
      return new Stage02RevitFailureReportDecision(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        null);
    }

    internal static Stage02RevitFailureReportDecision SelectionTechnical(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitFailureReportDecision(
        true,
        "PREVIEW_SELECTION",
        "STAGE02_SELECTION_SERVICE_EXCEPTION",
        "Stage02 元素选择发生技术失败。",
        exception);
    }

    internal static Stage02RevitFailureReportDecision SelectionNoResult(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitFailureReportDecision(
        true,
        "PREVIEW_SELECTION",
        "STAGE02_SELECTION_NO_RESULT",
        "Stage02 元素选择服务未返回结果。",
        exception);
    }

    internal static Stage02RevitFailureReportDecision PreviewTechnical(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitFailureReportDecision(
        true,
        "PREVIEW_BUILD",
        "STAGE02_PREVIEW_SERVICE_EXCEPTION",
        "Stage02 预览构建发生技术失败。",
        exception);
    }

    internal static Stage02RevitFailureReportDecision PreviewNoResult(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitFailureReportDecision(
        true,
        "PREVIEW_BUILD",
        "STAGE02_PREVIEW_NO_RESULT",
        "Stage02 预览服务未返回可发布结果。",
        exception);
    }
  }

  internal static class Stage02RevitHostFailurePolicy
  {
    internal static Stage02RevitSelectionResult ForSelection(
      string selectionMode,
      string message,
      Exception exception)
    {
      return exception == null
        ? Stage02RevitSelectionResult.BusinessBlocked(
          selectionMode,
          new[] { message ?? string.Empty })
        : Stage02RevitSelectionResult.TechnicalFailure(
          selectionMode,
          message,
          exception);
    }

    internal static Stage02RevitPreviewResult ForPreview(
      string message,
      Exception exception)
    {
      return exception == null
        ? Stage02RevitPreviewResult.BusinessBlocked(
          "REVIT_HOST_UNAVAILABLE",
          message)
        : Stage02RevitPreviewResult.TechnicalFailure(
          message,
          exception);
    }
  }

  internal sealed class Stage02RevitWriteEnqueueFailureDecision
  {
    internal Stage02RevitWriteEnqueueFailureDecision(
      string operationStage,
      string errorCode,
      string diagnosticMessage,
      string userMessage,
      Exception exception)
    {
      OperationStage = operationStage ?? string.Empty;
      ErrorCode = errorCode ?? string.Empty;
      DiagnosticMessage = diagnosticMessage ?? string.Empty;
      UserMessage = userMessage ?? string.Empty;
      Exception = exception
        ?? throw new ArgumentNullException(nameof(exception));
    }

    internal string OperationStage { get; }
    internal string ErrorCode { get; }
    internal string DiagnosticMessage { get; }
    internal string UserMessage { get; }
    internal Exception Exception { get; }
  }

  internal static class Stage02RevitWriteEnqueueFailurePolicy
  {
    internal static Stage02RevitWriteEnqueueFailureDecision ForFailure(
      string error,
      Exception exception)
    {
      bool threw = exception != null;
      Exception diagnosticException = exception
        ?? new InvalidOperationException(
          "Stage02 write enqueue returned false.");
      return new Stage02RevitWriteEnqueueFailureDecision(
        "WRITE_ENQUEUE",
        threw
          ? "STAGE02_WRITE_ENQUEUE_EXCEPTION"
          : "STAGE02_WRITE_ENQUEUE_REJECTED",
        "Stage02 写入请求提交发生技术失败。",
        string.IsNullOrWhiteSpace(error)
          ? "无法提交 Stage02 写入请求。"
          : error,
        diagnosticException);
    }
  }

  internal sealed class Stage02RevitWriteHostCallbackFailureDecision
  {
    internal Stage02RevitWriteHostCallbackFailureDecision(
      string operationStage,
      string errorCode,
      string diagnosticMessage,
      string userMessage,
      Exception exception)
    {
      OperationStage = operationStage ?? string.Empty;
      ErrorCode = errorCode ?? string.Empty;
      DiagnosticMessage = diagnosticMessage ?? string.Empty;
      UserMessage = userMessage ?? string.Empty;
      Exception = exception
        ?? throw new ArgumentNullException(nameof(exception));
    }

    internal string OperationStage { get; }
    internal string ErrorCode { get; }
    internal string DiagnosticMessage { get; }
    internal string UserMessage { get; }
    internal Exception Exception { get; }
  }

  internal static class Stage02RevitWriteHostCallbackFailurePolicy
  {
    internal static Stage02RevitWriteHostCallbackFailureDecision ForFailure(
      Exception exception)
    {
      if (exception == null) throw new ArgumentNullException(nameof(exception));
      return new Stage02RevitWriteHostCallbackFailureDecision(
        "WRITE_HOST_CALLBACK",
        "WRITE_HOST_CALLBACK",
        "Stage02 写入宿主回调发生技术失败。",
        "Stage02 写入宿主回调失败。",
        exception);
    }
  }

  internal static class Stage02RevitFailureReportPolicy
  {
    internal static Stage02RevitFailureReportDecision ForSelection(
      Stage02RevitSelectionResult result)
    {
      if (result == null)
      {
        return Stage02RevitFailureReportDecision.SelectionNoResult(
          new InvalidOperationException(
            "Stage02 selection service returned no result."));
      }
      if (result.Disposition
          != Stage02RevitSelectionDisposition.TechnicalFailure)
      {
        return Stage02RevitFailureReportDecision.None();
      }
      return Stage02RevitFailureReportDecision.SelectionTechnical(
        result.Exception);
    }

    internal static Stage02RevitFailureReportDecision ForPreview(
      Stage02RevitPreviewResult result)
    {
      if (result == null)
      {
        return Stage02RevitFailureReportDecision.PreviewNoResult(
          Stage02RevitPreviewResult.NoResult().Exception);
      }
      switch (result.Disposition)
      {
        case Stage02RevitPreviewDisposition.TechnicalFailure:
          return Stage02RevitFailureReportDecision.PreviewTechnical(
            result.Exception);
        case Stage02RevitPreviewDisposition.NoResult:
          return Stage02RevitFailureReportDecision.PreviewNoResult(
            result.Exception);
        default:
          return Stage02RevitFailureReportDecision.None();
      }
    }
  }
}
