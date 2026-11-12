using Hifc.Web.Contracts;
using Hifc.Reference;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Profile,string Source),bool> BooleanSources=new();
    private static bool IsBooleanSource(string source,string profile)=>BooleanSources.GetOrAdd((profile??"",source??""),key=>BooleanPolicy.IsSource(key.Source,key.Profile));
    private static string DefaultBoolean(CheckResult basis,string source,string name,string owner)
    {
        var baseId=basis.Settings.Underground?.BaseLevelUniqueId;
        if(source=="建筑基底"&&!string.IsNullOrEmpty(baseId)&&!basis.Elements.Any(e=>e.Element.UniqueId==baseId&&e.Element.Entity=="IfcBuildingStorey"))
            throw new InvalidOperationException("已确认的基底楼层被删除或替换，请重新确认后写入/导出是否属性。");
        return BooleanPolicy.DefaultValue(source,name,owner,baseId);
    }
    public string RuleVersion => "reference-0.4.32-web3";
    public RuleProfileInfo[] Profiles => new[] { new RuleProfileInfo { Id = "", Title = "参考成功案例", Scope = "报规" }, new RuleProfileInfo { Id = PlatformRules.ProfileId, Title = PlatformRules.Title, Scope = "报规 / 保康县 420626" } };
    private static PlanningSettings Planning(ModelSettings settings) => new() { RuleProfileId = settings.RuleProfileId,
        Roles = (settings.Roles ?? []).Select(r => new PlanningRoleChoice { UniqueId = r.UniqueId, Slot = r.Slot, Identity = r.Identity }).ToArray(),
        CommonValues = settings.CommonValues ?? new(), LevelsConfirmed = settings.LevelsConfirmed,
        BuildingLevelUniqueIds = settings.BuildingLevelUniqueIds ?? [], TopBoundaryLevelUniqueId = settings.TopBoundaryLevelUniqueId };
    private static UndergroundSettings Underground(ModelSettings settings) => new() { Confirmed = settings.Underground?.Confirmed == true,
        GroundElevationM = settings.Underground?.GroundElevationM, LevelUniqueIds = settings.Underground?.LevelUniqueIds ?? [], BaseLevelUniqueId = settings.Underground?.BaseLevelUniqueId };
    private static ModelElementCheck Row(ElementRecord e) => new()
    {
        Id = e.Id, UniqueId = e.UniqueId, TypeId = e.TypeId, Name = e.Name, TypeName = e.TypeName, Category = e.Category,
        Entity = e.Entity, Slot = e.AssignedSlot ?? e.DeclaredSlot, Level = e.Level, Building = e.Building,
        AreaScheme = e.AreaScheme, CoverageNote = e.CoverageNote, InventoryOnly = true, SourceEntity = e.SourceEntity,
        LevelUniqueId = e.LevelUniqueId, LevelElevationM = e.LevelElevationM, ProjectedAreaM2 = e.AreaScheme != null ? (string.IsNullOrEmpty(e.CoverageNote) && e.NativeAreaM2 > 0 ? e.NativeAreaM2 : null) : e.ProjectedAreaM2,
        ProjectionKey = e.ProjectionKey, AreaAggregationIssue = e.AreaAggregationIssue,
        AreaReason = e.AreaReason ?? (e.AreaScheme != null ? "Revit 面积（Area）的实际面积，转换为 m²" : null)
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
        var profile = pages[0].Settings.RuleProfileId;
        if (!Profiles.Any(p => p.Id == (profile ?? ""))) throw new InvalidOperationException("所选规则方案不存在。");
        var rules = CaseWorkflow.ForModel("报规", kind, profile);
        var targets = new List<CaptureTarget>();
        foreach (var element in elements)
        {
            if (element.AssignedSlot != null && element.AssignedSlot != "" && !rules.Any(r => r.Slot == element.AssignedSlot && CaseWorkflow.CanClassify(r,Row(element))))
                throw new InvalidOperationException(element.Id + "：所选分类不适用于当前模型或构件载体。");
            Add(element, element.UniqueId, element.Id, "类目", "instance");
            if (!string.IsNullOrEmpty(element.TypeUniqueId)) Add(element, element.TypeUniqueId, element.TypeId, "类目", "type");
            if ((element.SourceEntity ?? element.Entity) == "IfcSlab")
                foreach(var source in new[]{"IfcExportAs","IfcExportType"}) Add(element,element.UniqueId,element.Id,source,"instance");
            foreach (var rule in rules.Where(r => r.Entities.Contains(element.Entity)))
                foreach (var property in rule.Properties)
                {
                    foreach (var source in new[] { property.SourceParameter, BooleanPolicy.LegacySource(property.SourceParameter) }.Where(s => s != null))
                    {
                        if (rule.Binding == "实例") Add(element, element.UniqueId, element.Id, source, "instance");
                        if (!string.IsNullOrEmpty(element.TypeUniqueId)) Add(element, element.TypeUniqueId, element.TypeId, source, "type");
                    }
                }
        }
        return new CapturePlan { Context = context, InventoryComplete = true, ModelKind = kind, DetectionEvidence = detection.Evidence,
            Targets = targets.GroupBy(t => t.ElementUniqueId + "|" + t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).ToArray(),
            DefinitionSources = targets.Select(t => t.Source).Concat(rules.SelectMany(r => r.Properties).Select(ReferencePolicy.BridgeName))
                .Concat(rules.SelectMany(r => r.Properties).Where(p=>p.ExportType=="Real"||p.ExportType=="Boolean").Select(p=>p.Property))
                .Concat(new[]{"IfcExportAs","IfcExportType"}).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray() };
        void Add(ElementRecord element, string owner, int ownerId, string source, string origin) => targets.Add(new CaptureTarget
        { ElementUniqueId = element.UniqueId, OwnerUniqueId = owner, OwnerId = ownerId, Source = source, Origin = origin });
    }
}
