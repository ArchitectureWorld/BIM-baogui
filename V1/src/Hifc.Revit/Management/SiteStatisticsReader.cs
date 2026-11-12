using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class SiteStatisticsDocument
    {
        internal SiteStatisticsSource Source;
        internal Document Document;
        internal Transform Placement;
        internal ElementId LinkId;
    }

    internal sealed class SiteStatisticsReader : IDisposable
    {
        private sealed class Target { internal SiteStatisticsDocument Source; internal ElementId Id; }
        private readonly SiteStatisticsInput input;
        private readonly string profile;
        private readonly Target[] targets;
        private readonly List<SiteStatisticsComponent> components = new List<SiteStatisticsComponent>();
        private readonly Dictionary<string, Solid[]> projected = new Dictionary<string, Solid[]>();
        private readonly List<SiteStatisticsGeometry> measurements = new List<SiteStatisticsGeometry>();
        private readonly List<string> notes = new List<string>();
        private KeyValuePair<string, SiteStatisticsComponent[]>[] groups;
        private int position, geometryPosition;
        private bool disposed;
        internal bool Complete => position == targets.Length && groups != null && geometryPosition == groups.Length;
        internal string Progress => groups == null ? "读取总图构件 " + position + " / " + targets.Length : "核对分类投影 " + geometryPosition + " / " + groups.Length;

        internal static SiteStatisticsDocument[] Sources(Document host, string key)
        {
            var list = new List<SiteStatisticsDocument> { new SiteStatisticsDocument {
                Document = host, Placement = Transform.Identity,
                Source = new SiteStatisticsSource { Key = key, Name = host.Title, Path = host.PathName, IsHost = true, Selected = true }
            } };
            foreach (var link in new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var doc = link.GetLinkDocument();
                list.Add(new SiteStatisticsDocument {
                    Document = doc, LinkId = link.Id, Placement = link.GetTotalTransform(),
                    Source = new SiteStatisticsSource { Key = key + "/link/" + link.UniqueId, Name = link.Name,
                        Path = doc?.PathName, Available = doc != null, Notes = doc == null ? "链接尚未加载" : "所选直接链接实例" }
                });
            }
            return list.ToArray();
        }

        internal SiteStatisticsReader(Document host, string key, SiteStatisticsDocument[] sources, string[] selectedKeys)
        {
            profile = PlatformContext.ProfileId(host);
            var selected = new HashSet<string>(selectedKeys ?? new string[0]); selected.Add(key);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var active = new List<SiteStatisticsDocument>();
            foreach (var source in sources)
            {
                source.Source.Selected = selected.Contains(source.Source.Key) && source.Source.Available;
                if (!source.Source.Selected) continue;
                var identity = PlacementIdentity(source);
                if (!seen.Add(identity)) { notes.Add(source.Source.Name + "与已纳入来源为同模型同位置引用，已去除重复来源。"); continue; }
                active.Add(source);
            }
            input = new SiteStatisticsInput { DocumentKey = key, DocumentTitle = host.Title, SourcePath = host.PathName, Sources = sources.Select(s => s.Source).ToArray() };
            targets = active.SelectMany(source => new FilteredElementCollector(source.Document).WhereElementIsNotElementType()
                .Where(e => e is Floor || e is Area || e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel)
                .Select(e => new Target { Source = source, Id = e.Id })).ToArray();
            notes.Add("范围包含当前总图及所选直接链接，净用地分母取当前总图。源模型和链接实例分别列明。");
        }

        internal static string PlacementIdentity(SiteStatisticsDocument source)
        {
            var t = source.Placement;
            var points = new[] { t.Origin, t.BasisX, t.BasisY, t.BasisZ };
            var coordinates = string.Join("|", points.SelectMany(p => new[] {p.X,p.Y,p.Z}).Select(v => Math.Round(v,8).ToString("R",CultureInfo.InvariantCulture)));
            return (source.Document?.PathName ?? source.Source.Path ?? source.Source.Key) + "|" + coordinates;
        }

        internal void ReadNext()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SiteStatisticsReader));
            if (position < targets.Length)
            {
                var end = Math.Min(position + 15, targets.Length);
                for (; position < end; position++)
                {
                    var target = targets[position];
                    if (target.Source.Document?.IsValidObject != true) throw new InvalidOperationException("统计来源已关闭或重载，请刷新。");
                    var element = target.Source.Document.GetElement(target.Id) ?? throw new InvalidOperationException("构件读取期间已变化，请刷新。");
                    var row = Read(target.Source, element);
                    if (row != null) components.Add(row);
                }
                return;
            }
            if (groups == null)
            {
                var included = components.Where(c => c.Included).ToArray();
                groups = included.GroupBy(c => SiteStatistics.GroupKey(c.Kind,c.Subtype))
                    .Select(g => new KeyValuePair<string,SiteStatisticsComponent[]>(g.Key,g.ToArray()))
                    .Concat(included.GroupBy(c => c.Kind).Select(g => new KeyValuePair<string,SiteStatisticsComponent[]>(SiteStatistics.GroupKey(g.Key,"*"),g.ToArray()))).ToArray();
            }
            if (geometryPosition < groups.Length)
            {
                var group = groups[geometryPosition++]; measurements.Add(Measure(group.Key,group.Value));
            }
        }

        internal SiteStatisticsReport Report()
        {
            if (!Complete) throw new InvalidOperationException("总图统计尚未完成。");
            input.Components = components.ToArray(); input.Geometry = measurements.ToArray(); input.Notes = notes.ToArray();
            return SiteStatistics.Build(input);
        }

        internal static SiteStatisticsReport Read(Document host, string key, bool includeLinks)
        {
            var sources = Sources(host,key);
            try
            {
                using (var reader = new SiteStatisticsReader(host,key,sources,sources.Where(s => s.Source.IsHost || includeLinks).Select(s => s.Source.Key).ToArray()))
                { while (!reader.Complete) reader.ReadNext(); return reader.Report(); }
            }
            finally { foreach (var source in sources) source.Placement.Dispose(); }
        }

        private SiteStatisticsComponent Read(SiteStatisticsDocument source, Element element)
        {
            var issues = new List<string>(); var doc = source.Document;
            string slot;
            try { slot = PlatformContext.CanonicalSlot(RevitMappingService.Slot(doc,element),profile); }
            catch (InvalidOperationException ex) { return Candidate(source,element,ex.Message); }
            if (string.IsNullOrWhiteSpace(slot))
            {
                if (new[] { "绿地投影面积", "规划净用地投影面积", "规划总用地投影面积", "车位数" }
                    .Any(n => RevitMappingService.Reads(doc,element,n).Any(r => r.HasValue && !string.IsNullOrWhiteSpace(ReferencePolicy.ToBridgeValue(r)))))
                    return Candidate(source,element,"存在总图或停车来源参数，业务类目待确认");
                return null;
            }
            var properties = CaseExportPolicy.PropertiesForSlot(slot,profile);
            var kind = slot; var subtype = "";
            if (slot == "绿地") subtype = Text(doc,element,properties,issues,"绿地类型");
            else if (slot == "规划总用地" || slot == "规划净用地") { if (!source.Source.IsHost) return null; }
            else if (slot == "道路边界线") { kind = "道路"; subtype = Text(doc,element,properties,issues,"区内道路类型"); }
            else if (slot == "总平构件")
            {
                subtype = Text(doc,element,properties,issues,"构件类型");
                if (string.IsNullOrEmpty(subtype) || !new[] { "道路", "路面", "铺装" }.Any(subtype.Contains)) return null;
                kind = "道路";
            }
            else if (slot == "停车位" || slot == "停车场" || slot == "室内停车场" || slot == "室外车位") kind = "停车";
            else return null;
            var level = doc.GetElement(element.LevelId) as Level;
            var row = new SiteStatisticsComponent {
                SourceKey = source.Source.Key, ElementId = element.Id.IntegerValue, UniqueId = element.UniqueId, Name = element.Name,
                Slot = slot, Kind = kind, Subtype = subtype, PropertySet = properties.FirstOrDefault()?.PropertySet, Included = true,
                DeclaredAreaApplicable = properties.Any(p => p.Property == "投影面积"),
                ElevationM = level == null ? (decimal?)0 : (decimal)(source.Placement.OfPoint(new XYZ(0,0,level.ProjectElevation)).Z * .3048),
                DeclaredArea = Number(doc,element,properties,issues,"投影面积")
            };
            if (kind == "绿地") row.Factor = Number(doc,element,properties,issues,"折算系数");
            if (kind == "停车") Parking(doc,element,properties,row,issues);
            if (element is Area spatial && (spatial.Location == null || spatial.Area <= 0))
            {
                row.Included = false; row.Status = "未放置或无有效边界";
                row.Notes = "该面积对象的历史填值列于待完善项目，汇总采用有效面积载体。";
                return row;
            }
            try
            {
                var solids = SiteStatisticsGeometryReader.Project(element,source.Placement);
                projected[row.SourceKey + "|" + row.UniqueId] = solids;
                row.GeometricArea = SiteStatisticsGeometryReader.UnionArea(solids);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                if (element is Area area && area.Area > 0 && area.Location != null)
                {
                    row.GeometricArea = (decimal)UnitUtils.ConvertFromInternalUnits(area.Area,DisplayUnitType.DUT_SQUARE_METERS);
                    issues.Add("已读取Revit面积对象实际面积，边界并集待核对 " + ex.Message);
                }
                else issues.Add("几何投影待核对 " + ex.Message);
            }
            row.Notes = string.Join("；",issues); row.Status = issues.Count == 0 ? null : "待核对";
            return row;
        }

        private static SiteStatisticsComponent Candidate(SiteStatisticsDocument source, Element element, string notes)
            => new SiteStatisticsComponent { SourceKey = source.Source.Key, ElementId = element.Id.IntegerValue, UniqueId = element.UniqueId,
                Name = element.Name, Kind = "待确认", Included = false, Notes = notes };

        private void Parking(Document doc, Element element, ReferenceProperty[] properties, SiteStatisticsComponent row, List<string> issues)
        {
            if (!(element is Area)) { row.Included = false; issues.Add("停车业务需要原生面积载体，当前类别为 " + element.Category?.Name); }
            if (row.Slot == "停车场")
            {
                row.MotorCount = Number(doc,element,properties,issues,"机动车位数量"); row.NonMotorCount = Number(doc,element,properties,issues,"非机动车位数量");
                row.Subtype = "停车场 · " + Text(doc,element,properties,issues,"车场类型"); return;
            }
            var type = Text(doc,element,properties,issues,"停车位类型","车位类型","停车类型");
            var count = Number(doc,element,properties,issues,"车位数");
            row.Subtype = row.Slot + " · " + (type ?? "类型待补");
            var field = properties.FirstOrDefault(p => p.Property == "停车位类型" || p.Property == "车位类型" || p.Property == "停车类型");
            var choices = field == null ? new string[0] : PlatformRules.Choices(field.PropertySet,field.Property);
            if (!string.IsNullOrWhiteSpace(type) && type.Contains("非机动车")) { row.MotorCount = 0; row.NonMotorCount = count; }
            else if (!string.IsNullOrWhiteSpace(type) && (type.Contains("机动车") || choices.Contains(type))) { row.MotorCount = count; row.NonMotorCount = 0; }
            else issues.Add("停车类型尚不能确认机动车／非机动车归属");
            if (row.Slot == "室内停车场" && !properties.Any(p => p.Property == "车位数")) issues.Add("当前室内停车场属性集未提供数量字段，车位数量待核对");
        }

        private string Text(Document doc, Element element, ReferenceProperty[] properties, List<string> issues, params string[] names)
        {
            var property = properties.FirstOrDefault(p => names.Contains(p.Property));
            if (property == null) return null;
            try
            {
                var value = ReferencePolicy.Resolve(RevitMappingService.Reads(doc,element,property.SourceParameter),property.SourceParameter);
                return RevitMappingService.ExportValue(doc,element,property,value,profile);
            }
            catch (InvalidOperationException ex) { issues.Add(property.SourceParameter + " " + ex.Message); return null; }
        }
        private decimal? Number(Document doc, Element element, ReferenceProperty[] properties, List<string> issues, params string[] names)
        {
            var text = Text(doc,element,properties,issues,names);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)) return value;
            issues.Add(names[0] + "数值无效，原值 " + text); return null;
        }

        private SiteStatisticsGeometry Measure(string key, SiteStatisticsComponent[] members)
        {
            var result = new SiteStatisticsGeometry { Key = key };
            try
            {
                if (members.Any(c => !c.GeometricArea.HasValue)) throw new InvalidOperationException("部分构件的几何投影未取得");
                if (members.Any(c => !projected.ContainsKey(c.SourceKey + "|" + c.UniqueId))) throw new InvalidOperationException("部分面积边界尚不能形成可靠并集");
                decimal area = 0;
                foreach (var plane in members.GroupBy(c => Math.Round(c.ElevationM ?? 0,6)))
                    area += SiteStatisticsGeometryReader.UnionArea(plane.SelectMany(c => projected[c.SourceKey + "|" + c.UniqueId]));
                result.UnionArea = area; result.OverlapArea = Math.Max(members.Sum(c => c.GeometricArea.Value) - area,0m);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.Verified = false;
                result.UnionArea = members.All(c=>c.GeometricArea.HasValue) ? members.Sum(c=>c.GeometricArea.Value) : (decimal?)null;
                result.Notes = "分类投影合并未完成，当前值为逐构件几何小计，重叠尚待核对。" + ex.Message;
            }
            return result;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var solid in projected.Values.SelectMany(s => s)) solid.Dispose();
            projected.Clear();
        }
    }
}
