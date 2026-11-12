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
            StorageType = value.StorageType, DefinitionType = value.DefinitionType, ParameterGuid = value.SharedGuid, DisplayValue = value.DisplayValue, ReadOnly = value.ReadOnly };
    }
    private static FieldCheck CoreField(FieldResult f) => new() { RuleProfileId=f.RuleProfileId, PropertySet=f.PropertySet, Property=f.Property, Source=f.Source, RawType=f.RawType,
        State=f.State, Value=f.Value, DisplayValue=f.DisplayValue, MetricValue=f.MetricValue, Origin=f.Binding=="类型"?"type":"instance", Binding=f.Binding,
        Editable=f.Editable, OwnerId=f.OwnerId, OwnerUniqueId=f.OwnerUniqueId, SourceSignature=f.SourceSignature, AllowedEmpty=f.AllowedEmpty,
        SuggestedValue=f.SuggestedValue, RequiredValue=f.RequiredValue, SuggestionUnit=f.SuggestionUnit, SuggestionReason=f.Detail, FloorAreaOverride=f.FloorAreaOverride,
        FireRoadWidth=f.ManualInputRequired && f.Property=="道路宽度" };
    private static ModelElementCheck[] CoreRows(CheckResult basis)
    {
        var fields=basis.Fields.ToLookup(f=>f.ElementUniqueId,StringComparer.Ordinal);
        return basis.Elements.Select(e => {
        var row=Row(e.Element); row.Fields=fields[row.UniqueId].Select(CoreField).ToArray();
        row.Slots=e.Slots; row.Classified=e.Slots.Length>0; row.InventoryOnly=row.Fields.Length==0&&e.CandidateSlots.Length==0;
        row.Candidates=e.CandidateSlots.Select((s,i)=>new ClassificationCandidate {Slot=s, CanPrepare=e.CandidateSlots.Length==1&&row.Fields.Length>0, RequiresReview=row.Fields.Length==0,
            Evidence=i<e.CandidateEvidence.Length?e.CandidateEvidence[i]:null}).ToArray(); return row;
        }).ToArray();
    }
    public CheckResult Evaluate(InventoryPage[] inventory, CapturePlan plan, CapturePage[] pages)
    {
        ProtocolGuard.InventoryPages(inventory); ProtocolGuard.CapturePages(pages, plan.Targets);
        ProtocolGuard.Match(inventory[0].Context, plan.Context); ProtocolGuard.Match(plan.Context, pages[0].Context);
        if (plan.Context.RuleVersion != RuleVersion) throw new InvalidOperationException("规则版本已更新，请重新全面检查。");
        var values=pages.SelectMany(p=>p.Values).ToArray();
        var lookup=values.ToLookup(v=>v.OwnerUniqueId+"|"+v.Source,StringComparer.Ordinal);
        var definitions=ProtocolGuard.Definitions(pages);
        if(definitions.Any(d=>!plan.DefinitionSources.Contains(d.Source)))throw new InvalidOperationException("包含未请求的参数定义。");
        var settings=inventory[0].Settings; var planning=Planning(settings); var underground=Underground(settings);
        var rules=CaseWorkflow.ForModel("报规",plan.ModelKind,planning.RuleProfileId);
        var elements=inventory.SelectMany(p=>p.Elements).ToArray();var rows=elements.Select(Row).ToArray();
        var elementsById=elements.ToDictionary(e=>e.UniqueId,StringComparer.Ordinal);
        foreach(var row in rows.Where(r=>string.IsNullOrEmpty(r.Slot)))row.Candidates=PlanningInputs.Candidates(row,plan.ModelKind,planning);
        var selectedLevels=planning.LevelsConfirmed?planning.BuildingLevelUniqueIds:PlanningInputs.DefaultBuildingLevels(rows,planning.RuleProfileId);
        var levels=rows.Where(r=>r.Entity=="IfcBuildingStorey"&&r.LevelElevationM.HasValue).Select(r=>new UndergroundLevel{UniqueId=r.UniqueId,Name=r.Name,ElevationM=r.LevelElevationM.Value}).ToArray();
        var undergroundResult=UndergroundPolicy.Evaluate(underground,levels);
        foreach(var row in rows)
        {
            var element=elementsById[row.UniqueId];
            if(row.Entity=="LINK")continue;
            if(PlatformRules.IsActive(planning.RuleProfileId)&&row.Entity=="IfcBuildingStorey"&&!selectedLevels.Contains(row.UniqueId))continue;
            if(CaseWorkflow.AreaSlots.Contains(row.Slot)&&element.AreaScheme==null){row.CoverageNote="该报规区域类目需要面积（Area）载体，请核对模型。";continue;}
            var info=row.Entity=="IfcBuilding"||row.Entity=="IfcBuildingStorey";
            var matches=rules.Where(r=>r.Entities.Contains(row.Entity)&&(info||CaseWorkflow.CanClassify(r,row))).ToArray();
            var applicable=new List<CaseRule>();
            if(info)applicable.AddRange(matches);
            else if(!string.IsNullOrEmpty(row.Slot))applicable.AddRange(matches.Where(r=>r.Slot==row.Slot));
            else {
                row.SuggestedSlots=row.Candidates.Select(c=>c.Slot).ToArray();
                if(row.Candidates.Length==1&&row.Candidates[0].CanPrepare)applicable.AddRange(matches.Where(r=>r.Slot==row.Candidates[0].Slot));
                if(row.Candidates.Length>1||row.Candidates.Any(c=>c.RequiresReview))row.Issue="待确认分类：多候选或载体关系需核对";
            }
            applicable.AddRange(rules.Where(r=>r.Binding=="类型"&&r.Entities.Contains(row.Entity)&&(r.Required&&r.Slot!="总平构件"||r.Slot=="窗"&&row.Slot=="窗")));
            applicable=applicable.Distinct().ToList();
            row.Slots=applicable.Where(r=>r.Binding=="类型"||info||r.Slot==row.Slot).Select(r=>r.Slot).Distinct().ToArray();
            row.Classified=row.Slots.Length>0;row.InventoryOnly=applicable.Count==0&&row.Candidates.Length==0&&string.IsNullOrEmpty(row.CoverageNote);
            if(!string.IsNullOrEmpty(row.Slot)&&!matches.Any(r=>r.Slot==row.Slot))row.CoverageNote="已声明类目不在当前案例/载体范围："+row.Slot;
            row.Fields=applicable.SelectMany(rule=>rule.Properties.Where(p=>p.Entity.Split(',').Contains(row.Entity)).Select(property=>{
                var reads=(rule.Binding=="实例"?lookup[element.UniqueId+"|"+property.SourceParameter]:Enumerable.Empty<ParameterValue>())
                    .Concat(lookup[element.TypeUniqueId+"|"+property.SourceParameter]).ToArray();
                var f=ModelChecks.Evaluate(property,reads.Where(v=>v.Exists).Select(Read).ToArray(),planning.RuleProfileId);
                var type=f.Origin=="type"||rule.Binding=="类型"; f.OwnerUniqueId=type?element.TypeUniqueId:element.UniqueId;f.OwnerId=type?element.TypeId:element.Id;
                var current=reads.Where(v=>v.OwnerUniqueId==f.OwnerUniqueId).ToArray();f.Binding=type?"类型":"实例";
                f.SourceSignature=Canonical.ValuesHash(current);f.AllowedEmpty=rule.EmptyAllowed.Contains(property.Property);
                f.Editable=!string.IsNullOrEmpty(f.OwnerUniqueId)&&current.Length==1&&(!current[0].Exists||!current[0].ReadOnly&&current[0].StorageType!="ElementId")&&f.State!="有歧义";
                f.MetricValue=current.Length==1?current[0].MetricValue:null;
                if(current.Length==1&&current[0].StorageType=="ElementId") {
                    if(f.MetricValue.HasValue&&property.PropertySet=="Pset_窗信息属性集"&&property.Property=="标高") { f.State="有效";f.DisplayValue=f.MetricValue.Value.ToString("0.######",CultureInfo.InvariantCulture)+" m"; }
                    else {f.MetricValue=null;f.State="类型待核验";f.Detail="该对象引用不能作为业务数值；原参数保持不变。";}
                }
                AutoFillPolicy.PopulateWithSource(f,rule.Slot,element.Name,row.ProjectedAreaM2,row.AreaReason);
                var legacy=BooleanPolicy.LegacySource(f.Source);
                if(f.RawType=="Boolean"&&f.State=="缺参数"&&legacy!=null) {
                    var old=lookup[f.OwnerUniqueId+"|"+legacy].Where(v=>v.Exists).ToArray();
                    if(old.Length>1) {f.SuggestedValue=null;f.Editable=false;f.Detail="旧布尔来源有歧义，请核对。";}
                    else if(old.Length==1&&old[0].HasValue&&!string.IsNullOrWhiteSpace(old[0].RawValue)) {
                        try{f.SuggestedValue=BooleanPolicy.Normalize(old[0].RawValue);f.SuggestionReason="核验并复制旧来源“"+legacy+"”；保留旧定义";}
                        catch(InvalidOperationException ex){f.SuggestedValue=null;f.Editable=false;f.Detail=ex.Message;}
                    }
                }
                if(row.Entity=="IfcBuilding")UndergroundPolicy.Populate(f,undergroundResult,f.MetricValue);
                if(row.Entity=="IfcBuildingStorey")FootprintPolicy.Populate(f,underground,levels,row.UniqueId);
                if((f.State=="缺参数"||f.State=="未填写")&&property.Property=="楼层名称")f.SuggestedValue=element.Name;
                if((f.State=="缺参数"||f.State=="未填写")&&row.Entity=="IfcBuilding"&&property.Property=="建筑名称"&&!string.IsNullOrWhiteSpace(element.Building))f.SuggestedValue=element.Building;
                return f;
            })).ToArray();
        }
        CheckOverlaps(rows,elements);
        PlanningInputs.Populate(rows,planning,underground);
        var fields=rows.SelectMany(row=>row.Fields.Select(f=>ToResult(row,f,elements,lookup))).ToArray();
        var commonRow=new ModelElementCheck{UniqueId="common",Name="项目统一设置"};
        commonRow.Fields=PlanningInputs.CommonFields(plan.ModelKind,planning.RuleProfileId).Select(p=>{
            var f=ModelChecks.Evaluate(p,[],planning.RuleProfileId);f.Binding=p.Binding;f.OwnerUniqueId="";
            f.Value=settings.CommonValues?.GetValueOrDefault(ReferenceCatalog.LegacySlot(p.PropertySet)+"|"+p.Property);return f;
        }).ToArray();
        var common=commonRow.Fields.Select(f=>{
            var result=ToResult(commonRow,f,elements);
            result.Key=ReferenceCatalog.LegacySlot(f.PropertySet)+"|"+f.Property;result.Editable=true;return result;
        }).ToArray();
        return new CheckResult {Context=plan.Context,Stage="报规",ModelKind=plan.ModelKind,Complete=true,CaptureHash=Canonical.CaptureHash(values,definitions),ServerTimeUtc=DateTime.UtcNow,
            Elements=rows.Select(r=>new ElementResult {Element=elementsById[r.UniqueId],Slots=r.Slots,CandidateSlots=r.Candidates.Select(c=>c.Slot).ToArray(),CandidateEvidence=r.Candidates.Select(c=>c.Evidence).ToArray(),Status=r.StatusText,Identity=PlanningInputs.Identity(r)}).ToArray(),
            Fields=fields,AvailableSlots=rules.Where(r=>r.Binding=="实例").Select(r=>r.Slot).Distinct().ToArray(),ReviewCount=rows.Count(r=>r.Candidates.Any(c=>c.RequiresReview)||!string.IsNullOrEmpty(r.CoverageNote)),
            Settings=settings,ProjectData=inventory[0].ProjectData,Definitions=definitions,DefinitionSources=plan.DefinitionSources,RuleProfiles=Profiles,CommonFields=common,CalculationIssues=PlanningInputs.Issues(rows,plan.ModelKind,planning.RuleProfileId)};
    }
    private static FieldResult ToResult(ModelElementCheck row,FieldCheck f,ElementRecord[] elements,ILookup<string,ParameterValue> lookup=null)
    {
        var current=lookup?[f.OwnerUniqueId+"|"+f.Source].ToArray()??[];
        var parent=PlanningInputs.ParentValue(row.Fields,f.PropertySet,true);
        var choices=PlatformRules.IsActive(f.RuleProfileId)?PlatformRules.Choices(f.PropertySet,f.Property,parent):[];
        var custom=ClassificationInputs.Supports(f.PropertySet,f.Property);
        return new FieldResult {Key=row.UniqueId+"|"+f.PropertySet+"|"+f.Binding+"|"+f.Property,ElementId=row.Id,ElementUniqueId=row.UniqueId,ElementName=row.Name,
            OwnerId=f.OwnerId,OwnerUniqueId=f.OwnerUniqueId,Slot=ReferenceCatalog.LegacySlot(f.PropertySet),PropertySet=f.PropertySet,Property=f.Property,Binding=f.Binding,Source=f.Source,
            SourceGuid=(IsBooleanSource(f.Source,f.RuleProfileId)?BooleanPolicy.SourceGuid(f.Source):ReferencePolicy.SourceGuid(f.Source)).ToString("D"),RawType=f.RawType,
            State=f.State,Value=f.Value,DisplayValue=f.DisplayValue,SuggestedValue=f.SuggestedValue,SuggestionUnit=f.SuggestionUnit??current.FirstOrDefault()?.Unit,Detail=f.SuggestionReason??f.Detail,
            Editable=f.Editable,AllowedEmpty=f.AllowedEmpty,ManualInputRequired=f.RequiredForSubmission,SourceSignature=f.SourceSignature,InputUnit=PropertyUnits.Canonical(f.Property,f.RawType),
            AffectedInstances=f.Binding=="类型"?elements.Count(e=>e.TypeUniqueId==f.OwnerUniqueId):1,RuleProfileId=f.RuleProfileId,RequiredValue=f.RequiredValue,MetricValue=f.MetricValue,
            FloorAreaOverride=f.FloorAreaOverride,DefinitionType=current.Length==1?current[0].DefinitionType:null,InputKind=f.RawType=="Boolean"?"boolean":custom?"editable-choice":choices.Length>0?"choice":f.RawType=="Real"||f.RawType=="Integer"?"number":"text",
            Options=choices,ParentProperty=ClassificationInputs.IsChild(f.Property)?f.Property=="子功能类别"?"主功能类别":"公建大类":null};
    }
    private static void CheckOverlaps(ModelElementCheck[] rows,ElementRecord[] elements)
    {
        foreach(var row in rows.Where(r=>PlanningInputs.EffectiveSlot(r)=="建筑区域")) {
            var source=elements.Single(e=>e.UniqueId==row.UniqueId);
            var issues=(source.ProjectionOverlaps??[]).Where(p=>rows.Any(other=>other.UniqueId==p.OtherUniqueId&&other.LevelUniqueId==row.LevelUniqueId&&PlanningInputs.EffectiveSlot(other)=="建筑区域"))
                .Where(p=>!string.IsNullOrEmpty(p.Error)||!p.OverlapM2.HasValue||p.OverlapM2>1e-5).Select(p=>!string.IsNullOrEmpty(p.Error)?p.Error:"本层建筑区域投影重叠/无法可靠核对："+p.OtherUniqueId).ToArray();
            if(issues.Length>0)row.AreaAggregationIssue=string.Join("；",issues);
        }
    }
}
