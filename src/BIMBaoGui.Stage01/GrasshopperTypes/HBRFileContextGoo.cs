using System;
using BIMBaoGui.Stage01.Context;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRFileContextGoo : GH_Goo<HBRFileContext>
  {
    public HBRFileContextGoo()
    {
    }

    public HBRFileContextGoo(HBRFileContext value)
    {
      Value = value;
    }

    public override bool IsValid => Value != null
      && !string.IsNullOrWhiteSpace(Value.FileGuid)
      && !string.IsNullOrWhiteSpace(Value.FileContextHash);

    public override string TypeName => "HBR File Context";
    public override string TypeDescription => "湖北 BIM 报规单文件上下文，包含文件身份、模型类型、规划目标、项目条件及上下文哈希。";

    public override IGH_Goo Duplicate()
    {
      return new HBRFileContextGoo(Value);
    }

    public override string ToString()
    {
      return Value?.ToString() ?? "空 HBR_FileContext";
    }

    public override bool CastFrom(object source)
    {
      if (source is HBRFileContext context)
      {
        Value = context;
        return true;
      }
      if (source is HBRFileContextGoo goo)
      {
        Value = goo.Value;
        return true;
      }
      if (source is string json && HBRFileContextCanonicalizer.TryParse(json, out HBRFileContext parsed, out _))
      {
        Value = parsed;
        return true;
      }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (typeof(Q).IsAssignableFrom(typeof(HBRFileContext)))
      {
        object value = Value;
        target = (Q) value;
        return true;
      }
      if (typeof(Q).IsAssignableFrom(typeof(string)))
      {
        object value = Value == null ? string.Empty : HBRFileContextCanonicalizer.ToJson(Value);
        target = (Q) value;
        return true;
      }
      return false;
    }

    public override bool Write(GH_IWriter writer)
    {
      if (Value == null) return true;
      writer.SetString("HBR.FileContext.Json", HBRFileContextCanonicalizer.ToJson(Value));
      return true;
    }

    public override bool Read(GH_IReader reader)
    {
      if (!reader.ItemExists("HBR.FileContext.Json"))
      {
        Value = null;
        return true;
      }
      string json = reader.GetString("HBR.FileContext.Json");
      if (!HBRFileContextCanonicalizer.TryParse(json, out HBRFileContext context, out _))
      {
        Value = null;
        return false;
      }
      Value = context;
      return true;
    }
  }
}
