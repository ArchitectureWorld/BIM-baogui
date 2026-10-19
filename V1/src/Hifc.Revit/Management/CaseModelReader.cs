using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class CaseScanJob
    {
        private readonly ElementId[] ids;
        private readonly List<ModelElementCheck> rows = new List<ModelElementCheck>();
        private readonly string stage, requestedKind;
        private string kind;
        private ModelDetection detection;
        private CaseRule[] rules;
        private readonly UndergroundResult underground;
        internal int Position { get; private set; }
        internal int Total => ids.Length * 2;
        internal bool Complete => Position == Total;
        internal CaseScanJob(Document doc,string stage,string requestedKind):this(doc,stage,requestedKind,null) { }
        internal CaseScanJob(Document doc, string stage, string requestedKind, UndergroundSettings undergroundSettings)
        {
            if (stage != "报规" && stage != "施工图") throw new InvalidOperationException("请先人工选择报规或施工图阶段。");
            this.stage = stage; this.requestedKind = requestedKind;
            underground=UndergroundPolicy.Evaluate(undergroundSettings,UndergroundContextReader.Levels(doc));
            // Re-enumerate every run. UI filters, old exclusions and previous completion
            // records never take part in the collector.
            ids = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(IsModelRecord)
                .Select(e => e.Id).ToArray();
        }
        private static bool IsModelRecord(Element element)
        {
            if (element is ProjectInfo || element is Level || element is RevitLinkInstance || element is Area) return true;
            if (element.ViewSpecific || element.Category?.CategoryType != CategoryType.Model) return false;
            // Revit's Model category also contains non-model resource definitions.
            // Exclude them before reading any object metadata or business fields.
            if (element is Material || element is AppearanceAssetElement || element is PropertySetElement ||
                element is View || element is GraphicsStyle || element is Sketch || element is SketchPlane ||
                element is SunAndShadowSettings) return false;
            if (element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PreviewLegendComponents) return false;
            var category = ((BuiltInCategory)element.Category.Id.IntegerValue).ToString();
            if (category.StartsWith("OST_SunPath", StringComparison.Ordinal) || category.StartsWith("OST_Sketch", StringComparison.Ordinal)) return false;
            return category != "OST_LegendComponents" && category != "OST_Cameras" && category != "OST_PipeSegments";
        }
        internal void ReadNext(Document doc, int batch)
        {
            var end = Math.Min(Total, Position + batch);
            for (; Position < end; Position++)
            {
                if (Position == ids.Length)
                {
                    detection = CaseWorkflow.Detect(rows.ToArray());
                    kind = requestedKind == "总图" || requestedKind == "单体建筑" ? requestedKind : detection.Kind;
                    rules = CaseWorkflow.ForModel(stage, kind);
                }
                var index = Position < ids.Length ? Position : Position - ids.Length;
                var element = doc.GetElement(ids[index]);
                if (element == null) throw new InvalidOperationException("构件在扫描中被删除，请重新检查。");
                if (Position < ids.Length) rows.Add(CaseModelReader.Inventory(doc, element));
                else CaseModelReader.ReadFieldsWithContext(doc, element, rows[index], rules, kind, underground);
            }
        }
        internal ModelSnapshot Snapshot(Document doc)
        {
            if (!Complete) throw new InvalidOperationException("扫描尚未完整完成。");
            return new ModelSnapshot { DocumentKey = ModelReader.Key(doc), Title = doc.Title, Stage = stage, RequestedKind = requestedKind,
                ModelKind = kind ?? "待确认", DetectionEvidence = (detection?.Evidence ?? new string[0]).Concat(new[]{underground.Note}).ToArray(), Complete = true,
                ScanId = Guid.NewGuid().ToString("N"), CompletedUtc = DateTime.UtcNow.ToString("O"), Elements = rows.ToArray() };
        }
    }
    internal static class CaseModelReader
    {
        internal static ModelElementCheck Inventory(Document doc, Element element)
        {
            var type = doc.GetElement(element.GetTypeId()); var level = doc.GetElement(element.LevelId);
            var row = new ModelElementCheck { Id = element.Id.IntegerValue, UniqueId = element.UniqueId, Name = element.Name,
                TypeId = type?.Id.IntegerValue ?? -1, TypeName = type?.Name, Category = element.Category?.Name ?? element.GetType().Name,
                Building = doc.ProjectInformation.BuildingName ?? doc.Title, Level = element is Level ? element.Name : level?.Name ?? "项目信息 / 无所属楼层",
                Entity = "UNKNOWN", InventoryOnly = true };
            if (element is Area area)
            {
                row.AreaScheme = area.AreaScheme?.Name ?? "";
                if (area.Location == null) row.CoverageNote = "面积对象未放置，请补充模型";
                else if (area.Area <= 0)
                {
                    using (var options = new SpatialElementBoundaryOptions())
                    {
                        var boundaries = area.GetBoundarySegments(options);
                        row.CoverageNote = boundaries == null || boundaries.Count == 0 ? "面积对象未封闭，请补充面积边界" : "面积对象面积为零，请核对模型";
                    }
                }
            }
            if (element is RevitLinkInstance link)
            {
                row.Entity = "LINK"; row.CoverageNote = link.GetLinkDocument() == null ? "链接未加载 · 尚未检查" : "链接模型 · 请在源文档全面检查";
                return row;
            }
            try
            {
                row.Entity = RevitMappingService.Entity(doc, element);
                var slot = RevitMappingService.Slot(doc, element);
                if (!string.IsNullOrWhiteSpace(slot)) { row.Slot = ReferenceCatalog.LegacySlot(slot); row.Classified = true; }
                // Existing nonempty dedicated source fields are evidence even when a
                // user has erased the classification. Blank category-wide bindings are not.
                if (string.IsNullOrEmpty(row.Slot))
                {
                    var candidates = RevitMappingService.Catalog.Properties.Where(p => !p.NativeGroup && p.Binding == "实例" && p.Entity.Split(',').Contains(row.Entity))
                        .GroupBy(p => p.PropertySet).Where(g => g.Any(p => p.SourceParameter.StartsWith(ReferenceCatalog.LegacySlot(g.Key), StringComparison.Ordinal) &&
                            RevitMappingService.Reads(doc, element, p.SourceParameter).Any(r => r.HasValue && !string.IsNullOrWhiteSpace(ReferencePolicy.ToBridgeValue(r)))))
                        .Select(g => ReferenceCatalog.LegacySlot(g.Key)).ToArray();
                    row.SuggestedSlots = candidates;
                }
            }
            catch (Exception ex) { row.Issue = "来源冲突：" + ex.Message; }
            return row;
        }
        internal static void ReadFields(Document doc,Element element,ModelElementCheck row,CaseRule[] rules,string kind)
            =>ReadFieldsWithContext(doc,element,row,rules,kind,null);
        internal static void ReadFieldsWithContext(Document doc, Element element, ModelElementCheck row, CaseRule[] rules, string kind, UndergroundResult underground)
        {
            if (rules.Length == 0 || row.Entity == "LINK") return;
            if(CaseWorkflow.AreaSlots.Contains(row.Slot) && !(element is Area))
            {row.CoverageNote="该报规区域类目需要面积（Area）载体，请核对模型；不将其他构件强制改为 IfcSpace。";return;}
            try
            {
                var applicable = new List<CaseRule>();
                bool info = element is ProjectInfo || element is Level;
                if (info) applicable.AddRange(rules.Where(r => r.Entities.Contains(row.Entity)));
                else
                {
                    var matches = rules.Where(r => r.Binding == "实例" && r.Entities.Contains(row.Entity)).ToArray();
                    if (!string.IsNullOrEmpty(row.Slot))
                    {
                        applicable.AddRange(matches.Where(r => r.Slot == row.Slot));
                        if (!rules.Any(r => r.Slot == row.Slot && r.Entities.Contains(row.Entity)))
                            row.CoverageNote = "已声明类目不在当前案例/载体范围：" + row.Slot;
                    }
                    else
                    {
                        row.Candidates = CaseWorkflow.GetCandidates(row, kind);
                        row.SuggestedSlots = row.Candidates.Select(c => c.Slot).ToArray();
                        if (row.Candidates.Length == 1 && row.Candidates[0].CanPrepare) applicable.AddRange(matches.Where(r => r.Slot == row.Candidates[0].Slot));
                        row.Recognition = string.Join("\n", row.Candidates.Select(c => c.Slot + "：" + c.Evidence));
                        if (row.Candidates.Length > 1 || row.Candidates.Any(c => c.RequiresReview)) row.Issue = "待确认分类：多候选或载体关系需核对";
                    }
                }
                applicable.AddRange(rules.Where(r => r.Binding == "类型" && r.Required && r.Entities.Contains(row.Entity)));
                // Optional window properties apply only to explicitly classified windows.
                if (row.Slot == "窗") applicable.AddRange(rules.Where(r => r.Binding == "类型" && r.Slot == "窗" && r.Entities.Contains(row.Entity)));
                applicable = applicable.Distinct().ToList();
                row.Slots = applicable.Where(r => r.Binding == "类型" || info || r.Slot == row.Slot).Select(r => r.Slot).Distinct().ToArray();
                row.Classified = row.Slots.Length > 0; row.InventoryOnly = applicable.Count == 0 && row.SuggestedSlots.Length == 0 && string.IsNullOrEmpty(row.CoverageNote);
                double? projectedArea = applicable.Any(r => r.Properties.Any(p => p.Property == "投影面积")) ? ModelGeometry.ProjectedAreaM2(element) : null;
                row.Fields = applicable.SelectMany(rule => rule.Properties.Select(property => {
                    var owner = rule.Binding == "类型" ? doc.GetElement(element.GetTypeId()) : element;
                    var reads = owner == null ? new ParameterRead[0] : rule.Binding == "类型"
                        ? RevitMappingService.ReadOwner(owner, property.SourceParameter, "type").ToArray()
                        : RevitMappingService.Reads(doc, element, property.SourceParameter);
                    var field = ModelChecks.Evaluate(property, reads);
                    if (rule.Binding == "实例" && field.Origin == "type") owner = doc.GetElement(element.GetTypeId());
                    field.OwnerId = owner?.Id.IntegerValue ?? -1; field.OwnerUniqueId = owner?.UniqueId;
                    field.SourceSignature = Evidence.Hash(Evidence.Json(reads));
                    field.Binding = rule.Binding; field.AllowedEmpty = rule.EmptyAllowed.Contains(property.Property);
                    var parameters = owner?.GetParameters(property.SourceParameter);
                    field.Editable = parameters != null && parameters.Count == 1 && !parameters[0].IsReadOnly && parameters[0].StorageType != StorageType.ElementId;
                    if (field.AllowedEmpty && field.State == "未填写") field.Detail = "案例中允许空值；参数已存在";
                    if (field.Origin == "type") field.Detail = (field.Valid ? "类型来源" : field.Detail) + "；修改将影响当前文档同类型实例，预览显示范围";
                    AutoFillPolicy.PopulateWithSource(field, rule.Slot, element.Name, projectedArea,element is Area?"Revit 面积（Area）的实际面积，转换为 m²；名称和所属楼层取当前对象":null);
                    if(element is ProjectInfo)
                    {
                        var parameter=parameters?.Count==1?parameters[0]:null;
                        double? existingMetric=parameter?.StorageType==StorageType.Double && parameter.Definition.UnitType==UnitType.UT_Length
                            ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(),DisplayUnitType.DUT_METERS):(double?)null;
                        UndergroundPolicy.Populate(field,underground,existingMetric);
                    }
                    if ((field.State == "缺参数" || field.State == "未填写") && element is Level && property.Property == "楼层名称") { field.SuggestedValue = element.Name; field.SuggestionReason = "Revit 标高名称"; }
                    if ((field.State == "缺参数" || field.State == "未填写") && element is ProjectInfo && property.Property == "建筑名称" && !string.IsNullOrWhiteSpace(doc.ProjectInformation.BuildingName))
                    { field.SuggestedValue = doc.ProjectInformation.BuildingName; field.SuggestionReason = "Revit 项目信息 / 建筑名称"; }
                    return field;
                })).ToArray();
            }
            catch (Exception ex) { row.CoverageNote = "属性读取未完成：" + ex.Message; }
        }
    }
}
