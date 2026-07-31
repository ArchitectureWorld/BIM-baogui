using System;
using BIMBaoGui.Stage01.TaskPlanning;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRTaskPlanGoo : GH_Goo<HBRTaskPlan>
  {
    public HBRTaskPlanGoo()
    {
    }

    public HBRTaskPlanGoo(HBRTaskPlan value)
    {
      Value = value;
    }

    public override bool IsValid => Value != null && Value.IsValid;
    public override string TypeName => "HBR Task Plan";
    public override string TypeDescription => "由文件上下文编译得到的报规模型任务计划。";

    public override IGH_Goo Duplicate()
    {
      return new HBRTaskPlanGoo(Value);
    }

    public override string ToString()
    {
      return Value?.ToString() ?? "空 HBR_TaskPlan";
    }

    public override bool CastFrom(object source)
    {
      if (source is HBRTaskPlan plan)
      {
        Value = plan;
        return true;
      }
      if (source is HBRTaskPlanGoo goo)
      {
        Value = goo.Value;
        return true;
      }
      if (source is string json && HBRTaskPlanCanonicalizer.TryParse(json, out HBRTaskPlan parsed, out _))
      {
        Value = parsed;
        return true;
      }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (typeof(Q).IsAssignableFrom(typeof(HBRTaskPlan)))
      {
        object value = Value;
        target = (Q) value;
        return true;
      }
      if (typeof(Q).IsAssignableFrom(typeof(string)))
      {
        object value = Value == null ? string.Empty : HBRTaskPlanCanonicalizer.ToJson(Value);
        target = (Q) value;
        return true;
      }
      return false;
    }

    public override bool Write(GH_IWriter writer)
    {
      if (Value == null) return true;
      writer.SetString("HBR.TaskPlan.Json", HBRTaskPlanCanonicalizer.ToJson(Value));
      return true;
    }

    public override bool Read(GH_IReader reader)
    {
      if (!reader.ItemExists("HBR.TaskPlan.Json"))
      {
        Value = null;
        return true;
      }
      string json = reader.GetString("HBR.TaskPlan.Json");
      if (!HBRTaskPlanCanonicalizer.TryParse(json, out HBRTaskPlan plan, out _))
      {
        Value = null;
        return false;
      }
      Value = plan;
      return true;
    }
  }
}
