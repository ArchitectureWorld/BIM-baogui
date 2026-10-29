using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit
{
    // Legacy names shared by a Boolean and a Real/Label must never be retyped.
    // Copy only verified Boolean owners to the new physical source; keep the
    // original definition, binding, values and references intact.
    internal static class BooleanAliasService
    {
        internal sealed class Value
        {
            public int Id { get; set; }
            public string UniqueId { get; set; }
            public string Slot { get; set; }
            public string Canonical { get; set; }
            public ParameterRead[] LegacyReads { get; set; }
            internal Parameter Parameter;
        }
        internal sealed class Plan
        {
            public string Source { get; set; }
            public string Legacy { get; set; }
            public int[] CategoryIds { get; set; } = new int[0];
            public Value[] Values { get; set; } = new Value[0];
            public bool Required => Values.Length>0;
        }
        internal static ParameterRead[] LegacyReads(Document doc,Element owner,string source)
        {
            var legacy=BooleanPolicy.LegacySource(source);
            return legacy==null||owner==null?new ParameterRead[0]:RevitMappingService.Reads(doc,owner,legacy);
        }
        static Value Read(Document doc,Element owner,string source)
        {
            var legacy=BooleanPolicy.LegacySource(source);
            var reads=LegacyReads(doc,owner,source);
            var result=new Value {Id=owner.Id.IntegerValue,UniqueId=owner.UniqueId,Slot=RevitMappingService.Slot(doc,owner),LegacyReads=reads,Canonical="0"};
            if(reads.Length==0)return result;
            if(reads.Length!=1||reads[0].Origin!="instance")throw new InvalidOperationException("旧布尔来源有同名或类型歧义，不能复制："+legacy+"（"+owner.Id.IntegerValue+"）");
            var parameter=owner.GetParameters(legacy).Single();
            var known=SharedParameterElement.Lookup(doc,ReferencePolicy.SourceGuid(legacy));
            var binding=known==null?null:doc.ParameterBindings.get_Item(known.GetDefinition()) as ElementBinding;
            if(!parameter.IsShared||parameter.GUID!=ReferencePolicy.SourceGuid(legacy)||
                parameter.Definition.ParameterType!=ParameterType.Text&&parameter.Definition.ParameterType!=ParameterType.YesNo||
                known==null||parameter.Id!=known.Id||!(binding is InstanceBinding)||owner.Category==null||!binding.Categories.Contains(owner.Category))
                throw new InvalidOperationException("旧布尔来源不是可核验的插件文字/是否参数："+legacy+"（"+owner.Id.IntegerValue+"）");
            result.Parameter=parameter;
            result.Canonical=BooleanPolicy.Normalize(ReferencePolicy.ToBridgeValue(reads[0]));
            return result;
        }
        internal static bool NeedsCopy(Document doc,Element owner,string source)
        {
            if(BooleanPolicy.LegacySource(source)==null||owner.GetParameters(source).Count>0)return false;
            Read(doc,owner,source);return true;
        }
        internal static void PopulateSuggestion(Document doc,Element owner,FieldCheck field)
        {
            if(field.RawType!="Boolean"||field.State!="缺参数"||BooleanPolicy.LegacySource(field.Source)==null)return;
            field.SuggestedValue=null;
            try {
                var value=Read(doc,owner,field.Source);
                field.SuggestedValue=value.Canonical;
                field.SuggestionReason="独立原生复选框；核验并复制旧来源“"+BooleanPolicy.LegacySource(field.Source)+"”，保留旧定义及其他业务数据";
            }catch(InvalidOperationException error){field.State="类型待核验";field.Editable=false;field.Detail=error.Message;}
        }
        internal static Plan Inspect(Document doc,string source,Element[] requested)
        {
            var plan=new Plan {Source=source,Legacy=BooleanPolicy.LegacySource(source)};
            if(plan.Legacy==null)return plan;
            var missing=requested.Where(e=>e.GetParameters(source).Count==0).ToArray();
            if(missing.Length==0)return plan;
            if(missing.Any(e=>e is ElementType||e.Category==null))throw new InvalidOperationException("独立布尔来源需要可核验的实例类别："+source);
            var categories=missing.Select(e=>e.Category).GroupBy(c=>c.Id.IntegerValue).Select(g=>g.First()).ToArray();
            var explicitIds=new HashSet<int>(requested.Select(e=>e.Id.IntegerValue));
            var values=new List<Value>();
            foreach(var owner in BooleanParameterService.Owners(doc,categories,false)) {
                var existing=owner.GetParameters(source);
                if(existing.Count>0) {
                    if(existing.Count!=1||!existing[0].IsShared||existing[0].GUID!=BooleanPolicy.SourceGuid(source)||existing[0].Definition.ParameterType!=ParameterType.YesNo)
                        throw new InvalidOperationException("独立布尔来源已有不兼容定义："+source+"（"+owner.Id.IntegerValue+"）");
                    continue;
                }
                var value=Read(doc,owner,source);
                if(value.LegacyReads.Any(r=>r.HasValue&&!string.IsNullOrWhiteSpace(ReferencePolicy.ToBridgeValue(r)))&&!explicitIds.Contains(owner.Id.IntegerValue)) {
                    bool applicable=false;
                    try {applicable=RevitMappingService.Properties(doc,owner).Any(p=>p.SourceParameter==source&&p.ExportType=="Boolean");}catch(InvalidOperationException){ }
                    if(!applicable)throw new InvalidOperationException("同类别构件 "+owner.Id.IntegerValue+" 的旧来源“"+plan.Legacy+"”业务含义未确认；请先明确分类，不能默认覆盖为否。");
                }
                values.Add(value);
            }
            plan.CategoryIds=categories.Select(c=>c.Id.IntegerValue).OrderBy(i=>i).ToArray();plan.Values=values.OrderBy(v=>v.UniqueId,StringComparer.Ordinal).ToArray();
            CheckReferences(doc,plan);
            return plan;
        }
        static void CheckReferences(Document doc,Plan plan)
        {
            var oldIds=new HashSet<int>(plan.Values.Where(v=>v.Parameter!=null).Select(v=>v.Parameter.Id.IntegerValue));
            if(oldIds.Count==0)return;
            foreach(var schedule in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()) {
                var definitions=new List<ScheduleDefinition>{schedule.Definition};
                if(schedule.Definition.HasEmbeddedSchedule)definitions.Add(schedule.Definition.EmbeddedDefinition);
                foreach(var definition in definitions)
                    if((definition.CategoryId==ElementId.InvalidElementId||plan.CategoryIds.Contains(definition.CategoryId.IntegerValue))&&
                        definition.GetFieldOrder().Any(id=>oldIds.Contains(definition.GetField(id).ParameterId.IntegerValue)))
                        throw new InvalidOperationException("旧布尔来源仍被明细表引用，需先处理引用再切换复选框："+schedule.Name);
            }
            foreach(var filter in new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                if(filter.GetCategories().Any(c=>plan.CategoryIds.Contains(c.IntegerValue))&&filter.GetElementFilterParameters().Any(id=>oldIds.Contains(id.IntegerValue)))
                    throw new InvalidOperationException("旧布尔来源仍被视图过滤器引用："+filter.Name);
            if(plan.Values.Any(v=>v.Parameter!=null&&v.Parameter.GetAssociatedGlobalParameter()!=ElementId.InvalidElementId))
                throw new InvalidOperationException("旧布尔来源有关联的全局参数，不能静默切换来源："+plan.Legacy);
        }
        internal static void Apply(Document doc,Plan plan,string directory)
        {
            if(!plan.Required)return;
            var owners=plan.Values.Select(v=>doc.GetElement(new ElementId(v.Id))).ToArray();
            RevitMappingService.EnsureDefinitionsWithCategories(doc,owners,new[]{new ParameterDefinition {Name=plan.Source,Guid=BooleanPolicy.SourceGuid(plan.Source)}},directory,false,
                plan.CategoryIds.Select(id=>Category.GetCategory(doc,new ElementId(id))).ToArray());
            doc.Regenerate();
            foreach(var value in plan.Values) {
                var owner=doc.GetElement(new ElementId(value.Id));
                if(owner==null||owner.UniqueId!=value.UniqueId)throw new InvalidOperationException("旧布尔复制载体变化："+plan.Source);
                var parameter=owner.GetParameters(plan.Source).Single();var next=value.Canonical=="1"?1:0;
                if((!parameter.HasValue||parameter.AsInteger()!=next)&&!parameter.Set(next)||parameter.AsInteger()!=next)
                    throw new InvalidOperationException("独立布尔复制回读失败："+plan.Source);
            }
        }
        internal static void RequireWrittenForExport(Document doc,Element owner,string source)
        {
            if(BooleanPolicy.LegacySource(source)==null||owner.GetParameters(source).Count>0)return;
            var value=Read(doc,owner,source);
            if(value.LegacyReads.Length>0)throw new InvalidOperationException("旧来源“"+BooleanPolicy.LegacySource(source)+"”需先在属性管理中核验并点“4 写入 Revit”复制为“"+source+"”；不会以默认否替代已有值。");
        }
    }
}
