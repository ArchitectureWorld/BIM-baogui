using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ScanJob
    {
        private readonly ElementId[] ids;
        private readonly List<ModelElementCheck> rows = new List<ModelElementCheck>();
        internal int Position { get; private set; }
        internal int Total => ids.Length;
        internal bool Complete => Position == Total;
        internal ScanJob(Document doc)
        {
            var categories = new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Rooms,
                BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_StairsRuns };
            ids = new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(categories)).ToElementIds().ToArray();
        }
        internal void ReadNext(Document doc, int batch)
        {
            var end = Math.Min(Total, Position + batch);
            for (; Position < end; Position++)
            {
                var element = doc.GetElement(ids[Position]);
                if (element != null) rows.Add(ModelReader.Read(doc, element));
            }
        }
        internal ModelSnapshot Snapshot(Document doc)
        {
            if (!Complete) throw new InvalidOperationException("扫描未完成，不能作为完整检测结果。");
            return new ModelSnapshot { DocumentKey = ModelReader.Key(doc), Title = doc.Title, Elements = rows.ToArray() };
        }
    }
    internal static class ModelReader
    {
        internal static string Key(Document doc) => Evidence.Hash(doc.PathName + "|" + doc.ProjectInformation.UniqueId
            + (string.IsNullOrEmpty(doc.PathName) ? "|unsaved-" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(doc) : ""));
        private static string Text(Document doc, Element element, string name)
        {
            try { return ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(RevitMappingService.Reads(doc, element, name), name)); }
            catch { return null; }
        }
        internal static ModelElementCheck Read(Document doc, Element element)
        {
            var level = doc.GetElement(element.LevelId);
            var row = new ModelElementCheck { Id = element.Id.IntegerValue, UniqueId = element.UniqueId, Name = element.Name,
                Category = element.Category?.Name, Building = Text(doc, element, "楼栋") ?? Text(doc, element, "建筑名称") ?? "未指定楼栋",
                Level = level?.Name ?? "未指定楼层", Entity = "UNKNOWN" };
            try
            {
                row.Entity = RevitMappingService.Entity(doc, element);
                var slot = RevitMappingService.Slot(doc, element);
                if (string.IsNullOrWhiteSpace(slot)) { row.Issue = "类目未确认"; return row; }
                var properties = RevitMappingService.Properties(doc, element);
                row.Slot = ReferenceCatalog.LegacySlot(properties[0].PropertySet);
                row.Classified = true;
                row.Fields = properties.Select(p => ModelChecks.Evaluate(p, RevitMappingService.Reads(doc, element, p.SourceParameter))).ToArray();
            }
            catch (Exception ex) { row.Issue = ex.Message; }
            return row;
        }
    }
}
