using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit
{
    internal sealed class ParameterDefinition
    {
        internal string Name;
        internal Guid Guid;
        internal bool Temporary;
        internal BuiltInParameterGroup Group = BuiltInParameterGroup.PG_DATA;
    }

    internal static class RevitMappingService
    {
        internal static readonly ReferenceCatalog Catalog = ReferenceCatalog.FromEmbeddedBaseline();
        internal const string SlotName = "类目";

        internal static ParameterRead[] Reads(Document doc, Element element, string name)
        {
            var parameters = element.GetParameters(name);
            if (name == "标高" && element is FamilyInstance
                && element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Windows
                && PlatformRules.IsActive(Management.PlatformContext.ProfileId(doc)) && parameters.Count == 2)
            {
                // Revit exposes the same window level through two built-in parameters.
                // Normalize only this verified alias pair; custom or conflicting sources
                // remain separate observations and retain the usual ambiguity check.
                var family = parameters.FirstOrDefault(p => p.Id.IntegerValue == (int)BuiltInParameter.FAMILY_LEVEL_PARAM);
                var schedule = parameters.FirstOrDefault(p => p.Id.IntegerValue == (int)BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (family?.HasValue == true && schedule?.HasValue == true
                    && family.StorageType == StorageType.ElementId && schedule.StorageType == StorageType.ElementId
                    && family.AsElementId() == schedule.AsElementId() && family.AsElementId() == element.LevelId
                    && doc.GetElement(family.AsElementId()) is Level)
                    parameters = new List<Parameter> { family };
            }
            var result = ReadParameters(parameters, name, "instance").ToList();
            var type = doc.GetElement(element.GetTypeId());
            if (type != null) result.AddRange(ReadOwner(type, name, "type"));
            return result.ToArray();
        }

        internal static IEnumerable<ParameterRead> ReadOwner(Element element, string name, string origin)
            => ReadParameters(element.GetParameters(name), name, origin);

        private static IEnumerable<ParameterRead> ReadParameters(IEnumerable<Parameter> parameters, string name, string origin)
        {
            foreach (var parameter in parameters)
            {
                object value = null;
                if (parameter.HasValue)
                {
                    switch (parameter.StorageType)
                    {
                        case StorageType.String: value = parameter.AsString(); break;
                        case StorageType.Integer: value = parameter.AsInteger(); break;
                        case StorageType.Double: value = parameter.AsDouble(); break;
                        case StorageType.ElementId: value = parameter.AsElementId().IntegerValue; break;
                    }
                }
                yield return new ParameterRead { Name = name, Origin = origin, HasValue = parameter.HasValue, Value = value,
                    StorageType = parameter.StorageType.ToString(), DefinitionType = parameter.Definition.ParameterType.ToString(),
                    ParameterGuid = parameter.IsShared ? parameter.GUID.ToString("D") : null,
                    DisplayValue = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString(), ReadOnly = parameter.IsReadOnly };
            }
        }

        internal static string Slot(Document doc, Element element)
        { return ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(Reads(doc, element, SlotName), SlotName)); }

        internal static string Entity(Document doc, Element element)
            => CaseWorkflow.ClassificationEntity(new ModelElementCheck { SourceEntity = SourceEntity(doc, element) }, ReferenceCatalog.LegacySlot(Slot(doc, element) ?? ""));

        internal static string SourceEntity(Document doc, Element element)
        {
            if (element is ProjectInfo) return "IfcBuilding";
            if (element is Level) return "IfcBuildingStorey";
            if (element is Area) return "IfcSpace";
            if (element is Autodesk.Revit.DB.Architecture.TopographySurface) return "IfcSite";
            var explicitType = ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(Reads(doc, element, "IfcExportAs"), "IfcExportAs"));
            if (!string.IsNullOrWhiteSpace(explicitType) && explicitType.StartsWith("Ifc", StringComparison.Ordinal))
                return explicitType.Split(new[] { '.', ':' }, 2)[0];
            var category = element.Category;
            if (category == null) return "UNKNOWN";
            switch ((BuiltInCategory)category.Id.IntegerValue)
            {
                case BuiltInCategory.OST_Floors: return "IfcSlab";
                case BuiltInCategory.OST_Walls: return "IfcWall";
                case BuiltInCategory.OST_Roofs: return "IfcRoof";
                case BuiltInCategory.OST_Doors: return "IfcDoor";
                case BuiltInCategory.OST_Windows: return "IfcWindow";
                case BuiltInCategory.OST_Rooms: return "IfcSpace";
                case BuiltInCategory.OST_GenericModel: return "IfcBuildingElementProxy";
                case BuiltInCategory.OST_StairsRuns: return "IfcStairFlight";
                default: return "UNKNOWN";
            }
        }

        internal static ReferenceProperty[] Properties(Document doc, Element element)
        {
            var profileId = Management.PlatformContext.ProfileId(doc);
            if(element is ProjectInfo || element is Level)
            {
                var names=element is ProjectInfo?new[]{"Pset_申报信息属性集","Pset_建筑技术信息属性集"}:new[]{"Pset_建筑楼层信息属性集"};
                return CaseWorkflow.ForModel(Management.PlatformContext.Stage(doc) ?? "报规","单体建筑",profileId).SelectMany(r=>r.Properties).Where(p=>!p.NativeGroup && p.Binding=="实例" && names.Contains(p.PropertySet))
                    .GroupBy(p=>ReferencePolicy.BridgeGuid(p)).Select(g=>g.First()).ToArray();
            }
            var slot = Slot(doc, element);
            if(IsAreaSlot(slot, profileId) && !(element is Area))throw new InvalidOperationException("构件 "+element.Id.IntegerValue+"：该报规区域类目需要面积（Area）载体。");
            var entity = Entity(doc, element);
            var properties = CaseExportPolicy.Properties(entity, slot, HasTypeEvidence(doc, element), profileId);
            if (properties.Length == 0 && !string.IsNullOrWhiteSpace(slot))
                throw new InvalidOperationException("构件 " + element.Id.IntegerValue + "：类目 " + slot + " 与 IFC 实体 " + entity + " 不匹配。本版请先选择支持的构件类别。");
            return properties.GroupBy(p => ReferencePolicy.BridgeGuid(p)).Select(g => g.First()).ToArray();
        }
        internal static bool HasTypeEvidence(Document doc, Element element)
        {
            if (SourceEntity(doc, element) != "IfcSlab") return false;
            var slot = Slot(doc, element);
            if (ReferenceCatalog.LegacySlot((slot ?? "").Trim()) != "总平构件") return false;
            var type = doc.GetElement(element.GetTypeId());
            var read = type == null ? null : ReferencePolicy.Resolve(ReadOwner(type, "构件类型", "type"), "构件类型");
            return CaseExportPolicy.HasGeneralSiteTypeEvidence("IfcSlab", slot, ReferencePolicy.ToBridgeValue(read),
                Management.PlatformContext.ProfileId(doc));
        }

        private static bool IsAreaSlot(string slot, string profileId)
        {
            slot = ReferenceCatalog.LegacySlot((slot ?? "").Trim());
            return CaseWorkflow.AreaSlots.Contains(slot) || PlatformRules.IsActive(profileId) && slot == "室内停车场";
        }

        internal static object Capture(Document doc, Element[] elements)
        {
            return new
            {
                schema_version = "hifc.reference-observations/1.0", revit_version = doc.Application.VersionNumber,
                document_title = doc.Title, document_path = doc.PathName, reference_sha256 = ReferenceCatalog.BaselineSha256,
                value_convention = "Revit raw storage values; no assumed unit correction",
                elements = elements.OrderBy(e => e.UniqueId, StringComparer.Ordinal).Select(element => new
                {
                    id = element.Id.IntegerValue, unique_id = element.UniqueId, name = element.Name,
                    category = element.Category?.Name, ifc_entity_candidate = Entity(doc, element), slot = Slot(doc, element),
                    parameters = CaseExportPolicy.SourceProperties(Management.PlatformContext.ProfileId(doc)).Select(p => p.SourceParameter).Append(SlotName).Append("IfcExportAs").Append("IfcExportType").Distinct(StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.Ordinal).Select(name => new { name, observations = Reads(doc, element, name) }).ToArray()
                }).ToArray()
            };
        }

        internal static void Prepare(Document doc, Element[] elements, string slot, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(slot)) throw new InvalidOperationException("类目为空，请先选择明确的业务分类。");
            var profileId = Management.PlatformContext.ProfileId(doc);
            if (IsAreaSlot(slot, profileId) && elements.Any(e => !(e is Area)))
                throw new InvalidOperationException("该报规区域类目需要面积（Area）载体。");
            var properties = CaseExportPolicy.PropertiesForSlot(slot, profileId);
            foreach (var source in properties.Where(p=>p.ExportType=="Boolean").Select(p=>p.SourceParameter).Distinct())
            {
                foreach(var element in elements)BooleanParameterService.InspectOwner(doc,element,source);
                BooleanParameterService.Migrate(doc,source,outputDirectory);
                BooleanAliasService.Apply(doc,BooleanAliasService.Inspect(doc,source,elements),outputDirectory);
            }
            var definitions = properties.Select(p => p.SourceParameter).Append(SlotName).Distinct(StringComparer.Ordinal)
                .Where(name => name != "IfcExportAs" && name != "IfcExportType" && name != "IfcObjectType")
                .Where(name => elements.Any(e => Reads(doc, e, name).Length == 0 || name == SlotName && e.GetParameters(name).Count == 0))
                .Select(name => new ParameterDefinition { Name = name, Guid = ReferencePolicy.SourceGuid(name) }).ToArray();
            EnsureDefinitions(doc, elements, definitions, outputDirectory);
            doc.Regenerate();
            foreach (var element in elements)
            {
                var candidates = element.GetParameters(SlotName);
                if (candidates.Count != 1 || candidates[0].StorageType != StorageType.String || candidates[0].IsReadOnly)
                    throw new InvalidOperationException("构件 " + element.Id.IntegerValue + " 的类目参数有歧义或不可写。");
                if (!candidates[0].Set(slot) || candidates[0].AsString() != slot)
                    throw new InvalidOperationException("类目参数回读失败。");
            }
        }

        internal static void EnsureBridges(Document doc, Element[] elements, string outputDirectory)
        {
            var plan = elements.SelectMany(e => Properties(doc, e).Select(p => new { Element=e, Property=p })).ToArray();
            foreach (var binding in plan.GroupBy(x => x.Property.Binding))
            {
                var owners = binding.Select(x => binding.Key == "类型" ? doc.GetElement(x.Element.GetTypeId()) : x.Element)
                    .Where(e => e != null).GroupBy(e => e.Id.IntegerValue).Select(g => g.First()).ToArray();
                var definitions = binding.GroupBy(x => ReferencePolicy.BridgeGuid(x.Property))
                    .Select(g => new ParameterDefinition { Guid=g.Key, Name=ReferencePolicy.BridgeName(g.First().Property), Temporary=true }).ToArray();
                if (owners.Length > 0) EnsureDefinitions(doc, owners, definitions, outputDirectory, binding.Key == "类型");
            }
            doc.Regenerate();
        }

        internal static void EnsureDefinitions(Document doc, Element[] elements, ParameterDefinition[] definitions, string outputDirectory, bool typeSource = false)
            =>EnsureDefinitionsWithCategories(doc,elements,definitions,outputDirectory,typeSource,null);
        internal static void EnsureDefinitionsWithCategories(Document doc, Element[] elements, ParameterDefinition[] definitions, string outputDirectory, bool typeSource, Category[] bindingCategories)
        {
            var categories = (bindingCategories ?? elements.Select(e => e.Category).ToArray()).Where(c => c != null).GroupBy(c => c.Id.IntegerValue).Select(g => g.First()).ToArray();
            if (categories.Length == 0 || categories.Any(c => !c.AllowsBoundParameters))
                throw new InvalidOperationException("所选类别不支持共享参数绑定。");
            Directory.CreateDirectory(outputDirectory);
            var parameterPath = Path.Combine(outputDirectory, "binding-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(parameterPath, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tHIFC V1\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n", System.Text.Encoding.Unicode);
            var previous = doc.Application.SharedParametersFilename;
            try
            {
                doc.Application.SharedParametersFilename = parameterPath;
                var file = doc.Application.OpenSharedParameterFile();
                if (file == null) throw new InvalidOperationException("无法创建共享参数定义文件。");
                var group = file.Groups.get_Item("HIFC V1") ?? file.Groups.Create("HIFC V1");
                foreach (var item in definitions)
                {
                    var boolean = !item.Temporary && BooleanParameterService.IsSource(doc,item.Name);
                    var parameterType = boolean ? ParameterType.YesNo : ParameterType.Text;
                    var guid = boolean ? BooleanPolicy.SourceGuid(item.Name) : item.Guid;
                    foreach (var element in elements)
                        if (item.Temporary && element.GetParameters(item.Name).Any(p => !p.IsShared || p.GUID != item.Guid))
                            throw new InvalidOperationException("临时导出参数同名冲突：" + item.Name);
                    var existing = SharedParameterElement.Lookup(doc, guid);
                    Definition definition = existing?.GetDefinition();
                    if (definition != null && (definition.Name != item.Name || definition.ParameterType != parameterType))
                        throw new InvalidOperationException("共享参数 GUID 定义冲突：" + item.Name);
                    if (definition == null)
                    {
                        if(boolean&&new FilteredElementCollector(doc).OfClass(typeof(ParameterElement)).Any(e=>e.Name==item.Name))
                            throw new InvalidOperationException("存在其他 GUID 的同名是否定义，不能新增重复复选框："+item.Name+"。请先统一来源。");
                        if (!item.Temporary)
                        {
                            // New project bindings affect the whole category, including unselected families.
                            // Reject a name already supplied by another family/type definition.
                            var categoryIds = categories.Select(c => c.Id).ToArray();
                            var collector = new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(categoryIds));
                            if (typeSource) collector.WhereElementIsElementType(); else collector.WhereElementIsNotElementType();
                            foreach (var owner in collector)
                                if (Reads(doc, owner, item.Name).Length > 0)
                                    throw new InvalidOperationException("新增类别共享参数将与已有族/类型同名参数冲突：" + item.Name + "（构件 " + owner.Id.IntegerValue + "）。请先统一该参数来源。");
                        }
                        // Revit 2020's native IFC parameter cache excludes invisible definitions.
                        // Bridges exist only inside the export transaction, which is rolled back.
                        var options = new ExternalDefinitionCreationOptions(item.Name, parameterType)
                        { GUID = guid, Visible = true, UserModifiable = !item.Temporary, Description = item.Temporary ? "Temporary IFC routing value" : boolean ? "是否属性；未填写按否，建筑基底按确认楼层；可人工勾选" : "Reference mapping source; keep original parameter name" };
                        definition = group.Definitions.Create(options) as ExternalDefinition;
                    }
                    var set = doc.Application.Create.NewCategorySet();
                    var binding = doc.ParameterBindings.get_Item(definition);
                    if (binding != null && (binding is TypeBinding) != typeSource) throw new InvalidOperationException("共享参数实例/类型绑定冲突：" + item.Name);
                    if (binding is ElementBinding old) foreach (Category category in old.Categories) set.Insert(category);
                    foreach (var category in categories) set.Insert(category);
                    var newOwners = boolean ? BooleanParameterService.Owners(doc,set.Cast<Category>(),typeSource).Where(e=>e.GetParameters(item.Name).Count==0).Select(e=>e.UniqueId).ToArray() : new string[0];
                    Autodesk.Revit.DB.Binding instanceBinding = typeSource ? (Autodesk.Revit.DB.Binding)doc.Application.Create.NewTypeBinding(set) : doc.Application.Create.NewInstanceBinding(set);
                    bool ok = binding == null
                        ? doc.ParameterBindings.Insert(definition, instanceBinding, item.Group)
                        : doc.ParameterBindings.ReInsert(definition, instanceBinding, item.Group);
                    if (!ok) throw new InvalidOperationException("参数绑定失败：" + item.Name);
                    if(boolean) {
                        doc.Regenerate();
                        foreach(var id in newOwners) {
                            var owner=doc.GetElement(id);var parameter=owner.GetParameters(item.Name).Single();
                            var value=BooleanParameterService.DefaultValue(doc,owner,item.Name)=="1"?1:0;
                            if(!parameter.Set(value)||parameter.AsInteger()!=value)throw new InvalidOperationException("是否属性默认值回读失败："+item.Name);
                        }
                    }
                }
            }
            finally { doc.Application.SharedParametersFilename = previous; }
        }

        internal static object Materialize(Document doc, Element[] elements)
            => MaterializeWithFloorAreas(doc,elements,null);
        internal static object MaterializeWithFloorAreas(Document doc,Element[] elements,FloorAreaPlan floorAreas)
        {
            var writes = new List<object>();
            var written = new Dictionary<string, string>();
            var profileId = Management.PlatformContext.ProfileId(doc);
            foreach (var element in elements)
            {
                var properties = Properties(doc, element);
                foreach (var property in properties)
                {
                    var owner = property.Binding == "类型" ? doc.GetElement(element.GetTypeId()) : element;
                    if (owner == null) throw new InvalidOperationException("类型属性缺少类型载体：" + element.Id);
                    var value = ResolvedExportValue(doc,element,property,floorAreas,out var read,out var allocation);
                    var parameter = owner.get_Parameter(ReferencePolicy.BridgeGuid(property));
                    if (parameter == null || parameter.StorageType != StorageType.String || parameter.IsReadOnly)
                        throw new InvalidOperationException("临时导出参数不可写：" + property.PropertySet + "/" + property.Property);
                    var key = owner.UniqueId + "|" + ReferencePolicy.BridgeGuid(property);
                    if (written.TryGetValue(key, out var previous))
                    {
                        if (previous != value) throw new InvalidOperationException("共享类型导出值不一致：" + property.SourceParameter);
                    }
                    else
                    {
                        if (parameter.HasValue) throw new InvalidOperationException("临时导出参数已有残留值：" + property.SourceParameter);
                        if (value != null && (!parameter.Set(value) || parameter.AsString() != value))
                            throw new InvalidOperationException("临时导出值回读失败：" + property.SourceParameter);
                        written[key] = value;
                    }
                    writes.Add(new { element_id = element.Id.IntegerValue, unique_id = element.UniqueId, property_set = property.PropertySet,
                        binding = property.Binding, owner_id = owner.Id.IntegerValue,
                        property = property.Property, raw_type = property.RawType, export_type=property.ExportType, source_parameter = property.SourceParameter,
                        source_origin = read?.Origin, floor_area_derivation=allocation, value, status = value == null ? "MISSING" : "MATERIALIZED" });
                }
            }
            return writes;
        }
        internal static string ResolvedExportValue(Document doc,Element element,ReferenceProperty property,FloorAreaPlan floorAreas,
            out ParameterRead read,out FloorAreaAllocation allocation)
        {
            var owner=property.Binding=="类型"?doc.GetElement(element.GetTypeId()):element;
            if(owner==null)throw new InvalidOperationException("类型属性缺少类型载体："+element.Id);
            read=ReferencePolicy.Resolve(property.Binding=="类型"?ReadOwner(owner,property.SourceParameter,"type").ToArray()
                :Reads(doc,element,property.SourceParameter),property.SourceParameter);
            var value=ExportValue(doc,element,property,read,Management.PlatformContext.ProfileId(doc));
            allocation=property.PropertySet==FloorAreaPolicy.RegionSet&&property.Property=="投影面积"
                ?floorAreas?.Allocations.SingleOrDefault(c=>c.UniqueId==element.UniqueId&&c.Source==property.SourceParameter):null;
            value=allocation?.ValueM2??value;
            var profileId=Management.PlatformContext.ProfileId(doc);
            if(PlatformRules.IsActive(profileId))
            {
                var parent=Properties(doc,element).FirstOrDefault(p=>p.PropertySet==property.PropertySet&&(p.Property=="主功能类别"||p.Property=="公建大类"));
                var parentValue=parent==null?null:ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(Reads(doc,element,parent.SourceParameter),parent.SourceParameter));
                try { value=CaseExportPolicy.ValueForExport(property,value,profileId,parentValue); }
                catch(InvalidOperationException error) {
                    throw new InvalidOperationException("构件 "+element.Id.IntegerValue+" / "+property.PropertySet+"/"+property.Property+"："+error.Message+"。保留原参数值，请先核对业务输入。",error);
                }
            }
            return value;
        }
        internal static string ExportValue(Document doc,Element element,ReferenceProperty property,ParameterRead read,string profileId)
        {
            if(property.ExportType=="Boolean") {
                var raw=ReferencePolicy.ToBridgeValue(read);
                var owner=property.Binding=="类型"?doc.GetElement(element.GetTypeId()):element;
                BooleanAliasService.RequireWrittenForExport(doc,owner,property.SourceParameter);
                return string.IsNullOrWhiteSpace(raw)?BooleanParameterService.DefaultValue(doc,owner,property.SourceParameter):BooleanPolicy.Normalize(raw);
            }
            var windowLength = PlatformRules.IsActive(profileId) && property.PropertySet == "Pset_窗信息属性集"
                && new[] { "标高", "底高度", "高度", "宽度" }.Contains(property.Property);
            if (windowLength)
            {
                var metres = WindowDimensionMeters(doc, element, property, read);
                if (metres.HasValue) return metres.Value.ToString("R", CultureInfo.InvariantCulture);
            }
            if(read?.StorageType=="Double" && read.HasValue)
            {
                var owner=read.Origin=="type"?doc.GetElement(element.GetTypeId()):element;
                var parameter=owner.GetParameters(property.SourceParameter).Single();
                return PropertyUnits.ExportValue(property,read,parameter.Definition.UnitType.ToString());
            }
            return ReferencePolicy.ToBridgeValue(read);
        }
        internal static double? WindowDimensionMeters(Document doc, Element element, ReferenceProperty property, ParameterRead read)
        {
            if (read?.HasValue != true || read.Value == null) return null;
            if (read.StorageType == "ElementId")
            {
                var level = property.Property == "标高" ? doc.GetElement(new ElementId(Convert.ToInt32(read.Value, CultureInfo.InvariantCulture))) as Level : null;
                if (level == null) throw new InvalidOperationException("窗 " + element.Id.IntegerValue + " / " + property.Property + " 是对象引用，不能作为长度导出。");
                return UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, DisplayUnitType.DUT_METERS);
            }
            if (read.StorageType == "Double")
            {
                var owner = read.Origin == "type" ? doc.GetElement(element.GetTypeId()) : element;
                var parameter = owner.GetParameters(property.SourceParameter).Single();
                if (parameter.Definition.UnitType == UnitType.UT_Length)
                    return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), DisplayUnitType.DUT_METERS);
            }
            var text = ReferencePolicy.ToBridgeValue(read);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric)
                && !double.IsNaN(numeric) && !double.IsInfinity(numeric) ? numeric : (double?)null;
        }
    }
}
