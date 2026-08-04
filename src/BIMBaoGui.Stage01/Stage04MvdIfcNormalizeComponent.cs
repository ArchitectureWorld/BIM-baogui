using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Mvd;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage04MvdIfcNormalizeComponent : GH_Component
  {
    private readonly ExplicitExecutionGate _executionGate =
      new ExplicitExecutionGate();
    private readonly object _stateLock = new object();
    private bool _pending;
    private MvdIfcComponentResult _result = new MvdIfcComponentResult
    {
      Success = false,
      Status = "等待执行",
      OutputPath = string.Empty,
      Messages = Array.Empty<string>()
    };

    public Stage04MvdIfcNormalizeComponent()
      : base(
        "湖北BIM报规｜04 MVD IFC规范化",
        "MVD规范化",
        "读取官方导出的 IFC4，保留几何并按官方示例名称和 MVD 数据类型"
        + "生成新的 -MVD.ifc。源文件不会被覆盖，也不会创建备份文件。",
        "湖北BIM报规",
        "报规工作流")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("b43c4b26-80dc-4bb5-9171-5e2387bc7da2");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "执行",
        "Run",
        "从 false 切换为 true 时执行一次规范化。",
        GH_ParamAccess.item,
        false);
      pManager.AddTextParameter(
        "源IFC",
        "Source",
        "官方 H-IFC 导出的 IFC4 文件绝对路径。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "输出IFC",
        "Destination",
        "可选。为空时在源文件旁生成 <源文件名>-MVD.ifc。"
        + "已存在文件不会被覆盖。",
        GH_ParamAccess.item,
        string.Empty);
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "成功",
        "Success",
        "规范化写出和回读验收全部通过时为 true。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "状态",
        "Status",
        "当前执行状态。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "输出IFC",
        "Output",
        "成功生成的 MVD IFC 绝对路径。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "消息",
        "Messages",
        "规范化计数、哈希、验收结果或失败报告位置。",
        GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      bool execute = false;
      string source = string.Empty;
      string destination = string.Empty;
      dataAccess.GetData(0, ref execute);
      dataAccess.GetData(1, ref source);
      dataAccess.GetData(2, ref destination);

      bool start;
      lock (_stateLock)
      {
        start = _executionGate.Observe(execute) && !_pending;
        if (start)
        {
          _pending = true;
          _result = new MvdIfcComponentResult
          {
            Success = false,
            Status = "执行中",
            OutputPath = string.Empty,
            Messages = new[] { "正在解析、规范化并回读 IFC。" }
          };
        }
      }

      if (start)
      {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        Task.Run(() => new MvdIfcNormalizationCoordinator(assemblyPath).Execute(
          source,
          destination)).ContinueWith(task =>
          {
            MvdIfcComponentResult completed;
            if (task.Status == TaskStatus.RanToCompletion)
            {
              completed = task.Result;
            }
            else
            {
              Exception exception = task.Exception?.GetBaseException()
                ?? new InvalidOperationException("Stage04 后台任务未返回结果。");
              completed = new MvdIfcComponentResult
              {
                Success = false,
                Status = "MVD IFC 规范化失败",
                OutputPath = string.Empty,
                Messages = new[] { exception.Message }
              };
            }

            lock (_stateLock)
            {
              _result = completed;
              _pending = false;
            }
            OnPingDocument()?.ScheduleSolution(
              1,
              document => ExpireSolution(false));
          }, TaskScheduler.Default);
      }

      MvdIfcComponentResult snapshot;
      bool pending;
      lock (_stateLock)
      {
        snapshot = _result;
        pending = _pending;
      }

      Message = pending
        ? "执行中"
        : (snapshot.Success ? "MVD通过" : snapshot.Status);
      dataAccess.SetData(0, snapshot.Success);
      dataAccess.SetData(1, pending ? "执行中" : snapshot.Status);
      dataAccess.SetData(2, snapshot.OutputPath ?? string.Empty);
      dataAccess.SetDataList(3, snapshot.Messages ?? Array.Empty<string>());
    }
  }
}
