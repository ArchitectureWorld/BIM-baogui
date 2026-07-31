using System;
using System.Collections.Generic;
using System.Drawing;
using BIMBaoGui.Stage01.UI;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRFileContextParam : GH_PersistentParam<HBRFileContextGoo>
  {
    public HBRFileContextParam()
      : base(
        "HBR 文件上下文",
        "HBR_Context",
        "由 01 文件初始化输出的强类型报规文件上下文。",
        "湖北BIM报规",
        "报规数据")
    {
    }

    public override Guid ComponentGuid => new Guid("5c66a9d1-5a02-4ab4-9ec1-1d65cccf18d1");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override GH_GetterResult Prompt_Singular(ref HBRFileContextGoo value)
    {
      return GH_GetterResult.cancel;
    }

    protected override GH_GetterResult Prompt_Plural(ref List<HBRFileContextGoo> values)
    {
      return GH_GetterResult.cancel;
    }
  }
}
