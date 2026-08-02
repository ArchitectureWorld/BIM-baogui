using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.TaskPlanning;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage02TaskPlanComponent : GH_Component
  {
    private HBRFileContext _context;
    private HBRTaskPlan _plan;
    private Stage02RevitContextSnapshot _snapshot = new Stage02RevitContextSnapshot();
    private IReadOnlyList<string> _blockers = Array.Empty<string>();
    private IReadOnlyList<string> _messages = Array.Empty<string>();
    private string _status = "等待文件上下文";
    private string _previousFileContextHash = string.Empty;

    public Stage02TaskPlanComponent()
      : base(
        "湖北BIM报规｜02 模型任务与骨架分流",
        "任务与骨架分流",
        "读取 HBR_FileContext，根据模型类型、规划目标和项目条件编译 HBR_TaskPlan。",
        "湖北BIM报规",
        "报规工作流")
    {
    }

    public override Guid ComponentGuid => new Guid("c9374518-d5b0-4f9b-898b-5ecf01c94470");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    internal HBRFileContext CurrentContext => _context;
    internal HBRTaskPlan CurrentPlan => _plan;
    internal Stage02RevitContextSnapshot Snapshot => _snapshot;
    internal IReadOnlyList<string> Blockers => _blockers;
    internal IReadOnlyList<string> Messages => _messages;
    internal string Status => _status;

    public override void CreateAttributes()
    {
      m_attributes = new Stage02ComponentAttributes(this);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRFileContextParam(),
        "文件上下文",
        "Context",
        "必须连接 01 文件初始化的“文件上下文”输出。",
        GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRTaskPlanParam(),
        "任务计划",
        "TaskPlan",
        "按当前文件类型与项目条件编译的强类型 HBR_TaskPlan。",
        GH_ParamAccess.item);
      pManager.AddTextParameter("骨架路径", "Path", "总平、单体建筑—地上或单体建筑—地下。", GH_ParamAccess.item);
      pManager.AddTextParameter("激活任务", "Tasks", "当前文件需要执行的建模与检查任务。", GH_ParamAccess.list);
      pManager.AddTextParameter("阻断信息", "Blockers", "上下文、文档和版本检查的阻断原因。", GH_ParamAccess.list);
      pManager.AddTextParameter("任务计划JSON", "JSON", "HBR_TaskPlan 的确定性 JSON，仅用于调试。", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      _context = null;
      _plan = null;
      _snapshot = Stage02RevitContextService.ReadSnapshot();
      var blockers = new List<string>();
      var messages = new List<string>();

      HBRFileContextGoo contextGoo = null;
      if (!dataAccess.GetData(0, ref contextGoo) || contextGoo?.Value == null)
      {
        blockers.Add("请连接 01 文件初始化的“文件上下文”输出。不要连接上下文 JSON 文本。");
      }
      else
      {
        _context = contextGoo.Value;
        blockers.AddRange(TaskPlanCompiler.ValidateContext(_context));
      }

      if (_snapshot.Messages != null)
        blockers.AddRange(_snapshot.Messages);

      bool fingerprintMatches = _context != null
        && _snapshot.HostAvailable
        && string.Equals(
          _context.RevitDocumentFingerprint,
          _snapshot.DocumentFingerprint,
          StringComparison.OrdinalIgnoreCase);
      if (_context != null && _snapshot.HostAvailable && !fingerprintMatches)
      {
        blockers.Add(
          "文件上下文属于“" + _context.RevitDocumentTitle
          + "”，当前活动文件为“" + _snapshot.DocumentTitle
          + "”。请回到对应 Revit 文件，或重新运行 01 文件初始化。");
      }
      if (fingerprintMatches)
      {
        blockers.AddRange(HBRLiveContextPolicy.Validate(
          _context.FileGuid,
          _context.SourcePayloadHash,
          _snapshot.IsInitialized,
          _snapshot.StoredFileGuid,
          _snapshot.StoredPayloadHash,
          _snapshot.StoredWorkflowVersion));
      }

      if (blockers.Count == 0 && _context != null)
      {
        TaskPlanCompilationResult compilation = TaskPlanCompiler.Compile(_context);
        blockers.AddRange(compilation.Blockers);
        if (compilation.Success)
        {
          _plan = compilation.Plan;
          bool changed = !string.IsNullOrWhiteSpace(_previousFileContextHash)
            && !string.Equals(_previousFileContextHash, _context.FileContextHash, StringComparison.OrdinalIgnoreCase);
          _previousFileContextHash = _context.FileContextHash;
          _status = changed ? "上游变化｜任务已重新编译" : "任务编译通过";
          messages.Add(
            _context.ModelFileType + "：已激活 " + _plan.ActiveTasks.Count
            + " 项任务，" + _plan.NotApplicableTasks.Count + " 项不适用。");
          if (changed)
            messages.Add("FileContextHash 已变化；下游既有建模和检查结果应进入待复检状态。");
        }
      }

      _blockers = blockers
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      if (_blockers.Count > 0)
      {
        _status = "任务编译被阻断";
        messages.AddRange(_blockers);
      }
      _messages = messages.Distinct(StringComparer.Ordinal).ToArray();

      dataAccess.SetData(0, _plan == null ? null : new HBRTaskPlanGoo(_plan));
      dataAccess.SetData(1, _plan?.SkeletonPath ?? string.Empty);
      dataAccess.SetDataList(2, _plan == null
        ? Array.Empty<string>()
        : _plan.ActiveTasks.Select(task => task.Sequence.ToString("000") + "｜" + task.Name + "｜" + task.TaskId));
      dataAccess.SetDataList(3, _blockers);
      dataAccess.SetData(4, _plan == null ? string.Empty : HBRTaskPlanCanonicalizer.ToJson(_plan));
    }
  }
}
