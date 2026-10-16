using System;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class FieldCheck
    {
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string Source { get; set; }
        public string RawType { get; set; }
        public string State { get; set; }
        public string Value { get; set; }
        public string DisplayValue { get; set; }
        public string Origin { get; set; }
        public string OriginLabel => Origin == "instance" ? "实例" : Origin == "type" ? "类型" : "";
        public string Detail { get; set; }
        public bool Editable { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string SourceSignature { get; set; }
        public string Binding { get; set; }
        public bool AllowedEmpty { get; set; }
        public string SuggestedValue { get; set; }
        public string SuggestionUnit { get; set; }
        public string SuggestionReason { get; set; }
        public bool Acceptable => Valid || AllowedEmpty && State == "未填写";
        public bool Valid => State == "有效";
        public bool ManualInputRequired => PropertySet == "Pset_道路边界线信息属性集" && Property == "道路宽度";
    }
    public sealed class ModelElementCheck
    {
        public int Id { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Entity { get; set; }
        public string Slot { get; set; }
        public string Building { get; set; }
        public string Level { get; set; }
        public string Issue { get; set; }
        public bool Classified { get; set; }
        public int TypeId { get; set; }
        public string TypeName { get; set; }
        public string AreaScheme { get; set; }
        public string[] Slots { get; set; } = new string[0];
        public string[] SuggestedSlots { get; set; } = new string[0];
        public ClassificationCandidate[] Candidates { get; set; } = new ClassificationCandidate[0];
        public string Recognition { get; set; }
        public bool InventoryOnly { get; set; }
        public string CoverageNote { get; set; }
        public FieldCheck[] Fields { get; set; } = new FieldCheck[0];
        public string StatusText => !string.IsNullOrEmpty(CoverageNote) ? CoverageNote : !string.IsNullOrEmpty(Issue) ? Issue : SuggestedSlots.Length > 0 ? "待确认分类：" + string.Join("、", SuggestedSlots) : InventoryOnly ? "已盘点 · 无案例专用要求" : !Classified ? "待确认分类" : Fields.All(f => f.Acceptable) ? "案例字段已齐" : "待补齐 " + Fields.Count(f => !f.Acceptable) + " 项";
        public string SlotText => string.Join("、", Slots.Concat(string.IsNullOrEmpty(Slot) ? new string[0] : new[] { Slot }).Distinct())
            + (SuggestedSlots.Length == 0 ? "" : "；待确认：" + string.Join("、", SuggestedSlots));
    }
    public sealed class RequirementSetting
    {
        public string Slot { get; set; }
        public string Mode { get; set; } = "待确认";
    }
    public sealed class RoleCoverage
    {
        public string Slot { get; set; }
        public string Mode { get; set; }
        public int Count { get; set; }
        public int Candidates { get; set; }
        public int MissingFields { get; set; }
        public int MissingManualFields { get; set; }
        public int IncompleteObjects { get; set; }
        public bool NeedsAttention => MissingManualFields > 0;
        public string State { get; set; }
    }
    public sealed class ModelSummary
    {
        public RoleCoverage[] Roles { get; set; }
        public int ElementCount { get; set; }
        public int ClassifiedCount { get; set; }
        public int FieldCount { get; set; }
        public int ValidFields { get; set; }
        public int MissingRequired { get; set; }
        public int PendingClassificationRoles { get; set; }
        public int PendingRequirements { get; set; }
        public double? FieldPercent { get; set; }
        public bool Ready { get; set; }
        public int PresentFields { get; set; }
        public int ReviewCount { get; set; }
        public int CoverageGaps { get; set; }
        public string Basis { get; set; }
        public string Stage { get; set; }
        public string ModelKind { get; set; }
    }
    public static class ModelChecks
    {
        public static FieldCheck Evaluate(ReferenceProperty property, ParameterRead[] reads)
        {
            var result = new FieldCheck { PropertySet = property.PropertySet, Property = property.Property,
                Source = property.SourceParameter, RawType = property.RawType, State = "缺参数", Detail = "需准备原映射参数" };
            if (reads.Length == 0) return result;
            ParameterRead value;
            try { value = ReferencePolicy.Resolve(reads, property.SourceParameter); }
            catch (InvalidOperationException ex) { result.State = "有歧义"; result.Detail = ex.Message; return result; }
            var target = value ?? reads.FirstOrDefault(r => r.Origin == "instance") ?? reads[0];
            result.Origin = target.Origin;
            result.Editable = target.Origin == "instance" && !target.ReadOnly && target.StorageType != "ElementId";
            result.Value = ReferencePolicy.ToBridgeValue(value);
            result.DisplayValue = value?.DisplayValue ?? result.Value;
            if (string.IsNullOrWhiteSpace(result.Value)) { result.State = "未填写"; result.Detail = "参数已存在，值未填写"; return result; }
            if (!new[] { "Real", "Integer", "Boolean", "Label", "Text" }.Contains(property.RawType))
            { result.State = "类型待核验"; result.Detail = "保留参考原始类型：" + property.RawType; return result; }
            result.State = ValueIsValid(property.RawType, result.Value) ? "有效" : "值非法";
            result.Detail = result.State == "有效" ? target.Origin == "type" ? "类型来源；本版仅查看，避免影响范围外实例" : "实例来源" : "值不符合参考映射类型：" + property.RawType;
            return result;
        }
        public static ModelSummary Summarize(ReferenceCatalog catalog, ModelElementCheck[] elements, RequirementSetting[] settings)
        {
            var roles = catalog.Slots().Select(slot => {
                var mode = settings.FirstOrDefault(s => s.Slot == slot)?.Mode ?? "待确认";
                if (mode != "必要" && mode != "不适用") mode = "待确认";
                var names = catalog.ForSlot(slot).SelectMany(p => p.Entity.Split(',')).Distinct().ToArray();
                var count = elements.Count(e => e.Classified && e.Slot == slot);
                var candidates = elements.Count(e => !e.Classified && names.Contains(e.Entity));
                var supported = names.Any(n => new[] { "IfcSlab", "IfcWall", "IfcRoof", "IfcDoor", "IfcWindow", "IfcSpace", "IfcBuildingElementProxy", "IfcStairFlight" }.Contains(n));
                return new RoleCoverage { Slot = slot, Mode = mode, Count = count, Candidates = candidates,
                    State = mode == "不适用" ? "不适用" : mode != "必要" ? "待确认适用" : count > 0 ? "已识别" : !supported ? "载体待支持" : candidates > 0 ? "待分类" : "未识别到" };
            }).ToArray();
            var fields = elements.SelectMany(e => e.Fields).ToArray();
            var result = new ModelSummary { Roles = roles, ElementCount = elements.Length, ClassifiedCount = elements.Count(e => e.Classified),
                FieldCount = fields.Length, ValidFields = fields.Count(f => f.Valid), MissingRequired = roles.Count(r => r.Mode == "必要" && r.Count == 0),
                PendingRequirements = roles.Count(r => r.Mode == "待确认"), FieldPercent = fields.Length == 0 ? (double?)null : 100.0 * fields.Count(f => f.Valid) / fields.Length };
            result.Ready = elements.Length > 0 && result.PendingRequirements == 0 && roles.Any(r => r.Mode == "必要") && result.MissingRequired == 0
                && result.ClassifiedCount == result.ElementCount && result.ValidFields == result.FieldCount && result.FieldCount > 0 && elements.All(e => string.IsNullOrEmpty(e.Issue));
            return result;
        }
        public static bool ValueIsValid(string rawType, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (rawType == "Label" || rawType == "Text") return true;
            if (rawType == "Boolean") return value == "0" || value == "1";
            if (rawType == "Integer") return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            if (rawType == "Real") return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number);
            return false;
        }
    }
}
