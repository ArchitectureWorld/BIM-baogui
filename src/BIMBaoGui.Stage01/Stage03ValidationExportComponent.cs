using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Stage03;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01
{
  internal sealed class Stage03ComponentViewState
  {
    internal Stage03ComponentViewState(
      Stage03GateMode mode,
      bool pending,
      string status,
      bool allowExport,
      string rawIfcPath,
      string finalIfcPath,
      string fieldsJsonPath,
      string rulePackageSha256,
      IReadOnlyList<Stage03FieldDetail> fieldDetails,
      IReadOnlyList<string> blockers,
      int passedFields,
      int blockedFields,
      bool forcedWithBusinessDefects,
      int businessBlockerCount)
    {
      Mode = mode;
      Pending = pending;
      Status = status ?? string.Empty;
      AllowExport = allowExport;
      RawIfcPath = rawIfcPath ?? string.Empty;
      FinalIfcPath = finalIfcPath ?? string.Empty;
      FieldsJsonPath = fieldsJsonPath ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      FieldDetails = fieldDetails ?? Array.Empty<Stage03FieldDetail>();
      Blockers = blockers ?? Array.Empty<string>();
      PassedFields = passedFields;
      BlockedFields = blockedFields;
      ForcedWithBusinessDefects = forcedWithBusinessDefects;
      BusinessBlockerCount = businessBlockerCount;
    }

    internal Stage03GateMode Mode { get; }
    internal bool Pending { get; }
    internal string Status { get; }
    internal bool AllowExport { get; }
    internal string RawIfcPath { get; }
    internal string FinalIfcPath { get; }
    internal string FieldsJsonPath { get; }
    internal string RulePackageSha256 { get; }
    internal IReadOnlyList<Stage03FieldDetail> FieldDetails { get; }
    internal IReadOnlyList<string> Blockers { get; }
    internal int TotalFields => FieldDetails.Count;
    internal int PassedFields { get; }
    internal int BlockedFields { get; }
    internal bool ForcedWithBusinessDefects { get; }
    internal int BusinessBlockerCount { get; }
  }

  public sealed class Stage03ValidationExportComponent : GH_Component
  {
    private const bool DefaultStrictMode = true;
    private readonly object _stateLock = new object();
    private readonly Stage03ComponentStatePolicy _state =
      new Stage03ComponentStatePolicy();
    private readonly Stage03WorkflowCoordinator _coordinator =
      new Stage03WorkflowCoordinator(
        Stage03ProductionWorkflowServices.Create());
    private Stage03GateMode _mode = Stage03GateMode.Strict;
    private Stage03RunResult _result;
    private IReadOnlyList<Stage03FieldDetail> _fieldDetails =
      Array.Empty<Stage03FieldDetail>();
    private IReadOnlyList<string> _blockers = Array.Empty<string>();
    private string _status = "等待执行";
    private long _currentPendingGeneration;
    private bool _hasObservedSignature;

    public Stage03ValidationExportComponent()
      : base(
        "湖北BIM报规｜03 检测、导出与 H-IFC 转译",
        "检测导出H-IFC",
        "扫描当前 Revit 2020 文档，执行 Stage03 门禁，生成 RAW IFC、"
        + "HIFC-MVD IFC 与 fields JSON。",
        "湖北BIM报规",
        "报规工作流")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("9bf87680-c1dc-499a-b267-33a430ee4201");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.primary;

    internal Stage03ComponentViewState ViewState
    {
      get
      {
        lock (_stateLock) return BuildViewStateLocked();
      }
    }

    public override void CreateAttributes()
    {
      m_attributes = new Stage03ComponentAttributes(this);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRFileContextParam(),
        "文件上下文",
        "Context",
        "必须连接 01 文件初始化的强类型文件上下文。",
        GH_ParamAccess.item);
      pManager.AddBooleanParameter(
        "执行",
        "Run",
        "仅在 false→true 上升沿提交一次 Stage03 运行。",
        GH_ParamAccess.item,
        false);
      pManager.AddTextParameter(
        "输出目录",
        "Output",
        "必填的现有绝对目录；正式产物存在时不会覆盖。",
        GH_ParamAccess.item);
      pManager.AddBooleanParameter(
        "全部通过才导出",
        "Strict",
        "true 为 Strict；false 为 Force，且必须填写强制原因。",
        GH_ParamAccess.item,
        DefaultStrictMode);
      pManager.AddTextParameter(
        "强制原因",
        "Reason",
        "Force 模式必填；原始文本参与运行签名。",
        GH_ParamAccess.item,
        string.Empty);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "允许导出",
        "Allow",
        "最近一次当前签名运行的门禁是否允许导出。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "字段通过",
        "Fields",
        "完整字段 Data Tree；按 carrier 分支、字段稳定排序，含 RAW/final 证据。",
        GH_ParamAccess.tree);
      pManager.AddTextParameter(
        "全部阻断",
        "Blockers",
        "输入检查失败、业务阻断、技术致命码和阻断级诊断；每项均为稳定 JSON。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "RAW IFC",
        "RawIfc",
        "Autodesk IFC4 RAW 正式路径。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "HIFC-MVD IFC",
        "FinalIfc",
        "转译并复读验收后的 HIFC-MVD IFC 正式路径。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "fields JSON",
        "FieldsJson",
        "字段审计报告正式路径。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "规则哈希",
        "RuleHash",
        "本次扫描实际使用的规则包 SHA-256。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "状态",
        "Status",
        "等待、执行、门禁、发布或失败状态。",
        GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      HBRFileContextGoo contextGoo = null;
      bool execute = false;
      string outputDirectory = string.Empty;
      bool allMustPass = DefaultStrictMode;
      string originalForceReason = string.Empty;
      dataAccess.GetData(0, ref contextGoo);
      dataAccess.GetData(1, ref execute);
      dataAccess.GetData(2, ref outputDirectory);
      dataAccess.GetData(3, ref allMustPass);
      dataAccess.GetData(4, ref originalForceReason);

      Stage02RevitContextSnapshot live =
        Stage02RevitContextService.ReadSnapshot();
      Stage03GateMode mode = allMustPass
        ? Stage03GateMode.Strict
        : Stage03GateMode.Force;
      var signature = new Stage03ComponentInputSignature(
        contextGoo?.Value?.FileContextHash ?? string.Empty,
        outputDirectory,
        mode,
        originalForceReason,
        live?.DocumentPath ?? string.Empty,
        live?.DocumentFingerprint ?? string.Empty);

      Stage03WorkflowRequest request = null;
      Stage03ComponentRunToken token = null;
      bool start = false;
      lock (_stateLock)
      {
        _mode = mode;
        bool changed = _state.UpdateSignature(signature);
        if (changed)
        {
          ClearPublishedState(
            _hasObservedSignature
              ? "输入已变化｜等待重新执行"
              : "等待执行",
            Array.Empty<string>());
          _hasObservedSignature = true;
        }

        bool risingEdge = _state.ObserveExecution(execute);
        bool currentRunPending = _currentPendingGeneration != 0
          && _currentPendingGeneration == _state.Generation;
        if (risingEdge && !currentRunPending)
        {
          ClearPublishedState("输入检查中", Array.Empty<string>());
          if (!TryCreateRequest(
            contextGoo,
            outputDirectory,
            mode,
            originalForceReason,
            live,
            out request,
            out string preflightError))
          {
            _status = "输入阻断";
            _blockers = Stage03FieldDetailFormatter.FormatComponentFailure(
              "COMPONENT_PREFLIGHT",
              "COMPONENT_PREFLIGHT",
              preflightError);
          }
          else if (!_state.TryBegin(signature, out token, out string startError))
          {
            _status = "输入阻断";
            _blockers = Stage03FieldDetailFormatter.FormatComponentFailure(
              "COMPONENT_PREFLIGHT",
              "COMPONENT_PREFLIGHT",
              startError);
          }
          else
          {
            _currentPendingGeneration = token.Generation;
            _status = "执行中";
            _blockers = Array.Empty<string>();
            start = true;
          }
        }
      }

      if (start) _ = CompleteAsync(token, request);

      Stage03ComponentViewState view = ViewState;
      Message = Compact(view.Status, 24);
      dataAccess.SetData(0, view.AllowExport);
      dataAccess.SetDataTree(1, BuildFieldTree(view.FieldDetails));
      dataAccess.SetDataList(2, view.Blockers);
      dataAccess.SetData(3, view.RawIfcPath);
      dataAccess.SetData(4, view.FinalIfcPath);
      dataAccess.SetData(5, view.FieldsJsonPath);
      dataAccess.SetData(6, view.RulePackageSha256);
      dataAccess.SetData(7, view.Status);
    }

    private async Task CompleteAsync(
      Stage03ComponentRunToken token,
      Stage03WorkflowRequest request)
    {
      Stage03RunResult completed = null;
      Exception failure = null;
      try
      {
        completed = await _coordinator.RunAsync(request).ConfigureAwait(false);
      }
      catch (Exception exception)
      {
        failure = exception;
      }

      lock (_stateLock)
      {
        if (_currentPendingGeneration == token.Generation)
          _currentPendingGeneration = 0;
        if (_state.TryPublish(token))
        {
          if (completed != null)
          {
            PublishResult(completed);
          }
          else
          {
            ClearPublishedState(
              "Stage03 失败",
              Stage03FieldDetailFormatter.FormatComponentFailure(
                Stage03TechnicalFatalCodes.InvalidIfc,
                "COMPONENT",
                failure == null
                  ? "Stage03 workflow 未返回结果。"
                  : failure.Message,
                new[] { Stage03TechnicalFatalCodes.InvalidIfc }));
          }
        }
      }
      ScheduleRefresh();
    }

    private void PublishResult(Stage03RunResult completed)
    {
      _result = completed;
      _fieldDetails = Stage03FieldDetailFormatter.Format(completed.Fields);
      _blockers = Stage03FieldDetailFormatter.FormatAllBlockers(
        completed.GateDecision,
        completed.TechnicalFatalCodes,
        completed.Diagnostics);
      _status = completed.Status;
    }

    private void ClearPublishedState(
      string status,
      IEnumerable<string> blockers)
    {
      _result = null;
      _fieldDetails = Array.Empty<Stage03FieldDetail>();
      _blockers = Freeze(blockers);
      _status = status ?? string.Empty;
    }

    private Stage03ComponentViewState BuildViewStateLocked()
    {
      bool pending = _currentPendingGeneration != 0
        && _currentPendingGeneration == _state.Generation;
      Stage03FieldResult[] fields = _result == null
        ? Array.Empty<Stage03FieldResult>()
        : _result.Fields.ToArray();
      int passed = fields.Count(field =>
        field.Status == Stage03FieldStatus.Pass
        || field.Status == Stage03FieldStatus.NotApplicable);
      int blocked = fields.Count(field => field.IsBusinessBlocker);
      int businessBlockerCount =
        _result?.GateDecision?.BusinessBlockers?.Count ?? 0;
      bool forcedWithBusinessDefects =
        Stage03ComponentPresentationPolicy.IsForcedWithBusinessDefects(
          _result != null && _result.Forced,
          businessBlockerCount);
      return new Stage03ComponentViewState(
        _mode,
        pending,
        pending ? "执行中" : _status,
        _result != null && _result.AllowExport,
        _result?.RawIfcPath ?? string.Empty,
        _result?.FinalIfcPath ?? string.Empty,
        _result?.FieldReportPath ?? string.Empty,
        _result?.RulePackageSha256 ?? string.Empty,
        Freeze(_fieldDetails),
        Freeze(_blockers),
        passed,
        blocked,
        forcedWithBusinessDefects,
        businessBlockerCount);
    }

    private static bool TryCreateRequest(
      HBRFileContextGoo contextGoo,
      string outputDirectory,
      Stage03GateMode mode,
      string forceReason,
      Stage02RevitContextSnapshot live,
      out Stage03WorkflowRequest request,
      out string error)
    {
      request = null;
      error = string.Empty;
      if (contextGoo?.Value == null)
      {
        error = "请连接 01 文件初始化的强类型文件上下文。";
        return false;
      }
      if (!contextGoo.IsValid)
      {
        error = contextGoo.IsValidWhyNot;
        return false;
      }
      HBRFileContext context = contextGoo.Value;
      if (live == null || !live.HostAvailable)
      {
        error = "Rhino.Inside.Revit 当前没有可用活动文档。";
        return false;
      }
      string documentPath = live.DocumentPath ?? string.Empty;
      if (string.IsNullOrWhiteSpace(documentPath)
        || !Path.IsPathRooted(documentPath)
        || !string.Equals(
          Path.GetExtension(documentPath),
          ".rvt",
          StringComparison.OrdinalIgnoreCase))
      {
        error = "活动 Revit 文档必须是已保存的绝对 RVT 路径。";
        return false;
      }
      string fullDocumentPath = Path.GetFullPath(documentPath);
      if (!File.Exists(fullDocumentPath))
      {
        error = "活动 RVT 路径不存在：" + fullDocumentPath;
        return false;
      }
      if (!string.Equals(
        context.RevitDocumentFingerprint,
        live.DocumentFingerprint,
        StringComparison.OrdinalIgnoreCase))
      {
        error = "文件上下文与当前活动 Revit 文档指纹不一致。";
        return false;
      }
      if (string.IsNullOrWhiteSpace(outputDirectory))
      {
        error = "输出目录必须填写。";
        return false;
      }
      if (!Path.IsPathRooted(outputDirectory))
      {
        error = "输出目录必须是绝对路径。";
        return false;
      }
      string fullOutputDirectory;
      try
      {
        fullOutputDirectory = Path.GetFullPath(outputDirectory);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        error = "输出目录路径无效。";
        return false;
      }
      if (!Directory.Exists(fullOutputDirectory))
      {
        error = "输出目录不存在：" + fullOutputDirectory;
        return false;
      }
      request = new Stage03WorkflowRequest
      {
        Context = context,
        OutputDirectory = outputDirectory,
        RvtStem = Path.GetFileNameWithoutExtension(fullDocumentPath),
        RunId = CreateRunId(),
        DocumentPath = fullDocumentPath,
        PluginVersion = PluginVersion(),
        Mode = mode,
        ForceReason = forceReason ?? string.Empty
      };
      return true;
    }

    private static GH_Structure<GH_String> BuildFieldTree(
      IReadOnlyList<Stage03FieldDetail> details)
    {
      var tree = new GH_Structure<GH_String>();
      foreach (IGrouping<int, Stage03FieldDetail> carrier in
        (details ?? Array.Empty<Stage03FieldDetail>())
          .OrderBy(detail => detail.CarrierIndex)
          .ThenBy(detail => detail.FieldIndex)
          .GroupBy(detail => detail.CarrierIndex))
      {
        var path = new GH_Path(carrier.Key);
        tree.EnsurePath(path);
        foreach (Stage03FieldDetail detail in carrier)
          tree.Append(new GH_String(detail.Text), path);
      }
      return tree;
    }

    private void ScheduleRefresh()
    {
      OnPingDocument()?.ScheduleSolution(
        1,
        document => ExpireSolution(false));
    }

    private static string CreateRunId()
    {
      return "run-" + DateTimeOffset.UtcNow.ToString(
        "yyyyMMddHHmmssfff",
        CultureInfo.InvariantCulture)
        + "-" + Guid.NewGuid().ToString("N");
    }

    private static string PluginVersion()
    {
      Version version = typeof(Stage03ValidationExportComponent)
        .Assembly.GetName().Version;
      return version == null ? "0.9.0" : version.ToString(3);
    }

    private static string Compact(string value, int maximum)
    {
      string text = (value ?? string.Empty)
        .Replace("\r", " ")
        .Replace("\n", " ")
        .Trim();
      return text.Length <= maximum
        ? text
        : text.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>(
        (values ?? Enumerable.Empty<T>()).ToArray());
    }
  }
}
