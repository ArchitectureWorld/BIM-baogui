using System;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.TaskPlanning;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRTaskPlanGoo : GH_Goo<HBRTaskPlan>
  {
    private string _invalidReason = string.Empty;
    private string _invalidJson = string.Empty;

    public HBRTaskPlanGoo()
    {
    }

    public HBRTaskPlanGoo(HBRTaskPlan value)
    {
      Value = value;
    }

    public override bool IsValid => Value != null
      && Value.IsValid
      && MatchesCurrentRulePackage(Value)
      && HasValidHash(Value);
    public override string IsValidWhyNot
    {
      get
      {
        if (IsValid) return string.Empty;
        if (!string.IsNullOrWhiteSpace(_invalidReason)) return _invalidReason;
        if (Value != null && Value.IsValid)
        {
          if (!MatchesCurrentRulePackage(Value))
            return "规则数据库已升级，请重新运行任务规划。";
          if (!HasValidHash(Value))
            return "任务计划哈希无效，数据损坏。";
        }
        return "HBR 任务计划为空或无效。";
      }
    }
    public override string TypeName => "HBR Task Plan";
    public override string TypeDescription => "由文件上下文编译得到的报规模型任务计划。";

    public override IGH_Goo Duplicate()
    {
      return new HBRTaskPlanGoo(Value)
      {
        _invalidReason = _invalidReason,
        _invalidJson = _invalidJson
      };
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
        _invalidReason = string.Empty;
        _invalidJson = string.Empty;
        return true;
      }
      if (source is HBRTaskPlanGoo goo)
      {
        Value = goo.Value;
        _invalidReason = goo._invalidReason;
        _invalidJson = goo._invalidJson;
        return true;
      }
      if (source is string json)
      {
        if (HBRTaskPlanCanonicalizer.TryParse(
          json,
          out HBRTaskPlan parsed,
          out string error))
        {
          Value = parsed;
          _invalidReason = string.Empty;
          _invalidJson = string.Empty;
          return true;
        }
        Value = null;
        _invalidReason = error;
        _invalidJson = HBRTaskPlanCanonicalizer.IsLegacyUpgradeError(error)
          ? json
          : string.Empty;
        return HBRTaskPlanCanonicalizer.IsLegacyUpgradeError(error);
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
      if (Value != null)
      {
        writer.SetString(
          "HBR.TaskPlan.Json",
          HBRTaskPlanCanonicalizer.ToJson(Value));
      }
      else if (!string.IsNullOrWhiteSpace(_invalidJson))
      {
        writer.SetString("HBR.TaskPlan.Json", _invalidJson);
      }
      return true;
    }

    public override bool Read(GH_IReader reader)
    {
      if (!reader.ItemExists("HBR.TaskPlan.Json"))
      {
        Value = null;
        _invalidReason = string.Empty;
        _invalidJson = string.Empty;
        return true;
      }
      string json = reader.GetString("HBR.TaskPlan.Json");
      if (!HBRTaskPlanCanonicalizer.TryParse(
        json,
        out HBRTaskPlan plan,
        out string error))
      {
        Value = null;
        _invalidReason = error;
        if (HBRTaskPlanCanonicalizer.IsLegacyUpgradeError(error))
        {
          _invalidJson = json;
          return true;
        }
        _invalidJson = string.Empty;
        return false;
      }
      Value = plan;
      _invalidReason = string.Empty;
      _invalidJson = string.Empty;
      return true;
    }

    private static bool MatchesCurrentRulePackage(HBRTaskPlan plan)
    {
      if (plan == null) return false;
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      return string.Equals(
          plan.RulePackageId,
          package.PackageId,
          StringComparison.Ordinal)
        && string.Equals(
          plan.RulePackageVersion,
          package.PackageVersion,
          StringComparison.Ordinal)
        && string.Equals(
          plan.RulePackageSha256,
          package.RulePackageSha256,
          StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidHash(HBRTaskPlan plan)
    {
      return plan != null
        && string.Equals(
          plan.TaskPlanHash,
          HBRTaskPlanCanonicalizer.ComputeHash(plan),
          StringComparison.OrdinalIgnoreCase);
    }
  }
}
