using System.Globalization;
using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    private void ValidateBasis(CheckResult basis,ParameterValue[] values)
    {
        if(basis==null||!basis.Complete||basis.Context.RuleVersion!=RuleVersion||basis.CaptureHash!=Canonical.CaptureHash(values,basis.Definitions)
            ||basis.Context.ConfigurationHash!=Canonical.ConfigurationHash(basis.Settings,basis.ProjectData))throw new InvalidOperationException("检查尚未完成或模型、定义、设置、资料快照已改变。");
    }
    public EditPlan BuildEditPlan(CheckResult basis,ParameterValue[] values,EditDraft[] drafts,bool includeSuggestions,DateTime nowUtc,ResolvedMaterialDecision[] materials=null)
    {
        ValidateBasis(basis,values);
        var imported=ResolveMaterials(basis,materials??[],nowUtc);
        var allDrafts=drafts.Concat(imported.Drafts).ToArray();
        var rebase=DraftRebaser.Rebase(allDrafts,basis.Fields);
        if(rebase.Conflicts.Length>0||rebase.InvalidTargets.Length>0)throw new InvalidOperationException("人工草稿与当前模型存在冲突，请先确认原值、现值和拟写值。");
        var fieldsByKey=basis.Fields.ToDictionary(f=>f.Key,StringComparer.Ordinal);
        var draftsByKey=allDrafts.ToDictionary(d=>d.FieldKey,StringComparer.Ordinal);
        var elementsById=basis.Elements.ToDictionary(e=>e.Element.UniqueId,e=>e.Element,StringComparer.Ordinal);
        var captured=values.ToLookup(v=>v.OwnerUniqueId+"|"+v.Source,StringComparer.Ordinal);
        var rows=CoreRows(basis);CheckOverlaps(rows,basis.Elements.Select(e=>e.Element).ToArray());
        var duplicateLand=PlanningInputs.DuplicateLandIssues(rows);
        if(duplicateLand.Length>0)throw new InvalidOperationException(string.Join("；",duplicateLand));
        var overrides=allDrafts.Select(d=>{
            var f=fieldsByKey[d.FieldKey];if(!f.Editable)throw new InvalidOperationException(f.Property+"：来源有歧义、只读或载体不可写。");
            var value=Normalize(f.RawType,d.Value);var unit=imported.Units.GetValueOrDefault(d.FieldKey)??PropertyUnits.Canonical(f.Property,f.RawType);
            return new PlanningValueOverride{OwnerUniqueId=f.OwnerUniqueId,PropertySet=f.PropertySet,Source=f.Source,Binding=f.Binding,Value=value,
                MetricValue=(unit=="m"||unit=="m²")&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)?number:null};
        }).ToArray();
        rows=PlanningInputs.WithDraftValues(rows,overrides);
        PlanningInputs.Populate(rows,Planning(basis.Settings),Underground(basis.Settings));
        var rowsById=rows.ToDictionary(r=>r.UniqueId,StringComparer.Ordinal);
        foreach(var d in allDrafts) {
            var original=fieldsByKey[d.FieldKey];var row=rowsById[original.ElementUniqueId];
            var field=row.Fields.Single(f=>f.Source==original.Source&&f.PropertySet==original.PropertySet&&f.Binding==original.Binding);
            var parent=PlanningInputs.ParentValue(row.Fields,field.PropertySet);
            var error=ModelChecks.InputError(field,field.Value,parent);
            if(error!=null)throw new InvalidOperationException(original.ElementName+" / "+field.Property+"："+error);
            if(ClassificationInputs.IsParent(field.Property))foreach(var child in row.Fields.Where(f=>f.PropertySet==field.PropertySet&&ClassificationInputs.IsChild(f.Property))) {
                error=ModelChecks.InputError(child,child.Value,field.Value);if(error!=null)throw new InvalidOperationException("请同时确认主、子功能："+error);
            }
            if(field.FloorAreaOverride&&field.PropertySet==FloorAreaPolicy.RegionSet)throw new InvalidOperationException("建筑区域面积修改与已填写楼层面积不一致，请先统一整层总值或各区域分配。");
        }
        var changes=new List<WriteChange>();
        foreach(var row in rows) {
            var element=elementsById[row.UniqueId];
            var selected=element.AssignedSlot;
            if(string.IsNullOrEmpty(selected)&&includeSuggestions&&string.IsNullOrEmpty(element.DeclaredSlot)&&row.Candidates.Length==1&&row.Candidates[0].CanPrepare)selected=row.Candidates[0].Slot;
            if(!string.IsNullOrEmpty(selected)&&selected!=element.DeclaredSlot) {
                var rules=CaseWorkflow.ForModel(basis.Stage,basis.ModelKind,basis.Settings.RuleProfileId);
                if(!rules.Any(r=>r.Slot==selected&&CaseWorkflow.CanClassify(r,Row(element))))throw new InvalidOperationException("分类与当前载体不匹配。");
                var original=Captured(captured[element.UniqueId+"|类目"].ToArray(),element.UniqueId,"类目");
                if(original.ReadOnly||original.Exists&&original.StorageType!="String")throw new InvalidOperationException("类目来源不可写或有歧义。");
                changes.Add(Change(element.UniqueId+"|classification",original,"Text",selected,"人工/计算设置确认分类",true));
                var retained=rules.Where(r=>r.Slot==selected).SelectMany(r=>r.Properties).Select(p=>p.SourceParameter).ToHashSet(StringComparer.Ordinal);
                var oldSources=rules.Where(r=>r.Binding=="实例"&&r.Slot==element.DeclaredSlot).SelectMany(r=>r.Properties).Select(p=>p.SourceParameter)
                    .Where(s=>s.StartsWith(element.DeclaredSlot??"",StringComparison.Ordinal)&&!retained.Contains(s)).Distinct();
                foreach(var source in oldSources) {
                    var old=captured[element.UniqueId+"|"+source].Where(v=>v.Origin=="instance").ToArray();
                    if(old.Length==1&&(!old[0].Exists||!old[0].HasValue||string.IsNullOrEmpty(old[0].RawValue)))continue;
                    if(old.Length!=1||old[0].ReadOnly||old[0].StorageType!="String")throw new InvalidOperationException("旧分类来源无法安全清理："+source);
                    changes.Add(Change(element.UniqueId+"|cleanup|"+source,old[0],"Text","","清理旧分类实例值，保留参数定义和类型值",true));
                }
            }
        }
        foreach(var original in basis.Fields) {
            var field=rowsById[original.ElementUniqueId].Fields.Single(f=>f.Source==original.Source&&f.PropertySet==original.PropertySet&&f.Property==original.Property&&f.Binding==original.Binding);
            var draft=draftsByKey.GetValueOrDefault(original.Key);
            if(!original.Editable)continue;
            var current=Captured(captured[original.OwnerUniqueId+"|"+original.Source].ToArray(),original.OwnerUniqueId,original.Source);
            var element=elementsById[original.ElementUniqueId];
            var classification=!string.IsNullOrEmpty(element.AssignedSlot)&&element.AssignedSlot!=element.DeclaredSlot;
            var sync=field.FloorAreaOverride&&(includeSuggestions||allDrafts.Any(d=>fieldsByKey[d.FieldKey].PropertySet==FloorAreaPolicy.FloorSet));
            var required=includeSuggestions&&!string.IsNullOrEmpty(field.RequiredValue)&&field.Value!=field.RequiredValue;
            if(draft==null&&!includeSuggestions&&!sync&&!(classification&&!current.Exists))continue;
            var value=draft!=null?Normalize(field.RawType,draft.Value):required?field.RequiredValue:sync?field.SuggestedValue:includeSuggestions?field.SuggestedValue:null;
            if(draft==null&&!sync&&!required&&current.HasValue&&!string.IsNullOrEmpty(current.RawValue)) {
                if(field.RawType!="Boolean"||current.DefinitionType=="YesNo")continue;
                value=BooleanPolicy.Normalize(current.RawValue);
            }
            if(value==null&&current.Exists)continue;
            if(field.RawType=="Boolean"&&value==null)value=DefaultBoolean(basis,field.Source,original.ElementName,field.OwnerUniqueId);
            if(value!=null) {
                var error=ModelChecks.InputError(field,value,PlanningInputs.ParentValue(rowsById[original.ElementUniqueId].Fields,field.PropertySet,true));
                if(error!=null)throw new InvalidOperationException(field.Property+"："+error);
            }
            if(value==""&&current.Exists&&current.StorageType!="String")throw new InvalidOperationException("数值/复选框参数不能安全清空；原值保留。");
            var unit=draft!=null?imported.Units.GetValueOrDefault(original.Key)??PropertyUnits.Canonical(field.Property,field.RawType):field.SuggestionUnit??current.Unit;
            if(current.Exists&&current.HasValue&&SameValue(current,value,unit)&&!(field.RawType=="Boolean"&&current.DefinitionType!="YesNo"))continue;
            var change=Change(original.Key,current,field.RawType,value,draft!=null?"人工修改":field.SuggestionReason??field.Detail,draft!=null);
            change.PropertySet=field.PropertySet;change.AffectedInstances=original.AffectedInstances;
            change.Unit=unit;
            changes.Add(change);
        }
        var unique=changes.GroupBy(c=>c.OwnerUniqueId+"|"+c.Source).Select(g=>{
            if(g.Select(c=>c.Value).Distinct().Count()>1)throw new InvalidOperationException("同一来源存在不同拟写值，请核对类型影响范围。");return g.First();
        }).ToArray();
        var settingsWrite=SettingsWrite(basis);
        var dataWrites=settingsWrite==null?imported.Writes:imported.Writes.Append(settingsWrite).ToArray();
        if(dataWrites.GroupBy(w=>w.SchemaId).Any(g=>g.Count()!=1))throw new InvalidOperationException("同轮模型资料存在重复的 Schema 写入。");
        var plan=new EditPlan{Context=basis.Context,Id=Guid.NewGuid().ToString("N"),CaptureHash=basis.CaptureHash,ExpiresAtUtc=nowUtc.AddSeconds(120),Changes=unique,
            Native=new NativeOperations{Definitions=DefinitionOperations(basis,unique),DataWrites=dataWrites}};
        plan.Hash=Canonical.PlanHash(plan);return plan;
    }
    private static bool SameValue(ParameterValue current,string value,string unit)
    {
        // A metric draft cannot be compared with Revit's feet/square-feet raw text.
        // Number parameters already contain the business quantity and keep their raw comparison.
        if(current.StorageType=="Double"&&(unit=="m"||unit=="m²")&&(current.DefinitionType=="Length"||current.DefinitionType=="Area"))
            return unit==(current.DefinitionType=="Area"?"m²":"m")&&current.MetricValue.HasValue
                &&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var proposed)&&double.IsFinite(proposed)
                &&proposed==current.MetricValue.Value;
        return value==current.RawValue;
    }
    private static ParameterValue Captured(ParameterValue[] values,string owner,string source)
    {
        var current=values.Where(v=>v.OwnerUniqueId==owner&&v.Source==source).ToArray();
        if(current.Length!=1)throw new InvalidOperationException("参数未采集或来源有歧义："+source);return current[0];
    }
    private static WriteChange Change(string key,ParameterValue current,string rawType,string value,string reason,bool manual) => new() {
        FieldKey=key,OwnerId=current.OwnerId,OwnerUniqueId=current.OwnerUniqueId,Source=current.Source,Origin=current.Origin,RawType=rawType,
        ExpectedSignature=Canonical.ValuesHash(new[]{current}),ExpectedValue=current.RawValue,Value=value,Action=!current.Exists?"Create":value==""?"Clear":"Set",Reason=reason,Manual=manual,AffectedInstances=1,
        DefinitionType=rawType=="Boolean"?"YesNo":current.Exists?current.DefinitionType:"Text" };
    private static NativeDefinitionOperation[] DefinitionOperations(CheckResult basis,WriteChange[] changes,bool bridges=false)
    {
        var operations=new List<NativeDefinitionOperation>();
        var owners=basis.Elements.SelectMany(e=>new[]{new{Id=e.Element.UniqueId,Element=e.Element},new{Id=e.Element.TypeUniqueId,Element=e.Element}}).Where(e=>e.Id!=null).GroupBy(e=>e.Id).ToDictionary(g=>g.Key,g=>g.First().Element,StringComparer.Ordinal);
        foreach(var group in changes.GroupBy(c=>c.Source)) {
            var first=group.First();var source=first.Source;
            if(!basis.DefinitionSources.Contains(source))throw new InvalidOperationException("参数定义未采集："+source);
            if(group.Select(c=>c.Origin).Distinct().Count()!=1)throw new InvalidOperationException("同名参数不能混合实例/类型绑定。");
            var boolean=!bridges&&IsBooleanSource(source,basis.Settings.RuleProfileId);
            var guid=bridges?first.SourceGuid:(boolean?BooleanPolicy.SourceGuid(source):ReferencePolicy.SourceGuid(source)).ToString("D");
            foreach(var c in group){c.SourceGuid=guid;c.DefinitionType=boolean?"YesNo":bridges?"Text":c.DefinitionType;}
            var existing=basis.Definitions.Where(d=>d.Source==source).ToArray();
            if(existing.Length>1)throw new InvalidOperationException("存在多个同名参数定义："+source);
            var definition=existing.SingleOrDefault();
            var migration=boolean&&definition!=null&&definition.DefinitionType!="YesNo";
            if(migration&&(definition.Guid!=ReferencePolicy.SourceGuid(source).ToString("D")||definition.DefinitionType!="Text"||definition.Origin!=first.Origin))throw new InvalidOperationException("旧是否参数并非本插件可迁移的文字项目绑定："+source);
            if(migration&&definition.Dependencies.Length>0)throw new InvalidOperationException("旧是否参数被引用，无法安全迁移："+source);
            var categories=group.Select(c=>owners[c.OwnerUniqueId].CategoryId).Concat(definition?.CategoryIds??[]).Distinct().OrderBy(i=>i).ToArray();
            if(definition!=null&&!migration&&(definition.Guid!=guid||definition.Origin!=first.Origin||boolean&&definition.DefinitionType!="YesNo")) {
                // Existing unrelated fields may be assigned, but their definitions must never be replaced or rebound.
                if(group.Any(c=>c.Action=="Create")||boolean||bridges)throw new InvalidOperationException("现有参数定义与拟创建定义不兼容："+source);
                continue;
            }
            if(!migration&&!group.Any(c=>c.Action=="Create")&&!bridges)continue;
            var migrated=migration?(definition.Values??[]).Select(v=>{
                if(!v.Exists||v.ReadOnly||v.Source!=source||v.Origin!=definition.Origin||v.SharedGuid!=definition.Guid||v.DefinitionType!="Text")throw new InvalidOperationException("迁移绑定中存在不可靠或只读值："+source);
                var proposed=group.SingleOrDefault(c=>c.OwnerUniqueId==v.OwnerUniqueId);
                var owner=owners.GetValueOrDefault(v.OwnerUniqueId);
                var value=proposed?.Value??(v.HasValue&&!string.IsNullOrWhiteSpace(v.RawValue)?BooleanPolicy.Normalize(v.RawValue):DefaultBoolean(basis,source,owner?.Name,v.OwnerUniqueId));
                return new NativeMigrationValue{Source=source,OwnerId=v.OwnerId,OwnerUniqueId=v.OwnerUniqueId,ExpectedSignature=Canonical.ValuesHash(new[]{v}),Value=value,HasValue=true};
            }).ToArray():[];
            if(migration&&group.Any(c=>!migrated.Any(v=>v.OwnerUniqueId==c.OwnerUniqueId)))throw new InvalidOperationException("迁移没有覆盖拟写载体。");
            var alias=boolean?BooleanPolicy.LegacySource(source):null;
            if(!migration&&alias!=null) {
                if(!basis.DefinitionSources.Contains(alias))throw new InvalidOperationException("旧布尔别名定义未采集："+alias);
                var oldDefinitions=basis.Definitions.Where(d=>d.Source==alias).ToArray();
                if(oldDefinitions.Length>1||oldDefinitions.Any(d=>d.Origin!=first.Origin))throw new InvalidOperationException("旧布尔别名定义存在歧义或绑定类型不匹配："+alias);
                var old=oldDefinitions.SingleOrDefault();
                if(old!=null) {
                    migrated=old.Values.Where(v=>!((definition?.Values??[]).Any(target=>target.OwnerUniqueId==v.OwnerUniqueId&&target.Exists)))
                        .Where(v=>owners.TryGetValue(v.OwnerUniqueId,out var item)&&categories.Contains(item.CategoryId))
                        .Select(v=>{
                            if(!v.Exists||v.Source!=alias||v.Origin!=first.Origin)throw new InvalidOperationException("旧布尔别名值不可靠："+alias);
                            var owner=owners[v.OwnerUniqueId];
                            var proposed=group.SingleOrDefault(c=>c.OwnerUniqueId==v.OwnerUniqueId);
                            var value=proposed?.Value??(v.HasValue&&!string.IsNullOrWhiteSpace(v.RawValue)?BooleanPolicy.Normalize(v.RawValue):DefaultBoolean(basis,source,owner.Name,v.OwnerUniqueId));
                            return new NativeMigrationValue{Source=alias,OwnerId=v.OwnerId,OwnerUniqueId=v.OwnerUniqueId,ExpectedSignature=Canonical.ValuesHash(new[]{v}),Value=value,HasValue=true};
                        }).ToArray();
                }
                // Definition snapshots are the only complete category-wide evidence. A family-only alias cannot be copied safely.
                if(old==null&&basis.Fields.Any(f=>f.Source==source&&f.Detail?.Contains("复制旧来源")==true))throw new InvalidOperationException("旧布尔别名未提供完整项目绑定，不能仅复制所选构件："+alias);
            }
            operations.Add(new NativeDefinitionOperation{Source=source,Guid=guid,DefinitionType=boolean?"YesNo":bridges?"Text":definition?.DefinitionType??"Text",Origin=first.Origin,CategoryIds=categories,
                ExpectedBindingHash=Canonical.DefinitionSetHash(source,basis.Definitions),ReplaceParameterId=migration?definition.ParameterId:null,MigrationValues=migrated});
        }
        return operations.ToArray();
    }
    private static string Normalize(string rawType,string value)
    {
        if(value==null)throw new InvalidOperationException("拟写值缺失；清空请提供空字符串。");
        if(rawType=="Boolean")return BooleanPolicy.Normalize(value);
        if(value==""||rawType=="Label"||rawType=="Text")return value;
        if(rawType=="Integer"&&int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var integer))return integer.ToString(CultureInfo.InvariantCulture);
        if(rawType=="Real"&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number))return number.ToString("R",CultureInfo.InvariantCulture);
        throw new InvalidOperationException("拟写值不符合字段类型："+rawType);
    }
}
