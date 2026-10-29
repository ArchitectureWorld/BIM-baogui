using System.Globalization;
using System.Text.RegularExpressions;
using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    public ExportPlan BuildExportPlan(CheckResult basis,ParameterValue[] values,ExportRequest request,DateTime nowUtc)
    {
        ValidateBasis(basis,values);ProtocolGuard.Match(basis.Context,request.Context);
        if(request.HasPendingMaterials)throw new InvalidOperationException("存在未提交资料，请写入并重新全面检查后导出。");
        var name=request.FileName;
        if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'/','\\',':','\r','\n','\t','<','>','"','|','?','*'})>=0||name.Length>180)throw new InvalidOperationException("导出文件名只能是有效文件名，不能包含目录。");
        var match=Regex.Match(name,@"^OUT_(.+)_(报规)_(.+)_([0-9]{8})_V([0-9]+)\.ifc$",RegexOptions.CultureInvariant);
        if(!match.Success||!DateTime.TryParseExact(match.Groups[4].Value,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            ||!long.TryParse(match.Groups[5].Value,out var revision)||revision<1)throw new InvalidOperationException("请确认项目、单体、日期和版本，使用 OUT_项目_报规_单体_yyyyMMdd_V1.ifc 文件名。");
        var stem=ExportFileNaming.StemForNames(match.Groups[1].Value,match.Groups[3].Value,basis.Stage,basis.ModelKind,date);
        if(name!=stem+"_V"+revision+".ifc")throw new InvalidOperationException("导出文件名与模型阶段/类型不一致。");
        if(basis.Elements.Any(e=>!string.IsNullOrEmpty(e.Element.AssignedSlot)&&e.Element.AssignedSlot!=e.Element.DeclaredSlot))throw new InvalidOperationException("分类尚未写入模型，请先提交并复检。");
        var rows=CoreRows(basis);CheckOverlaps(rows,basis.Elements.Select(e=>e.Element).ToArray());
        var rowsById=rows.ToDictionary(r=>r.UniqueId,StringComparer.Ordinal);
        var fieldsByElement=basis.Fields.ToLookup(f=>f.ElementUniqueId,StringComparer.Ordinal);
        var valueLookup=values.ToLookup(v=>v.OwnerUniqueId+"|"+v.Source,StringComparer.Ordinal);
        var definitionLookup=basis.Definitions.ToLookup(d=>d.Source,StringComparer.Ordinal);
        var duplicate=PlanningInputs.DuplicateLandIssues(rows);if(duplicate.Length>0)throw new InvalidOperationException(string.Join("；",duplicate));
        var floors=FloorAreaPolicy.Build(rows,Planning(basis.Settings));if(floors.Issues.Length>0)throw new InvalidOperationException(string.Join("；",floors.Issues));
        if(floors.Allocations.Any(a=>string.IsNullOrEmpty(basis.Elements.Single(e=>e.Element.UniqueId==a.UniqueId).Element.DeclaredSlot)))throw new InvalidOperationException("楼层面积同步包含尚未写入分类的建筑区域。");
        var selected=new List<(ReferenceProperty Property,ElementRecord Element,FieldResult Field)>();
        var profile=basis.Settings.RuleProfileId;
        foreach(var element in basis.Elements.Select(e=>e.Element)) {
            if(!string.IsNullOrEmpty(element.CoverageNote))continue;
            var slots=element.Entity=="IfcBuilding"?new[]{"建筑技术"}:element.Entity=="IfcBuildingStorey"?
                (fieldsByElement[element.UniqueId].Any()?new[]{"建筑楼层"}:[]):string.IsNullOrEmpty(element.DeclaredSlot)?[]:new[]{element.DeclaredSlot};
            var entity=CaseWorkflow.ClassificationEntity(Row(element),element.DeclaredSlot);
            foreach(var slot in slots)foreach(var p in CaseExportPolicy.Properties(entity,slot,false,profile)) {
                var f=fieldsByElement[element.UniqueId].SingleOrDefault(f=>f.PropertySet==p.PropertySet&&f.Property==p.Property);
                if(f==null)throw new InvalidOperationException("导出属性尚未完整采集："+element.Name+" / "+p.Property);
                if(f.State=="有歧义"||f.State=="类型待核验")throw new InvalidOperationException("导出属性来源需核对："+f.Property);
                if(!string.IsNullOrEmpty(f.RequiredValue)&&f.Value!=f.RequiredValue)throw new InvalidOperationException("强制规则值尚未写入："+f.Property);
                selected.Add((p,element,f));
            }
        }
        foreach(var group in selected.Where(x=>x.Element.Entity=="IfcWindow"&&x.Property.Binding=="类型").GroupBy(x=>x.Element.TypeUniqueId))
            if(basis.Elements.Any(e=>e.Element.TypeUniqueId==group.Key&&!group.Any(x=>x.Element.UniqueId==e.Element.UniqueId)))throw new InvalidOperationException("窗类型仍有未确认的同类型实例，请先复制类型或确认整组范围。");
        var changes=new List<WriteChange>();var routes=new List<NativeExportRoute>();
        foreach(var item in selected) {
            var p=item.Property;var f=item.Field;var element=item.Element;var owner=p.Binding=="类型"?element.TypeUniqueId:element.UniqueId;
            var parent=PlanningInputs.ParentValue(rowsById[element.UniqueId].Fields,p.PropertySet);
            var value=f.MetricValue.HasValue?f.MetricValue.Value.ToString("R",CultureInfo.InvariantCulture):f.Value;
            var allocation=floors.Allocations.SingleOrDefault(a=>a.UniqueId==element.UniqueId&&a.Source==p.SourceParameter);
            if(allocation!=null)value=allocation.ValueM2;
            var raw=valueLookup[f.OwnerUniqueId+"|"+f.Source].ToArray();
            if(raw.Length!=1)throw new InvalidOperationException("导出来源未唯一采集："+f.Source);
            if(raw[0].StorageType=="ElementId"&&(!f.MetricValue.HasValue||p.PropertySet!="Pset_窗信息属性集"||p.Property!="标高")&&!string.IsNullOrEmpty(value))throw new InvalidOperationException("对象编号不能作为导出尺寸："+f.Source);
            if(p.ExportType=="Boolean") {
                var legacy=BooleanPolicy.LegacySource(p.SourceParameter);
                if(!raw[0].Exists&&legacy!=null&&valueLookup[owner+"|"+legacy].Any(v=>v.Exists))throw new InvalidOperationException("旧布尔别名需要先在属性管理中核验并写入："+legacy);
                value=string.IsNullOrWhiteSpace(value)?DefaultBoolean(basis,p.SourceParameter,element.Name,owner):BooleanPolicy.Normalize(value);
            }
            value=CaseExportPolicy.ValueForExport(p,value,profile,parent);
            var bridge=ReferencePolicy.BridgeName(p);var guid=ReferencePolicy.BridgeGuid(p).ToString("D");
            var existing=definitionLookup[bridge].SelectMany(d=>d.Values).Where(v=>v.OwnerUniqueId==owner).ToArray();
            if(existing.Length>1)throw new InvalidOperationException("导出临时参数来源不唯一。");
            var baseline=existing.SingleOrDefault()??new ParameterValue{OwnerId=p.Binding=="类型"?element.TypeId:element.Id,OwnerUniqueId=owner,Source=bridge,Origin=p.Binding=="类型"?"type":"instance",Exists=false};
            changes.Add(new WriteChange{FieldKey=f.Key,OwnerId=baseline.OwnerId,OwnerUniqueId=owner,Source=bridge,SourceGuid=guid,Origin=baseline.Origin,PropertySet=p.PropertySet,RawType="Text",DefinitionType="Text",
                ExpectedSignature=Canonical.ValuesHash(new[]{baseline}),ExpectedValue=baseline.RawValue,Value=value??"",Action=baseline.Exists?"Set":"Create",Reason=allocation?.Reason??"本轮已保存模型值的隔离导出映射",Unit=null,AffectedInstances=f.AffectedInstances});
            routes.Add(new NativeExportRoute{PropertySet=p.PropertySet,Entity=CaseWorkflow.ClassificationEntity(Row(element),element.DeclaredSlot),Origin=p.Binding=="类型"?"type":"instance",ExactOwnerFilter=p.Binding=="类型"||PlatformRules.IsActive(profile)&&p.PropertySet=="Pset_窗信息属性集",OwnerUniqueIds=new[]{owner},Properties=new[]{new NativeExportProperty{Property=p.Property,BridgeName=bridge,BridgeGuid=guid,ExportType=p.ExportType}}});
        }
        var overrides=new List<WriteChange>();
        foreach(var e in basis.Elements.Select(e=>e.Element).Where(e=>(e.SourceEntity??e.Entity)=="IfcSlab"&&e.DeclaredSlot=="规划控制线"))
            foreach(var source in new[]{"IfcExportAs","IfcExportType"}) {
                var current=Captured(valueLookup[e.UniqueId+"|"+source].ToArray(),e.UniqueId,source);
                if(current.ReadOnly||current.Exists&&current.StorageType!="String")throw new InvalidOperationException("规划控制线载体无法安全设置临时导出身份："+source);
                overrides.Add(Change(e.UniqueId+"|export|"+source,current,"Text",source=="IfcExportAs"?"IfcBuildingElementProxy":"NOTDEFINED","规划控制线临时使用代理实体；导出后整体回滚",false));
            }
        var unique=changes.GroupBy(c=>c.OwnerUniqueId+"|"+c.Source).Select(g=>{if(g.Select(c=>c.Value).Distinct().Count()!=1)throw new InvalidOperationException("类型导出值冲突。");return g.First();}).ToArray();
        var properties=selected.Select(x=>x.Property).ToArray();
        bool Exact(ReferenceProperty p)=>p.Binding=="类型"||PlatformRules.IsActive(profile)&&p.PropertySet=="Pset_窗信息属性集";
        var plan=new ExportPlan{Context=basis.Context,Id=Guid.NewGuid().ToString("N"),CaptureHash=basis.CaptureHash,ExpiresAtUtc=nowUtc.AddMinutes(2),FileName=name,
            MappingText=ReferenceCatalog.FromEmbeddedBaseline().BuildSelectedExportConfiguration(properties.Where(p=>!Exact(p))),TemporaryChanges=unique.Concat(overrides).ToArray(),
            Native=new NativeOperations{Definitions=DefinitionOperations(basis,unique,true).Concat(DefinitionOperations(basis,overrides.ToArray())).ToArray()},ExpectedDataHash=Canonical.ProjectDataHash(basis.ProjectData),
            Routes=MergeExportRoutes(routes),
            SuppressParameterNames=properties.Where(p=>!Exact(p)&&(p.ExportType=="Real"||p.ExportType=="Boolean")).Select(p=>p.Property).Distinct().ToArray(),
            Options=new Dictionary<string,string>{["FileVersion"]="IFC4DTV",["ExportBaseQuantities"]="true",["ExportRoomsInView"]="true",["UseActiveViewGeometry"]="false",["ExportLinkedFiles"]="false",["ExportUserDefinedPsets"]="true",["ExportInternalRevitPropertySets"]="false",["ExportIFCCommonPropertySets"]="true",["TessellationLevelOfDetail"]="1",["SitePlacement"]="2"}};
        plan.Hash=Canonical.ExportHash(plan);return plan;
    }
    private static NativeExportRoute[] MergeExportRoutes(IEnumerable<NativeExportRoute> routes)
    {
        // As in Local NativeTypePropertyScope, one compatible Pset description
        // owns the whole confirmed instance/type set, not one description per owner.
        // Keep distinct bridge definitions or property sets separate: a union could
        // otherwise attach an owner's properties to an incompatible peer.
        var byOwner=routes.GroupBy(r=>new{r.PropertySet,r.Entity,r.Origin,r.ExactOwnerFilter,Owner=r.OwnerUniqueIds.Single()})
            .Select(group=>{
                var properties=group.SelectMany(r=>r.Properties).GroupBy(p=>p.Property,StringComparer.Ordinal).Select(matches=>{
                    if(matches.Select(p=>new{p.BridgeName,p.BridgeGuid,p.ExportType}).Distinct().Count()!=1)
                        throw new InvalidOperationException("同一导出属性存在不兼容的桥接定义："+matches.Key);
                    return matches.First();
                }).OrderBy(p=>p.Property,StringComparer.Ordinal).ToArray();
                return new NativeExportRoute{PropertySet=group.Key.PropertySet,Entity=group.Key.Entity,Origin=group.Key.Origin,
                    ExactOwnerFilter=group.Key.ExactOwnerFilter,OwnerUniqueIds=[group.Key.Owner],Properties=properties};
            });
        return byOwner.GroupBy(r=>new{r.PropertySet,r.Entity,r.Origin,r.ExactOwnerFilter,Properties=System.Text.Json.JsonSerializer.Serialize(r.Properties)})
            .Select(group=>new NativeExportRoute{PropertySet=group.Key.PropertySet,Entity=group.Key.Entity,Origin=group.Key.Origin,
                ExactOwnerFilter=group.Key.ExactOwnerFilter,OwnerUniqueIds=group.SelectMany(r=>r.OwnerUniqueIds).Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
                Properties=group.First().Properties})
            .OrderBy(r=>r.PropertySet,StringComparer.Ordinal).ThenBy(r=>r.Entity,StringComparer.Ordinal).ThenBy(r=>r.Origin,StringComparer.Ordinal).ToArray();
    }
}
