using System;
using System.Collections.Generic;
using System.Drawing;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRStage02PreviewParam
    : GH_PersistentParam<HBRStage02PreviewGoo>
  {
    public HBRStage02PreviewParam()
      : base(
        "HBR Stage02 预览",
        "HBR_Preview",
        "Stage02 构件与属性准备的强类型运行时预览；不持久化业务值。",
        "湖北BIM报规",
        "报规数据")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("c74ce3ec-b9bb-4f06-aacd-56435d3ab4a6");

    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override GH_GetterResult Prompt_Singular(
      ref HBRStage02PreviewGoo value)
    {
      return GH_GetterResult.cancel;
    }

    protected override GH_GetterResult Prompt_Plural(
      ref List<HBRStage02PreviewGoo> values)
    {
      return GH_GetterResult.cancel;
    }
  }
}
