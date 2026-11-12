using System;
using System.Linq;
using Hifc.Reference;

internal static class BuildingAreaStatisticsTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static BuildingAreaComponent Region(int id, decimal area, decimal calculation = 1m, decimal ratio = 1m,
        string level = "1F", decimal elevation = 0m, string slot = "建筑区域", string function = "办公")
    {
        return new BuildingAreaComponent {
            ElementId = id, UniqueId = "area-" + id, Name = "区域 " + id, Slot = slot,
            LevelUniqueId = level, LevelName = level, ElevationM = elevation, Included = true,
            DeclaredProjectionArea = area, GeometricArea = area,
            CalculationFactor = calculation, FloorRatioFactor = ratio, Function = function
        };
    }
    static BuildingAreaStatisticsInput Input(params BuildingAreaComponent[] components)
    {
        return new BuildingAreaStatisticsInput {
            DocumentKey = "doc-a", BuildingName = "1#", Components = components,
            Levels = components.GroupBy(c => c.LevelUniqueId).Select(g => new BuildingAreaLevelInput {
                UniqueId = g.Key, Name = g.First().LevelName, ElevationM = g.First().ElevationM,
                HeightM = 3.6m, ExplicitBuildingLevel = true
            }).ToArray()
        };
    }
    public static void Run(Action<string, Action> check)
    {
        check("building_statistics_matches_supplied_floor_and_total_example", () => {
            var input = Input(
                Region(1, 560.62m, 1, 0, "-1F", -4.8m, "建筑区域", "设备用房"),
                Region(2, 519.66m, 1, 1, "RF", 23.1m, "楼顶间", "设备用房"),
                Region(3, 1012.84m, 1, 1, function: "养老服务设施"),
                Region(4, 67.42m, 1, 1, function: "山地面楼梯间"),
                Region(5, 1665.33m, 1, 0, slot: "架空", function: "架空停车"),
                Region(6, 145.14m, 1, 1, function: "配电房"),
                Region(7, 2879.81m, 1, 1, "2F", 4.5m),
                Region(8, 2712.99m, 1, 1, "3F", 8.7m),
                Region(9, 2703.84m, 1, 1, "4F", 12.3m),
                Region(10, 2703.84m, 1, 1, "5F", 15.9m),
                Region(11, 2703.84m, 1, 1, "6F", 19.5m));
            var report = BuildingAreaStatistics.Build(input);
            var first = report.Floors.Single(f => f.Name == "1F");
            Assert(first.CalculatedArea == 2890.73m && first.FloorRatioArea == 1225.40m && first.NonFloorRatioArea == 1665.33m, "1F functional split differs");
            Assert(report.Total.CalculatedArea == 17675.33m && report.Total.FloorRatioArea == 15449.38m && report.Total.NonFloorRatioArea == 2225.95m, "Screenshot total differs");
            Assert(report.Floors.First().Name == "-1F" && report.Floors.Last().Name == "RF", "Floors are not elevation ordered");
        });
        check("building_statistics_factors_apply_independently_to_projection", () => {
            var report = BuildingAreaStatistics.Build(Input(Region(1, 20.36m, .5m, .5m, slot: "阳台")));
            Assert(report.Total.CalculatedArea == 10.18m && report.Total.FloorRatioArea == 10.18m && report.Total.NonFloorRatioArea == 0m, "Coefficients were multiplied together");
        });
        check("building_statistics_merges_same_height_auxiliary_levels", () => {
            var report = BuildingAreaStatistics.Build(Input(Region(1, 1225.40m), Region(2, 1665.33m, 1, 0, "1F 容积率0", 0, "架空")));
            Assert(report.Floors.Length == 1 && report.Floors[0].Name == "1F", "Auxiliary level became another natural storey");
            Assert(report.Floors[0].ElementIds.SequenceEqual(new[] { 1, 2 }) && report.Floors[0].SourceLevels.Contains("容积率0"), "Original level ownership lost");
        });
        check("building_statistics_geometry_is_independent_and_threshold_is_inclusive", () => {
            var a = Region(1, 105m); a.GeometricArea = 100m;
            var b = Region(2, 105.01m, level: "2F", elevation: 3.6m); b.GeometricArea = 100m;
            var report = BuildingAreaStatistics.Build(Input(a, b));
            Assert(report.Floors[0].DifferencePercent == 5m && !report.Floors[0].ExceedsTolerance, "Exactly five percent should pass threshold");
            Assert(report.Floors[1].ExceedsTolerance && report.Floors[1].DifferenceArea == 5.01m, "Over-five-percent delta not flagged");
            Assert(a.DeclaredProjectionArea == 105m && a.GeometricArea == 100m, "Statistics changed source input");
        });
        check("building_statistics_uses_projected_union_and_discloses_overlap", () => {
            var input = Input(Region(1, 100), Region(2, 80));
            input.Geometry = new[] { new BuildingAreaGeometryInput { FloorKey = BuildingAreaStatistics.FloorKey(0m, "1F"), UnionArea = 150m, ComponentAreaSum = 180m, OverlapArea = 30m } };
            var report = BuildingAreaStatistics.Build(input);
            Assert(report.Total.GeometricArea == 150m && report.Total.DeclaredProjectionArea == 180m, "Overlap counted twice as geometry");
            Assert(report.Floors[0].Notes.Contains("重叠") && !report.Complete, "Overlap was silently accepted as complete");
        });
        check("building_statistics_missing_inputs_are_partial_but_zero_is_valid", () => {
            var a = Region(1, 100, 0, 0); var b = Region(2, 50); b.CalculationFactor = null;
            var report = BuildingAreaStatistics.Build(Input(a, b));
            Assert(report.Components.Single(c => c.ElementId == 1).CalculatedArea == 0m, "Zero coefficient treated as absent");
            Assert(report.Components.Single(c => c.ElementId == 2).CalculatedArea == null && !report.Complete, "Missing coefficient was fabricated");
            Assert(report.Total.CalculatedArea == 0m && report.Total.FloorRatioArea == 50m, "Known subtotals lost");
            Assert(report.Pending.Any(c => c.ElementId == 2 && c.Included), "Missing classified input is absent from pending list");
        });
        check("building_statistics_pending_objects_are_visible_and_not_added", () => {
            var pending = Region(2, 1000); pending.Included = false; pending.Slot = null;
            var report = BuildingAreaStatistics.Build(Input(Region(1, 100), pending));
            Assert(report.Total.CalculatedArea == 100m && report.Pending.Single().ElementId == 2, "Pending area altered classified total");
        });
        check("building_statistics_project_and_footprint_are_independent", () => {
            var input = Input(Region(1, 100)); input.DeclaredBuildingArea = 120; input.DeclaredFootprintArea = 234.98m;
            var report = BuildingAreaStatistics.Build(input);
            Assert(report.Comparisons.Any(c => c.Name == "项目建筑面积" && c.Difference == 20m), "Project building area comparison missing");
            Assert(report.Comparisons.Any(c => c.Name == "建筑基底面积" && c.AttributeValue == 234.98m && c.CalculatedValue == null), "Footprint was compared to floor area sum");
        });
        check("building_statistics_extra_far_does_not_cancel_unaccounted_area", () => {
            var report = BuildingAreaStatistics.Build(Input(Region(1, 100, 1, 2), Region(2, 100, 1, 0, slot: "架空")));
            Assert(report.Total.CalculatedArea == 200 && report.Total.FloorRatioArea == 200 && report.Total.NonFloorRatioArea == 100, "Excess FAR cancelled a different component's non-FAR area");
            Assert(report.Total.Notes.Contains("增计容"), "Extra FAR is not disclosed");
        });
        check("building_statistics_rounds_only_for_display_and_counts_each_owner_once", () => {
            var a = Region(1, 100.555m); var b = Region(2, 419.098m);
            var report = BuildingAreaStatistics.Build(Input(a, b, a));
            Assert(report.Total.CalculatedArea == 519.653m && report.Components.Length == 2, "Premature rounding or duplicate source count");
        });
        check("building_statistics_uses_confirmed_empty_storey_for_height", () => {
            var input = Input(Region(1, 100), Region(2, 100, level: "3F", elevation: 7.2m));
            foreach (var level in input.Levels) level.HeightM = null;
            input.Levels = input.Levels.Concat(new[] { new BuildingAreaLevelInput { UniqueId = "2F", Name = "2F", ElevationM = 3.6m, ExplicitBuildingLevel = true } }).ToArray();
            var report = BuildingAreaStatistics.Build(input);
            Assert(report.Floors[0].HeightM == 3.6m, "Height jumped across a confirmed empty floor");
            Assert(!report.Floors.Last().Complete && !report.Complete, "Unknown uppermost height declared complete");
        });
        check("building_statistics_partial_geometry_does_not_generate_false_difference", () => {
            var a = Region(1, 100); var b = Region(2, 100); b.GeometricArea = null;
            var report = BuildingAreaStatistics.Build(Input(a, b));
            Assert(report.Total.GeometricArea == 100m, "Known geometry subtotal lost");
            Assert(report.Total.DifferenceArea == null && report.Floors[0].DifferencePercent == null, "Full projection compared with partial geometry");
        });
    }
}
