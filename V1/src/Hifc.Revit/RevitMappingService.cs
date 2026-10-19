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
    }

    internal static class RevitMappingService
    {
        internal static readonly ReferenceCatalog Catalog = ReferenceCatalog.FromEmbeddedBaseline();
        internal const string SlotName = "类目";

        internal static ParameterRead[] Reads(Document doc, Element element, string name)
        {
            var result = ReadOwner(element, name, "instance").ToList();
            var type = doc.GetElement(element.GetTypeId());
            if (type != null) result.AddRange(ReadOwner(type, name, "type"));
            return result.ToArray();
        }

        internal static IEnumerable<ParameterRead> ReadOwner(Element element, string name, string origin)
        {
            foreach (var parameter in element.GetParameters(name))
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
            if(element is ProjectInfo || element is Level)
            {
                var names=element is ProjectInfo?new[]{"Pset_申报信息属性集","Pset_建筑技术信息属性集"}:new[]{"Pset_建筑楼层信息属性集"};
                return CaseWorkflow.ForModel("报规","单体建筑").SelectMany(r=>r.Properties).Where(p=>!p.NativeGroup && p.Binding=="实例" && names.Contains(p.PropertySet))
                    .GroupBy(p=>ReferencePolicy.BridgeGuid(p)).Select(g=>g.First()).ToArray();
            }
            var slot = Slot(doc, element);
            if(CaseWorkflow.AreaSlots.Contains(slot) && !(element is Area))throw new InvalidOperationException("构件 "+element.Id.IntegerValue+"：该报规区域类目需要面积（Area）载体。");
            var entity = Entity(doc, element);
            var properties = Catalog.ForSlot(slot).Where(p => p.Entity.Split(',').Contains(entity, StringComparer.Ordinal)).ToArray();
            if (properties.Length == 0)
                throw new InvalidOperationException("构件 " + element.Id.IntegerValue + "：类目 " + slot + " 与 IFC 实体 " + entity + " 不匹配。本版请先选择支持的构件类别。");
            return properties.GroupBy(p => ReferencePolicy.BridgeGuid(p)).Select(g => g.First()).ToArray();
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
                    parameters = Catalog.Properties.Select(p => p.SourceParameter).Append(SlotName).Distinct(StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.Ordinal).Select(name => new { name, observations = Reads(doc, element, name) }).ToArray()
                }).ToArray()
            };
        }

        internal static void Prepare(Document doc, Element[] elements, string slot, string outputDirectory)
        {
            var properties = Catalog.ForSlot(slot);
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
            var definitions = Catalog.Properties.Where(p => !p.NativeGroup).GroupBy(p => ReferencePolicy.BridgeGuid(p))
                .Select(g => new ParameterDefinition { Guid = g.Key, Name = ReferencePolicy.BridgeName(g.First()), Temporary = true }).ToArray();
            EnsureDefinitions(doc, elements, definitions, outputDirectory);
            doc.Regenerate();
        }

        internal static void EnsureDefinitions(Document doc, Element[] elements, ParameterDefinition[] definitions, string outputDirectory, bool typeSource = false)
        {
            var categories = elements.Select(e => e.Category).Where(c => c != null).GroupBy(c => c.Id.IntegerValue).Select(g => g.First()).ToArray();
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
                    foreach (var element in elements)
                        if (item.Temporary && element.GetParameters(item.Name).Any(p => !p.IsShared || p.GUID != item.Guid))
                            throw new InvalidOperationException("临时导出参数同名冲突：" + item.Name);
                    var existing = SharedParameterElement.Lookup(doc, item.Guid);
                    Definition definition = existing?.GetDefinition();
                    if (definition != null && (definition.Name != item.Name || definition.ParameterType != ParameterType.Text))
                        throw new InvalidOperationException("共享参数 GUID 定义冲突：" + item.Name);
                    if (definition == null)
                    {
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
                        var options = new ExternalDefinitionCreationOptions(item.Name, ParameterType.Text)
                        { GUID = item.Guid, Visible = true, UserModifiable = !item.Temporary, Description = item.Temporary ? "Temporary IFC routing value" : "Reference mapping source; keep original parameter name" };
                        definition = group.Definitions.Create(options) as ExternalDefinition;
                    }
                    var set = doc.Application.Create.NewCategorySet();
                    var binding = doc.ParameterBindings.get_Item(definition);
                    if (binding != null && (binding is TypeBinding) != typeSource) throw new InvalidOperationException("共享参数实例/类型绑定冲突：" + item.Name);
                    if (binding is ElementBinding old) foreach (Category category in old.Categories) set.Insert(category);
                    foreach (var category in categories) set.Insert(category);
                    Autodesk.Revit.DB.Binding instanceBinding = typeSource ? (Autodesk.Revit.DB.Binding)doc.Application.Create.NewTypeBinding(set) : doc.Application.Create.NewInstanceBinding(set);
                    bool ok = binding == null
                        ? doc.ParameterBindings.Insert(definition, instanceBinding, BuiltInParameterGroup.PG_DATA)
                        : doc.ParameterBindings.ReInsert(definition, instanceBinding, BuiltInParameterGroup.PG_DATA);
                    if (!ok) throw new InvalidOperationException("参数绑定失败：" + item.Name);
                }
            }
            finally { doc.Application.SharedParametersFilename = previous; }
        }

        internal static object Materialize(Document doc, Element[] elements)
        {
            var writes = new List<object>();
            var bridges = Catalog.Properties.Where(p => !p.NativeGroup).GroupBy(p => ReferencePolicy.BridgeGuid(p)).Select(g => g.First()).ToArray();
            foreach (var element in elements)
            {
                foreach (var bridge in bridges)
                {
                    var parameter = element.get_Parameter(ReferencePolicy.BridgeGuid(bridge));
                    if (parameter == null || parameter.StorageType != StorageType.String || parameter.IsReadOnly)
                        throw new InvalidOperationException("临时导出参数不可写：" + bridge.PropertySet + "/" + bridge.Property);
                    // Setting an unset Text parameter to "" marks it as having a value.
                    // The 2020 exporter then emits empty properties and unrelated Psets.
                    if (parameter.HasValue)
                        throw new InvalidOperationException("临时导出参数已有残留值，无法保证类目隔离：" + bridge.PropertySet + "/" + bridge.Property);
                }
                foreach (var property in Properties(doc, element))
                {
                    var read = ReferencePolicy.Resolve(Reads(doc, element, property.SourceParameter), property.SourceParameter);
                    var value = ExportValue(doc,element,property,read);
                    var parameter = element.get_Parameter(ReferencePolicy.BridgeGuid(property));
                    if (value != null)
                    {
                        if (!parameter.Set(value) || parameter.AsString() != value)
                            throw new InvalidOperationException("临时导出值回读失败：" + property.SourceParameter);
                    }
                    writes.Add(new { element_id = element.Id.IntegerValue, unique_id = element.UniqueId, property_set = property.PropertySet,
                        property = property.Property, raw_type = property.RawType, export_type=property.ExportType, source_parameter = property.SourceParameter,
                        source_origin = read?.Origin, value, status = value == null ? "MISSING" : "MATERIALIZED" });
                }
            }
            return writes;
        }
        private static string ExportValue(Document doc,Element element,ReferenceProperty property,ParameterRead read)
        {
            if(read?.StorageType=="Double" && read.HasValue)
            {
                var owner=read.Origin=="type"?doc.GetElement(element.GetTypeId()):element;
                var parameter=owner.GetParameters(property.SourceParameter).Single();
                if(property.Property=="投影面积" && parameter.Definition.UnitType==UnitType.UT_Area)
                    return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(),DisplayUnitType.DUT_SQUARE_METERS).ToString("R",CultureInfo.InvariantCulture);
                if((property.Property=="地下建筑高度" || property.Property=="地坪标高") && parameter.Definition.UnitType==UnitType.UT_Length)
                    return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(),DisplayUnitType.DUT_METERS).ToString("R",CultureInfo.InvariantCulture);
            }
            return ReferencePolicy.ToBridgeValue(read);
        }
    }
}
