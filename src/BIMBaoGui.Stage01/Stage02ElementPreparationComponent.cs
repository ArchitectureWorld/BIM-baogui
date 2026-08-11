using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Stage02;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01
{
  internal sealed class Stage02PreparationUiSnapshot
  {
    internal string RevitVersion { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string SelectionMode { get; set; } = string.Empty;
    internal string MatchedRoles { get; set; } = string.Empty;
    internal int SelectedCount { get; set; }
    internal int MatchedCount { get; set; }
    internal string PreviewHash { get; set; } = string.Empty;
    internal int PendingInstallCount { get; set; }
    internal int InstalledCount { get; set; }
    internal int PendingWriteCount { get; set; }
    internal int WrittenCount { get; set; }
    internal int RuntimeNotImplementedCount { get; set; }
    internal int RuntimeUnclassifiedRequirementCount { get; set; }
    internal string FirstRuntimeBlockReason { get; set; } = string.Empty;
    internal string FirstBlocker { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
  }

  public sealed class Stage02ElementPreparationComponent : GH_Component
  {
    private const string WaitingContext = "等待上下文";
    private const string WaitingPreview = "等待预览";
    private const string SelectionCancelled = "选择取消";
    private const string PreviewBlocked = "预览阻断";
    private const string PreviewTechnicalFailed = "预览技术失败";
    private const string PreviewReady = "预览就绪";
    private const string Confirming = "确认中";
    private const string WriteSucceeded = "写入成功";
    private const string WriteFailed = "写入失败";
    private const string ResultExpired = "结果过期";

    private readonly object _stateLock = new object();
    private readonly ExplicitExecutionGate _previewGate =
      new ExplicitExecutionGate();
    private readonly ExplicitExecutionGate _confirmGate =
      new ExplicitExecutionGate();
    private readonly Stage02RevitPreviewService _previewService =
      new Stage02RevitPreviewService();
    private readonly Stage02RevitWriteService _writeService =
      new Stage02RevitWriteService();
    private readonly Stage02PreparationWriteAttemptState _writeAttemptState =
      new Stage02PreparationWriteAttemptState();
    private readonly Stage02PreparationFailureReportState _failureReportState =
      new Stage02PreparationFailureReportState();
    private readonly Stage02PreparationPreviewCountCache _previewCountCache =
      new Stage02PreparationPreviewCountCache();

    private HBRFileContext _currentContext;
    private Stage02RevitContextSnapshot _hostSnapshot =
      new Stage02RevitContextSnapshot();
    private string _currentInputSignature = string.Empty;
    private string _currentHostFingerprint = string.Empty;
    private string _selectionMode = string.Empty;
    private bool _previewPending;
    private Stage02Preview _preview;
    private HBRFileContext _previewContext;
    private Stage02RevitSelectionResult _previewSelectionEvidence;
    private string _previewInputSignature = string.Empty;
    private string _previewHostFingerprint = string.Empty;
    private string _previewNonce = string.Empty;
    private IReadOnlyList<string> _previewBlockers = Array.Empty<string>();
    private IReadOnlyList<string> _blockers = Array.Empty<string>();
    private string _writeStatus = WaitingPreview;
    private string _status = WaitingContext;
    private int _installedCount;
    private int _writtenCount;

    public Stage02ElementPreparationComponent()
      : base(
        "湖北BIM报规｜02 构件与属性准备",
        "构件与属性准备",
        "按明确选择入口生成 HBR 属性预览；确认后在单个 Revit 原子事务组中"
        + "安装可见共享参数、写入建议值并回读验证。",
        "湖北BIM报规",
        "报规工作流")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("d874201c-c66d-4af4-b3f0-24d3d8d1ff9d");

    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override void CreateAttributes()
    {
      m_attributes = new Stage02PreparationAttributes(this);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRFileContextParam(),
        "文件上下文",
        "Context",
        "必须连接 01 文件初始化输出的强类型文件上下文。",
        GH_ParamAccess.item);
      pManager.AddIntegerParameter(
        "元素Id",
        "Ids",
        "可选。当前 Revit 文档内的 ElementId 列表；会立即解析并冻结 UniqueId。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "角色提示",
        "Role",
        "可选。为当前选择入口中的全部元素提供一个角色提示。",
        GH_ParamAccess.item,
        string.Empty);
      pManager.AddBooleanParameter(
        "交互点选",
        "Pick",
        "为 true 时，仅在“生成预览”的 false→true 边沿打开 Revit 点选。",
        GH_ParamAccess.item,
        false);
      pManager.AddBooleanParameter(
        "项目信息",
        "ProjectInfo",
        "为 true 时使用当前文档 ProjectInformation；空角色默认 PROJECT。",
        GH_ParamAccess.item,
        false);
      pManager.AddBooleanParameter(
        "生成预览",
        "Preview",
        "从 false 切换为 true 时生成一次新预览。",
        GH_ParamAccess.item,
        false);
      pManager.AddBooleanParameter(
        "确认写入",
        "Confirm",
        "从 false 切换为 true 时消费当前合格预览并提交一次原子写入。",
        GH_ParamAccess.item,
        false);
      pManager[1].Optional = true;
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRStage02PreviewParam(),
        "预览",
        "Preview",
        "强类型 Stage02 运行时预览；不会持久化到 GH 文档。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "匹配载体",
        "Carriers",
        "按 UniqueId 稳定排序的元素、角色和匹配来源。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "字段明细",
        "Fields",
        "稳定 Data Tree：每个元素一个分支，字段按 propertyId 排序。",
        GH_ParamAccess.tree);
      pManager.AddTextParameter(
        "阻断信息",
        "Blockers",
        "选择、预览、确认或写入的全部阻断信息。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "写入状态",
        "WriteStatus",
        "当前确认写入状态。",
        GH_ParamAccess.item);
      pManager.AddIntegerParameter(
        "待安装数量",
        "PendingInstall",
        "预览中尚需创建、绑定或扩展类别的固定 GUID 参数数量。",
        GH_ParamAccess.item);
      pManager.AddIntegerParameter(
        "已安装数量",
        "Installed",
        "预览中已满足绑定的参数数量；写入成功后为本次确认的安装总数。",
        GH_ParamAccess.item);
      pManager.AddIntegerParameter(
        "待写入数量",
        "PendingWrite",
        "预览中 ValueAction=SET 的字段数量。",
        GH_ParamAccess.item);
      pManager.AddIntegerParameter(
        "已写入数量",
        "Written",
        "最近一次成功确认并回读的字段数量。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "规则哈希",
        "RuleHash",
        "预览或当前文件上下文绑定的 HBR 规则包 SHA-256。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "报告路径",
        "ReportPath",
        "当前输入签名的预览或写入失败报告路径；无报告时为空。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "总状态",
        "Status",
        "等待、预览、确认、成功、失败或结果过期状态。",
        GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      HBRFileContextGoo contextGoo = null;
      var elementIds = new List<int>();
      string roleHint = string.Empty;
      bool explicitPick = false;
      bool projectInformation = false;
      bool generatePreview = false;
      bool confirmWrite = false;
      dataAccess.GetData(0, ref contextGoo);
      dataAccess.GetDataList(1, elementIds);
      dataAccess.GetData(2, ref roleHint);
      dataAccess.GetData(3, ref explicitPick);
      dataAccess.GetData(4, ref projectInformation);
      dataAccess.GetData(5, ref generatePreview);
      dataAccess.GetData(6, ref confirmWrite);

      HBRFileContext context = contextGoo?.Value;
      Stage02PreparationInputDecision decision =
        Stage02PreparationInputPolicy.Evaluate(
          context?.FileContextHash ?? string.Empty,
          elementIds,
          roleHint,
          explicitPick,
          projectInformation);
      Stage02RevitContextSnapshot hostSnapshot =
        Stage02RevitContextService.ReadSnapshot();
      string[] inputBlockers = CollectInputBlockers(
        contextGoo,
        context,
        hostSnapshot,
        decision);

      bool previewEdge;
      bool confirmEdge;
      lock (_stateLock)
      {
        bool inputChanged = !string.IsNullOrWhiteSpace(
            _currentInputSignature)
          && (!string.Equals(
              _currentInputSignature,
              decision.InputSignature,
              StringComparison.Ordinal)
            || !string.Equals(
              _currentHostFingerprint,
              hostSnapshot?.DocumentFingerprint ?? string.Empty,
              StringComparison.OrdinalIgnoreCase));
        bool hadPreview = _preview != null
          || !string.IsNullOrWhiteSpace(_previewNonce);
        _currentInputSignature = decision.InputSignature;
        _currentHostFingerprint =
          hostSnapshot?.DocumentFingerprint ?? string.Empty;
        _failureReportState.ObserveCurrent(
          _currentInputSignature,
          _currentHostFingerprint);
        _currentContext = context;
        _hostSnapshot = hostSnapshot ?? new Stage02RevitContextSnapshot();
        _selectionMode = SelectionModeLabel(decision.SelectionMode);

        if (inputChanged)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _writeStatus = ResultExpired + "｜请重新生成预览";
          _blockers = Array.Empty<string>();
          _status = hadPreview ? ResultExpired : WaitingPreview;
          if (_writeAttemptState.Phase ==
            Stage02PreparationWriteAttemptPhase.StalePending)
          {
            SetStalePendingStatusLocked();
          }
        }
        if (inputBlockers.Length > 0)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = inputBlockers;
          if (_writeAttemptState.Phase ==
            Stage02PreparationWriteAttemptPhase.StalePending)
          {
            SetStalePendingStatusLocked();
          }
          else
          {
            _writeStatus = PreviewBlocked;
            _status = context == null ? WaitingContext : PreviewBlocked;
          }
        }
        else if (string.Equals(_status, WaitingContext, StringComparison.Ordinal))
        {
          _blockers = Array.Empty<string>();
          _writeStatus = WaitingPreview;
          _status = WaitingPreview;
        }
        previewEdge = _previewGate.Observe(generatePreview);
        confirmEdge = _confirmGate.Observe(confirmWrite);
      }

      bool previewAllowed = previewEdge
        && inputBlockers.Length == 0
        && context != null;
      bool confirmAllowed = confirmEdge && inputBlockers.Length == 0;
      Stage02PreparationEdgeDecision edgeDecision =
        Stage02PreparationExecutionPolicy.Evaluate(
          previewAllowed,
          confirmAllowed);
      if (edgeDecision.ShouldGeneratePreview)
      {
        BeginPreview(
          context,
          decision,
          hostSnapshot?.DocumentFingerprint ?? string.Empty);
      }
      if (edgeDecision.ConfirmationDeferred) MarkConfirmationDeferred();
      if (edgeDecision.ShouldConfirmWrite) BeginWrite();

      Stage02PreparationRuntimeSnapshot snapshot = CaptureRuntimeSnapshot();
      Message = snapshot.Status;
      dataAccess.SetData(
        0,
        snapshot.Preview == null
          ? null
          : new HBRStage02PreviewGoo(snapshot.Preview));
      dataAccess.SetDataList(1, BuildMatchedCarriers(snapshot.Preview));
      dataAccess.SetDataTree(2, BuildFieldDetails(snapshot.Preview));
      dataAccess.SetDataList(3, snapshot.Blockers);
      dataAccess.SetData(4, snapshot.WriteStatus);
      dataAccess.SetData(5, snapshot.PreviewCounts.PendingInstallCount);
      dataAccess.SetData(6, snapshot.InstalledCount);
      dataAccess.SetData(7, snapshot.PreviewCounts.PendingWriteCount);
      dataAccess.SetData(8, snapshot.WrittenCount);
      dataAccess.SetData(
        9,
        snapshot.Preview?.RulePackageSha256
          ?? snapshot.Context?.RulePackageSha256
          ?? string.Empty);
      dataAccess.SetData(10, snapshot.ReportPath);
      dataAccess.SetData(11, snapshot.Status);
    }

    private void BeginPreview(
      HBRFileContext context,
      Stage02PreparationInputDecision decision,
      string hostFingerprint)
    {
      lock (_stateLock)
      {
        if (_previewPending) return;
        if (_writeAttemptState.IsPending)
        {
          SetPendingAttemptStatusLocked();
          return;
        }
        ClearPreviewLocked();
        ResetCountsLocked();
        _failureReportState.BeginPreview(
          decision.InputSignature,
          hostFingerprint);
        _previewPending = true;
        _blockers = Array.Empty<string>();
        _writeStatus = WaitingPreview;
        _status = WaitingPreview;
      }

      Stage02RevitSelectionResult selection;
      try
      {
        switch (decision.SelectionMode)
        {
          case Stage02PreparationSelectionMode.ProjectInformation:
            selection = Stage02RevitSelectionService
              .SelectProjectInformation(context, decision.RoleHint);
            break;
          case Stage02PreparationSelectionMode.ExplicitIds:
            selection = Stage02RevitSelectionService.ResolveElementIds(
              context,
              decision.ElementIds,
              decision.RoleHint);
            break;
          case Stage02PreparationSelectionMode.ExplicitPick:
            selection = Stage02RevitSelectionService.PickElements(
              context,
              decision.RoleHint);
            break;
          default:
            selection = Stage02RevitSelectionService.ReadCurrentSelection(
              context,
              decision.RoleHint);
            break;
        }
      }
      catch (Exception exception)
      {
        CompleteTechnicalPreviewFailure(
          context,
          decision.InputSignature,
          hostFingerprint,
          null,
          "PREVIEW_SELECTION",
          "STAGE02_SELECTION_SERVICE_EXCEPTION",
          "Stage02 元素选择发生技术失败。",
          exception,
          new[] { "选择 Revit 元素失败：" + exception.Message });
        return;
      }

      Stage02RevitFailureReportDecision selectionReport =
        Stage02RevitFailureReportPolicy.ForSelection(selection);
      if (selectionReport.ShouldWrite)
      {
        CompleteTechnicalPreviewFailure(
          context,
          decision.InputSignature,
          hostFingerprint,
          selection,
          selectionReport.OperationStage,
          selectionReport.ErrorCode,
          selectionReport.DiagnosticMessage,
          selectionReport.Exception,
          selection?.Messages
            ?? new[] { "Stage02 元素选择服务未返回结果。" });
        return;
      }

      if (selection.Cancelled)
      {
        CompletePreview(
          decision.InputSignature,
          hostFingerprint,
          context,
          selection,
          string.Empty,
          null);
        return;
      }
      if (!selection.Success)
      {
        CompletePreviewFailure(
          decision.InputSignature,
          hostFingerprint,
          selection.Messages);
        return;
      }

      string nonce = Guid.NewGuid().ToString("N");
      Stage02RevitPreviewResult result;
      try
      {
        result = _previewService.CreatePreview(
          context,
          selection,
          nonce);
      }
      catch (Exception exception)
      {
        CompleteTechnicalPreviewFailure(
          context,
          decision.InputSignature,
          hostFingerprint,
          selection,
          "PREVIEW_BUILD",
          "STAGE02_PREVIEW_SERVICE_EXCEPTION",
          "Stage02 预览构建发生技术失败。",
          exception,
          new[] { "生成 Stage02 预览失败：" + exception.Message });
        return;
      }
      Stage02RevitFailureReportDecision previewReport =
        Stage02RevitFailureReportPolicy.ForPreview(result);
      if (previewReport.ShouldWrite)
      {
        IReadOnlyList<string> messages = FormatBlockers(result?.Blockers);
        CompleteTechnicalPreviewFailure(
          context,
          decision.InputSignature,
          hostFingerprint,
          selection,
          previewReport.OperationStage,
          previewReport.ErrorCode,
          previewReport.DiagnosticMessage,
          previewReport.Exception,
          messages.Count == 0
            ? new[] { "预览服务未返回可发布的结果。" }
            : messages);
        return;
      }
      CompletePreview(
        decision.InputSignature,
        hostFingerprint,
        context,
        selection,
        nonce,
        result);
    }

    private void CompleteTechnicalPreviewFailure(
      HBRFileContext context,
      string inputSignature,
      string hostFingerprint,
      Stage02RevitSelectionResult selection,
      string operationStage,
      string errorCode,
      string diagnosticMessage,
      Exception exception,
      IEnumerable<string> messages)
    {
      DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
      Stage02FailureReportWriteResult report =
        Stage02FailureReportWriter.TryWrite(
          new Stage02FailureReportContext
          {
            DiagnosticCode = "DIAG_STAGE02_PREVIEW_FAILED",
            ErrorCode = errorCode,
            DiagnosticMessage = diagnosticMessage,
            InputSignature = inputSignature,
            FileGuid = context?.FileGuid ?? string.Empty,
            DocumentFingerprint = hostFingerprint ?? string.Empty,
            DocumentTitle = context?.RevitDocumentTitle ?? string.Empty,
            RulePackageId = context?.RulePackageId ?? string.Empty,
            RulePackageVersion = context?.RulePackageVersion ?? string.Empty,
            RulePackageSha256 = context?.RulePackageSha256 ?? string.Empty,
            PreviewHash = string.Empty,
            UniqueIds = selection?.Items
              .Select(item => item.UniqueId)
              .ToArray()
              ?? Array.Empty<string>(),
            PropertyIds = Array.Empty<string>(),
            OperationStage = operationStage,
            RootCauseStage = operationStage,
            CleanupStage = string.Empty,
            TransactionRolledBack = false,
            GroupRolledBack = false,
            RollbackConfirmed = false,
            TransactionStatus = "NOT_STARTED",
            TransactionGroupStatus = "NOT_STARTED",
            Exception = exception,
            OccurredUtc = occurredUtc,
            OccurredLocal = occurredUtc.ToLocalTime()
          });

      lock (_stateLock)
      {
        _previewPending = false;
        if (!IsInputSignatureCurrentLocked(
          inputSignature,
          hostFingerprint))
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
        }
        else
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          var blockers = new List<string>(FreezeStrings(messages));
          if (report.Success)
          {
            _failureReportState.TryPublish(
              inputSignature,
              hostFingerprint,
              report.ReportPath);
          }
          else
          {
            blockers.Add(
              report.ErrorCode
              + "｜Stage02 失败报告写入失败："
              + report.ReportWriteErrorSummary);
          }
          _blockers = FreezeStrings(blockers);
          _writeStatus = PreviewTechnicalFailed;
          _status = PreviewTechnicalFailed;
        }
      }
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private void CompletePreview(
      string inputSignature,
      string hostFingerprint,
      HBRFileContext context,
      Stage02RevitSelectionResult selection,
      string nonce,
      Stage02RevitPreviewResult result)
    {
      lock (_stateLock)
      {
        _previewPending = false;
        if (!IsInputSignatureCurrentLocked(
          inputSignature,
          hostFingerprint))
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
        }
        else if (selection != null && selection.Cancelled)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = SelectionCancelled;
          _status = SelectionCancelled;
        }
        else if (result == null || result.Preview == null)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = FormatBlockers(result?.Blockers);
          if (_blockers.Count == 0)
            _blockers = new[] { "预览服务未返回可发布的结果。" };
          _writeStatus = PreviewBlocked;
          _status = PreviewBlocked;
        }
        else
        {
          _preview = result.Preview;
          _previewContext = context;
          _previewSelectionEvidence = selection;
          _previewInputSignature = inputSignature;
          _previewHostFingerprint = hostFingerprint;
          _previewNonce = nonce ?? string.Empty;
          _previewBlockers = FormatBlockers(result.Blockers);
          _blockers = _previewBlockers;
          _previewCountCache.Publish(_preview);
          _installedCount =
            _previewCountCache.Current.AlreadyInstalledCount;
          _writtenCount = 0;
          _writeStatus = _blockers.Count == 0
            ? PreviewReady
            : PreviewBlocked;
          _status = _writeStatus;
        }
      }
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private void CompletePreviewFailure(
      string inputSignature,
      string hostFingerprint,
      params string[] messages)
    {
      CompletePreviewFailure(
        inputSignature,
        hostFingerprint,
        (IEnumerable<string>) messages);
    }

    private void CompletePreviewFailure(
      string inputSignature,
      string hostFingerprint,
      IEnumerable<string> messages)
    {
      lock (_stateLock)
      {
        _previewPending = false;
        if (!IsInputSignatureCurrentLocked(
          inputSignature,
          hostFingerprint))
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
        }
        else
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = FreezeStrings(messages);
          _writeStatus = PreviewBlocked;
          _status = PreviewBlocked;
        }
      }
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private void MarkConfirmationDeferred()
    {
      lock (_stateLock)
      {
        if (_writeAttemptState.IsPending
          || _preview == null
          || _previewBlockers.Count > 0)
        {
          return;
        }
        _writeStatus = PreviewReady + "｜请再次触发确认写入";
        _status = PreviewReady;
      }
    }

    private void SetPendingAttemptStatusLocked()
    {
      if (_writeAttemptState.Phase ==
        Stage02PreparationWriteAttemptPhase.StalePending)
      {
        SetStalePendingStatusLocked();
        return;
      }
      _writeStatus = Confirming;
      _status = Confirming;
    }

    private void SetStalePendingStatusLocked()
    {
      _writeStatus = ResultExpired + "｜请重新生成预览";
      _status = ResultExpired;
    }

    private void BeginWrite()
    {
      Stage02RevitWriteRequest request = null;
      string inputSignature = string.Empty;
      string hostFingerprint = string.Empty;
      string previewHash = string.Empty;
      Guid attemptToken = Guid.Empty;
      lock (_stateLock)
      {
        if (_writeAttemptState.IsPending)
        {
          SetPendingAttemptStatusLocked();
          return;
        }
        if (_preview == null
          || _previewContext == null
          || _previewSelectionEvidence == null
          || string.IsNullOrWhiteSpace(_previewNonce))
        {
          _blockers = new[] { "没有可确认的 Stage02 预览；请先生成预览。" };
          _writeStatus = PreviewBlocked;
          _status = PreviewBlocked;
          return;
        }
        if (_previewBlockers.Count > 0)
        {
          _writeStatus = PreviewBlocked;
          _status = PreviewBlocked;
          return;
        }
        if (!IsInputSignatureCurrentLocked(
          _previewInputSignature,
          _previewHostFingerprint))
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
          return;
        }

        inputSignature = _previewInputSignature;
        hostFingerprint = _previewHostFingerprint;
        previewHash = _preview.PreviewHash;
        attemptToken = _writeAttemptState.BeginAttempt();
        request = Stage02RevitWriteRequest.FromPreview(
          _previewContext,
          _preview,
          _previewSelectionEvidence,
          inputSignature,
          attemptToken);
        _blockers = _previewBlockers;
        _writeStatus = Confirming;
        _status = Confirming;
      }

      bool enqueued;
      string error;
      Exception enqueueException = null;
      try
      {
        enqueued = _writeService.EnqueueWrite(
          request,
          completed => CompleteWrite(
            attemptToken,
            inputSignature,
            hostFingerprint,
            previewHash,
            completed),
          exception => TerminateWriteCompletionConsumerFailure(
            attemptToken,
            inputSignature,
            hostFingerprint,
            previewHash,
            exception),
          exception => RecordWriteCompletionConsumerFailure(
            request,
            inputSignature,
            hostFingerprint,
            exception),
          RefreshAfterWriteCompletionConsumerFailure,
          out error);
      }
      catch (Exception exception)
      {
        enqueued = false;
        error = exception.Message;
        enqueueException = exception;
      }
      if (enqueued) return;

      lock (_stateLock)
      {
        Stage02PreparationWriteCompletionDisposition disposition =
          _writeAttemptState.CompleteAttempt(
            attemptToken,
            string.Empty);
        if (disposition ==
          Stage02PreparationWriteCompletionDisposition.Ignored)
        {
          return;
        }
        Stage02RevitWriteEnqueueFailureDecision failureDecision =
          Stage02RevitWriteEnqueueFailurePolicy.ForFailure(
            error,
            enqueueException);
        Stage02FailureReportDraft failureDraft =
          BuildEnqueueFailureReportDraft(request, failureDecision);
        bool currentIdentity = disposition ==
            Stage02PreparationWriteCompletionDisposition.Publish
          && IsInputSignatureCurrentLocked(
            inputSignature,
            hostFingerprint)
          && _preview != null
          && string.Equals(
            _preview.PreviewHash,
            previewHash,
            StringComparison.Ordinal);
        Stage02FailureReportPublicationResult reportPublication =
          Stage02FailureReportFinalizer.TryPublish(
            failureDraft,
            currentIdentity
              ? Stage02FailureReportPublicationDisposition.PublishedCurrent
              : Stage02FailureReportPublicationDisposition.DiscardedStale);
        var completed = new Stage02RevitWriteResult
        {
          Success = false,
          RequiresNewPreview = false,
          Status = WriteFailed,
          FailureReportDraft = failureDraft,
          Messages = new[] { failureDecision.UserMessage }
        };
        ApplyFailureReportPublication(completed, reportPublication);
        if (disposition ==
          Stage02PreparationWriteCompletionDisposition.Discarded)
        {
          SetStalePendingStatusLocked();
          return;
        }
        if (!currentIdentity)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
          return;
        }
        if (reportPublication.ShouldPublishCurrent)
        {
          _failureReportState.TryPublish(
            inputSignature,
            hostFingerprint,
            reportPublication.ReportPath);
        }
        _blockers = FreezeStrings(
          new[] { failureDecision.UserMessage }
            .Concat(FormatWriteMessages(completed)));
        _writeStatus = WriteFailed;
        _status = WriteFailed;
      }
    }

    private static Stage02FailureReportDraft BuildEnqueueFailureReportDraft(
      Stage02RevitWriteRequest request,
      Stage02RevitWriteEnqueueFailureDecision decision)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (decision == null) throw new ArgumentNullException(nameof(decision));
      DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
      return Stage02FailureReportDraft.Capture(
        new Stage02FailureReportContext
        {
          DiagnosticCode = "DIAG_STAGE02_WRITE_FAILED",
          ErrorCode = decision.ErrorCode,
          DiagnosticMessage = decision.DiagnosticMessage,
          InputSignature = request.InputSignature,
          AttemptToken = request.AttemptToken,
          FileGuid = request.Preview.FileGuid,
          DocumentFingerprint = request.DocumentFingerprint,
          DocumentTitle = request.Context.RevitDocumentTitle,
          RulePackageId = request.Preview.RulePackageId,
          RulePackageVersion = request.Preview.RulePackageVersion,
          RulePackageSha256 = request.Preview.RulePackageSha256,
          PreviewHash = request.PreviewHash,
          UniqueIds = request.Targets
            .Select(item => item.UniqueId)
            .ToArray(),
          PropertyIds = request.Preview.Elements
            .SelectMany(element => element.Operations)
            .Select(operation => operation.PropertyId)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
          OperationStage = "WRITE_ENQUEUE",
          RootCauseStage = "WRITE_ENQUEUE",
          CleanupStage = string.Empty,
          TransactionRolledBack = false,
          GroupRolledBack = false,
          RollbackConfirmed = false,
          TransactionStatus = "NOT_STARTED",
          TransactionGroupStatus = "NOT_STARTED",
          Exception = decision.Exception,
          OccurredUtc = occurredUtc,
          OccurredLocal = occurredUtc.ToLocalTime()
        });
    }

    private void TerminateWriteCompletionConsumerFailure(
      Guid attemptToken,
      string inputSignature,
      string hostFingerprint,
      string previewHash,
      Exception exception)
    {
      lock (_stateLock)
      {
        _writeAttemptState.CompleteAttempt(attemptToken, string.Empty);
        bool currentIdentity = IsInputSignatureCurrentLocked(
            inputSignature,
            hostFingerprint)
          && (_preview == null
            || string.Equals(
              _preview.PreviewHash,
              previewHash,
              StringComparison.Ordinal));
        if (!currentIdentity)
        {
          _writeStatus = ResultExpired;
          _status = ResultExpired;
          return;
        }

        ClearPreviewLocked();
        ResetCountsLocked();
        _blockers = new[]
        {
          "STAGE02_WRITE_COMPLETION_CONSUMER_FAILED｜"
          + "Stage02 写入结果发布发生技术失败；业务完成未重试，必须重新预览。"
        };
        _writeStatus = WriteFailed
          + "｜完成消费者技术失败｜必须重新预览";
        _status = WriteFailed;
      }
    }

    private void RecordWriteCompletionConsumerFailure(
      Stage02RevitWriteRequest request,
      string inputSignature,
      string hostFingerprint,
      Exception exception)
    {
      Stage02FailureReportDraft draft =
        BuildCompletionConsumerFailureReportDraft(request, exception);
      lock (_stateLock)
      {
        bool currentIdentity = IsInputSignatureCurrentLocked(
          inputSignature,
          hostFingerprint);
        Stage02FailureReportPublicationResult publication =
          Stage02FailureReportFinalizer.TryPublish(
            draft,
            currentIdentity
              ? Stage02FailureReportPublicationDisposition.PublishedCurrent
              : Stage02FailureReportPublicationDisposition.DiscardedStale);
        if (!currentIdentity) return;
        if (publication.ShouldPublishCurrent)
        {
          _failureReportState.TryPublish(
            inputSignature,
            hostFingerprint,
            publication.ReportPath);
        }
        if (!publication.WasWritten)
        {
          Stage02FailureReportWriteResult report = publication.WriteResult;
          _blockers = FreezeStrings(_blockers.Concat(new[]
          {
            "REPORT_WRITE_FAILED｜Stage02 完成消费者失败报告写入失败："
            + (report?.ReportWriteErrorSummary ?? string.Empty)
          }));
        }
      }
    }

    private static Stage02FailureReportDraft
      BuildCompletionConsumerFailureReportDraft(
        Stage02RevitWriteRequest request,
        Exception exception)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
      return Stage02FailureReportDraft.Capture(
        new Stage02FailureReportContext
        {
          DiagnosticCode = "DIAG_STAGE02_WRITE_FAILED",
          ErrorCode = "STAGE02_WRITE_COMPLETION_CONSUMER_FAILED",
          DiagnosticMessage =
            "Stage02 写入结果完成消费者发生技术失败；业务完成未重试。",
          InputSignature = request.InputSignature,
          AttemptToken = request.AttemptToken,
          FileGuid = request.Preview.FileGuid,
          DocumentFingerprint = request.DocumentFingerprint,
          DocumentTitle = request.Preview.DocumentTitle,
          RulePackageId = request.Preview.RulePackageId,
          RulePackageVersion = request.Preview.RulePackageVersion,
          RulePackageSha256 = request.Preview.RulePackageSha256,
          PreviewHash = request.PreviewHash,
          UniqueIds = request.Targets
            .Select(item => item.UniqueId)
            .ToArray(),
          PropertyIds = request.Preview.Elements
            .SelectMany(element => element.Operations)
            .Select(operation => operation.PropertyId)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
          OperationStage = "WRITE_COMPLETION_CONSUMER",
          RootCauseStage = "WRITE_COMPLETION_CONSUMER",
          CleanupStage = string.Empty,
          TransactionRolledBack = false,
          GroupRolledBack = false,
          RollbackConfirmed = false,
          TransactionStatus = "TERMINAL_RESULT_DELIVERED",
          TransactionGroupStatus = "TERMINAL_RESULT_DELIVERED",
          Exception = exception,
          OccurredUtc = occurredUtc,
          OccurredLocal = occurredUtc.ToLocalTime()
        });
    }

    private void RefreshAfterWriteCompletionConsumerFailure()
    {
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private void CompleteWrite(
      Guid attemptToken,
      string inputSignature,
      string hostFingerprint,
      string previewHash,
      Stage02RevitWriteResult completed)
    {
      Stage02PreparationWriteCompletionDisposition disposition;
      lock (_stateLock)
      {
        disposition = _writeAttemptState.CompleteAttempt(
          attemptToken,
          string.Empty);
        if (disposition ==
          Stage02PreparationWriteCompletionDisposition.Ignored)
        {
          return;
        }
        completed = completed ?? new Stage02RevitWriteResult
          {
            Success = false,
            RequiresNewPreview = true,
            Status = WriteFailed,
            Messages = new[] { "Stage02 写入未返回结果。" }
          };
        bool currentIdentity = disposition ==
            Stage02PreparationWriteCompletionDisposition.Publish
          && IsInputSignatureCurrentLocked(
            inputSignature,
            hostFingerprint)
          && _preview != null
          && string.Equals(
            _preview.PreviewHash,
            previewHash,
            StringComparison.Ordinal);
        Stage02FailureReportPublicationResult reportPublication = null;
        if (completed.FailureReportDraft != null)
        {
          reportPublication = Stage02FailureReportFinalizer.TryPublish(
            completed.FailureReportDraft,
            currentIdentity
              ? Stage02FailureReportPublicationDisposition.PublishedCurrent
              : Stage02FailureReportPublicationDisposition.DiscardedStale);
          ApplyFailureReportPublication(completed, reportPublication);
        }
        if (disposition ==
          Stage02PreparationWriteCompletionDisposition.Discarded)
        {
          SetStalePendingStatusLocked();
        }
        else if (!currentIdentity)
        {
          ClearPreviewLocked();
          ResetCountsLocked();
          _blockers = Array.Empty<string>();
          _writeStatus = ResultExpired;
          _status = ResultExpired;
        }
        else
        {
          if (reportPublication == null
            || reportPublication.ShouldPublishCurrent)
          {
            _failureReportState.TryPublish(
              inputSignature,
              hostFingerprint,
              completed.ReportPath);
          }
          _blockers = FormatWriteMessages(completed);
          Stage02PreparationWritePublicationDecision publication =
            Stage02PreparationWritePublicationPolicy.Evaluate(
              _previewCountCache.Current,
              _installedCount,
              completed.Success,
              completed.RequiresNewPreview);
          _installedCount = publication.InstalledCount;
          _writtenCount = publication.WrittenCount;
          if (completed.Success)
          {
            if (publication.ClearPreview) ClearPreviewLocked();
            _blockers = Array.Empty<string>();
            _writeStatus = WriteStatusText(
              WriteSucceeded,
              completed.Status);
            _status = WriteSucceeded;
          }
          else
          {
            _writeStatus = WriteStatusText(
              WriteFailed,
              completed.Status);
            _status = WriteFailed;
            if (publication.ClearPreview) ClearPreviewLocked();
          }
        }
      }
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private bool IsInputSignatureCurrentLocked(
      string inputSignature,
      string hostFingerprint)
    {
      return string.Equals(
          _currentInputSignature,
          inputSignature,
          StringComparison.Ordinal)
        && string.Equals(
          _currentHostFingerprint,
          hostFingerprint,
          StringComparison.OrdinalIgnoreCase);
    }

    private void ClearPreviewLocked()
    {
      _writeAttemptState.MarkActiveAttemptStale();
      _previewCountCache.Clear();
      _previewPending = false;
      _preview = null;
      _previewContext = null;
      _previewSelectionEvidence = null;
      _previewInputSignature = string.Empty;
      _previewHostFingerprint = string.Empty;
      _previewNonce = string.Empty;
      _previewBlockers = Array.Empty<string>();
    }

    private void ResetCountsLocked()
    {
      _installedCount = 0;
      _writtenCount = 0;
    }

    internal Stage02PreparationUiSnapshot GetUiSnapshot()
    {
      lock (_stateLock)
      {
        Stage02PreparationPreviewCounts previewCounts =
          _previewCountCache.Current;
        Stage02WriteOperation[] runtimeOperations = (_preview == null
            ? Array.Empty<Stage02MatchedElement>()
            : _preview.Elements)
          .OrderBy(x => x.Element.UniqueId, StringComparer.Ordinal)
          .SelectMany(x => x.Operations
            .OrderBy(y => y.PropertyId, StringComparer.Ordinal)
            .ThenBy(y => y.RuntimeBlockCode, StringComparer.Ordinal))
          .ToArray();
        return new Stage02PreparationUiSnapshot
        {
          RevitVersion = _hostSnapshot?.RevitVersion ?? string.Empty,
          DocumentTitle = _hostSnapshot?.DocumentTitle ?? string.Empty,
          RulePackageId = _preview?.RulePackageId
            ?? _currentContext?.RulePackageId
            ?? string.Empty,
          RulePackageVersion = _preview?.RulePackageVersion
            ?? _currentContext?.RulePackageVersion
            ?? string.Empty,
          RulePackageSha256 = _preview?.RulePackageSha256
            ?? _currentContext?.RulePackageSha256
            ?? string.Empty,
          SelectionMode = _selectionMode,
          MatchedRoles = string.Join(
            "、",
            (_preview == null
                ? Array.Empty<string>()
                : _preview.Elements
                  .Select(element => element.RoleId)
                  .Where(value => !string.IsNullOrWhiteSpace(value)))
              .Distinct(StringComparer.Ordinal)
              .OrderBy(value => value, StringComparer.Ordinal)),
          SelectedCount = _previewSelectionEvidence?.Items.Count ?? 0,
          MatchedCount = _preview?.Elements.Count ?? 0,
          PreviewHash = _preview?.PreviewHash ?? string.Empty,
          PendingInstallCount = previewCounts.PendingInstallCount,
          InstalledCount = _installedCount,
          PendingWriteCount = previewCounts.PendingWriteCount,
          WrittenCount = _writtenCount,
          RuntimeNotImplementedCount = runtimeOperations.Count(operation =>
            string.Equals(
              operation.RuntimeStatus,
              "NOT_IMPLEMENTED",
              StringComparison.Ordinal)),
          RuntimeUnclassifiedRequirementCount = runtimeOperations.Count(
            operation => string.Equals(
              operation.RuntimeStatus,
              "UNCLASSIFIED_REQUIREMENT",
              StringComparison.Ordinal)),
          FirstRuntimeBlockReason = runtimeOperations
            .Where(operation => !string.Equals(
              operation.RuntimeStatus,
              "SUPPORTED",
              StringComparison.Ordinal))
            .Select(operation => operation.RuntimeBlockReason)
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
              ?? string.Empty,
          FirstBlocker = _blockers.FirstOrDefault() ?? string.Empty,
          Status = _status
        };
      }
    }

    private Stage02PreparationRuntimeSnapshot CaptureRuntimeSnapshot()
    {
      lock (_stateLock)
      {
        return new Stage02PreparationRuntimeSnapshot(
          _currentContext,
          _preview,
          _blockers,
          _writeStatus,
          _failureReportState.ReportPath,
          _status,
          _previewCountCache.Current,
          _installedCount,
          _writtenCount);
      }
    }

    private static string[] CollectInputBlockers(
      HBRFileContextGoo contextGoo,
      HBRFileContext context,
      Stage02RevitContextSnapshot hostSnapshot,
      Stage02PreparationInputDecision decision)
    {
      var blockers = new List<string>();
      blockers.AddRange(decision.Blockers);
      if (context == null)
      {
        blockers.Add(
          "请连接 01 文件初始化输出的强类型“文件上下文”。");
      }
      else if (contextGoo != null && !contextGoo.IsValid)
      {
        blockers.Add(contextGoo.IsValidWhyNot);
      }

      if (context != null)
      {
        if (hostSnapshot == null || !hostSnapshot.HostAvailable)
        {
          blockers.AddRange(hostSnapshot?.Messages
            ?? new[] { "当前没有可用的 Revit 上下文。" });
        }
        else
        {
          blockers.AddRange(hostSnapshot.Messages
            ?? Array.Empty<string>());
          if (!string.Equals(
            context.RevitDocumentFingerprint,
            hostSnapshot.DocumentFingerprint,
            StringComparison.OrdinalIgnoreCase))
          {
            blockers.Add(
              "文件上下文与当前活动 RVT 不一致；请切回对应文档或重新运行 Stage01。");
          }
        }
      }
      return blockers
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    }

    private static IReadOnlyList<string> FormatBlockers(
      IEnumerable<Stage02Blocker> blockers)
    {
      return FreezeStrings((blockers ?? Array.Empty<Stage02Blocker>())
        .Where(blocker => blocker != null)
        .Select(blocker => blocker.Code + "｜" + blocker.Message));
    }

    private static void ApplyFailureReportPublication(
      Stage02RevitWriteResult completed,
      Stage02FailureReportPublicationResult publication)
    {
      if (completed == null || publication == null) return;
      completed.ReportPath = publication.ReportPath;
      Stage02FailureReportWriteResult report = publication.WriteResult;
      if (report == null) return;
      string diagnostic = report.Success
        ? "DIAG_STAGE02_WRITE_FAILED；错误报告=" + report.ReportPath
        : "DIAG_STAGE02_WRITE_FAILED；REPORT_WRITE_FAILED；原始异常="
          + report.OriginalExceptionSummary
          + "；报告写入异常="
          + report.ReportWriteErrorSummary;
      completed.Messages = new[] { diagnostic };
    }

    private static IReadOnlyList<string> FormatWriteMessages(
      Stage02RevitWriteResult result)
    {
      if (result == null) return new[] { "Stage02 写入未返回结果。" };
      string[] blockers = FormatBlockers(result.Blockers).ToArray();
      if (blockers.Length > 0) return blockers;
      return FreezeStrings(result.Messages);
    }

    private static IReadOnlyList<string> FreezeStrings(
      IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>(
        (values ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Distinct(StringComparer.Ordinal)
          .ToArray());
    }

    private static IReadOnlyList<string> BuildMatchedCarriers(
      Stage02Preview preview)
    {
      if (preview == null) return Array.Empty<string>();
      return preview.Elements
        .OrderBy(element => element.Element.UniqueId, StringComparer.Ordinal)
        .Select(element =>
          "ElementId=" + element.Element.ElementId.ToString(
            CultureInfo.InvariantCulture)
          + "｜UniqueId=" + element.Element.UniqueId
          + "｜角色=" + element.RoleId
          + "｜匹配来源=" + element.MatchSource
          + "｜类别=" + element.Element.Category
          + "｜类型=" + element.Element.ElementKind)
        .ToArray();
    }

    private static GH_Structure<GH_String> BuildFieldDetails(
      Stage02Preview preview)
    {
      var tree = new GH_Structure<GH_String>();
      if (preview == null) return tree;
      Stage02MatchedElement[] elements = preview.Elements
        .OrderBy(element => element.Element.UniqueId, StringComparer.Ordinal)
        .ToArray();
      for (int elementIndex = 0;
        elementIndex < elements.Length;
        ++elementIndex)
      {
        Stage02MatchedElement element = elements[elementIndex];
        var path = new GH_Path(elementIndex);
        tree.EnsurePath(path);
        foreach (Stage02WriteOperation operation in element.Operations
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal))
        {
          tree.Append(
            new GH_String(
              Stage02PreparationFieldDetailFormatter.Format(
                element,
                operation)),
            path);
        }
      }
      return tree;
    }

    private static string SelectionModeLabel(
      Stage02PreparationSelectionMode mode)
    {
      switch (mode)
      {
        case Stage02PreparationSelectionMode.ProjectInformation:
          return "项目信息";
        case Stage02PreparationSelectionMode.ExplicitIds:
          return "元素Id";
        case Stage02PreparationSelectionMode.ExplicitPick:
          return "交互点选";
        case Stage02PreparationSelectionMode.CurrentSelection:
          return "当前选择";
        default:
          return "未知";
      }
    }

    private static string WriteStatusText(string state, string backendStatus)
    {
      string detail = (backendStatus ?? string.Empty).Trim();
      return detail.Length == 0
        || string.Equals(detail, state, StringComparison.Ordinal)
        ? state
        : state + "｜" + detail;
    }

    private sealed class Stage02PreparationRuntimeSnapshot
    {
      internal Stage02PreparationRuntimeSnapshot(
        HBRFileContext context,
        Stage02Preview preview,
        IEnumerable<string> blockers,
        string writeStatus,
        string reportPath,
        string status,
        Stage02PreparationPreviewCounts previewCounts,
        int installedCount,
        int writtenCount)
      {
        Context = context;
        Preview = preview;
        Blockers = FreezeStrings(blockers);
        WriteStatus = writeStatus ?? string.Empty;
        ReportPath = reportPath ?? string.Empty;
        Status = status ?? string.Empty;
        PreviewCounts = previewCounts
          ?? Stage02PreparationPreviewCounts.Empty;
        InstalledCount = installedCount;
        WrittenCount = writtenCount;
      }

      internal HBRFileContext Context { get; }
      internal Stage02Preview Preview { get; }
      internal IReadOnlyList<string> Blockers { get; }
      internal string WriteStatus { get; }
      internal string ReportPath { get; }
      internal string Status { get; }
      internal Stage02PreparationPreviewCounts PreviewCounts { get; }
      internal int InstalledCount { get; }
      internal int WrittenCount { get; }
    }
  }
}
