using System;
using System.Drawing;
using Grasshopper.Kernel;
using BIMBaoGui.Stage01.UI;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage01AssemblyInfo : GH_AssemblyInfo
  {
    public override string Name => "BIMBaoGui.Stage01";
    public override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override string Description =>
      "湖北省 BIM 规划报建：文件初始化、模型任务分流与官方插件候选兼容属性写入。";
    public override Guid Id => new Guid("9f7b1bd4-fb6a-4853-b60f-6576193e1601");
    public override string AuthorName => "ArchitectureWorld";
    public override string AuthorContact => "ArchitectureWorld/BIM-baogui";
    public override string Version => "0.7.0";
  }
}
