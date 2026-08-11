using System;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BIMBaoGui.Stage01.GrasshopperTypes
{
  public sealed class HBRFileContextGoo : GH_Goo<HBRFileContext>
  {
    private string _invalidReason = string.Empty;
    private string _invalidJson = string.Empty;

    public HBRFileContextGoo()
    {
    }

    public HBRFileContextGoo(HBRFileContext value)
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
            return "规则数据库已升级，请重新运行 Stage01。";
          if (!HasValidHash(Value))
            return "文件上下文哈希无效，数据损坏。";
        }
        return "HBR 文件上下文为空或无效。";
      }
    }

    public override string TypeName => "HBR File Context";
    public override string TypeDescription => "湖北 BIM 报规单文件上下文，包含文件身份、模型类型、规划目标、项目条件及上下文哈希。";

    public override IGH_Goo Duplicate()
    {
      return new HBRFileContextGoo(Value)
      {
        _invalidReason = _invalidReason,
        _invalidJson = _invalidJson
      };
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
        _invalidReason = string.Empty;
        _invalidJson = string.Empty;
        return true;
      }
      if (source is HBRFileContextGoo goo)
      {
        Value = goo.Value;
        _invalidReason = goo._invalidReason;
        _invalidJson = goo._invalidJson;
        return true;
      }
      if (source is string json)
      {
        if (HBRFileContextCanonicalizer.TryParse(
          json,
          out HBRFileContext parsed,
          out string error))
        {
          Value = parsed;
          _invalidReason = string.Empty;
          _invalidJson = string.Empty;
          return true;
        }
        Value = null;
        _invalidReason = error;
        _invalidJson = HBRFileContextCanonicalizer.IsLegacyUpgradeError(error)
          ? json
          : string.Empty;
        return HBRFileContextCanonicalizer.IsLegacyUpgradeError(error);
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
      if (Value != null)
      {
        writer.SetString(
          "HBR.FileContext.Json",
          HBRFileContextCanonicalizer.ToJson(Value));
      }
      else if (!string.IsNullOrWhiteSpace(_invalidJson))
      {
        writer.SetString("HBR.FileContext.Json", _invalidJson);
      }
      return true;
    }

    public override bool Read(GH_IReader reader)
    {
      if (!reader.ItemExists("HBR.FileContext.Json"))
      {
        Value = null;
        _invalidReason = string.Empty;
        _invalidJson = string.Empty;
        return true;
      }
      string json = reader.GetString("HBR.FileContext.Json");
      if (!HBRFileContextCanonicalizer.TryParse(
        json,
        out HBRFileContext context,
        out string error))
      {
        Value = null;
        _invalidReason = error;
        if (HBRFileContextCanonicalizer.IsLegacyUpgradeError(error))
        {
          _invalidJson = json;
          return true;
        }
        _invalidJson = string.Empty;
        return false;
      }
      Value = context;
      _invalidReason = string.Empty;
      _invalidJson = string.Empty;
      return true;
    }

    private static bool MatchesCurrentRulePackage(HBRFileContext context)
    {
      if (context == null) return false;
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      return string.Equals(
          context.RulePackageId,
          package.PackageId,
          StringComparison.Ordinal)
        && string.Equals(
          context.RulePackageVersion,
          package.PackageVersion,
          StringComparison.Ordinal)
        && string.Equals(
          context.RulePackageSha256,
          package.RulePackageSha256,
          StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidHash(HBRFileContext context)
    {
      return context != null
        && string.Equals(
          context.FileContextHash,
          HBRFileContextCanonicalizer.ComputeHash(context),
          StringComparison.OrdinalIgnoreCase);
    }
  }
}
