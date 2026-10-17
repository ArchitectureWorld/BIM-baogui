using System.Globalization;
using Hifc.Reference;
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
        foreach (var result in basis.Elements.Where(e => !string.IsNullOrEmpty(e.Element.AssignedSlot) && e.Element.AssignedSlot != e.Element.DeclaredSlot))
        {
            var element = result.Element;
            var original = values.Where(v => v.OwnerUniqueId == element.UniqueId && v.Source == "类目" && v.Origin == "instance").ToArray();
            if (original.Length != 1 || original[0].ReadOnly || original[0].Exists && original[0].StorageType != "String") throw new InvalidOperationException("类目来源有歧义或不可写：" + element.Id);
            changes.Add(new WriteChange { FieldKey = element.UniqueId + "|classification", OwnerId = element.Id, OwnerUniqueId = element.UniqueId,
                Source = "类目", SourceGuid = ReferencePolicy.SourceGuid("类目").ToString("D"), Origin = "instance", RawType = "Text",
                ExpectedSignature = Canonical.ValuesHash(original), ExpectedValue = original[0].RawValue, Value = element.AssignedSlot,
                Action = original[0].Exists ? "Set" : "Create", Reason = "人工选择分类", Manual = true, AffectedInstances = 1 });
            var rules = CaseWorkflow.ForModel(basis.Stage, basis.ModelKind);
            var retained = rules.Where(r => r.Binding == "实例" && r.Slot == element.AssignedSlot).SelectMany(r => r.Properties).Select(p => p.SourceParameter).ToHashSet(StringComparer.Ordinal);
            var oldSources = rules.Where(r => r.Binding == "实例" && r.Slot == element.DeclaredSlot).SelectMany(r => r.Properties)
                .Select(p => p.SourceParameter).Where(s => s.StartsWith(element.DeclaredSlot, StringComparison.Ordinal) && !retained.Contains(s)).Distinct();
            foreach (var source in oldSources)
            {
                var old = values.Where(v => v.OwnerUniqueId == element.UniqueId && v.Source == source && v.Origin == "instance").ToArray();
                if (old.Length == 0 || old.Length == 1 && (!old[0].Exists || !old[0].HasValue || string.IsNullOrEmpty(old[0].RawValue))) continue;
                if (old.Length != 1 || old[0].ReadOnly || old[0].StorageType != "String") throw new InvalidOperationException("旧分类来源无法安全清理，请先在模型中核对：" + source);
                changes.Add(new WriteChange { FieldKey = element.UniqueId + "|cleanup|" + source, OwnerId = element.Id, OwnerUniqueId = element.UniqueId,
                    Source = source, Origin = "instance", RawType = "Text", ExpectedSignature = Canonical.ValuesHash(old), ExpectedValue = old[0].RawValue,
                    Value = "", Action = "Clear", Reason = "清理旧分类的实例值；保留参数定义与类型值", Manual = true, AffectedInstances = 1 });
            }
        }
        foreach (var field in basis.Fields)
        {
            var draft = drafts.SingleOrDefault(d => d.FieldKey == field.Key);
            if (draft != null && !field.Editable) throw new InvalidOperationException(field.ElementId + " / " + field.Property + "：来源有歧义、只读或载体不可写。");
            var current = values.Where(v => v.OwnerUniqueId == field.OwnerUniqueId && v.Source == field.Source).ToArray();
            if (current.Length != 1) continue;
            var changingClassification = field.Binding == "实例" && basis.Elements.Any(e => e.Element.UniqueId == field.ElementUniqueId && !string.IsNullOrEmpty(e.Element.AssignedSlot) && e.Element.AssignedSlot != e.Element.DeclaredSlot);
            if (!field.Editable || draft == null && !includeSuggestions && !(changingClassification && !current[0].Exists)) continue;
            var value = draft != null ? Normalize(field.RawType, draft.Value) : includeSuggestions ? field.SuggestedValue : null;
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
