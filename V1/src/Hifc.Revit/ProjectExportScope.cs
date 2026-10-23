using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit
{
    internal sealed class ProjectExportIssue
    {
        public int element_id { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public string detail { get; set; }
        public string property_set { get; set; }
        public string property { get; set; }
    }

    internal sealed class ProjectExportScope
    {
        internal Element[] Elements { get; private set; }
        internal Element[] MappedElements { get; private set; }
        internal ProjectExportIssue[] Issues { get; private set; }
        internal InputReviewItem[] InputReview { get; private set; }
        internal int AreaCount => Elements.OfType<Area>().Count();
        internal int ValidAreaCount => Elements.OfType<Area>().Count(ValidArea);
        internal int LinkCount => Elements.OfType<RevitLinkInstance>().Count();
        internal string Summary => "当前项目已全量导出，不受预选和当前视图过滤影响。\n"
            + "已盘点 Area：" + ValidAreaCount + " 个有效 / " + AreaCount + " 个总计；HIFC 属性载体：" + MappedElements.Length + " 个。\n"
            + (Issues.Length == 0 ? "" : "需核对：" + Issues.Length + " 项，详情见导出回执。\n")
            + (LinkCount == 0 ? "" : "链接源模型不在本次当前项目导出范围内。\n")
            + "临时导出改动已回滚。";

        internal static bool ValidArea(Area area) => area.Location != null && area.Area > 0;

        internal static ProjectExportScope Read(Document doc)
        {
            if (doc.IsFamilyDocument) throw new InvalidOperationException("IFC 全项目导出需要项目文档。");
            var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(Management.CaseScanJob.IsModelRecord).OrderBy(e => e.Id.IntegerValue).ToArray();
            var mapped = new List<Element>(); var issues = new List<ProjectExportIssue>();
            foreach (var element in elements)
            {
                if (element is RevitLinkInstance) continue;
                if (element is Area area && !ValidArea(area))
                {
                    issues.Add(new ProjectExportIssue { element_id = area.Id.IntegerValue, name = area.Name,
                        code = area.Location == null ? "AREA_UNPLACED" : "AREA_NOT_ENCLOSED",
                        detail = "面积对象未放置或没有有效闭合面积，原生 IFC 无法输出空间几何；请补充模型。" });
                    continue;
                }
                try
                {
                    // Auxiliary datum levels remain in Elements and Revit's native
                    // storey hierarchy; only their business property sets are excluded.
                    if (element is Level level && !Management.PlatformContext.IsBuildingLevel(doc, level)) continue;
                    if (element is ProjectInfo || element is Level)
                    {
                        // Metadata without any source values does not create an empty
                        // HIFC property set or become a new mandatory requirement.
                        if (RevitMappingService.Properties(doc, element).Any(p => RevitMappingService.Reads(doc, element, p.SourceParameter)
                            .Any(r => r.HasValue && !string.IsNullOrWhiteSpace(Convert.ToString(r.Value))))) mapped.Add(element);
                    }
                    else if (!string.IsNullOrWhiteSpace(RevitMappingService.Slot(doc, element)) || RevitMappingService.HasTypeEvidence(doc, element))
                    {
                        RevitMappingService.Properties(doc, element);
                        mapped.Add(element);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    issues.Add(new ProjectExportIssue { element_id = element.Id.IntegerValue, name = element.Name,
                        code = "HIFC_MAPPING_UNSUPPORTED", detail = ex.Message + "；几何仍交给原生 IFC 导出，未套用错误属性集。" });
                }
            }
            if (PlatformRules.IsActive(Management.PlatformContext.ProfileId(doc)))
            {
                // Even a wholly missing metadata group belongs in the receipt.
                // Auditing it must not manufacture an empty export property set.
                var audit = mapped.Concat(elements.Where(e => e is ProjectInfo || e is Level level && Management.PlatformContext.IsBuildingLevel(doc, level)))
                    .GroupBy(e => e.Id.IntegerValue).Select(group => group.First());
                foreach (var element in audit) AddMissingPlatformInputs(doc, element, issues);
            }
            var profileId=Management.PlatformContext.ProfileId(doc);
            var inputRows=mapped.Concat(elements.Where(e=>e is ProjectInfo||e is Level level&&Management.PlatformContext.IsBuildingLevel(doc,level)))
                .GroupBy(e=>e.Id.IntegerValue).Select(g=>ReadInputs(doc,g.First(),profileId)).ToArray();
            var review=PlatformInputReview.Build(inputRows,profileId);
            foreach(var item in review.Where(i=>i.Code=="HIFC_PLATFORM_DEFAULT_RISK"||i.Code=="FIRE_ROAD_WIDTH_REQUIRED"))
                issues.Add(new ProjectExportIssue{element_id=item.ElementId,name=item.Name,property_set=item.PropertySet,property=item.Property,code=item.Code,detail=item.Detail});
            return new ProjectExportScope { Elements = elements, MappedElements = mapped.ToArray(), Issues = issues.ToArray(),InputReview=review };
        }

        private static ModelElementCheck ReadInputs(Document doc,Element element,string profileId)
        {
            var row=new ModelElementCheck{Id=element.Id.IntegerValue,UniqueId=element.UniqueId,Name=element.Name};
            try {
                row.Fields=RevitMappingService.Properties(doc,element).Select(p=>{
                    var owner=p.Binding=="类型"?doc.GetElement(element.GetTypeId()):element;
                    var reads=owner==null?new ParameterRead[0]:p.Binding=="类型"?RevitMappingService.ReadOwner(owner,p.SourceParameter,"type").ToArray()
                        :RevitMappingService.Reads(doc,element,p.SourceParameter);
                    return ModelChecks.Evaluate(p,reads,profileId);
                }).ToArray();
            }catch(InvalidOperationException error){row.Issue=error.Message;}
            return row;
        }

        private static void AddMissingPlatformInputs(Document doc, Element element, List<ProjectExportIssue> issues)
        {
            foreach (var group in RevitMappingService.Properties(doc, element).GroupBy(p => p.PropertySet))
            {
                if (CalculationDisabled(doc, element, group.Key, group.ToArray())) continue;
                foreach (var required in PlatformRules.RequiredFields(group.Key))
                {
                    var property = group.FirstOrDefault(p => p.Property == required);
                    try
                    {
                        var owner = property?.Binding == "类型" ? doc.GetElement(element.GetTypeId()) : element;
                        var reads = property == null || owner == null ? new ParameterRead[0]
                            : property.Binding == "类型" ? RevitMappingService.ReadOwner(owner, property.SourceParameter, "type").ToArray()
                            : RevitMappingService.Reads(doc, element, property.SourceParameter);
                        var value = property == null ? null : ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(reads, property.SourceParameter));
                        // This is a source-input receipt, so suggestions and profile
                        // defaults cannot satisfy a missing value. Zero/false remain values.
                        if (!string.IsNullOrWhiteSpace(value)) continue;
                        issues.Add(new ProjectExportIssue { element_id = element.Id.IntegerValue, name = element.Name,
                            code = "HIFC_PLATFORM_INPUT_MISSING", property_set = group.Key, property = required,
                            detail = "缺少平台输入：" + group.Key + "/" + required + "；未以模板默认值补入，需核对来源后写入。" });
                    }
                    catch (InvalidOperationException ex)
                    {
                        issues.Add(new ProjectExportIssue { element_id = element.Id.IntegerValue, name = element.Name,
                            code = "HIFC_PLATFORM_SOURCE_CONFLICT", property_set = group.Key, property = required, detail = ex.Message });
                    }
                }
            }
        }

        private static bool CalculationDisabled(Document doc, Element element, string pset, ReferenceProperty[] properties)
        {
            var gate = PlatformRules.Rules(pset).FirstOrDefault(rule => rule.ComputeGate == "1" && !string.IsNullOrWhiteSpace(rule.ComputeField));
            if (gate == null) return false;
            var property = properties.FirstOrDefault(p => p.Property == gate.ComputeField);
            if (property == null) return false;
            try
            {
                var value = ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(RevitMappingService.Reads(doc, element, property.SourceParameter), property.SourceParameter))?.Trim();
                return value == "0" || value == "否" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException) { return false; }
        }
    }
}
