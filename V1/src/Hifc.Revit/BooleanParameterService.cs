using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit
{
    // Business sources are YesNo. Temporary IFC routing parameters remain Text.
    internal static class BooleanParameterService
    {
        internal sealed class OwnerValue
        {
            public int Id { get; set; }
            public string UniqueId { get; set; }
            public string Value { get; set; }
        }
        internal sealed class Migration
        {
            public string Source { get; set; }
            public int ParameterId { get; set; }
            public bool Required { get; set; }
            public bool TypeBinding { get; set; }
            public int[] CategoryIds { get; set; } = new int[0];
            public OwnerValue[] Values { get; set; } = new OwnerValue[0];
            public string[] References { get; set; } = new string[0];
            internal BuiltInParameterGroup Group;
        }
        internal static bool IsSource(Document doc, string source) => BooleanPolicy.IsSource(source, Management.PlatformContext.ProfileId(doc));
        internal static Guid SourceGuid(Document doc, string source) => IsSource(doc,source) ? BooleanPolicy.SourceGuid(source) : ReferencePolicy.SourceGuid(source);
        internal static string DefaultValue(Document doc, Element owner, string source)
        {
            if(source!="建筑基底")return "0";
            var state = new Management.ManagementStateStore(Path.Combine(RuntimePaths.OutputDirectory(),"management-profiles"), Management.ModelReader.Key(doc)).Load();
            var baseId = state.Underground?.BaseLevelUniqueId;
            if (!string.IsNullOrEmpty(baseId) && doc.GetElement(baseId) == null)
                throw new InvalidOperationException("已确认的基底楼层被删除，请重新确认后写入是否属性。");
            return BooleanPolicy.DefaultValue(source,owner.Name,owner.UniqueId,baseId);
        }
        internal static Element[] Owners(Document doc, IEnumerable<Category> categories, bool types)
        {
            var ids=categories.Select(c=>c.Id).Distinct().ToArray();
            if(ids.Length==0)return new Element[0];
            var collector=new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(ids));
            if(types)collector.WhereElementIsElementType();else collector.WhereElementIsNotElementType();
            return collector.OrderBy(e=>e.UniqueId,StringComparer.Ordinal).ToArray();
        }
        internal static Migration Inspect(Document doc, string source)
        {
            var result=new Migration {Source=source};
            if(!IsSource(doc,source))return result;
            var definitions=new FilteredElementCollector(doc).OfClass(typeof(ParameterElement)).Cast<ParameterElement>().Where(p=>p.Name==source).ToArray();
            if(definitions.Length>1)throw new InvalidOperationException("是否参数存在多个同名定义，需先统一来源："+source);
            if(definitions.Length==0)return result;
            var definition=definitions[0].GetDefinition();
            if(definition.ParameterType==ParameterType.YesNo)return result;
            var shared=definitions[0] as SharedParameterElement;
            if(shared==null || shared.GuidValue!=ReferencePolicy.SourceGuid(source) || definition.ParameterType!=ParameterType.Text)
                throw new InvalidOperationException("是否属性来自其他软件、族或非文字参数，不能安全自动迁移："+source+"。请先统一为本插件来源。");
            var binding=doc.ParameterBindings.get_Item(definition) as ElementBinding;
            if(binding==null)throw new InvalidOperationException("旧是否参数没有可核验的项目绑定："+source);
            result.Required=true;result.ParameterId=shared.Id.IntegerValue;result.TypeBinding=binding is TypeBinding;result.Group=definition.ParameterGroup;
            var categories=binding.Categories.Cast<Category>().ToArray();result.CategoryIds=categories.Select(c=>c.Id.IntegerValue).OrderBy(i=>i).ToArray();
            var references=new List<string>();
            foreach(var schedule in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                var defs=new List<ScheduleDefinition>{schedule.Definition};
                if(schedule.Definition.HasEmbeddedSchedule)defs.Add(schedule.Definition.EmbeddedDefinition);
                if(defs.Any(d=>d.GetFieldOrder().Any(id=>d.GetField(id).ParameterId==shared.Id)))references.Add("明细表："+schedule.Name);
            }
            foreach(var filter in new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                if(filter.GetElementFilterParameters().Contains(shared.Id))references.Add("视图过滤器："+filter.Name);
            result.Values=Owners(doc,categories,result.TypeBinding).Select(owner=>{
                var parameters=owner.GetParameters(source);
                if(parameters.Count!=1 || parameters[0].Id!=shared.Id || parameters[0].IsReadOnly)
                    throw new InvalidOperationException("类别内存在同名族参数、只读值或缺失值，不能安全迁移："+source+"（"+owner.Id.IntegerValue+"）");
                var parameter=parameters[0];
                if(parameter.GetAssociatedGlobalParameter()!=ElementId.InvalidElementId)references.Add("全局参数关联："+owner.Id.IntegerValue);
                var raw=parameter.HasValue?parameter.AsString():null;
                return new OwnerValue {Id=owner.Id.IntegerValue,UniqueId=owner.UniqueId,Value=string.IsNullOrWhiteSpace(raw)?DefaultValue(doc,owner,source):BooleanPolicy.Normalize(raw)};
            }).ToArray();
            result.References=references.Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            if(result.References.Length>0)throw new InvalidOperationException("是否参数被引用，不能自动替换定义："+source+"；"+string.Join("、",result.References));
            return result;
        }
        internal static Migration InspectOwner(Document doc,Element owner,string source)
        {
            if(!OwnerNeedsMigration(doc,owner,source))return new Migration {Source=source};
            var migration=Inspect(doc,source);
            if(!migration.Required||!migration.Values.Any(v=>v.Id==owner.Id.IntegerValue&&v.UniqueId==owner.UniqueId))throw new InvalidOperationException("是否属性不属于已捕获的项目绑定，无法迁移："+source);
            return migration;
        }
        internal static bool OwnerNeedsMigration(Document doc,Element owner,string source)
        {
            var parameters=owner.GetParameters(source);
            if(parameters.Count==0||parameters.Count==1&&parameters[0].Definition.ParameterType==ParameterType.YesNo)return false;
            if(parameters.Count!=1)throw new InvalidOperationException("是否属性存在同名来源，不能安全迁移："+source);
            var parameter=parameters[0];var known=SharedParameterElement.Lookup(doc,ReferencePolicy.SourceGuid(source));
            var binding=known==null?null:doc.ParameterBindings.get_Item(known.GetDefinition()) as ElementBinding;
            if(!parameter.IsShared||parameter.GUID!=ReferencePolicy.SourceGuid(source)||parameter.Definition.ParameterType!=ParameterType.Text||known==null||parameter.Id!=known.Id||
                binding==null||owner.Category==null||!binding.Categories.Contains(owner.Category)||(binding is TypeBinding)!=(owner is ElementType))
                throw new InvalidOperationException("是否属性是族/外部文字参数，无法迁移为原生复选框，请先统一来源："+source);
            return true;
        }
        internal static void Migrate(Document doc, string source, string outputDirectory)
        {
            var plan=Inspect(doc,source);if(!plan.Required)return;
            // Probe deletion inside a rollback-only subtransaction. Extra deleted
            // dependents are a hard stop, never silently removed with the definition.
            using(var probe=new SubTransaction(doc))
            {
                probe.Start();
                try { RemoveOldDefinition(doc,plan); }
                finally { probe.RollBack(); }
            }
            var categories=plan.CategoryIds.Select(id=>Category.GetCategory(doc,new ElementId(id))).ToArray();
            RemoveOldDefinition(doc,plan);doc.Regenerate();
            RevitMappingService.EnsureDefinitionsWithCategories(doc,Owners(doc,categories,plan.TypeBinding),new[]{new ParameterDefinition {Name=source,Guid=BooleanPolicy.SourceGuid(source),Group=plan.Group}},outputDirectory,plan.TypeBinding,categories);
            doc.Regenerate();
            foreach(var value in plan.Values)
            {
                var owner=doc.GetElement(new ElementId(value.Id));
                if(owner==null || owner.UniqueId!=value.UniqueId)throw new InvalidOperationException("迁移载体发生变化："+source);
                var parameter=owner.GetParameters(source).Single();
                if(parameter.Definition.ParameterType!=ParameterType.YesNo || !parameter.Set(value.Value=="1"?1:0) || parameter.AsInteger()!=(value.Value=="1"?1:0))
                    throw new InvalidOperationException("是否参数迁移回读失败："+source);
            }
        }
        static void RemoveOldDefinition(Document doc,Migration plan)
        {
            var element=(SharedParameterElement)doc.GetElement(new ElementId(plan.ParameterId));
            // Revit 2020 owns a bare, unnamed Element for the parameter binding.
            // Unbind first and permit only that verified infrastructure node;
            // public/model dependents and any extra definition deletion still block.
            var dependents=element.GetDependentElements(null).Where(id=>id.IntegerValue!=plan.ParameterId)
                .Select(id=>doc.GetElement(id)).Where(e=>e!=null)
                .Select(e=>new {Id=e.Id,BindingNode=e.GetType()==typeof(Element)&&e.Category==null&&string.IsNullOrEmpty(e.Name)}).ToArray();
            if(!doc.ParameterBindings.Remove(element.GetDefinition()))throw new InvalidOperationException("旧是否参数解绑失败："+plan.Source);
            doc.Regenerate();
            if(dependents.Any(e=>doc.GetElement(e.Id)==null&&!e.BindingNode))
                throw new InvalidOperationException("旧是否参数解绑影响了依赖对象，迁移已阻止："+plan.Source);
            var deleted=doc.Delete(new ElementId(plan.ParameterId));
            if(deleted.Any(id=>id.IntegerValue!=plan.ParameterId))
                throw new InvalidOperationException("旧是否参数还有依赖对象，迁移已阻止："+plan.Source);
        }
    }
}
