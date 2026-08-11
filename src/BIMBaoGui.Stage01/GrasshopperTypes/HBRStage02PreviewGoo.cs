using System;
using BIMBaoGui.Stage01.Stage02;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRStage02PreviewGoo : GH_Goo<Stage02Preview>
  {
    public HBRStage02PreviewGoo()
    {
    }

    public HBRStage02PreviewGoo(Stage02Preview value)
    {
      Value = value;
    }

    public override bool IsValid => Value != null
      && !string.IsNullOrWhiteSpace(Value.PreviewHash);

    public override string IsValidWhyNot =>
      IsValid ? string.Empty : "Stage02 预览为空或缺少预览哈希。";

    public override string TypeName => "HBR Stage02 Preview";

    public override string TypeDescription =>
      "仅在当前 Grasshopper 运行期间传递的 Stage02 强类型预览；不会写入 GH 文档。";

    public override IGH_Goo Duplicate()
    {
      return new HBRStage02PreviewGoo(Value);
    }

    public override string ToString()
    {
      if (Value == null) return "空 Stage02 预览";
      string hash = Value.PreviewHash ?? string.Empty;
      if (hash.Length > 12) hash = hash.Substring(0, 12) + "…";
      return "Stage02 预览｜"
        + Value.Elements.Count
        + " 个载体｜"
        + hash;
    }

    public override bool CastFrom(object source)
    {
      if (source is Stage02Preview preview)
      {
        Value = preview;
        return true;
      }
      if (source is HBRStage02PreviewGoo goo)
      {
        Value = goo.Value;
        return true;
      }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (typeof(Q).IsAssignableFrom(typeof(Stage02Preview)))
      {
        object preview = Value;
        target = (Q) preview;
        return true;
      }
      if (typeof(Q).IsAssignableFrom(typeof(string)))
      {
        object summary = ToString();
        target = (Q) summary;
        return true;
      }
      return false;
    }
  }
}
