using System;
using System.Linq;
using Hifc.Reference;

internal static class SiteStatisticsTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static SiteStatisticsComponent Area(int id, string kind, decimal declared, decimal geometry, decimal? factor = null, string subtype = "", string source = "host")
        => new SiteStatisticsComponent { ElementId = id, UniqueId = "element-" + id, SourceKey = source, Kind = kind, Slot = kind,
            Subtype = subtype, Name = kind + id, Included = true, DeclaredArea = declared, GeometricArea = geometry, Factor = factor, ElevationM = 0 };
    static SiteStatisticsInput Input(params SiteStatisticsComponent[] rows) => new SiteStatisticsInput {
        DocumentKey = "host", Sources = new[] { new SiteStatisticsSource { Key = "host", Name = "总图", IsHost = true, Selected = true } }, Components = rows
    };
    public static void Run(Action<string, Action> check)
    {
        check("site_statistics_green_rates_use_matching_weighted_numerators_and_denominators", () => {
            var report = SiteStatistics.Build(Input(Area(1, "规划总用地", 1200, 1100), Area(2, "规划净用地", 1000, 900),
                Area(3, "绿地", 200, 180, .5m, "屋顶绿地"), Area(4, "绿地", 100, 90, 1m, "附属绿地")));
            var rate = report.Metrics.Single(m => m.Name == "绿地率");
            Assert(rate.DeclaredValue == 20 && rate.GeometricValue == 20, "Rates mix declared and measured scopes");
            Assert(report.Rows.Single(r => r.Kind == "绿地" && r.IsSubtotal).DeclaredConvertedArea == 200, "Weighted green total differs");
            Assert(!report.Rows.Any(r => r.DeclaredArea == 2200), "Gross/net nested scopes added together");
        });
        check("site_statistics_link_selection_and_instance_identity_are_explicit", () => {
            var input = Input(Area(1, "规划净用地", 1000, 1000), Area(2, "绿地", 100, 100, 1m, "附属绿地"),
                Area(2, "绿地", 50, 50, 1m, "附属绿地", "link"));
            input.Sources = input.Sources.Concat(new[] { new SiteStatisticsSource { Key = "link", Name = "1#链接", Selected = false } }).ToArray();
            Assert(SiteStatistics.Build(input).Metrics.Single(m => m.Name == "绿地率").DeclaredValue == 10, "Unselected link counted");
            input.Sources[1].Selected = true;
            Assert(SiteStatistics.Build(input).Metrics.Single(m => m.Name == "绿地率").DeclaredValue == 15, "Selected placement suppressed by duplicate element ID");
        });
        check("site_statistics_zero_factor_valid_missing_factor_leaves_rate_unknown", () => {
            var input = Input(Area(1, "规划净用地", 1000, 1000), Area(2, "绿地", 100, 100, 0m, "屋顶绿地"));
            Assert(SiteStatistics.Build(input).Metrics.Single(m => m.Name == "绿地率").DeclaredValue == 0, "Zero coefficient treated missing");
            input.Components[1].Factor = null;
            var report = SiteStatistics.Build(input);
            Assert(report.Metrics.Single(m => m.Name == "绿地率").DeclaredValue == null && report.Pending.Length == 1, "Missing coefficient fabricated");
        });
        check("site_statistics_union_and_overlap_remain_visible", () => {
            var input = Input(Area(1, "规划净用地", 1000, 1000), Area(2, "绿地", 100, 100, 1m, "附属绿地"), Area(3, "绿地", 100, 100, 1m, "附属绿地"));
            input.Geometry = new[] { new SiteStatisticsGeometry { Key = SiteStatistics.GroupKey("绿地", "附属绿地"), UnionArea = 150, OverlapArea = 50 },
                new SiteStatisticsGeometry { Key = SiteStatistics.GroupKey("绿地", "*"), UnionArea = 150, OverlapArea = 50 } };
            var report = SiteStatistics.Build(input);
            Assert(report.Rows.Single(r => r.Kind == "绿地").GeometricArea == 150, "Geometry double counted");
            Assert(report.Metrics.Single(m => m.Name == "绿地率").GeometricValue == 15 && !report.Complete, "Overlap silently accepted");
        });
        check("site_statistics_parking_counts_are_preserved_without_area_or_count_defaults", () => {
            var a = Area(1, "停车", 0, 100); a.DeclaredArea = null; a.DeclaredAreaApplicable = false; a.MotorCount = 15; a.NonMotorCount = 0;
            var b = Area(2, "停车", 0, 50); b.DeclaredArea = null; b.DeclaredAreaApplicable = false; b.MotorCount = 0; b.NonMotorCount = 20;
            var report = SiteStatistics.Build(Input(a,b));
            var row = report.Rows.Single();
            Assert(row.MotorCount == 15 && row.NonMotorCount == 20 && row.DeclaredArea == null, "Parking counts/optional area changed");
            Assert(!row.ConvertedAreaApplicable && row.CountApplicable, "Field applicability incorrect");
        });
        check("site_statistics_unknown_zero_net_area_never_becomes_infinite_rate", () => {
            var report = SiteStatistics.Build(Input(Area(1, "规划净用地", 0, 0), Area(2, "绿地", 100, 100, 1m, "附属绿地")));
            Assert(report.Metrics.Single(m => m.Name == "绿地率").DeclaredValue == null && !report.Complete, "Zero denominator accepted");
        });
        check("site_statistics_missing_geometry_preserves_known_sum_but_suppresses_unmatched_delta", () => {
            var a = Area(1, "绿地", 100, 100, 1m, "附属绿地"); var b = Area(2, "绿地", 100, 100, 1m, "附属绿地"); b.GeometricArea = null;
            var report = SiteStatistics.Build(Input(a,b));
            Assert(report.Rows.Single().GeometricArea == 100 && report.Rows.Single().Difference == null, "Partial geometry compared with full declaration");
        });
        check("site_statistics_counts_duplicate_owner_once_and_keeps_raw_precision", () => {
            var a = Area(1, "绿地", 100.555m, 100.555m, .5m, "附属绿地");
            var report = SiteStatistics.Build(Input(a,a));
            Assert(report.Components.Length == 1 && report.Rows[0].DeclaredConvertedArea == 50.2775m, "Premature rounding or duplicate owner");
        });
        check("site_statistics_partial_parking_count_is_labeled_and_invalid_fraction_excluded", () => {
            var a=Area(1,"停车",0,100); a.DeclaredAreaApplicable=false; a.MotorCount=15; a.NonMotorCount=0;
            var b=Area(2,"停车",0,100); b.DeclaredAreaApplicable=false; b.MotorCount=null; b.NonMotorCount=20;
            var input=Input(a,b); var report=SiteStatistics.Build(input);
            var count=report.Metrics.Single(m=>m.Name=="机动车位数");
            Assert(count.DeclaredValue==15 && !count.Complete && count.Notes.Contains("小计"),"Incomplete count declared complete");
            b.MotorCount=1.5m; report=SiteStatistics.Build(input);
            Assert(report.Metrics.Single(m=>m.Name=="机动车位数").DeclaredValue==15,"Invalid fractional count included");
        });
    }
}
