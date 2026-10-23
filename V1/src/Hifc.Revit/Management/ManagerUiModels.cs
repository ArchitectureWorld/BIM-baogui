using System;
using System.Linq;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class AreaValueHint
    {
        internal const string Text = "该数值可手动修改并被平台读取";
        internal static string For(string property, string rawType, bool editable)
            => editable && rawType == "Real" && property?.EndsWith("面积", StringComparison.Ordinal) == true ? Text : "";
    }
    internal sealed class RuleProfileOption
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }
    internal static class RuleInputOptions
    {
        internal const string PlatformTitle = "保康县420626报规(2026-10-20)";
        internal static bool IsParentFunction(string property) => property=="主功能类别"||property=="公建大类";
        internal static bool IsChildFunction(string property) => property=="子功能类别"||property=="公建小类";
        internal static string[] Choices(string profileId, string propertySet, string property, string parentFunction = null)
        {
            var items = PlatformRules.IsActive(profileId) ? PlatformRules.Choices(propertySet, property, parentFunction)
                : propertySet == "Pset_建筑技术信息属性集" && property == "建筑类型名称" ? new[] { "住宅", "公建", "中小学等", "幼儿园等", "厂房", "办公", "生活服务设施", "其它设施" }
                : propertySet == "Pset_建筑技术信息属性集" && property == "建筑类别" ? new[] { "住宅", "商业", "商住", "配套车库", "中小学" }
                : new string[0];
            return items.Length == 0 ? items : new[] { "" }.Concat(items).ToArray();
        }
        internal static string ValueHint(string profileId, string propertySet, string property, string value, string parentFunction = null)
        {
            if (!PlatformRules.IsActive(profileId)) return "";
            if (string.IsNullOrWhiteSpace(value)) return PlatformInputReview.DefaultHint(profileId,propertySet,property,value,parentFunction);
            if (IsChildFunction(property) && string.IsNullOrWhiteSpace(parentFunction))
                return "请先核对主功能；子功能按主功能提供选项，现有值保留。";
            var error = PlatformRules.ValueError(propertySet, property, value, parentFunction);
            return string.IsNullOrEmpty(error) ? "" : "当前规则需核对：" + error;
        }
        internal static string Scope(string profileId) => PlatformRules.IsActive(profileId)
            ? "规则方案：" + PlatformTitle + "。仅核对当前模型适用的分类和计算输入，不要求每个项目包含全部规则类目。模板默认值不作为模型事实。"
            : "规则方案：旧案例参考。未选择地区平台规则；案例检查完成不能代替平台验收。";
    }
    internal sealed class ModelSnapshot
    {
        public string Scope { get; set; } = "全项目";
        public string DocumentKey { get; set; }
        public string Title { get; set; }
        public string Stage { get; set; }
        public string RequestedKind { get; set; }
        public string ModelKind { get; set; }
        public string[] DetectionEvidence { get; set; } = new string[0];
        public bool Complete { get; set; }
        public string ScanId { get; set; }
        public string CompletedUtc { get; set; }
        public PlanningSettings Planning { get; set; } = new PlanningSettings();
        public UndergroundSettings Underground { get; set; }
        public string[] CalculationIssues { get; set; } = new string[0];
        public ModelElementCheck[] Elements { get; set; } = new ModelElementCheck[0];
    }
    internal sealed class EditRequest
    {
        public string Kind { get; set; }
        public int[] Ids { get; set; }
        public string Slot { get; set; }
        public string Source { get; set; }
        public string Value { get; set; }
        public string ValueUnit { get; set; }
        public string[] WorkbookImportKeys { get; set; }
        public string PropertySet { get; set; }
        public string Binding { get; set; }
        public EditBaseline[] Baselines { get; set; } = new EditBaseline[0];
    }
    internal sealed class EditBaseline
    {
        public int ElementId { get; set; }
        public string ElementUniqueId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Value { get; set; }
        public string State { get; set; }
        public string SourceSignature { get; set; }
    }
    internal sealed class EditChange
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string ExpectedRaw { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Action { get; set; }
        public string Binding { get; set; }
        public string Detail { get; set; }
    }
    internal sealed class EditPreview
    {
        public string DocumentKey { get; set; }
        public string SnapshotHash { get; set; }
        public string AreaSyncFingerprint { get; set; }
        public EditRequest Request { get; set; }
        public EditChange[] Changes { get; set; }
        public string[] Conflicts { get; set; } = new string[0];
        public string ScanId { get; set; }
        public ModelElementCheck[] ProposedElements { get; set; } = new ModelElementCheck[0];
    }
    internal interface IManagerSession
    {
        void Scan();
        void PrepareAll();
        void Cancel();
        void Locate(int[] ids);
        void Preview(EditRequest request);
        void Apply(EditPreview preview);
        void Write(EditRequest[] requests);
        void ExportProject(string path);
        void Close();
    }
}
