using System;
using System.Collections.Generic;
using System.Linq;

namespace Hifc.Reference
{
    public static class SiteStatistics
    {
        public static readonly string[] Kinds = { "规划总用地", "规划净用地", "绿地", "道路", "停车" };
        public static string GroupKey(string kind, string subtype) => kind + "|" + (subtype ?? "");

        public static SiteStatisticsReport Build(SiteStatisticsInput input, decimal tolerancePercent = 5m)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var sources = input.Sources ?? new SiteStatisticsSource[0];
            var selected = new HashSet<string>(sources.Where(s => (s.IsHost || s.Selected) && s.Available).Select(s => s.Key));
            var sourceRows = (input.Components ?? new SiteStatisticsComponent[0]).Where(c => selected.Contains(c.SourceKey)).ToArray();
            var owners = sourceRows.GroupBy(c => c.SourceKey + "|" + (c.UniqueId ?? c.ElementId.ToString())).Select(g => g.First()).ToArray();
            var components = owners.Select(c => Calculate(c, sources, tolerancePercent)).ToArray();
            var included = components.Where(c => c.Included).ToArray();
            var pending = components.Where(c => !c.Included || !c.Complete).ToArray();
            var rows = new List<SiteStatisticsRow>();
            var totals = new Dictionary<string, SiteStatisticsRow>();
            foreach (var kind in Kinds)
            {
                var members = included.Where(c => c.Kind == kind).ToArray();
                if (members.Length == 0) continue;
                var groups = members.GroupBy(c => c.Subtype ?? "").OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
                foreach (var group in groups)
                    rows.Add(Aggregate(kind, group.Key, group.ToArray(), input.Geometry, tolerancePercent));
                var total = Aggregate(kind, "*", members, input.Geometry, tolerancePercent);
                total.Name = kind + "合计"; total.IsSubtotal = true; totals[kind] = total;
                if (groups.Length > 1) rows.Add(total);
            }
            var notes = new List<string>(input.Notes ?? new string[0]);
            if (sourceRows.Length != owners.Length) notes.Add("相同来源实例的重复记录已按构件身份去重。");
            if (pending.Length > 0) notes.Add("存在待确认或缺项构件，各列显示已知小计。完整范围见来源及待完善项目。");
            if (included.All(c => c.Kind != "绿地")) notes.Add("当前选定范围未识别到已确认绿地构件，绿地量按0累计。");
            notes.Add("绿地折算面积逐片乘模型中填写的折算系数。车位数量汇总模型填写数量，区域几何面积单独显示。");
            notes.Add("总用地、净用地与其内部分类分别统计。投影重叠在各分类中提示，来源与模型编辑不会被统计操作改写。");
            var metrics = Metrics(totals, included);
            return new SiteStatisticsReport {
                DocumentKey = input.DocumentKey, DocumentTitle = input.DocumentTitle, SourcePath = input.SourcePath,
                CapturedUtc = DateTime.UtcNow.ToString("O"), Sources = sources.Select(s => new SiteStatisticsSource {
                    Key=s.Key, Name=s.Name, Path=s.Path, IsHost=s.IsHost, Selected=s.Selected, Available=s.Available, Notes=s.Notes
                }).ToArray(), Components = included, Pending = pending,
                Rows = rows.ToArray(), Metrics = metrics, Notes = notes.Distinct().ToArray(), TolerancePercent = tolerancePercent,
                Complete = included.Length > 0 && pending.Length == 0 && rows.All(r => r.Complete) && metrics.All(m => m.Complete)
            };
        }

        private static SiteStatisticsComponent Calculate(SiteStatisticsComponent source, SiteStatisticsSource[] sources, decimal tolerance)
        {
            var row = new SiteStatisticsComponent {
                SourceKey = source.SourceKey, ElementId = source.ElementId, UniqueId = source.UniqueId, Name = source.Name,
                Kind = source.Kind, Subtype = source.Subtype, Slot = source.Slot, PropertySet = source.PropertySet,
                Factor = source.Factor, DeclaredArea = source.DeclaredArea, GeometricArea = source.GeometricArea,
                MotorCount = source.MotorCount, NonMotorCount = source.NonMotorCount, ElevationM = source.ElevationM,
                Included = source.Included && Kinds.Contains(source.Kind), Status = source.Status, Notes = source.Notes,
                DeclaredAreaApplicable = source.DeclaredAreaApplicable, ConvertedAreaApplicable = source.Kind == "绿地", CountApplicable = source.Kind == "停车",
                SourceNames = sources.FirstOrDefault(s => s.Key == source.SourceKey)?.Name ?? source.SourceNames,
                Selection = new[] { new SiteStatisticsSelection { SourceKey = source.SourceKey, ElementIds = new[] { source.ElementId } } }
            };
            row.Key = row.SourceKey + "|" + (row.UniqueId ?? row.ElementId.ToString());
            if (!row.Included) { row.Status = source.Status ?? "待确认分类"; return row; }
            var issues = new List<string>();
            Validate(row.GeometricArea, "实际几何面积", issues);
            if (row.DeclaredAreaApplicable) Validate(row.DeclaredArea, "填报投影面积", issues);
            if (row.ConvertedAreaApplicable)
            {
                Validate(row.Factor, "折算系数", issues);
                if (string.IsNullOrWhiteSpace(row.Subtype)) issues.Add("绿地类型待补");
                row.DeclaredConvertedArea = Product(row.DeclaredArea, row.Factor);
                row.GeometricConvertedArea = Product(row.GeometricArea, row.Factor);
            }
            if (row.CountApplicable)
            {
                Validate(row.MotorCount, "机动车数量", issues); Validate(row.NonMotorCount, "非机动车数量", issues);
                if (row.MotorCount.HasValue && decimal.Truncate(row.MotorCount.Value) != row.MotorCount) issues.Add("机动车数量应为整数");
                if (row.NonMotorCount.HasValue && decimal.Truncate(row.NonMotorCount.Value) != row.NonMotorCount) issues.Add("非机动车数量应为整数");
            }
            row.Complete = issues.Count == 0 && string.IsNullOrEmpty(source.Status);
            row.Status = row.Complete ? "已计入" : "待补 / 核对";
            row.Notes = Join(row.Notes, string.Join("；", issues));
            Compare(row, tolerance);
            return row;
        }

        private static SiteStatisticsRow Aggregate(string kind, string subtype, SiteStatisticsComponent[] members,
            SiteStatisticsGeometry[] geometry, decimal tolerance)
        {
            var row = new SiteStatisticsRow {
                Key = GroupKey(kind, subtype), Kind = kind, Subtype = subtype,
                Name = string.IsNullOrWhiteSpace(subtype) ? kind : kind + " · " + subtype,
                DeclaredAreaApplicable = members.Any(c => c.DeclaredAreaApplicable), ConvertedAreaApplicable = kind == "绿地", CountApplicable = kind == "停车",
                DeclaredArea = Sum(members.Select(c => c.DeclaredArea)), GeometricArea = Sum(members.Select(c => c.GeometricArea)),
                DeclaredConvertedArea = Sum(members.Select(c => c.DeclaredConvertedArea)), GeometricConvertedArea = Sum(members.Select(c => c.GeometricConvertedArea)),
                MotorCount = Sum(members.Select(c => ValidCount(c.MotorCount) ? c.MotorCount : null)),
                NonMotorCount = Sum(members.Select(c => ValidCount(c.NonMotorCount) ? c.NonMotorCount : null)),
                Complete = members.All(c => c.Complete), SourceNames = string.Join("、", members.Select(c => c.SourceNames).Distinct()),
                Selection = members.GroupBy(c => c.SourceKey).Select(g => new SiteStatisticsSelection { SourceKey = g.Key, ElementIds = g.Select(c => c.ElementId).Distinct().OrderBy(i => i).ToArray() }).ToArray(),
                Notes = string.Join("；", members.Select(c => c.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            };
            var measured = (geometry ?? new SiteStatisticsGeometry[0]).FirstOrDefault(g => g.Key == row.Key);
            if (measured != null)
            {
                row.GeometricArea = measured.UnionArea;
                row.Notes = Join(row.Notes, measured.Notes);
                if (!measured.UnionArea.HasValue || !measured.Verified) row.Complete = false;
                if (measured.OverlapArea > .001m)
                {
                    row.Complete = false;
                    row.Notes = Join(row.Notes, "投影重叠 " + measured.OverlapArea.Value.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture) + " ㎡，需核对计量范围");
                }
                if (row.ConvertedAreaApplicable)
                {
                    var factors = members.Select(c => c.Factor).Distinct().ToArray();
                    if (factors.Length == 1) row.GeometricConvertedArea = Product(row.GeometricArea, factors[0]);
                    else if (!row.GeometricArea.HasValue || measured.OverlapArea > .001m)
                    {
                        row.GeometricConvertedArea = null;
                        row.Notes = Join(row.Notes, "不同折算系数的重叠范围需核对，几何折算合计待确认");
                    }
                }
            }
            Compare(row, tolerance);
            if (members.Any(c => c.DeclaredAreaApplicable && !Valid(c.DeclaredArea)) || measured == null && members.Any(c => !Valid(c.GeometricArea)))
            {
                row.Difference = null; row.DifferencePercent = null; row.ExceedsTolerance = false;
                row.Notes = Join(row.Notes, "填报与几何覆盖未齐，差异待完整数据后比较");
            }
            if (!row.Complete) row.Notes = Join(row.Notes, "含待补或重叠项，数值为已知小计");
            return row;
        }

        private static SiteStatisticsMetric[] Metrics(Dictionary<string, SiteStatisticsRow> totals, SiteStatisticsComponent[] included)
        {
            var result = new List<SiteStatisticsMetric>();
            totals.TryGetValue("规划总用地", out var gross); totals.TryGetValue("规划净用地", out var net);
            totals.TryGetValue("绿地", out var green); totals.TryGetValue("停车", out var parking);
            if (gross != null) result.Add(AreaMetric("规划总用地", gross));
            result.Add(net == null ? new SiteStatisticsMetric { Name = "规划净用地", Unit = "㎡", Notes = "当前总图缺少明确的净用地载体" } : AreaMetric("规划净用地", net));
            result.Add(green == null ? new SiteStatisticsMetric { Name = "折算绿地面积", Unit = "㎡", DeclaredValue = 0, GeometricValue = 0, Complete = true }
                : new SiteStatisticsMetric { Name = "折算绿地面积", Unit = "㎡", DeclaredValue = green.DeclaredConvertedArea, GeometricValue = green.GeometricConvertedArea, Complete = green.Complete, Notes = green.Notes });
            var greenRows = included.Where(c => c.Kind == "绿地").ToArray();
            var netRows = included.Where(c => c.Kind == "规划净用地").ToArray();
            var declaredNumerator = green == null ? 0m : green.DeclaredConvertedArea;
            var geometricNumerator = green == null ? 0m : green.GeometricConvertedArea;
            var declaredComplete = greenRows.All(c => Valid(c.DeclaredArea) && Valid(c.Factor)) && netRows.Length > 0 && netRows.All(c => Valid(c.DeclaredArea));
            var geometricComplete = greenRows.All(c => Valid(c.GeometricArea) && Valid(c.Factor)) && netRows.Length > 0 && netRows.All(c => Valid(c.GeometricArea));
            var rate = new SiteStatisticsMetric {
                Name = "绿地率", Unit = "%", Formula = "折算绿地面积 ÷ 规划净用地面积 × 100，填报与几何分别使用对应分子和分母",
                DeclaredValue = declaredComplete && net?.DeclaredArea > 0 ? declaredNumerator / net.DeclaredArea * 100m : null,
                GeometricValue = geometricComplete && net?.GeometricArea > 0 ? geometricNumerator / net.GeometricArea * 100m : null,
                Notes = Join(green?.Notes, net?.Notes)
            };
            rate.Complete = rate.DeclaredValue.HasValue && rate.GeometricValue.HasValue && net.Complete && (green == null || green.Complete);
            if (!rate.DeclaredValue.HasValue || !rate.GeometricValue.HasValue) rate.Notes = Join(rate.Notes, "绿地系数、面积或净用地分母待核对，分母须大于0");
            result.Add(rate);
            if (parking != null)
            {
                var parkingRows = included.Where(c => c.Kind == "停车").ToArray();
                result.Add(CountMetric("机动车位数",parking.MotorCount,parkingRows.All(c=>ValidCount(c.MotorCount))));
                result.Add(CountMetric("非机动车位数",parking.NonMotorCount,parkingRows.All(c=>ValidCount(c.NonMotorCount))));
            }
            return result.ToArray();
        }

        private static SiteStatisticsMetric AreaMetric(string name, SiteStatisticsRow row)
            => new SiteStatisticsMetric { Name = name, Unit = "㎡", DeclaredValue = row.DeclaredArea, GeometricValue = row.GeometricArea, Complete = row.Complete, Notes = row.Notes };
        private static SiteStatisticsMetric CountMetric(string name, decimal? value, bool complete)
            => new SiteStatisticsMetric { Name=name, Unit="个", DeclaredValue=value, GeometricApplicable=false, Complete=complete,
                Notes=complete?"有效面积载体的填写数量合计":"数量存在缺项或无效值，显示已知有效小计" };
        private static bool ValidCount(decimal? value) => Valid(value) && decimal.Truncate(value.Value) == value;
        private static bool Valid(decimal? value) => value.HasValue && value >= 0;
        private static decimal? Product(decimal? area, decimal? factor) => Valid(area) && Valid(factor) ? area * factor : null;
        private static decimal? Sum(IEnumerable<decimal?> values)
        {
            var known = values.Where(Valid).Select(v => v.Value).ToArray();
            return known.Length == 0 ? (decimal?)null : known.Sum();
        }
        private static void Validate(decimal? value, string name, List<string> issues) { if (!Valid(value)) issues.Add(name + (value < 0 ? "为负值" : "待补")); }
        private static string Join(string a, string b) => BuildingAreaStatistics.Join(a, b);
        private static void Compare(SiteStatisticsRow row, decimal tolerance)
        {
            if (!row.DeclaredAreaApplicable || !Valid(row.DeclaredArea) || !Valid(row.GeometricArea)) return;
            row.Difference = row.DeclaredArea - row.GeometricArea;
            row.DifferencePercent = row.GeometricArea > 0 ? row.Difference / row.GeometricArea * 100m : row.Difference == 0 ? 0m : (decimal?)null;
            row.ExceedsTolerance = row.DifferencePercent.HasValue ? Math.Abs(row.DifferencePercent.Value) > tolerance : row.Difference != 0;
        }
    }
}
