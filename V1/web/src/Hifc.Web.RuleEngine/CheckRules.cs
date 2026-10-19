using System.Globalization;
using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    private static ParameterRead Read(ParameterValue value)
    {
        object raw = value.RawValue;
        if (value.HasValue && value.StorageType == "Integer") raw = int.Parse(value.RawValue, CultureInfo.InvariantCulture);
        if (value.HasValue && value.StorageType == "Double") raw = double.Parse(value.RawValue, CultureInfo.InvariantCulture);
        return new ParameterRead { Name = value.Source, Origin = value.Origin, HasValue = value.HasValue, Value = value.HasValue ? raw : null,
            StorageType = value.StorageType, ParameterGuid = value.SharedGuid, DisplayValue = value.DisplayValue, ReadOnly = value.ReadOnly };
    }
    public CheckResult Evaluate(InventoryPage[] inventory, CapturePlan plan, CapturePage[] pages)
    {
        ProtocolGuard.InventoryPages(inventory); ProtocolGuard.CapturePages(pages, plan.Targets);
        ProtocolGuard.Match(inventory[0].Context, plan.Context); ProtocolGuard.Match(plan.Context, pages[0].Context);
        if (plan.Context.RuleVersion != RuleVersion) throw new InvalidOperationException("规则版本已更新，请重新全面检查。");
        var values = pages.SelectMany(p => p.Values).ToArray();
        var rules = CaseWorkflow.ForModel("报规", plan.ModelKind);
        var results = new List<ElementResult>(); var fields = new List<FieldResult>();
        var reviewCount = 0;
        var elements = inventory.SelectMany(p => p.Elements).ToArray();
        foreach (var element in elements)
        {
            var row = Row(element);
            var invalidArea = CaseWorkflow.AreaSlots.Contains(row.Slot) && element.AreaScheme == null;
            if (invalidArea) row.CoverageNote = "该报规区域类目需要面积（Area）载体，请核对模型；不将其他构件强制改为 IfcSpace。";
            var matches = invalidArea ? Array.Empty<CaseRule>() : rules.Where(r => r.Entities.Contains(row.Entity)).ToArray();
            row.SuggestedSlots = matches.Where(r => r.Binding == "实例" && r.Properties.Any(p => p.SourceParameter.StartsWith(r.Slot, StringComparison.Ordinal)
                && values.Any(v => v.OwnerUniqueId == element.UniqueId && v.Source == p.SourceParameter && v.Exists && v.HasValue && !string.IsNullOrWhiteSpace(v.RawValue))))
                .Select(r => r.Slot).Distinct().ToArray();
            var applicable = new List<CaseRule>();
            var info = row.Entity == "IfcBuilding" || row.Entity == "IfcBuildingStorey";
            if (info) applicable.AddRange(matches);
            else if (!string.IsNullOrWhiteSpace(row.Slot) && row.Slot != "总平构件") applicable.AddRange(matches.Where(r => r.Binding == "实例" && r.Slot == row.Slot));
            else
            {
                row.Candidates = CaseWorkflow.GetCandidates(row, plan.ModelKind);
                if (row.Candidates.Length == 1 && row.Candidates[0].CanPrepare)
                    applicable.AddRange(matches.Where(r => r.Binding == "实例" && r.Slot == row.Candidates[0].Slot));
            }
            applicable.AddRange(matches.Where(r => r.Binding == "类型" && (r.Required || r.Slot == "窗" && row.Slot == "窗")));
            applicable = applicable.Distinct().ToList();
            row.Slots = applicable.Where(r => r.Binding == "类型" || info || r.Slot == row.Slot).Select(r => r.Slot).Distinct().ToArray();
            row.Classified = row.Slots.Length > 0;
            row.InventoryOnly = applicable.Count == 0 && row.Candidates.Length == 0 && string.IsNullOrEmpty(row.CoverageNote);
            if (!invalidArea && !string.IsNullOrWhiteSpace(row.Slot) && row.Slot != "总平构件" && !matches.Any(r => r.Slot == row.Slot)) row.CoverageNote = "已有类目与当前案例/载体不匹配：" + row.Slot;
            foreach (var rule in applicable)
                foreach (var property in rule.Properties)
                {
                    var ownerValues = values.Where(v => v.Source == property.SourceParameter && (v.OwnerUniqueId == element.UniqueId && rule.Binding == "实例" || v.OwnerUniqueId == element.TypeUniqueId)).ToArray();
                    var checkedField = ModelChecks.Evaluate(property, ownerValues.Where(v => v.Exists).Select(Read).ToArray());
                    var typeOwner = checkedField.Origin == "type" || rule.Binding == "类型";
                    var owner = typeOwner ? element.TypeUniqueId : element.UniqueId;
                    var ownerId = typeOwner ? element.TypeId : element.Id;
                    var current = ownerValues.Where(v => v.OwnerUniqueId == owner).ToArray();
                    var area = element.AreaScheme == null ? element.ProjectedAreaM2
                        : string.IsNullOrEmpty(row.CoverageNote) && element.NativeAreaM2 > 0 ? element.NativeAreaM2 : null;
                    AutoFillPolicy.PopulateWithSource(checkedField, rule.Slot, element.Name, area,
                        element.AreaScheme == null ? null : "Revit 面积（Area）的实际面积，转换为 m²；名称和所属楼层取当前对象");
                    if ((checkedField.State == "缺参数" || checkedField.State == "未填写") && row.Entity == "IfcBuildingStorey" && property.Property == "楼层名称")
                        checkedField.SuggestedValue = element.Name;
                    if ((checkedField.State == "缺参数" || checkedField.State == "未填写") && row.Entity == "IfcBuilding" && property.Property == "建筑名称" && !string.IsNullOrWhiteSpace(element.Building))
                        checkedField.SuggestedValue = element.Building;
                    var editable = !string.IsNullOrEmpty(owner) && current.Length == 1 && (!current[0].Exists || !current[0].ReadOnly && current[0].StorageType != "ElementId") && checkedField.State != "有歧义";
                    fields.Add(new FieldResult { Key = element.UniqueId + "|" + property.PropertySet + "|" + rule.Binding + "|" + property.Property,
                        ElementId = element.Id, ElementUniqueId = element.UniqueId, ElementName = element.Name, OwnerId = ownerId, OwnerUniqueId = owner,
                        Slot = rule.Slot, PropertySet = property.PropertySet, Property = property.Property, Binding = typeOwner ? "类型" : "实例",
                        Source = property.SourceParameter, SourceGuid = ReferencePolicy.SourceGuid(property.SourceParameter).ToString("D"), RawType = property.ExportType,
                        State = checkedField.State, Value = checkedField.Value, DisplayValue = checkedField.DisplayValue, SuggestedValue = checkedField.SuggestedValue,
                        SuggestionUnit = checkedField.SuggestionUnit ?? current.FirstOrDefault()?.Unit, Detail = checkedField.SuggestionReason ?? checkedField.Detail,
                        Editable = editable, AllowedEmpty = rule.EmptyAllowed.Contains(property.Property), ManualInputRequired = checkedField.ManualInputRequired,
                        SourceSignature = Canonical.ValuesHash(current), AffectedInstances = typeOwner ? elements.Count(e => e.TypeUniqueId == owner) : 1 });
                }
            results.Add(new ElementResult { Element = element, Slots = row.Slots, CandidateSlots = row.Candidates.Select(c => c.Slot).ToArray(),
                CandidateEvidence = row.Candidates.Select(c => c.Evidence).ToArray(), Status = row.StatusText });
            if (row.Candidates.Length > 0 || !string.IsNullOrEmpty(row.CoverageNote)) reviewCount++;
        }
        return new CheckResult { Context = plan.Context, Stage = "报规", ModelKind = plan.ModelKind, Complete = true, CaptureHash = Canonical.ValuesHash(values),
            ServerTimeUtc = DateTime.UtcNow, Elements = results.ToArray(), Fields = fields.ToArray(), AvailableSlots = rules.Where(r => r.Binding == "实例").Select(r => r.Slot).Distinct().ToArray(),
            ReviewCount = reviewCount };
    }
}
