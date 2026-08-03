using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Revit.Parameters;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02RevitWriteTargetRequest
  {
    internal Stage02RevitWriteTargetRequest(
      string uniqueId,
      int elementId)
    {
      UniqueId = uniqueId ?? string.Empty;
      ElementId = elementId;
    }

    internal string UniqueId { get; }
    internal int ElementId { get; }
  }

  internal sealed class Stage02RevitWriteRequest
  {
    private Stage02RevitWriteRequest(
      HBRFileContext context,
      Stage02Preview preview,
      Stage02RevitSelectionResult currentSelectionEvidence,
      IEnumerable<Stage02RevitWriteTargetRequest> targets)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      Preview = preview ?? throw new ArgumentNullException(nameof(preview));
      CurrentSelectionEvidence = currentSelectionEvidence
        ?? throw new ArgumentNullException(nameof(currentSelectionEvidence));
      DocumentFingerprint = preview.DocumentFingerprint;
      Targets = new ReadOnlyCollection<Stage02RevitWriteTargetRequest>(
        (targets ?? Array.Empty<Stage02RevitWriteTargetRequest>()).ToArray());
    }

    internal HBRFileContext Context { get; }
    internal Stage02Preview Preview { get; }
    internal Stage02RevitSelectionResult CurrentSelectionEvidence { get; }
    internal string DocumentFingerprint { get; }
    internal IReadOnlyList<Stage02RevitWriteTargetRequest> Targets { get; }

    internal static Stage02RevitWriteRequest FromPreview(
      HBRFileContext context,
      Stage02Preview preview,
      Stage02RevitSelectionResult currentSelectionEvidence)
    {
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      return new Stage02RevitWriteRequest(
        context,
        preview,
        currentSelectionEvidence,
        preview.Elements.Select(element =>
          new Stage02RevitWriteTargetRequest(
            element.Element.UniqueId,
            element.Element.ElementId)));
    }
  }

  internal sealed class Stage02RevitWriteResult
  {
    internal bool Success { get; set; }
    internal bool RequiresNewPreview { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal string ReportPath { get; set; } = string.Empty;
    internal IReadOnlyList<Stage02Blocker> Blockers { get; set; } =
      Array.Empty<Stage02Blocker>();
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  internal sealed class Stage02RevitWriteService
  {
    private readonly HbrRuleDatabase _database;
    private readonly Stage02RevitPreviewService _previewService;
    private readonly Stage02ConfirmationPolicy _confirmationPolicy;
    private readonly HbrSharedParameterInstaller _installer;
    private readonly HbrParameterValueConverter _valueConverter;
    private readonly HbrParameterReadbackVerifier _readbackVerifier;

    internal Stage02RevitWriteService()
      : this(
        HbrRuleDatabase.Current,
        new Stage02ConfirmationPolicy())
    {
    }

    internal Stage02RevitWriteService(
      HbrRuleDatabase database,
      Stage02ConfirmationPolicy confirmationPolicy)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
      _previewService = new Stage02RevitPreviewService(database);
      _confirmationPolicy = confirmationPolicy
        ?? throw new ArgumentNullException(nameof(confirmationPolicy));
      _installer = new HbrSharedParameterInstaller();
      _valueConverter = new HbrParameterValueConverter();
      _readbackVerifier = new HbrParameterReadbackVerifier();
    }

    internal bool EnqueueWrite(
      Stage02RevitWriteRequest request,
      Action<Stage02RevitWriteResult> completed,
      out string error)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (completed == null) throw new ArgumentNullException(nameof(completed));
      return RevitHost.EnqueueAction(
        uiApplication => ExecuteInHostContext(
          uiApplication,
          request,
          completed),
        out error);
    }

    internal void ExecuteInHostContext(
      UIApplication uiApplication,
      Stage02RevitWriteRequest request,
      Action<Stage02RevitWriteResult> completed)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (completed == null) throw new ArgumentNullException(nameof(completed));
      UIDocument uiDocument = uiApplication.ActiveUIDocument;
      Document document = uiDocument == null ? null : uiDocument.Document;
      if (document == null)
      {
        completed(Failure(false, "当前没有活动 Revit 项目文档。"));
        return;
      }

      var execution = new Stage02RevitTransactionExecution(
        uiApplication,
        document,
        request,
        completed);
      bool consumed = false;
      string operationStage = "BUILD_LIVE_CONFIRMATION";
      try
      {
        Stage02ConfirmationSnapshot current = _previewService
          .BuildLiveConfirmationSnapshot(
            uiApplication,
            document,
            request.Context,
            request.Preview,
            request.CurrentSelectionEvidence);
        operationStage = "VALIDATE_AND_CONSUME";
        Stage02ConfirmationResult confirmation = _confirmationPolicy
          .ValidateAndConsumeForExecution(request.Preview, current);
        if (!confirmation.Accepted)
        {
          Stage02ConfirmationUiDecision uiDecision =
            Stage02ConfirmationUiPolicy.Decide(confirmation.Blockers);
          execution.Complete(new Stage02RevitWriteResult
          {
            Success = false,
            RequiresNewPreview = uiDecision.RequiresNewPreview,
            Status = uiDecision.Status,
            Blockers = confirmation.Blockers
          });
          return;
        }
        consumed = true;

        operationStage = "TRANSACTION_GROUP_START";
        execution.StartGroup("湖北BIM报规｜HBR属性准备");
        operationStage = "TRANSACTION_START";
        execution.StartTransaction("安装并填写HBR可见参数");

        operationStage = "REVALIDATE_IN_TRANSACTION";
        RevalidateDocumentAndPreview(document, request);
        operationStage = "ENSURE_BINDINGS";
        _installer.EnsureBindings(document, request.Preview, _database);
        operationStage = "WRITE_VALUES";
        _valueConverter.WriteNonBlankSuggestions(
          document,
          request.Preview,
          _database);
        operationStage = "REGENERATE";
        document.Regenerate();
        operationStage = "READBACK";
        _readbackVerifier.Verify(document, request.Preview, _database);
        operationStage = "AUDIT_METADATA";
        Stage02MetadataStorage.WriteAuditOnly(document, request.Preview);
        operationStage = "TRANSACTION_COMMIT";
        execution.Commit(operationStage);
      }
      catch (Stage02ContractException exception)
      {
        Stage02PreConsumptionUiDecision decision =
          Stage02PreConsumptionUiPolicy.Decide(exception, consumed);
        if (decision.Handled && !decision.ShouldWriteFailureReport)
        {
          execution.Complete(new Stage02RevitWriteResult
          {
            Success = false,
            RequiresNewPreview = decision.RequiresNewPreview,
            Status = decision.Status,
            Blockers = decision.Blockers
          });
          return;
        }
        execution.Fail(exception, operationStage, consumed);
      }
      catch (Exception exception)
      {
        execution.Fail(exception, operationStage, consumed);
      }
    }

    private void RevalidateDocumentAndPreview(
      Document document,
      Stage02RevitWriteRequest request)
    {
      if (document.IsFamilyDocument)
        throw new InvalidOperationException("族文档不能执行 Stage02 写入。");
      if (document.IsReadOnly)
        throw new InvalidOperationException("当前 Revit 文档为只读。");
      string fingerprint = HBRDocumentFingerprint.Compute(
        document.PathName,
        document.Title,
        document.Application.VersionNumber);
      if (!string.Equals(
        fingerprint,
        request.DocumentFingerprint,
        StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "事务内 DocumentFingerprint 已变化。");
      }
      HbrRulePackage package = _database.Package;
      if (!string.Equals(
          package.PackageId,
          request.Preview.RulePackageId,
          StringComparison.Ordinal)
        || !string.Equals(
          package.PackageVersion,
          request.Preview.RulePackageVersion,
          StringComparison.Ordinal)
        || !string.Equals(
          package.RulePackageSha256,
          request.Preview.RulePackageSha256,
          StringComparison.Ordinal))
      {
        throw new InvalidOperationException("事务内规则包身份已变化。");
      }
      string[] expectedUniqueIds = request.Preview.Elements
        .Select(element => element.Element.UniqueId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] requestedUniqueIds = request.Targets
        .Select(target => target.UniqueId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (!expectedUniqueIds.SequenceEqual(
        requestedUniqueIds,
        StringComparer.Ordinal))
      {
        throw new InvalidOperationException("事务内 UniqueId 请求集合已变化。");
      }
      foreach (Stage02RevitWriteTargetRequest targetRequest in request.Targets)
      {
        if (ResolveTarget(document, targetRequest) == null)
          throw new InvalidOperationException("事务内 UniqueId 目标已不存在。");
      }
    }

    private static Element ResolveTarget(
      Document document,
      Stage02RevitWriteTargetRequest request)
    {
      return document.GetElement(request.UniqueId);
    }

    private static Stage02RevitWriteResult Failure(
      bool requiresNewPreview,
      string message)
    {
      return new Stage02RevitWriteResult
      {
        Success = false,
        RequiresNewPreview = requiresNewPreview,
        Status = "Stage02 写入不可用",
        Messages = new[] { message ?? string.Empty }
      };
    }
  }

  internal sealed class Stage02RevitTransactionExecution
    : IFailuresPreprocessor, ITransactionFinalizer
  {
    private readonly UIApplication _uiApplication;
    private readonly Document _document;
    private readonly Stage02RevitWriteRequest _request;
    private readonly Action<Stage02RevitWriteResult> _completed;
    private readonly Stage02TransactionHandoff _handoff =
      new Stage02TransactionHandoff();
    private readonly Stage02ExecutionCleanupGate _cleanupGate =
      new Stage02ExecutionCleanupGate();
    private readonly Stage02LateCleanupCoordinator _lateCleanup =
      new Stage02LateCleanupCoordinator();
    private readonly Stage02ExecutionOutcomeGate _outcomeGate =
      new Stage02ExecutionOutcomeGate();
    private readonly Stage02DeferredFailureBudget
      _transactionDeferredFailureBudget =
        new Stage02DeferredFailureBudget(3);
    private readonly Stage02DeferredFailureBudget
      _groupDeferredFailureBudget =
        new Stage02DeferredFailureBudget(3);
    private TransactionGroup _group;
    private Transaction _transaction;
    private bool _groupStarted;
    private bool _transactionStarted;
    private bool _consumed;
    private Exception _failure;
    private string _rootCauseStage = "TRANSACTION_SETUP";
    private string _cleanupStage = string.Empty;
    private string _deferredTerminalStatus = string.Empty;
    private bool _deferredGroupOnly;
    private bool _deferredAwaitTransactionTerminal;
    private string _handoffFinalizerTerminalStatus = string.Empty;
    private string _handoffEndCallTerminalStatus = string.Empty;
    private string _lastObservedTransactionStatus = string.Empty;
    private string _lastObservedGroupStatus = string.Empty;
    private string _transactionStatusForReport = string.Empty;
    private bool _handoffTerminalConflict;
    private int _completionIssued;
    private int _idlingScheduled;

    internal Stage02RevitTransactionExecution(
      UIApplication uiApplication,
      Document document,
      Stage02RevitWriteRequest request,
      Action<Stage02RevitWriteResult> completed)
    {
      _uiApplication = uiApplication
        ?? throw new ArgumentNullException(nameof(uiApplication));
      _document = document ?? throw new ArgumentNullException(nameof(document));
      _request = request ?? throw new ArgumentNullException(nameof(request));
      _completed = completed ?? throw new ArgumentNullException(nameof(completed));
    }

    internal void StartGroup(string name)
    {
      _group = new TransactionGroup(_document, name ?? string.Empty);
      TransactionStatus status = _group.Start();
      _lastObservedGroupStatus = StatusName(status);
      _groupStarted = status == TransactionStatus.Started;
      if (!_groupStarted)
      {
        var startFailure = new InvalidOperationException(
          "无法启动 Stage02 Revit 事务组；状态=" + StatusName(status) + "。");
        if (Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart(
          StatusName(status)))
        {
          try
          {
            _group.Dispose();
            _group = null;
          }
          catch (Exception disposeException)
          {
            RecordCleanupStage("TRANSACTION_GROUP_DISPOSE");
            throw new AggregateException(startFailure, disposeException);
          }
        }
        throw startFailure;
      }
    }

    internal void StartTransaction(string name)
    {
      _transaction = new Transaction(_document, name ?? string.Empty);
      TransactionStatus status = _transaction.Start();
      _lastObservedTransactionStatus = StatusName(status);
      _transactionStarted = status == TransactionStatus.Started;
      if (!_transactionStarted)
      {
        var startFailure = new InvalidOperationException(
          "无法启动 Stage02 Revit 事务；状态=" + StatusName(status) + "。");
        throw startFailure;
      }

      FailureHandlingOptions options =
        _transaction.GetFailureHandlingOptions();
      options.SetFailuresPreprocessor(this);
      options.SetClearAfterRollback(true);
      options.SetTransactionFinalizer(this);
      options.SetForcedModalHandling(true);
      _transaction.SetFailureHandlingOptions(options);
    }

    internal void Commit(string operationStage)
    {
      _consumed = true;
      _rootCauseStage = operationStage ?? "TRANSACTION_COMMIT";
      TransactionStatus returnedStatus = _transaction.Commit();
      HandleEndCallReturn(returnedStatus);
    }

    internal void Fail(
      Exception exception,
      string operationStage,
      bool consumed)
    {
      _consumed = consumed;
      string failureStage = operationStage ?? string.Empty;
      RecordFailure(exception, failureStage, false);

      if (_transactionStarted && _transaction != null)
      {
        RecordCleanupStage("TRANSACTION_ROLLBACK");
        try
        {
          TransactionStatus liveStatus = _transaction.GetStatus();
          _lastObservedTransactionStatus = StatusName(liveStatus);
          if (liveStatus == TransactionStatus.Started)
          {
            liveStatus = _transaction.RollBack();
            _lastObservedTransactionStatus = StatusName(liveStatus);
          }
          HandleEndCallReturn(liveStatus);
          return;
        }
        catch (Exception rollbackException)
        {
          RecordFailure(rollbackException, _cleanupStage, true);
          try
          {
            TransactionStatus liveStatus = _transaction.GetStatus();
            _lastObservedTransactionStatus = StatusName(liveStatus);
            if (liveStatus == TransactionStatus.Pending
              || liveStatus == TransactionStatus.Committed
              || liveStatus == TransactionStatus.RolledBack)
            {
              HandleEndCallReturn(liveStatus);
              return;
            }
          }
          catch (Exception statusException)
          {
            RecordFailure(statusException, _cleanupStage, true);
          }
          ScheduleDeferredCompletion(string.Empty, false, true);
          return;
        }
      }

      if (_transaction != null
        && !_transactionStarted
        && !Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart(
          _lastObservedTransactionStatus))
      {
        CompleteFailureWithoutUnsafeCleanup(
          _lastObservedTransactionStatus,
          _lastObservedGroupStatus);
        return;
      }

      CloseGroupWithoutStartedTransaction();
    }

    internal void Complete(Stage02RevitWriteResult result)
    {
      if (Interlocked.Exchange(ref _completionIssued, 1) != 0) return;
      try
      {
        _completed(result);
      }
      catch
      {
        // Revit failure callbacks and host actions must never leak UI callback errors.
      }
    }

    public FailureProcessingResult PreprocessFailures(
      FailuresAccessor failuresAccessor)
    {
      try
      {
        failuresAccessor.DeleteAllWarnings();
        return failuresAccessor.GetSeverity() == FailureSeverity.None
          ? FailureProcessingResult.Continue
          : FailureProcessingResult.ProceedWithRollBack;
      }
      catch
      {
        return FailureProcessingResult.ProceedWithRollBack;
      }
    }

    public void OnCommitted(Document document, string transactionName)
    {
      FinalizeFromCallback("Committed");
    }

    public void OnRolledBack(Document document, string transactionName)
    {
      FinalizeFromCallback("RolledBack");
    }

    private void HandleEndCallReturn(TransactionStatus returnedStatus)
    {
      _lastObservedTransactionStatus = StatusName(returnedStatus);
      bool isPending = returnedStatus == TransactionStatus.Pending;
      Stage02TransactionHandoffDecision decision =
        _handoff.RegisterEndCallReturn(StatusName(returnedStatus));
      if (decision.TerminalConflict)
      {
        _handoffTerminalConflict = true;
        _transactionStatusForReport = "CONFLICT";
        _handoffFinalizerTerminalStatus =
          decision.FinalizerTerminalStatus;
        _handoffEndCallTerminalStatus = decision.EndCallTerminalStatus;
        RecordFailure(
          new InvalidOperationException(
            "Stage02 Revit 事务终态冲突；finalizerTerminalStatus="
            + decision.FinalizerTerminalStatus
            + "；endCallTerminalStatus="
            + decision.EndCallTerminalStatus
            + "。已按回滚失败路径 fail-closed。"),
          _rootCauseStage,
          false);
        FinalizeTerminalNoThrow("RolledBack");
        return;
      }
      if (isPending && decision.DeferredToFinalizer) return;
      if (decision.DeferredToFinalizer) return;
      if (decision.CallerMustFinalize)
      {
        FinalizeTerminalNoThrow(decision.TerminalStatus);
        return;
      }

      RecordFailure(
        new InvalidOperationException(
          "Stage02 Revit 事务返回非终态且非 Pending 状态："
          + StatusName(returnedStatus)
          + "。为避免释放仍活动的事务，已按 fail-closed 处理。"),
        _rootCauseStage,
        false);
      ScheduleDeferredCompletion(string.Empty, false, true);
    }

    private void FinalizeFromCallback(string terminalStatus)
    {
      try
      {
        _lastObservedTransactionStatus = terminalStatus ?? string.Empty;
        if (_cleanupGate.IsClaimed) return;
        Stage02LateCleanupDecision lateCleanupDecision =
          _lateCleanup.ObserveTerminal(terminalStatus);
        bool handoffGranted = _handoff.NotifyFinalizerTerminal(terminalStatus);
        if (handoffGranted || lateCleanupDecision.ShouldAttemptCleanup)
          FinalizeTerminalNoThrow(terminalStatus);
      }
      catch
      {
        // ITransactionFinalizer must never throw back into Revit failure handling.
      }
    }

    private void FinalizeTerminalNoThrow(string terminalStatus)
    {
      try
      {
        FinalizeTerminal(terminalStatus);
      }
      catch (Exception finalizerException)
      {
        RecordFailure(
          finalizerException,
          string.IsNullOrWhiteSpace(_cleanupStage)
            ? "TRANSACTION_GROUP_FINALIZE"
            : _cleanupStage,
          true);
        ScheduleDeferredCompletion(terminalStatus, false, false);
      }
    }

    private void FinalizeTerminal(string terminalStatus)
    {
      if (_cleanupGate.IsClaimed) return;
      if (!Stage02TransactionStatePolicy.IsTerminal(terminalStatus))
        throw new InvalidOperationException(
          "只有 Committed/RolledBack 才能结束 Stage02 事务。" );
      _lateCleanup.ObserveTerminal(terminalStatus);

      if (string.Equals(
        terminalStatus,
        "RolledBack",
        StringComparison.Ordinal) && _failure == null)
      {
        RecordFailure(
          new InvalidOperationException(
            "Stage02 Revit 事务在失败处理阶段被回滚。"),
          _rootCauseStage,
          false);
      }

      bool mayAssimilate = string.Equals(
          terminalStatus,
          "Committed",
          StringComparison.Ordinal)
        && _failure == null
        && !_outcomeGate.IsClaimed;
      RecordCleanupStage("TRANSACTION_GROUP_FINALIZE");
      Stage02DeferredTransactionDecision groupDecision =
        Stage02DeferredGroupPolicy.Advance(
          ReadGroupStatus,
          () => CloseStartedGroup(terminalStatus, mayAssimilate));
      _lastObservedGroupStatus = groupDecision.ObservedStatus;
      if (groupDecision.ShouldDefer)
      {
        ScheduleDeferredCompletion(terminalStatus, false, false);
        return;
      }
      if (groupDecision.ShouldFailClosed)
      {
        CompleteFatalUnknownGroupStatus(
          terminalStatus,
          groupDecision.ObservedStatus);
        return;
      }

      string groupStatus = groupDecision.TerminalStatus;
      if (mayAssimilate
        && !string.Equals(
          groupStatus,
          "Committed",
          StringComparison.Ordinal))
      {
        RecordFailure(
          new InvalidOperationException(
            "Stage02 Revit 事务组 Assimilate 未提交；状态="
            + groupStatus
            + "。"),
          _cleanupStage,
          true);
      }
      if (!mayAssimilate && _failure == null)
        RecordFailure(
          new InvalidOperationException("Stage02 事务未提交。"),
          _rootCauseStage,
          false);
      CompleteTerminalTransactionGroup(terminalStatus, groupStatus);
    }

    private string CloseStartedGroup(
      string terminalStatus,
      bool mayAssimilate)
    {
      TransactionStatus status;
      if (mayAssimilate)
      {
        RecordCleanupStage("TRANSACTION_GROUP_ASSIMILATE");
        status = _group.Assimilate();
      }
      else
      {
        if (!Stage02TransactionStatePolicy.CanRollbackGroup(
          terminalStatus,
          "Started"))
        {
          throw new InvalidOperationException(
            "事务未终态时禁止回滚 Stage02 事务组。");
        }
        RecordCleanupStage("TRANSACTION_GROUP_ROLLBACK");
        status = _group.RollBack();
      }
      _lastObservedGroupStatus = StatusName(status);
      return _lastObservedGroupStatus;
    }

    private void CompleteTerminalTransactionGroup(
      string terminalStatus,
      string groupStatus)
    {
      if (!Stage02TransactionStatePolicy.CanDispose(
        terminalStatus,
        groupStatus))
      {
        return;
      }
      if (!_cleanupGate.TryClaimTerminal(terminalStatus, groupStatus)) return;
      DisposeTerminalObjects();
      _lateCleanup.MarkCleanupCompleted();
      if (!_outcomeGate.TryClaim()) return;
      bool success = string.Equals(
          terminalStatus,
          "Committed",
          StringComparison.Ordinal)
        && string.Equals(groupStatus, "Committed", StringComparison.Ordinal)
        && _failure == null;
      if (success)
      {
        Complete(new Stage02RevitWriteResult
        {
          Success = true,
          RequiresNewPreview = false,
          Status = "Stage02 可见参数准备完成",
          Messages = new[]
          {
            "HBR 参数已在 Revit UI 中可见、可编辑并通过 GUID typed 回读。"
          }
        });
        return;
      }

      bool transactionRolledBack = !_handoffTerminalConflict
        && string.Equals(
          terminalStatus,
          "RolledBack",
          StringComparison.Ordinal);
      bool groupRolledBack = string.Equals(
          groupStatus,
          "RolledBack",
          StringComparison.Ordinal);
      Complete(BuildFailureResult(
        transactionRolledBack,
        groupRolledBack,
        terminalStatus,
        groupStatus));
    }

    private void CloseGroupWithoutStartedTransaction()
    {
      try
      {
        CloseGroupWithoutStartedTransactionCore();
      }
      catch (Exception exception)
      {
        RecordFailure(
          exception,
          string.IsNullOrWhiteSpace(_cleanupStage)
            ? "TRANSACTION_GROUP_ROLLBACK"
            : _cleanupStage,
          true);
        ScheduleDeferredCompletion(string.Empty, true, false);
      }
    }

    private void CloseGroupWithoutStartedTransactionCore()
    {
      if (_cleanupGate.IsClaimed) return;
      if (!_groupStarted || _group == null)
      {
        CompleteFailureWithoutUnsafeCleanup();
        return;
      }
      RecordCleanupStage("TRANSACTION_GROUP_FINALIZE");
      Stage02DeferredTransactionDecision decision =
        Stage02DeferredGroupPolicy.Advance(
          ReadGroupStatus,
          () =>
          {
            RecordCleanupStage("TRANSACTION_GROUP_ROLLBACK");
            TransactionStatus status = _group.RollBack();
            _lastObservedGroupStatus = StatusName(status);
            return _lastObservedGroupStatus;
          });
      _lastObservedGroupStatus = decision.ObservedStatus;
      if (decision.ShouldDefer)
      {
        ScheduleDeferredCompletion(string.Empty, true, false);
        return;
      }
      if (decision.ShouldFailClosed)
      {
        CompleteFatalUnknownGroupStatus(
          string.Empty,
          decision.ObservedStatus);
        return;
      }
      string groupStatus = decision.TerminalStatus;
      if (!_cleanupGate.TryClaimGroupOnlyTerminal(groupStatus)) return;
      if (_transaction != null
        && !_transactionStarted
        && Stage02TransactionStatePolicy.CanDisposeAfterRejectedStart(
          _lastObservedTransactionStatus))
      {
        try
        {
          _transaction.Dispose();
        }
        catch (Exception disposeException)
        {
          RecordFailure(
            disposeException,
            "TRANSACTION_DISPOSE",
            true);
        }
        finally
        {
          _transaction = null;
        }
      }
      try
      {
        _group.Dispose();
        _group = null;
      }
      catch (Exception disposeException)
      {
        RecordFailure(
          disposeException,
          "TRANSACTION_GROUP_DISPOSE",
          true);
      }
      _lateCleanup.MarkCleanupCompleted();
      if (!_outcomeGate.TryClaim()) return;
      Complete(BuildFailureResult(
        false,
        string.Equals(
          groupStatus,
          "RolledBack",
          StringComparison.Ordinal),
        _lastObservedTransactionStatus,
        groupStatus));
    }

    private void ScheduleDeferredCompletion(
      string terminalStatus,
      bool groupOnly,
      bool awaitTransactionTerminal)
    {
      if (_cleanupGate.IsClaimed) return;
      _deferredTerminalStatus = terminalStatus ?? string.Empty;
      _deferredGroupOnly = groupOnly;
      _deferredAwaitTransactionTerminal = awaitTransactionTerminal;
      if (Interlocked.CompareExchange(ref _idlingScheduled, 1, 0) != 0)
        return;
      try
      {
        _uiApplication.Idling += OnDeferredCompletionIdling;
      }
      catch (Exception exception)
      {
        Interlocked.Exchange(ref _idlingScheduled, 0);
        RecordFailure(exception, "IDLING_SUBSCRIPTION_FAILED", true);
        CompleteFatalDeferredException(
          awaitTransactionTerminal,
          "IDLING_SUBSCRIPTION_FAILED",
          1);
      }
    }

    private void OnDeferredCompletionIdling(
      object sender,
      IdlingEventArgs eventArgs)
    {
      try
      {
        _uiApplication.Idling -= OnDeferredCompletionIdling;
      }
      catch (Exception exception)
      {
        Interlocked.Exchange(ref _idlingScheduled, 0);
        RecordFailure(exception, "IDLING_UNSUBSCRIBE_FAILED", true);
        CompleteFatalDeferredException(
          _deferredAwaitTransactionTerminal,
          "IDLING_UNSUBSCRIBE_FAILED",
          1);
        return;
      }
      Interlocked.Exchange(ref _idlingScheduled, 0);
      if (_cleanupGate.IsClaimed) return;
      try
      {
        if (_deferredAwaitTransactionTerminal)
        {
          Stage02DeferredTransactionDecision decision =
            Stage02DeferredTransactionPolicy.Advance(
              ReadTransactionStatus,
              RollBackTransaction);
          _transactionDeferredFailureBudget.Reset();
          if (decision.ShouldFinalize)
          {
            _deferredAwaitTransactionTerminal = false;
            _deferredTerminalStatus = decision.TerminalStatus;
            FinalizeTerminal(decision.TerminalStatus);
          }
          else if (decision.ShouldDefer)
            ScheduleDeferredCompletion(string.Empty, false, true);
          else if (decision.ShouldFailClosed)
            CompleteFatalUnknownTransactionStatus(decision.ObservedStatus);
          return;
        }
        if (_deferredGroupOnly)
        {
          CloseGroupWithoutStartedTransactionCore();
          _groupDeferredFailureBudget.Reset();
          return;
        }
        FinalizeTerminal(_deferredTerminalStatus);
        _groupDeferredFailureBudget.Reset();
      }
      catch (Exception exception)
      {
        HandleDeferredException(
          exception,
          _deferredAwaitTransactionTerminal);
      }
    }

    private void HandleDeferredException(
      Exception exception,
      bool transactionScope)
    {
      string failureStage = string.IsNullOrWhiteSpace(_cleanupStage)
        ? transactionScope
          ? "TRANSACTION_DEFERRED_API_EXCEPTION"
          : "TRANSACTION_GROUP_DEFERRED_API_EXCEPTION"
        : _cleanupStage;
      RecordFailure(exception, failureStage, true);
      Stage02DeferredFailureBudget budget = transactionScope
        ? _transactionDeferredFailureBudget
        : _groupDeferredFailureBudget;
      Stage02DeferredFailureDecision decision = budget.RegisterFailure();
      if (decision.ShouldFailClosed)
      {
        CompleteFatalDeferredException(
          transactionScope,
          "DEFERRED_API_EXCEPTION_BUDGET_EXHAUSTED",
          decision.FailureCount);
        return;
      }
      ScheduleDeferredCompletion(
        _deferredTerminalStatus,
        _deferredGroupOnly,
        _deferredAwaitTransactionTerminal);
    }

    private void CompleteFatalDeferredException(
      bool transactionScope,
      string reason,
      int failureCount)
    {
      string failureStage = transactionScope
        ? "TRANSACTION_DEFERRED_FATAL_EXCEPTION"
        : "TRANSACTION_GROUP_DEFERRED_FATAL_EXCEPTION";
      RecordFailure(
        new InvalidOperationException(
          "Stage02 Revit 延迟终态恢复失败；scope="
          + (transactionScope ? "transaction" : "transactionGroup")
          + "；reason="
          + (reason ?? string.Empty)
          + "；failureCount="
          + failureCount
          + "；rollbackConfirmed=false。"),
        failureStage,
        true);
      CompleteFailureWithoutUnsafeCleanup(
        _lastObservedTransactionStatus,
        _lastObservedGroupStatus);
    }

    private void CompleteFatalUnknownGroupStatus(
      string transactionStatus,
      string groupStatus)
    {
      const string failureStage =
        "TRANSACTION_GROUP_STATUS_FATAL_UNKNOWN";
      if (!string.IsNullOrWhiteSpace(transactionStatus))
        _lastObservedTransactionStatus = transactionStatus;
      _lastObservedGroupStatus = groupStatus ?? string.Empty;
      RecordFailure(
        new InvalidOperationException(
          "Stage02 Revit 事务组状态无法安全恢复；transactionStatus="
          + _lastObservedTransactionStatus
          + "；transactionGroupStatus="
          + _lastObservedGroupStatus
          + "；rollbackConfirmed=false。"),
        failureStage,
        true);
      CompleteFailureWithoutUnsafeCleanup(
        _lastObservedTransactionStatus,
        _lastObservedGroupStatus);
    }

    private void CompleteFatalUnknownTransactionStatus(
      string transactionStatus)
    {
      const string failureStage = "TRANSACTION_STATUS_FATAL_UNKNOWN";
      _lastObservedTransactionStatus = transactionStatus ?? string.Empty;
      RecordFailure(
        new InvalidOperationException(
          "Stage02 Revit 事务状态无法安全恢复；transactionStatus="
          + _lastObservedTransactionStatus
          + "；transactionGroupStatus="
          + _lastObservedGroupStatus
          + "；rollbackConfirmed=false。"),
        failureStage,
        true);
      CompleteFailureWithoutUnsafeCleanup(
        _lastObservedTransactionStatus,
        _lastObservedGroupStatus);
    }

    private void DisposeTerminalObjects()
    {
      try
      {
        _transaction.Dispose();
      }
      catch (Exception exception)
      {
        RecordFailure(exception, "TRANSACTION_DISPOSE", true);
      }
      try
      {
        _group.Dispose();
      }
      catch (Exception exception)
      {
        RecordFailure(exception, "TRANSACTION_GROUP_DISPOSE", true);
      }
      _transaction = null;
      _group = null;
    }

    private void CompleteFailureWithoutUnsafeCleanup(
      string transactionStatus = "",
      string groupStatus = "")
    {
      if (!_outcomeGate.TryClaim()) return;
      Stage02LateCleanupDecision lateCleanupDecision =
        _lateCleanup.DeclareFailureOutcome();
      if (_failure == null)
        RecordFailure(
          new InvalidOperationException("Stage02 Revit 写入失败。"),
          _rootCauseStage,
          false);
      if (lateCleanupDecision.ShouldAttemptCleanup)
        FinalizeTerminalNoThrow(lateCleanupDecision.TerminalStatus);
      string observedTransactionStatus = string.IsNullOrWhiteSpace(
          _lastObservedTransactionStatus)
        ? transactionStatus
        : _lastObservedTransactionStatus;
      string observedGroupStatus = string.IsNullOrWhiteSpace(
          _lastObservedGroupStatus)
        ? groupStatus
        : _lastObservedGroupStatus;
      bool transactionRolledBack = !_handoffTerminalConflict
        && string.Equals(
          observedTransactionStatus,
          "RolledBack",
          StringComparison.Ordinal);
      bool groupRolledBack = string.Equals(
        observedGroupStatus,
        "RolledBack",
        StringComparison.Ordinal);
      Complete(BuildFailureResult(
        transactionRolledBack,
        groupRolledBack,
        observedTransactionStatus,
        observedGroupStatus));
    }

    private Stage02RevitWriteResult BuildFailureResult(
      bool transactionRolledBack,
      bool groupRolledBack,
      string transactionStatus = "",
      string transactionGroupStatus = "")
    {
      DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
      Stage02FailureReportWriteResult report =
        Stage02FailureReportWriter.TryWrite(
          new Stage02FailureReportContext
          {
            FileGuid = _request.Preview.FileGuid,
            DocumentFingerprint = _request.DocumentFingerprint,
            DocumentTitle = _document.Title,
            RulePackageId = _request.Preview.RulePackageId,
            RulePackageVersion = _request.Preview.RulePackageVersion,
            RulePackageSha256 = _request.Preview.RulePackageSha256,
            PreviewHash = _request.Preview.PreviewHash,
            UniqueIds = _request.Targets
              .Select(item => item.UniqueId)
              .ToArray(),
            PropertyIds = _request.Preview.Elements
              .SelectMany(element => element.Operations)
              .Select(item => item.PropertyId)
              .Distinct(StringComparer.Ordinal)
              .ToArray(),
            OperationStage = _rootCauseStage,
            RootCauseStage = _rootCauseStage,
            CleanupStage = _cleanupStage,
            TransactionRolledBack = transactionRolledBack,
            GroupRolledBack = groupRolledBack,
            RollbackConfirmed =
              transactionRolledBack || groupRolledBack,
            TransactionStatus = string.IsNullOrWhiteSpace(
                _transactionStatusForReport)
              ? transactionStatus ?? string.Empty
              : _transactionStatusForReport,
            TransactionGroupStatus =
              transactionGroupStatus ?? string.Empty,
            HandoffFinalizerTerminalStatus =
              _handoffFinalizerTerminalStatus,
            HandoffEndCallTerminalStatus =
              _handoffEndCallTerminalStatus,
            Exception = _failure,
            OccurredUtc = occurredUtc,
            OccurredLocal = DateTimeOffset.Now
          });
      string diagnostic = report.Success
        ? "DIAG_STAGE02_WRITE_FAILED；错误报告=" + report.ReportPath
        : "DIAG_STAGE02_WRITE_FAILED；REPORT_WRITE_FAILED；原始异常="
          + report.OriginalExceptionSummary
          + "；报告写入异常="
          + report.ReportWriteErrorSummary;
      return new Stage02RevitWriteResult
      {
        Success = false,
        RequiresNewPreview = _consumed,
        Status = _consumed
          ? "Stage02 写入失败｜必须重新预览"
          : "Stage02 预检失败｜预览未消费",
        ReportPath = report.ReportPath ?? string.Empty,
        Messages = new[] { diagnostic }
      };
    }

    private string ReadTransactionStatus()
    {
      if (_transaction == null) return string.Empty;
      _lastObservedTransactionStatus =
        StatusName(_transaction.GetStatus());
      return _lastObservedTransactionStatus;
    }

    private string RollBackTransaction()
    {
      if (_transaction == null) return string.Empty;
      RecordCleanupStage("TRANSACTION_ROLLBACK");
      _lastObservedTransactionStatus =
        StatusName(_transaction.RollBack());
      return _lastObservedTransactionStatus;
    }

    private string ReadGroupStatus()
    {
      if (_group == null) return string.Empty;
      _lastObservedGroupStatus = StatusName(_group.GetStatus());
      return _lastObservedGroupStatus;
    }

    private void RecordCleanupStage(string cleanupStage)
    {
      _cleanupStage = cleanupStage ?? string.Empty;
    }

    private void RecordFailure(
      Exception exception,
      string failureStage,
      bool isCleanup)
    {
      string normalizedStage = string.IsNullOrWhiteSpace(failureStage)
        ? _rootCauseStage
        : failureStage;
      if (isCleanup) RecordCleanupStage(normalizedStage);
      if (_failure == null) _rootCauseStage = normalizedStage;
      _failure = Combine(_failure, exception);
    }

    private static string StatusName(TransactionStatus status)
    {
      return status.ToString();
    }

    private static Exception Combine(Exception first, Exception second)
    {
      if (first == null) return second;
      if (second == null) return first;
      return new AggregateException(first, second);
    }
  }
}
