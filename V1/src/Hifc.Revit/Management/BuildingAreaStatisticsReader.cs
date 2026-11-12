using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // All Revit access stays on its API thread. No transactions or parameter writes.
    internal sealed class BuildingAreaStatisticsReader
    {
        private readonly Document document;
        private readonly string profile;
        private readonly ElementId[] elements;
        private readonly List<BuildingAreaComponent> components = new List<BuildingAreaComponent>();
        private readonly List<BuildingAreaGeometryInput> geometry = new List<BuildingAreaGeometryInput>();
        private readonly List<string> notes = new List<string>();
        private readonly BuildingAreaStatisticsInput input;
        private BuildingAreaComponent[][] floors;
        private int position, geometryPosition;
        internal bool Complete => position == elements.Length && floors != null && geometryPosition == floors.Length;
        internal string Progress => floors == null ? "读取面积载体 " + position + " / " + elements.Length : "核对分层投影 " + geometryPosition + " / " + floors.Length;

        internal BuildingAreaStatisticsReader(Document doc, string key)
        {
            document = doc ?? throw new ArgumentNullException(nameof(doc));
            if (doc.IsFamilyDocument) throw new InvalidOperationException("请打开单体建筑项目。");
            if (PlatformContext.Kind(doc) == "总图") throw new InvalidOperationException("当前选择为总图，请选择单体建筑文档。");
            profile = PlatformContext.ProfileId(doc);
            var properties = CaseExportPolicy.SourceProperties(profile);
            var buildingProperties = properties.Where(p => p.PropertySet == "Pset_建筑技术信息属性集").ToArray();
            var levelProperties = properties.Where(p => p.PropertySet == "Pset_建筑楼层信息属性集").ToArray();
            var project = doc.ProjectInformation;
            input = new BuildingAreaStatisticsInput {
                DocumentKey = key, DocumentTitle = doc.Title, SourcePath = doc.PathName,
                CapturedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                BuildingName = ReadText(project, buildingProperties, notes, "建筑名称") ?? project.BuildingName ?? doc.Title,
                BuildingNumber = ReadText(project, buildingProperties, notes, "建筑编号", "楼栋编号"),
                DeclaredBuildingArea = ReadNumber(project, buildingProperties, notes, "建筑面积"),
                DeclaredFootprintArea = ReadNumber(project, buildingProperties, notes, "投影面积"),
                Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l => ReadLevel(l, levelProperties)).ToArray()
            };
            elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e is Floor || e is RoofBase || e is Area || e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel)
                .Select(e => e.Id).ToArray();
            if (new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Any())
                notes.Add("统计范围为当前源文档，链接单体可在打开其源文档后单独统计。");
        }

        internal void ReadNext()
        {
            if (!document.IsValidObject) throw new InvalidOperationException("统计文档已关闭。");
            if (position < elements.Length)
            {
                var end = Math.Min(position + 20, elements.Length);
                for (; position < end; position++)
                {
                    var element = document.GetElement(elements[position]);
                    if (element == null) throw new InvalidOperationException("读取期间构件已删除，请刷新。");
                    var component = ReadComponent(element);
                    if (component != null) components.Add(component);
                }
                return;
            }
            if (floors == null) floors = components.Where(c => c.Included)
                .GroupBy(c => BuildingAreaStatistics.FloorKey(c.ElevationM, c.LevelUniqueId)).Select(g => g.ToArray()).ToArray();
            if (geometryPosition < floors.Length) geometry.Add(MeasureUnion(floors[geometryPosition++]));
        }

        internal BuildingAreaStatisticsReport Report()
        {
            if (!Complete) throw new InvalidOperationException("统计尚未完成。");
            input.Components = components.ToArray(); input.Geometry = geometry.ToArray(); input.Notes = notes.ToArray();
            return BuildingAreaStatistics.Build(input);
        }

        internal static BuildingAreaStatisticsReport Read(Document doc, string key)
        {
            var reader = new BuildingAreaStatisticsReader(doc, key);
            while (!reader.Complete) reader.ReadNext();
            return reader.Report();
        }

        private BuildingAreaLevelInput ReadLevel(Level level, ReferenceProperty[] properties)
        {
            var issues = new List<string>();
            var footprint = ReadText(level, properties, issues, "建筑基底");
            var settings = PlatformContext.Settings(document);
            var result = new BuildingAreaLevelInput {
                UniqueId = level.UniqueId, Name = level.Name, ElevationM = (decimal)(level.ProjectElevation * .3048),
                HeightM = ReadNumber(level, properties, issues, "建筑层高", "层高"),
                BuildingFootprint = footprint == "1" || footprint == "true" || footprint == "True" || footprint == "是",
                ExplicitBuildingLevel = settings.LevelsConfirmed && (settings.BuildingLevelUniqueIds ?? new string[0]).Contains(level.UniqueId),
                ExplicitTopBoundary = settings.TopBoundaryLevelUniqueId == level.UniqueId
            };
            result.Notes = string.Join("；", issues);
            if (issues.Count > 0) notes.Add(level.Name + " " + result.Notes);
            return result;
        }

        private BuildingAreaComponent ReadComponent(Element element)
        {
            var issues = new List<string>();
            string slot = null;
            try { slot = PlatformContext.CanonicalSlot(RevitMappingService.Slot(document, element), profile); }
            catch (InvalidOperationException ex) { issues.Add(ex.Message); }
            var confirmed = BuildingAreaStatistics.Slots.Contains(slot);
            if (!confirmed && !string.IsNullOrWhiteSpace(slot)) return null;
            if (!confirmed && !HasAreaSource(element) && issues.Count == 0) return null;
            var level = document.GetElement(element.LevelId) as Level;
            var row = new BuildingAreaComponent {
                ElementId = element.Id.IntegerValue, UniqueId = element.UniqueId, Name = element.Name, Slot = slot,
                Included = confirmed, LevelUniqueId = level?.UniqueId, LevelName = level?.Name,
                ElevationM = level == null ? (decimal?)null : (decimal)(level.ProjectElevation * .3048)
            };
            var area = ModelGeometry.ProjectedAreaM2(element);
            row.GeometricArea = area.HasValue ? (decimal)area.Value : (decimal?)null;
            if (confirmed)
            {
                var properties = CaseExportPolicy.PropertiesForSlot(slot, profile);
                row.PropertySet = properties.FirstOrDefault()?.PropertySet;
                row.DeclaredProjectionArea = ReadNumber(element, properties, issues, "投影面积");
                row.CalculationFactor = ReadNumber(element, properties, issues, "计算系数");
                row.FloorRatioFactor = ReadNumber(element, properties, issues, "计容系数");
                row.MainFunction = ReadText(element, properties, issues, "公建大类", "主功能类别");
                row.Function = ReadText(element, properties, issues, "公建小类", "子功能类别");
                if (!(element is Floor)) issues.Add("当前面积类目采用楼板载体，需核对实际构件类别 " + element.Category?.Name);
            }
            else issues.Add("存在面积来源参数，业务类目待确认");
            row.Notes = string.Join("；", issues);
            row.Status = issues.Count == 0 ? null : "待核对";
            return row;
        }

        private bool HasAreaSource(Element element)
        {
            foreach (var slot in BuildingAreaStatistics.Slots)
                foreach (var property in CaseExportPolicy.PropertiesForSlot(slot, profile).Where(p => p.Property == "投影面积"))
                    if (RevitMappingService.Reads(document, element, property.SourceParameter).Any(p => p.HasValue && !string.IsNullOrWhiteSpace(ReferencePolicy.ToBridgeValue(p)))) return true;
            return false;
        }

        private string ReadText(Element element, ReferenceProperty[] properties, List<string> issues, params string[] names)
        {
            var property = properties.FirstOrDefault(p => names.Contains(p.Property));
            if (property == null) return null;
            try
            {
                var read = ReferencePolicy.Resolve(RevitMappingService.Reads(document, element, property.SourceParameter), property.SourceParameter);
                // Read-only statistics use source conversion directly and never materialize temporary export values.
                return RevitMappingService.ExportValue(document, element, property, read, profile);
            }
            catch (InvalidOperationException ex) { issues.Add(property.SourceParameter + " " + ex.Message); return null; }
        }

        private decimal? ReadNumber(Element element, ReferenceProperty[] properties, List<string> issues, params string[] names)
        {
            var text = ReadText(element, properties, issues, names);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            issues.Add(names[0] + "数值无效，原值 " + text);
            return null;
        }

        private BuildingAreaGeometryInput MeasureUnion(BuildingAreaComponent[] members)
        {
            var result = new BuildingAreaGeometryInput {
                FloorKey = BuildingAreaStatistics.FloorKey(members[0].ElevationM, members[0].LevelUniqueId),
                ComponentAreaSum = members.All(c => c.GeometricArea.HasValue) ? members.Sum(c => c.GeometricArea.Value) : (decimal?)null
            };
            var solids = new List<Solid>();
            Solid union = null;
            try
            {
                foreach (var member in members)
                {
                    var projected = ModelGeometry.ProjectionSolids(document.GetElement(new ElementId(member.ElementId)));
                    if (projected == null || projected.Length == 0) throw new InvalidOperationException("构件 " + member.ElementId + " 无可合并的水平投影");
                    solids.AddRange(projected);
                }
                union = SolidUtils.Clone(solids[0]);
                foreach (var solid in solids.Skip(1))
                {
                    var merged = BooleanOperationsUtils.ExecuteBooleanOperation(union, solid, BooleanOperationsType.Union);
                    union.Dispose(); union = merged;
                }
                result.UnionArea = (decimal)(union.Volume * .09290304);
                result.OverlapArea = Math.Max((result.ComponentAreaSum ?? result.UnionArea.Value) - result.UnionArea.Value, 0m);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.Notes = "本层投影合并未完成，各构件几何面积见明细。" + ex.Message;
            }
            finally { union?.Dispose(); foreach (var solid in solids) solid.Dispose(); }
            return result;
        }
    }
}
