using System;
using System.Collections.Generic;
using System.Drawing;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRTaskPlanParam : GH_PersistentParam<HBRTaskPlanGoo>
  {
    public HBRTaskPlanParam()
      : base(
        "HBR 任务计划",
        "HBR_TaskPlan",
        "由 02 模型任务与骨架分流输出的强类型任务计划。",
        "湖北BIM报规",
        "报规数据")
    {
    }

    public override Guid ComponentGuid => new Guid("59ed0f7d-9c62-44be-8c3e-724098669a32");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override GH_GetterResult Prompt_Singular(ref HBRTaskPlanGoo value)
    {
      return GH_GetterResult.cancel;
    }

    protected override GH_GetterResult Prompt_Plural(ref List<HBRTaskPlanGoo> values)
    {
      return GH_GetterResult.cancel;
    }
  }
}
