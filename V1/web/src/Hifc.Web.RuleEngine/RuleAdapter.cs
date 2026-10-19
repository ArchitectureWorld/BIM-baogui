using Hifc.Web.Contracts;
using Hifc.Reference;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    public string RuleVersion => "reference-0.4.4-web1";
    private static ModelElementCheck Row(ElementRecord e) => new()
    {
        Id = e.Id, UniqueId = e.UniqueId, TypeId = e.TypeId, Name = e.Name, TypeName = e.TypeName, Category = e.Category,
        Entity = e.Entity, Slot = e.AssignedSlot ?? e.DeclaredSlot, Level = e.Level, Building = e.Building,
        AreaScheme = e.AreaScheme, CoverageNote = e.CoverageNote, InventoryOnly = true
    };
    public CapturePlan CreateCapturePlan(InventoryPage[] pages)
    {
        ProtocolGuard.InventoryPages(pages);
        var context = pages[0].Context;
        if (context.RuleVersion != RuleVersion || string.IsNullOrWhiteSpace(context.DocumentSessionId) || string.IsNullOrWhiteSpace(context.ScanId) || string.IsNullOrWhiteSpace(context.RequestId))
            throw new InvalidOperationException("规则版本或扫描标识不匹配。");
        if (pages[0].Stage != "报规") throw new InvalidOperationException("施工图专用规则尚未建立，请选择报规阶段。");
        var elements = pages.SelectMany(p => p.Elements).ToArray();
        var detection = CaseWorkflow.Detect(elements.Select(Row).ToArray());
        var requested = pages[0].ModelKind;
        var kind = requested == "总图" || requested == "单体建筑" ? requested : detection.Kind;
        var rules = CaseWorkflow.ForModel("报规", kind);
        var targets = new List<CaptureTarget>();
        foreach (var element in elements)
        {
            if (element.AssignedSlot != null && element.AssignedSlot != "" && !rules.Any(r => r.Slot == element.AssignedSlot && r.Binding == "实例" && r.Entities.Contains(element.Entity)
                && (!CaseWorkflow.AreaSlots.Contains(r.Slot) || element.AreaScheme != null)))
                throw new InvalidOperationException(element.Id + "：所选分类不适用于当前模型或构件载体。");
            Add(element, element.UniqueId, element.Id, "类目", "instance");
            if (!string.IsNullOrEmpty(element.TypeUniqueId)) Add(element, element.TypeUniqueId, element.TypeId, "类目", "type");
            foreach (var rule in rules.Where(r => r.Entities.Contains(element.Entity)))
                foreach (var property in rule.Properties)
                {
                    if (rule.Binding == "实例") Add(element, element.UniqueId, element.Id, property.SourceParameter, "instance");
                    if (!string.IsNullOrEmpty(element.TypeUniqueId)) Add(element, element.TypeUniqueId, element.TypeId, property.SourceParameter, "type");
                }
        }
        return new CapturePlan { Context = context, InventoryComplete = true, ModelKind = kind, DetectionEvidence = detection.Evidence,
            Targets = targets.GroupBy(t => t.ElementUniqueId + "|" + t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).ToArray() };
        void Add(ElementRecord element, string owner, int ownerId, string source, string origin) => targets.Add(new CaptureTarget
        { ElementUniqueId = element.UniqueId, OwnerUniqueId = owner, OwnerId = ownerId, Source = source, Origin = origin });
    }
}
