using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public static class BuildingAreaStatistics
    {
        public static readonly string[] Slots = { "建筑区域", "架空", "楼顶间", "阳台" };

        public static string FloorKey(decimal? elevation, string fallbackId)
            => elevation.HasValue ? "z:" + Math.Round(elevation.Value, 6).ToString("0.######", CultureInfo.InvariantCulture)
                : "level:" + (fallbackId ?? "unknown");

        public static BuildingAreaStatisticsReport Build(BuildingAreaStatisticsInput input, decimal tolerancePercent = 5m)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (tolerancePercent < 0) throw new ArgumentOutOfRangeException(nameof(tolerancePercent));
            var notes = new List<string>(input.Notes ?? new string[0]);
            var source = input.Components ?? new BuildingAreaComponent[0];
            var unique = source.GroupBy(c => string.IsNullOrWhiteSpace(c.UniqueId) ? "id:" + c.ElementId : c.UniqueId)
                .Select(g => g.First()).ToArray();
            if (source.Length != unique.Length) notes.Add("同一构件的重复来源记录已按实例身份去重。");
            var rows = unique.Select(c => Calculate(c, tolerancePercent)).ToArray();
            var included = rows.Where(c => c.Included).ToArray();
            var pending = rows.Where(c => !c.Included || !c.Complete).ToArray();
            var levels = input.Levels ?? new BuildingAreaLevelInput[0];
            var floors = included.GroupBy(c => c.FloorKey).Select(g => Floor(g.ToArray(), levels, input.Geometry, tolerancePercent))
                .OrderBy(f => f.ElevationM ?? decimal.MaxValue).ThenBy(f => f.Name, StringComparer.Ordinal).ToArray();
            var boundaries = floors.Select(f => f.ElevationM).Concat(levels.Where(l => l.ExplicitBuildingLevel || l.ExplicitTopBoundary).Select(l => l.ElevationM))
                .Where(z => z.HasValue).Select(z => z.Value).Distinct().OrderBy(z => z).ToArray();
            foreach (var floor in floors)
            {
                var next = boundaries.Where(z => z > floor.ElevationM).Select(z => (decimal?)z).FirstOrDefault();
                if (!floor.HeightM.HasValue && floor.ElevationM.HasValue && next.HasValue)
                    floor.HeightM = next - floor.ElevationM;
            }
            foreach (var floor in floors.Where(f => !f.HeightM.HasValue))
            {
                floor.Notes = Join(floor.Notes, "层高待确认");
                floor.Complete = false;
            }

            var total = new BuildingAreaFloor { Name = "合计", IsTotal = true, ElementIds = included.Select(c => c.ElementId).ToArray() };
            Aggregate(total, floors, tolerancePercent);
            var unclassified = pending.Count(c => !c.Included);
            if (unclassified > 0) notes.Add("待确认分类构件 " + unclassified + " 件，确认分类后可纳入对应统计范围。");
            if (pending.Any(c => c.Included)) notes.Add("已纳入构件存在缺项，详见待完善项目。");
            foreach (var level in levels.Where(l => l.ExplicitBuildingLevel && !floors.Any(f => f.Key == FloorKey(l.ElevationM, l.UniqueId))))
            {
                notes.Add("已确认楼层 " + level.Name + " 尚无已纳入面积载体。");
                total.Complete = false;
            }
            if (!total.Complete) notes.Add("存在缺项或几何复核项，合计展示各字段已知小计，详见构件明细与备注。");
            return new BuildingAreaStatisticsReport {
                DocumentKey = input.DocumentKey, DocumentTitle = input.DocumentTitle, SourcePath = input.SourcePath,
                BuildingName = input.BuildingName, BuildingNumber = input.BuildingNumber, CapturedUtc = input.CapturedUtc,
                TolerancePercent = tolerancePercent, Floors = floors, Total = total, Components = included, Pending = pending,
                Comparisons = Comparisons(input, total, tolerancePercent), Notes = notes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray(),
                Complete = total.Complete && included.Length > 0,
                ScopeDescription = "当前文档已确认的建筑区域、架空、楼顶间和阳台。实际几何面积按水平投影计量，同自然层合并重叠投影，同标高辅助层合并展示。",
                FormulaDescription = "计算面积＝投影面积×计算系数；计容面积＝投影面积×计容系数；不计容面积＝逐构件 max(计算面积－计容面积,0) 合计。差额＝填报投影－实际几何；差异率＝差额÷实际几何。"
            };
        }

        private static BuildingAreaComponent Calculate(BuildingAreaComponent source, decimal tolerance)
        {
            var row = new BuildingAreaComponent {
                ElementId = source.ElementId, UniqueId = source.UniqueId, Name = source.Name, Slot = source.Slot,
                PropertySet = source.PropertySet, LevelUniqueId = source.LevelUniqueId, LevelName = source.LevelName,
                ElevationM = source.ElevationM, FloorKey = FloorKey(source.ElevationM, source.LevelUniqueId),
                MainFunction = source.MainFunction, Function = source.Function, Status = source.Status,
                DeclaredProjectionArea = source.DeclaredProjectionArea, GeometricArea = source.GeometricArea,
                CalculationFactor = source.CalculationFactor, FloorRatioFactor = source.FloorRatioFactor,
                Included = source.Included && Slots.Contains(source.Slot), Notes = source.Notes
            };
            if (!row.Included) { row.Status = "待确认分类"; return row; }
            var issues = new List<string>();
            Validate(row.DeclaredProjectionArea, "填报投影面积", issues);
            Validate(row.GeometricArea, "实际几何面积", issues);
            Validate(row.CalculationFactor, "计算系数", issues);
            Validate(row.FloorRatioFactor, "计容系数", issues);
            if (!row.ElevationM.HasValue) issues.Add("所属标高待确认");
            if (string.IsNullOrWhiteSpace(row.Function)) issues.Add("使用性质待补");
            row.CalculatedArea = Product(row.DeclaredProjectionArea, row.CalculationFactor);
            row.FloorRatioArea = Product(row.DeclaredProjectionArea, row.FloorRatioFactor);
            if (row.CalculatedArea.HasValue && row.FloorRatioArea.HasValue)
            {
                row.NonFloorRatioArea = Math.Max(row.CalculatedArea.Value - row.FloorRatioArea.Value, 0m);
                if (row.FloorRatioArea > row.CalculatedArea)
                    row.Notes = Join(row.Notes, "增计容 " + Format(row.FloorRatioArea - row.CalculatedArea) + " ㎡");
            }
            row.Complete = issues.Count == 0 && string.IsNullOrWhiteSpace(source.Status);
            row.Notes = Join(row.Notes, string.Join("；", issues));
            row.Status = row.Complete ? "已计入" : "部分数据 / 待核对";
            Difference(row, tolerance);
            return row;
        }

        private static BuildingAreaFloor Floor(BuildingAreaComponent[] rows, BuildingAreaLevelInput[] levels,
            BuildingAreaGeometryInput[] geometry, decimal tolerance)
        {
            var names = rows.Select(c => c.LevelName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()
                .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToArray();
            var selectedLevels = levels.Where(l => rows.Any(c => c.LevelUniqueId == l.UniqueId)).ToArray();
            var heights = selectedLevels.Where(l => l.HeightM > 0).Select(l => l.HeightM.Value).Distinct().ToArray();
            var result = new BuildingAreaFloor {
                Key = rows[0].FloorKey, Name = names.FirstOrDefault() ?? "未关联标高", ElevationM = rows[0].ElevationM,
                SourceLevels = string.Join("、", names), HeightM = heights.Length == 1 ? heights[0] : (decimal?)null,
                ElementIds = rows.Select(c => c.ElementId).OrderBy(id => id).ToArray(),
                FunctionSummary = string.Join("\n", rows.GroupBy(c => JoinFunction(c)).Select(g => g.Key + "  " + Format(Sum(g.Select(c => c.CalculatedArea))) + " ㎡"))
            };
            Aggregate(result, rows, tolerance);
            if (heights.Length > 1) { result.Notes = Join(result.Notes, "同标高层高字段冲突"); result.Complete = false; }
            var measured = (geometry ?? new BuildingAreaGeometryInput[0]).FirstOrDefault(g => g.FloorKey == result.Key);
            if (measured != null)
            {
                // A failed union must stay unknown. The component tab retains each measured area.
                result.GeometricArea = measured.UnionArea;
                result.Notes = Join(result.Notes, measured.Notes);
                if (!measured.UnionArea.HasValue) result.Complete = false;
                if (measured.OverlapArea > .001m)
                {
                    result.Notes = Join(result.Notes, "水平投影重叠 " + Format(measured.OverlapArea) + " ㎡，填报计量范围待核对");
                    result.Complete = false;
                }
                Difference(result, tolerance);
                if (rows.Any(c => !c.DeclaredProjectionArea.HasValue || c.DeclaredProjectionArea < 0)) ClearDifference(result);
            }
            return result;
        }

        private static string JoinFunction(BuildingAreaComponent row)
            => string.IsNullOrWhiteSpace(row.Function) ? row.Slot + " / 使用性质待补" : row.Function;

        private static void Aggregate(BuildingAreaAmounts target, IEnumerable<BuildingAreaAmounts> values, decimal tolerance)
        {
            var rows = values.ToArray();
            target.DeclaredProjectionArea = Sum(rows.Select(r => r.DeclaredProjectionArea));
            target.GeometricArea = Sum(rows.Select(r => r.GeometricArea));
            target.CalculatedArea = Sum(rows.Select(r => r.CalculatedArea));
            target.FloorRatioArea = Sum(rows.Select(r => r.FloorRatioArea));
            target.NonFloorRatioArea = Sum(rows.Select(r => r.NonFloorRatioArea));
            target.Complete = rows.Length > 0 && rows.All(r => r.Complete);
            target.Notes = string.Join("；", rows.Select(r => r.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
            if (!target.Complete) target.Notes = Join(target.Notes, "包含待补或待核对项，数值为已知小计");
            Difference(target, tolerance);
            if (rows.Any(r => !r.DifferenceArea.HasValue)) ClearDifference(target);
        }

        private static BuildingAreaComparison[] Comparisons(BuildingAreaStatisticsInput input, BuildingAreaFloor total, decimal tolerance)
        {
            var comparison = new BuildingAreaComparison {
                Name = "项目建筑面积", AttributeValue = input.DeclaredBuildingArea, CalculatedValue = total.CalculatedArea,
                Difference = input.DeclaredBuildingArea - total.CalculatedArea,
                Notes = total.Complete ? "与各层计算面积合计比较" : "当前计算值含已知小计，补齐数据后刷新"
            };
            comparison.DifferencePercent = Percent(comparison.Difference, comparison.CalculatedValue);
            comparison.ExceedsTolerance = Exceeds(comparison.Difference, comparison.DifferencePercent, comparison.CalculatedValue, tolerance);
            var footprintLevels = (input.Levels ?? new BuildingAreaLevelInput[0]).Where(l => l.BuildingFootprint).Select(l => l.Name).ToArray();
            return new[] { comparison, new BuildingAreaComparison {
                Name = "建筑基底面积", AttributeValue = input.DeclaredFootprintArea, AttributeOnly = true,
                Notes = footprintLevels.Length == 0 ? "建筑基底所属楼层待确认，独立保留申报属性" : "建筑基底所属楼层 " + string.Join("、", footprintLevels) + "，独立保留申报属性"
            } };
        }

        private static void Validate(decimal? value, string label, List<string> issues)
        {
            if (!value.HasValue) issues.Add(label + "待补");
            else if (value < 0) issues.Add(label + "为负值");
        }

        private static decimal? Product(decimal? area, decimal? factor)
            => area >= 0 && factor >= 0 ? area * factor : null;

        private static decimal? Sum(IEnumerable<decimal?> values)
        {
            var known = values.Where(v => v.HasValue && v >= 0).Select(v => v.Value).ToArray();
            return known.Length == 0 ? (decimal?)null : known.Sum();
        }

        private static void Difference(BuildingAreaAmounts row, decimal tolerance)
        {
            row.DifferenceArea = row.DeclaredProjectionArea - row.GeometricArea;
            row.DifferencePercent = Percent(row.DifferenceArea, row.GeometricArea);
            row.ExceedsTolerance = Exceeds(row.DifferenceArea, row.DifferencePercent, row.GeometricArea, tolerance);
        }

        private static decimal? Percent(decimal? difference, decimal? baseline)
            => baseline > 0 ? difference / baseline * 100m : difference == 0 ? 0m : (decimal?)null;

        private static void ClearDifference(BuildingAreaAmounts row)
        {
            row.DifferenceArea = null; row.DifferencePercent = null; row.ExceedsTolerance = false;
            row.Notes = Join(row.Notes, "填报与几何覆盖范围未齐，差额待完整数据后比较");
        }

        private static bool Exceeds(decimal? difference, decimal? percent, decimal? baseline, decimal tolerance)
            => percent.HasValue ? Math.Abs(percent.Value) > tolerance : baseline == 0 && difference.HasValue && difference != 0;

        internal static string Format(decimal? value) => value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "待补";
        internal static string Join(string first, string second)
            => string.Join("；", new[] { first, second }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
