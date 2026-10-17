using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Revit.Management;
using Hifc.Web.Contracts;
using ElementRecord = Hifc.Web.Contracts.ElementRecord;
using ParameterValue = Hifc.Web.Contracts.ParameterValue;

namespace Hifc.Web.Revit
{
    public static class NativeInventory
    {
        private static bool Included(Element element)
        {
            if (element is ProjectInfo || element is Level || element is RevitLinkInstance || element is Area) return true;
            if (element.ViewSpecific || element.Category?.CategoryType != CategoryType.Model) return false;
            if (element is Material || element is AppearanceAssetElement || element is PropertySetElement || element is View
                || element is GraphicsStyle || element is Sketch || element is SketchPlane || element is SunAndShadowSettings) return false;
            var category = ((BuiltInCategory)element.Category.Id.IntegerValue).ToString();
            return !category.StartsWith("OST_SunPath", StringComparison.Ordinal) && !category.StartsWith("OST_Sketch", StringComparison.Ordinal)
                && category != "OST_PreviewLegendComponents" && category != "OST_LegendComponents" && category != "OST_Cameras" && category != "OST_PipeSegments";
        }
        private static string Text(Element owner, string name)
        {
            var parameters = owner?.GetParameters(name);
            if (parameters == null || parameters.Count == 0) return null;
            if (parameters.Count != 1) throw new InvalidOperationException("同名参数有歧义：" + name);
            var parameter = parameters[0];
            return !parameter.HasValue ? null : parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        }
        private static string Effective(Document document, Element element, string name)
        {
            var instance = Text(element, name);
            return string.IsNullOrWhiteSpace(instance) ? Text(document.GetElement(element.GetTypeId()), name) : instance;
        }
        private static string Entity(Document document, Element element)
        {
            if (element is ProjectInfo) return "IfcBuilding";
            if (element is Level) return "IfcBuildingStorey";
            if (element is Area) return "IfcSpace";
            if (element is RevitLinkInstance) return "LINK";
            if (element is Autodesk.Revit.DB.Architecture.TopographySurface) return "IfcSite";
            var declared = Effective(document, element, "IfcExportAs");
            if (!string.IsNullOrWhiteSpace(declared) && declared.StartsWith("Ifc", StringComparison.Ordinal)) return declared.Split(new[] { '.', ':' }, 2)[0];
            switch ((BuiltInCategory)(element.Category?.Id.IntegerValue ?? 0))
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
        public static ElementRecord[] Read(Document document, IDictionary<string, string> assigned = null)
        {
            if (document == null || !document.IsValidObject || document.IsFamilyDocument) throw new InvalidOperationException("请打开 Revit 项目模型。");
            return new FilteredElementCollector(document).WhereElementIsNotElementType().Where(Included).Select(element => {
                var type = document.GetElement(element.GetTypeId());
                var row = new ElementRecord { Id = element.Id.IntegerValue, UniqueId = element.UniqueId, TypeId = type?.Id.IntegerValue ?? -1,
                    TypeUniqueId = type?.UniqueId, Name = element.Name, TypeName = type?.Name, Category = element.Category?.Name ?? element.GetType().Name,
                    Entity = "UNKNOWN", Level = element is Level ? element.Name : document.GetElement(element.LevelId)?.Name ?? "项目信息 / 无所属楼层",
                    Building = string.IsNullOrWhiteSpace(document.ProjectInformation.BuildingName) ? document.Title : document.ProjectInformation.BuildingName,
                    ProjectedAreaM2 = ModelGeometry.ProjectedAreaM2(element) };
                if (assigned != null && assigned.TryGetValue(element.UniqueId, out var slot)) row.AssignedSlot = slot;
                try { row.Entity = Entity(document, element); row.DeclaredSlot = Effective(document, element, "类目"); }
                catch (InvalidOperationException ex) { row.CoverageNote = ex.Message; }
                if (element is RevitLinkInstance link) row.CoverageNote = link.GetLinkDocument() == null ? "链接未加载 · 尚未检查" : "链接模型 · 请在源文档全面检查";
                if (element is Area area)
                {
                    row.AreaScheme = area.AreaScheme?.Name ?? "";
                    row.NativeAreaM2 = UnitUtils.ConvertFromInternalUnits(area.Area, DisplayUnitType.DUT_SQUARE_METERS);
                    if (area.Location == null) row.CoverageNote = "面积对象未放置，请补充模型";
                    else if (area.Area <= 0) row.CoverageNote = "面积对象未封闭或面积为零，请核对面积边界";
                }
                return row;
            }).OrderBy(row => row.UniqueId, StringComparer.Ordinal).ToArray();
        }
        public static ParameterValue[] ReadValues(Document document, CaptureTarget[] targets)
        {
            if (document == null || targets == null) throw new InvalidOperationException("字段取数请求为空。");
            var values = new List<ParameterValue>();
            foreach (var target in targets.GroupBy(t => t.OwnerUniqueId + "\n" + t.Origin + "\n" + t.Source).Select(g => g.First()))
            {
                var owner = document.GetElement(target.OwnerUniqueId);
                if (owner == null || owner.Id.IntegerValue != target.OwnerId || (target.Origin != "instance" && target.Origin != "type")
                    || (owner is ElementType) != (target.Origin == "type") || string.IsNullOrWhiteSpace(target.Source)) throw new InvalidOperationException("字段载体已改变，请重新全面检查。");
                var parameters = owner.GetParameters(target.Source);
                if (parameters.Count == 0) values.Add(new ParameterValue { OwnerId = target.OwnerId, OwnerUniqueId = target.OwnerUniqueId, Origin = target.Origin, Source = target.Source });
                foreach (var parameter in parameters)
                {
                    string raw = null;
                    if (parameter.HasValue) switch (parameter.StorageType)
                    {
                        case StorageType.String: raw = parameter.AsString(); break;
                        case StorageType.Integer: raw = parameter.AsInteger().ToString(CultureInfo.InvariantCulture); break;
                        case StorageType.Double: raw = parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture); break;
                        case StorageType.ElementId: raw = parameter.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture); break;
                    }
                    values.Add(new ParameterValue { OwnerId = target.OwnerId, OwnerUniqueId = target.OwnerUniqueId, Origin = target.Origin, Source = target.Source,
                        Exists = true, HasValue = parameter.HasValue, RawValue = raw, DisplayValue = parameter.StorageType == StorageType.String ? raw : parameter.AsValueString(),
                        StorageType = parameter.StorageType.ToString(), Unit = parameter.StorageType == StorageType.Double ? "internal" : null,
                        ReadOnly = parameter.IsReadOnly, SharedGuid = parameter.IsShared ? parameter.GUID.ToString("D") : null });
                }
            }
            return values.ToArray();
        }
    }
}
