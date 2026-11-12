using System;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ReportingWorkbookModelStructure
    {
        internal string Kind { get; set; }
        internal string BuildingName { get; set; }
        internal string[] LevelNames { get; set; } = new string[0];
    }

    internal static class ReportingWorkbookModelReader
    {
        // Read identities only. Business parameter values never enter the workbook writer.
        internal static ReportingWorkbookModelStructure Read(Document document)
        {
            if (document == null || !document.IsValidObject || document.IsFamilyDocument)
                throw new InvalidOperationException("请先打开需要导出报规表的项目。");
            var kind = PlatformContext.Kind(document);
            if (kind != "总图" && kind != "单体建筑") kind = ExportDestinationPicker.Detect(document).Kind;
            if (kind != "总图" && kind != "单体建筑")
                throw new InvalidOperationException("请先在模型属性管理中确认当前模型属于总图或单体建筑。");
            if (kind == "总图") return new ReportingWorkbookModelStructure { Kind = kind, BuildingName = "总图" };

            var property = CaseExportPolicy.SourceProperties(PlatformContext.ProfileId(document))
                .FirstOrDefault(p => p.Entity == "IfcBuilding" && p.Property == "建筑名称");
            var name = property == null ? null : ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(
                RevitMappingService.Reads(document, document.ProjectInformation, property.SourceParameter), property.SourceParameter));
            if (string.IsNullOrWhiteSpace(name)) name = document.ProjectInformation.BuildingName;
            if (string.IsNullOrWhiteSpace(name)) name = document.Title;
            var rows = new FilteredElementCollector(document).WhereElementIsNotElementType()
                .Where(e => e is Level || e is Floor || e is RoofBase || e is Area || e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel)
                .Select(e => CaseModelReader.InventoryForDetection(document, e)).ToArray();
            return new ReportingWorkbookModelStructure {
                Kind = kind, BuildingName = name,
                LevelNames = ReportingWorkbookTemplate.ActualLevelNames(rows, PlatformContext.Settings(document))
            };
        }
    }
}
