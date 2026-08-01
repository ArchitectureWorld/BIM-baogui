using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage03OfficialHifcWriteComponent : GH_Component
  {
    private bool _pending;
    private bool _lastExecute;
    private OfficialHifcWriteResult _result = new OfficialHifcWriteResult
    {
      Success = false,
      OfficialCompatibilityVerified = false,
      Status = "等待执行",
      Messages = Array.Empty<string>()
    };

    public Stage03OfficialHifcWriteComponent()
      : base(
        "湖北BIM报规｜03 官方插件兼容属性写入",
        "官方兼容写入",
        "根据已提取规则向正确 Revit 对象写入候选兼容参数并回读。"
        + "最终是否兼容，必须由官方 H-IFC 插件导出并经官方检查软件确认。",
        "湖北BIM报规",
        "报规工作流")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("9a4b9171-0ab0-4d25-a840-79ba9bc8549e");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "执行",
        "Run",
        "从 false 切换为 true 时提交一次写入。",
        GH_ParamAccess.item,
        false);
      pManager.AddIntegerParameter(
        "元素Id",
        "Ids",
        "目标 Revit ElementId。留空仅适用于 IfcProject/IfcBuilding；"
        + "楼层、房间等属性必须提供对应对象 Id。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "属性",
        "Properties",
        "propertyId、参数 GUID 或完整参数名。",
        GH_ParamAccess.list);
      pManager.AddTextParameter(
        "值",
        "Values",
        "值列表。单个值会广播；否则数量需与属性一致。"
        + "长度按 m、面积按 m²、体积按 m³、角度按 °。",
        GH_ParamAccess.list);
      pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "成功",
        "Success",
        "最近一次 Revit 写入和回读是否成功；不代表官方导出已通过。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "状态",
        "Status",
        "当前写入与官方验收状态。",
        GH_ParamAccess.item);
      pManager.AddTextParameter(
        "消息",
        "Messages",
        "规则、对象、绑定、写入、回读和官方验收诊断。",
        GH_ParamAccess.list);
      pManager.AddIntegerParameter(
        "写入数量",
        "Count",
        "成功写入并回读的参数值数量。",
        GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      bool execute = false;
      var elementIds = new List<int>();
      var properties = new List<string>();
      var values = new List<string>();
      dataAccess.GetData(0, ref execute);
      dataAccess.GetDataList(1, elementIds);
      dataAccess.GetDataList(2, properties);
      dataAccess.GetDataList(3, values);

      bool risingEdge = execute && !_lastExecute;
      _lastExecute = execute;
      if (risingEdge && !_pending)
      {
        if (properties.Count == 0 || values.Count == 0)
        {
          _result = new OfficialHifcWriteResult
          {
            Success = false,
            OfficialCompatibilityVerified = false,
            Status = "输入不完整",
            Messages = new[] { "属性和值不能为空。" }
          };
        }
        else
        {
          var request = new OfficialHifcWriteRequest
          {
            ElementIds = elementIds.ToArray(),
            PropertyKeys = properties
              .Select(value => value?.Trim() ?? string.Empty)
              .ToArray(),
            Values = values.ToArray()
          };
          _pending = true;
          _result = new OfficialHifcWriteResult
          {
            Success = false,
            OfficialCompatibilityVerified = false,
            Status = "等待 Revit 执行",
            Messages = new[] { "写入请求已进入 Revit ExternalEvent 队列。" }
          };

          if (!OfficialHifcWriteService.Enqueue(request, completed =>
          {
            _pending = false;
            _result = completed ?? new OfficialHifcWriteResult
            {
              Success = false,
              OfficialCompatibilityVerified = false,
              Status = "写入失败",
              Messages = new[] { "Revit 写入未返回结果。" }
            };
            OnPingDocument()?.ScheduleSolution(
              1,
              document => ExpireSolution(false));
          }, out string error))
          {
            _pending = false;
            _result = new OfficialHifcWriteResult
            {
              Success = false,
              OfficialCompatibilityVerified = false,
              Status = "无法提交写入",
              Messages = new[] { error }
            };
          }
        }
      }

      Message = _pending
        ? "执行中"
        : (_result.Success ? "Revit已写｜待官方验收" : _result.Status);
      dataAccess.SetData(0, _result.Success);
      dataAccess.SetData(
        1,
        _pending ? "等待 Revit 执行" : _result.Status);
      dataAccess.SetDataList(
        2,
        _result.Messages ?? Array.Empty<string>());
      dataAccess.SetData(3, _result.WriteCount);
    }
  }
}
