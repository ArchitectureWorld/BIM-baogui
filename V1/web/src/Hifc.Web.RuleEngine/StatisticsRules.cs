using System.Globalization;
using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    private sealed class StatisticsModel
    {
        internal StatisticsSourceCapture Capture;
        internal ModelElementCheck[] Rows;
    }

    private void ValidateStatistics(StatisticsRequest request)
    {
        if (request == null || request.RuleVersion != RuleVersion || (request.Kind != "building" && request.Kind != "site")
            || string.IsNullOrWhiteSpace(request.DocumentKey) || request.Sources == null || request.Sources.Length == 0 || request.Sources.Length > 256)
            throw new InvalidOperationException("统计来源或规则版本无效，请重新刷新。");
        if (request.Sources.Any(s => s?.Source == null || string.IsNullOrWhiteSpace(s.Source.Key) || s.Settings == null || s.Elements == null || s.Values == null)
            || request.Sources.Select(s => s.Source.Key).Distinct().Count() != request.Sources.Length
            || request.Sources.Count(s => s.Source.IsHost) != 1 || request.Sources.Sum(s => s.Elements.Length) > 200000)
            throw new InvalidOperationException("统计来源缺失、重复或数据量超限。");
        if (request.Kind == "building" && request.Sources.Length != 1)
            throw new InvalidOperationException("单体统计一次读取一个已打开的源文档。");
    }

    private StatisticsSourceCapture[] ActiveStatisticsSources(StatisticsRequest request)
    {
        return request.Sources.Where(s => s.Source.Available && (s.Source.IsHost || s.Source.Selected))
            .OrderByDescending(s => s.Source.IsHost)
            .GroupBy(s => string.IsNullOrEmpty(s.PlacementIdentity) ? s.Source.Key : s.PlacementIdentity, StringComparer.Ordinal)
            .Select(g => g.First()).ToArray();
    }

    private InventoryPage[] StatisticsInventory(StatisticsRequest request, StatisticsSourceCapture source)
    {
        var elements = source.Elements;
        var context = new RequestContext {
            RuleVersion = RuleVersion, RequestId = Guid.NewGuid().ToString("N"), ScanId = Guid.NewGuid().ToString("N"),
            DocumentSessionId = source.Source.Key, SnapshotHash = Canonical.InventoryHash(elements),
            ConfigurationHash = Canonical.ConfigurationHash(source.Settings, [])
        };
        var count = Math.Max(1, (elements.Length + 255) / 256);
        return Enumerable.Range(0, count).Select(index => {
            var part = elements.Skip(index * 256).Take(256).ToArray();
            return new InventoryPage { Context = context, Stage = "报规", ModelKind = request.Kind == "building" ? "单体建筑" : "总图",
                Settings = source.Settings, PageIndex = index, PageCount = count, TotalElements = elements.Length,
                Elements = part, PageHash = Canonical.InventoryHash(part) };
        }).ToArray();
    }

    public StatisticsCapturePlan PlanStatisticsCapture(StatisticsRequest request)
    {
        ValidateStatistics(request);
        return new StatisticsCapturePlan { RuleVersion = RuleVersion, Sources = ActiveStatisticsSources(request).Select(source => new StatisticsSourcePlan {
            SourceKey = source.Source.Key, Targets = CreateCapturePlan(StatisticsInventory(request, source)).Targets
        }).ToArray() };
    }

    private StatisticsModel[] ReadStatisticsModels(StatisticsRequest request)
    {
        ValidateStatistics(request);
        return ActiveStatisticsSources(request).Select(source => {
            var inventory = StatisticsInventory(request, source);
            var plan = CreateCapturePlan(inventory);
            var count = Math.Max(1, (source.Values.Length + 255) / 256);
            var pages = Enumerable.Range(0, count).Select(index => {
                var part = source.Values.Skip(index * 256).Take(256).ToArray();
                return new CapturePage { Context = plan.Context, PageIndex = index, PageCount = count, TotalValues = source.Values.Length,
                    Values = part, PageHash = Canonical.ValuesHash(part) };
            }).ToArray();
            var rows = CoreRows(Evaluate(inventory, plan, pages));
            if (request.Kind == "site")
                foreach (var row in rows) row.ProjectedAreaM2 = source.Elements.Single(e => e.UniqueId == row.UniqueId).ProjectedAreaM2;
            return new StatisticsModel { Capture = source, Rows = rows };
        }).ToArray();
    }

    private static string StatisticsText(ModelElementCheck row, string pset, List<string> issues, params string[] names)
    {
        var field = row.Fields.FirstOrDefault(f => f.PropertySet == pset && names.Contains(f.Property));
        if (field == null) return null;
        if (field.State == "有歧义" || field.State == "类型待核验") { issues.Add(field.Source + "来源待核对"); return null; }
        var value = field.MetricValue.HasValue ? field.MetricValue.Value.ToString("R", CultureInfo.InvariantCulture) : field.Value;
        var property = CaseExportPolicy.SourceProperties(field.RuleProfileId).FirstOrDefault(p => p.PropertySet == pset && p.Property == field.Property && p.Binding == field.Binding);
        try { return property == null ? value : CaseExportPolicy.ValueForExport(property, value, field.RuleProfileId, PlanningInputs.ParentValue(row.Fields, pset)); }
        catch (InvalidOperationException ex) { issues.Add(field.Source + " " + ex.Message); return null; }
    }

    private static decimal? StatisticsNumber(ModelElementCheck row, string pset, List<string> issues, params string[] names)
    {
        var value = StatisticsText(row, pset, issues, names);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) return result;
        issues.Add(names[0] + "数值无效，原值 " + value);
        return null;
    }

    private BuildingAreaStatisticsInput BuildingStatisticsInput(StatisticsRequest request, StatisticsModel model)
    {
        var notes = new List<string>();
        var rows = model.Rows;
        var settings = model.Capture.Settings;
        var project = rows.SingleOrDefault(r => r.Entity == "IfcBuilding");
        const string buildingSet = "Pset_建筑技术信息属性集";
        const string levelSet = "Pset_建筑楼层信息属性集";
        var input = new BuildingAreaStatisticsInput {
            DocumentKey = request.DocumentKey, DocumentTitle = request.DocumentTitle, SourcePath = request.SourcePath,
            CapturedUtc = DateTime.UtcNow.ToString("O"), BuildingName = project == null ? request.DocumentTitle : StatisticsText(project, buildingSet, notes, "建筑名称") ?? project.Building,
            BuildingNumber = project == null ? null : StatisticsText(project, buildingSet, notes, "建筑编号", "楼栋编号"),
            DeclaredBuildingArea = project == null ? null : StatisticsNumber(project, buildingSet, notes, "建筑面积"),
            DeclaredFootprintArea = project == null ? null : StatisticsNumber(project, buildingSet, notes, "投影面积"),
            Levels = rows.Where(r => r.Entity == "IfcBuildingStorey").Select(row => {
                var issues = new List<string>();
                var footprint = StatisticsText(row, levelSet, issues, "建筑基底");
                return new BuildingAreaLevelInput { UniqueId = row.UniqueId, Name = row.Name, ElevationM = (decimal?)row.LevelElevationM,
                    HeightM = StatisticsNumber(row, levelSet, issues, "建筑层高", "层高"), BuildingFootprint = new[] { "1", "true", "True", "是" }.Contains(footprint),
                    ExplicitBuildingLevel = settings.LevelsConfirmed && settings.BuildingLevelUniqueIds.Contains(row.UniqueId),
                    ExplicitTopBoundary = settings.TopBoundaryLevelUniqueId == row.UniqueId, Notes = string.Join("；", issues) };
            }).ToArray()
        };
        var components = new List<BuildingAreaComponent>();
        foreach (var row in rows)
        {
            var slot = ReferenceCatalog.LegacySlot(row.Slot ?? "");
            var included = BuildingAreaStatistics.Slots.Contains(slot);
            if (!included && !string.IsNullOrEmpty(slot)) continue;
            var areaSources = BuildingAreaStatistics.Slots.SelectMany(s => CaseExportPolicy.PropertiesForSlot(s, settings.RuleProfileId))
                .Where(p => p.Property == "投影面积").Select(p => p.SourceParameter);
            if (!included && !HasRawStatisticsValue(model, row, areaSources)) continue;
            var issues = new List<string>();
            var pset = CaseExportPolicy.PropertiesForSlot(slot, settings.RuleProfileId).FirstOrDefault()?.PropertySet;
            var component = new BuildingAreaComponent { ElementId = row.Id, UniqueId = row.UniqueId, Name = row.Name, Slot = slot,
                PropertySet = pset, Included = included, LevelUniqueId = row.LevelUniqueId, LevelName = row.Level,
                ElevationM = (decimal?)row.LevelElevationM, GeometricArea = (decimal?)row.ProjectedAreaM2 };
            if (included)
            {
                component.DeclaredProjectionArea = StatisticsNumber(row, pset, issues, "投影面积");
                component.CalculationFactor = StatisticsNumber(row, pset, issues, "计算系数");
                component.FloorRatioFactor = StatisticsNumber(row, pset, issues, "计容系数");
                component.MainFunction = StatisticsText(row, pset, issues, "公建大类", "主功能类别");
                component.Function = StatisticsText(row, pset, issues, "公建小类", "子功能类别");
                if ((row.SourceEntity ?? row.Entity) != "IfcSlab") issues.Add("当前面积类目采用楼板载体，需核对实际构件类别 " + row.Category);
            }
            else issues.Add("存在面积来源参数，业务类目待确认");
            component.Notes = string.Join("；", issues); component.Status = issues.Count == 0 ? null : "待核对";
            components.Add(component);
        }
        input.Components = components.ToArray(); input.Notes = notes.ToArray();
        return input;
    }

    private SiteStatisticsInput SiteStatisticsInput(StatisticsRequest request, StatisticsModel[] models)
    {
        var components = new List<SiteStatisticsComponent>();
        var activeKeys = models.Select(m => m.Capture.Source.Key).ToHashSet(StringComparer.Ordinal);
        var notes = request.Sources.Where(s => s.Source.Selected && s.Source.Available && !activeKeys.Contains(s.Source.Key))
            .Select(s => s.Source.Name + "为重复的同位置来源，仅统计一次。").ToList();
        foreach (var model in models)
        foreach (var row in model.Rows)
        {
            var slot = ReferenceCatalog.LegacySlot(row.Slot ?? "");
            var pset = CaseExportPolicy.PropertiesForSlot(slot, model.Capture.Settings.RuleProfileId).FirstOrDefault()?.PropertySet;
            var issues = new List<string>(); var kind = slot; var subtype = "";
            if (string.IsNullOrEmpty(slot))
            {
                if (HasRawStatisticsValue(model, row, new[] { "绿地投影面积", "规划净用地投影面积", "规划总用地投影面积", "车位数" }))
                    components.Add(new SiteStatisticsComponent { SourceKey = model.Capture.Source.Key, ElementId = row.Id, UniqueId = row.UniqueId, Name = row.Name,
                        Kind = "待确认", Included = false, Notes = "存在总图或停车来源参数，业务类目待确认" });
                continue;
            }
            if (slot == "绿地") subtype = StatisticsText(row, pset, issues, "绿地类型");
            else if (slot == "规划总用地" || slot == "规划净用地") { if (!model.Capture.Source.IsHost) continue; }
            else if (slot == "道路边界线") { kind = "道路"; subtype = StatisticsText(row, pset, issues, "区内道路类型"); }
            else if (slot == "总平构件")
            {
                subtype = StatisticsText(row, pset, issues, "构件类型");
                if (string.IsNullOrEmpty(subtype) || !new[] { "道路", "路面", "铺装" }.Any(subtype.Contains)) continue;
                kind = "道路";
            }
            else if (new[] { "停车位", "停车场", "室内停车场", "室外车位" }.Contains(slot)) kind = "停车";
            else continue;
            var properties = CaseExportPolicy.PropertiesForSlot(slot, model.Capture.Settings.RuleProfileId);
            var native = model.Capture.Elements.Single(e => e.UniqueId == row.UniqueId);
            var component = new SiteStatisticsComponent { SourceKey = model.Capture.Source.Key, ElementId = row.Id, UniqueId = row.UniqueId,
                Name = row.Name, Slot = slot, Kind = kind, Subtype = subtype, PropertySet = pset, Included = true,
                DeclaredAreaApplicable = properties.Any(p => p.Property == "投影面积"), ElevationM = (decimal?)row.LevelElevationM ?? 0,
                DeclaredArea = StatisticsNumber(row, pset, issues, "投影面积"), GeometricArea = (decimal?)row.ProjectedAreaM2 };
            if (kind == "绿地") component.Factor = StatisticsNumber(row, pset, issues, "折算系数");
            if (kind == "停车")
            {
                if (native.AreaScheme == null) { component.Included = false; issues.Add("停车业务需要原生面积载体，当前类别为 " + row.Category); }
                if (slot == "停车场")
                {
                    component.MotorCount = StatisticsNumber(row, pset, issues, "机动车位数量");
                    component.NonMotorCount = StatisticsNumber(row, pset, issues, "非机动车位数量");
                    component.Subtype = "停车场 · " + StatisticsText(row, pset, issues, "车场类型");
                }
                else
                {
                    var type = StatisticsText(row, pset, issues, "停车位类型", "车位类型", "停车类型");
                    var count = StatisticsNumber(row, pset, issues, "车位数");
                    component.Subtype = slot + " · " + (type ?? "类型待补");
                    var field = properties.FirstOrDefault(p => new[] { "停车位类型", "车位类型", "停车类型" }.Contains(p.Property));
                    var choices = field == null ? [] : PlatformRules.Choices(field.PropertySet, field.Property);
                    if (!string.IsNullOrWhiteSpace(type) && type.Contains("非机动车")) { component.MotorCount = 0; component.NonMotorCount = count; }
                    else if (!string.IsNullOrWhiteSpace(type) && (type.Contains("机动车") || choices.Contains(type))) { component.MotorCount = count; component.NonMotorCount = 0; }
                    else issues.Add("停车类型尚不能确认机动车／非机动车归属");
                }
            }
            if (native.AreaScheme != null && (!native.NativeAreaM2.HasValue || native.NativeAreaM2 <= 0 || !string.IsNullOrEmpty(native.CoverageNote)))
            { component.Included = false; issues.Add("面积对象未放置或无有效边界"); }
            if (!string.IsNullOrEmpty(native.AreaReason) && !component.GeometricArea.HasValue) issues.Add(native.AreaReason);
            component.Notes = string.Join("；", issues); component.Status = issues.Count == 0 ? null : "待核对";
            components.Add(component);
        }
        return new SiteStatisticsInput { DocumentKey = request.DocumentKey, DocumentTitle = request.DocumentTitle, SourcePath = request.SourcePath,
            Sources = request.Sources.Select(s => new SiteStatisticsSource { Key = s.Source.Key, Name = s.Source.Name, Path = s.Source.Path,
                IsHost = s.Source.IsHost, Available = s.Source.Available, Selected = activeKeys.Contains(s.Source.Key), Notes = s.Source.Notes }).ToArray(),
            Components = components.ToArray(), Notes = notes.ToArray() };
    }

    public StatisticsGeometryPlan PlanStatisticsGeometry(StatisticsRequest request)
    {
        var models = ReadStatisticsModels(request);
        StatisticsGeometryGroup[] groups;
        if (request.Kind == "building")
        {
            var input = BuildingStatisticsInput(request, models.Single());
            groups = input.Components.Where(c => c.Included).GroupBy(c => BuildingAreaStatistics.FloorKey(c.ElevationM, c.LevelUniqueId))
                .Select(g => new StatisticsGeometryGroup { Key = g.Key, Members = g.Select(c => new StatisticsGeometryMember {
                    SourceKey = models[0].Capture.Source.Key, ElementId = c.ElementId, UniqueId = c.UniqueId, ElevationM = c.ElevationM, GeometricArea = c.GeometricArea }).ToArray() }).ToArray();
        }
        else
        {
            var input = SiteStatisticsInput(request, models);
            var included = input.Components.Where(c => c.Included).ToArray();
            var selected = included.GroupBy(c => SiteStatistics.GroupKey(c.Kind, c.Subtype)).Select(g => (g.Key, Rows: g.ToArray()))
                .Concat(included.GroupBy(c => c.Kind).Select(g => (Key: SiteStatistics.GroupKey(g.Key, "*"), Rows: g.ToArray())))
                .GroupBy(g => g.Key).Select(g => g.First());
            groups = selected.Select(g => new StatisticsGeometryGroup { Key = g.Key, Members = g.Rows.Select(c => new StatisticsGeometryMember {
                SourceKey = c.SourceKey, ElementId = c.ElementId, UniqueId = c.UniqueId, ElevationM = c.ElevationM, GeometricArea = c.GeometricArea }).ToArray() }).ToArray();
        }
        return new StatisticsGeometryPlan { CaptureHash = StatisticsIdentity.Capture(request), Groups = groups };
    }

    public StatisticsResponse EvaluateStatistics(StatisticsRequest request)
    {
        var plan = PlanStatisticsGeometry(request);
        if (request.CaptureHash != plan.CaptureHash || request.Geometry == null || request.Geometry.Any(g => g == null)
            || request.Geometry.Select(g => g.Key).Distinct().Count() != request.Geometry.Length
            || !request.Geometry.Select(g => g.Key).Order().SequenceEqual(plan.Groups.Select(g => g.Key).Order()))
            throw new InvalidOperationException("统计取数或几何核对已经变化，请重新刷新。");
        if (request.Geometry.Any(g => g.UnionArea < 0 || g.ComponentAreaSum < 0 || g.OverlapArea < 0 || g.Verified && !g.UnionArea.HasValue))
            throw new InvalidOperationException("统计几何结果不完整。");
        var models = ReadStatisticsModels(request);
        var response = new StatisticsResponse { CaptureHash = plan.CaptureHash };
        if (request.Kind == "building")
        {
            var input = BuildingStatisticsInput(request, models.Single());
            input.Geometry = request.Geometry.Select(g => new BuildingAreaGeometryInput { FloorKey = g.Key, UnionArea = g.Verified ? g.UnionArea : null,
                ComponentAreaSum = g.ComponentAreaSum, OverlapArea = g.OverlapArea, Notes = g.Notes }).ToArray();
            response.Building = BuildingAreaStatistics.Build(input);
        }
        else
        {
            var input = SiteStatisticsInput(request, models);
            input.Geometry = request.Geometry.Select(g => new SiteStatisticsGeometry { Key = g.Key, Verified = g.Verified,
                UnionArea = g.UnionArea, OverlapArea = g.OverlapArea, Notes = g.Notes }).ToArray();
            response.Site = SiteStatistics.Build(input);
        }
        return response;
    }

    public StatisticsWorkbook ExportStatisticsWorkbook(StatisticsRequest request)
    {
        var response = EvaluateStatistics(request);
        var path = Path.Combine(Path.GetTempPath(), "hifc-web-statistics-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            if (response.Building != null) BuildingAreaStatisticsWorkbook.Write(path, response.Building);
            else SiteStatisticsWorkbook.Write(path, response.Site);
            return new StatisticsWorkbook { CaptureHash = response.CaptureHash, FileName = (request.Kind == "building" ? "单体面积统计" : "总图统计") + "_" + DateTime.UtcNow.ToString("yyyyMMdd") + ".xlsx",
                ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(path)) };
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static bool HasRawStatisticsValue(StatisticsModel model, ModelElementCheck row, IEnumerable<string> names)
    {
        var element = model.Capture.Elements.Single(e => e.UniqueId == row.UniqueId);
        var sources = new HashSet<string>(names, StringComparer.Ordinal);
        return model.Capture.Values.Any(value => (value.OwnerUniqueId == element.UniqueId ||
            !string.IsNullOrEmpty(element.TypeUniqueId) && value.OwnerUniqueId == element.TypeUniqueId) &&
            sources.Contains(value.Source) && value.Exists && value.HasValue && !string.IsNullOrWhiteSpace(value.RawValue));
    }
}
