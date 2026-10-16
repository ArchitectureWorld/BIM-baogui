using System.Globalization;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    public EditPlan BuildEditPlan(CheckResult basis, ParameterValue[] values, EditDraft[] drafts, bool includeSuggestions, DateTime nowUtc)
    {
        if (basis == null || !basis.Complete || basis.Context.RuleVersion != RuleVersion || basis.CaptureHash != Canonical.ValuesHash(values))
            throw new InvalidOperationException("检查尚未完成或字段快照已改变。");
        var rebase = DraftRebaser.Rebase(drafts, basis.Fields);
        if (rebase.Conflicts.Length > 0 || rebase.InvalidTargets.Length > 0) throw new InvalidOperationException("人工草稿与当前模型存在冲突，请先确认原值、现值和拟写值。");
        var changes = new List<WriteChange>();
        foreach (var field in basis.Fields)
        {
            var draft = drafts.SingleOrDefault(d => d.FieldKey == field.Key);
            if (draft != null && !field.Editable) throw new InvalidOperationException(field.ElementId + " / " + field.Property + "：来源有歧义、只读或载体不可写。");
            if (!field.Editable || draft == null && !includeSuggestions) continue;
            var current = values.Where(v => v.OwnerUniqueId == field.OwnerUniqueId && v.Source == field.Source).ToArray();
            if (current.Length != 1) continue;
            var value = draft != null ? Normalize(field.RawType, draft.Value) : field.SuggestedValue;
            if (draft == null && current[0].HasValue && !string.IsNullOrEmpty(current[0].RawValue)) continue;
            if (draft == null && current[0].Exists && value == null) continue;
            if (draft != null && current[0].Exists && current[0].HasValue && value == current[0].RawValue) continue;
            changes.Add(new WriteChange { FieldKey = field.Key, OwnerId = field.OwnerId, OwnerUniqueId = field.OwnerUniqueId, Source = field.Source,
                SourceGuid = field.SourceGuid, Origin = field.Binding == "类型" ? "type" : "instance", PropertySet = field.PropertySet, RawType = field.RawType,
                ExpectedSignature = field.SourceSignature, ExpectedValue = current[0].RawValue, Value = value,
                Unit = draft != null ? current[0].Unit : field.SuggestionUnit ?? current[0].Unit,
                Action = !current[0].Exists ? "Create" : value == "" ? "Clear" : "Set", Reason = draft != null ? "人工修改" : field.Detail,
                AffectedInstances = field.AffectedInstances, Manual = draft != null });
        }
        var unique = new List<WriteChange>();
        foreach (var group in changes.GroupBy(c => c.OwnerUniqueId + "|" + c.Source))
        {
            if (group.Select(c => c.Value).Distinct().Count() > 1) throw new InvalidOperationException("同一来源有多个不同拟写值，请核对类型影响范围。");
            unique.Add(group.First());
        }
        var plan = new EditPlan { Context = basis.Context, Id = Guid.NewGuid().ToString("N"), CaptureHash = basis.CaptureHash,
            ExpiresAtUtc = nowUtc.AddSeconds(120), Changes = unique.ToArray() };
        plan.Hash = Canonical.PlanHash(plan);
        return plan;
    }
    private static string Normalize(string rawType, string value)
    {
        if (value == null) throw new InvalidOperationException("拟写值缺失；清空请提供空字符串。");
        if (value == "" || rawType == "Label" || rawType == "Text") return value;
        if (rawType == "Boolean")
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "是") return "1";
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "否") return "0";
        }
        if (rawType == "Integer" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if (rawType == "Real" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) return number.ToString("R", CultureInfo.InvariantCulture);
        throw new InvalidOperationException("拟写值不符合字段类型：" + rawType);
    }
}
