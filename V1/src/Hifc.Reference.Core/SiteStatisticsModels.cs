using System;

namespace Hifc.Reference
{
    public sealed class SiteStatisticsSource
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsHost { get; set; }
        public bool Selected { get; set; }
        public bool Available { get; set; } = true;
        public string Notes { get; set; }
    }
    public sealed class SiteStatisticsSelection
    {
        public string SourceKey { get; set; }
        public int[] ElementIds { get; set; } = new int[0];
    }
    public class SiteStatisticsRow
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Subtype { get; set; }
        public string SourceNames { get; set; }
        public decimal? DeclaredArea { get; set; }
        public decimal? GeometricArea { get; set; }
        public decimal? DeclaredConvertedArea { get; set; }
        public decimal? GeometricConvertedArea { get; set; }
        public decimal? MotorCount { get; set; }
        public decimal? NonMotorCount { get; set; }
        public decimal? Difference { get; set; }
        public decimal? DifferencePercent { get; set; }
        public bool ExceedsTolerance { get; set; }
        public bool Complete { get; set; }
        public bool IsSubtotal { get; set; }
        public bool DeclaredAreaApplicable { get; set; } = true;
        public bool ConvertedAreaApplicable { get; set; }
        public bool CountApplicable { get; set; }
        public string Notes { get; set; }
        public SiteStatisticsSelection[] Selection { get; set; } = new SiteStatisticsSelection[0];
    }
    public sealed class SiteStatisticsComponent : SiteStatisticsRow
    {
        public string SourceKey { get; set; }
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Slot { get; set; }
        public string PropertySet { get; set; }
        public decimal? Factor { get; set; }
        public decimal? ElevationM { get; set; }
        public bool Included { get; set; }
        public string Status { get; set; }
    }
    public sealed class SiteStatisticsMetric
    {
        public bool GeometricApplicable { get; set; } = true;
        public string Name { get; set; }
        public decimal? DeclaredValue { get; set; }
        public decimal? GeometricValue { get; set; }
        public string Unit { get; set; }
        public string Formula { get; set; }
        public string Notes { get; set; }
        public bool Complete { get; set; }
    }
    public sealed class SiteStatisticsReport
    {
        public string DocumentKey { get; set; }
        public string DocumentTitle { get; set; }
        public string SourcePath { get; set; }
        public string CapturedUtc { get; set; }
        public SiteStatisticsSource[] Sources { get; set; } = new SiteStatisticsSource[0];
        public SiteStatisticsRow[] Rows { get; set; } = new SiteStatisticsRow[0];
        public SiteStatisticsMetric[] Metrics { get; set; } = new SiteStatisticsMetric[0];
        public SiteStatisticsComponent[] Components { get; set; } = new SiteStatisticsComponent[0];
        public SiteStatisticsComponent[] Pending { get; set; } = new SiteStatisticsComponent[0];
        public string[] Notes { get; set; } = new string[0];
        public decimal TolerancePercent { get; set; } = 5m;
        public bool Complete { get; set; }
    }
    public sealed class SiteStatisticsGeometry
    {
        public bool Verified { get; set; } = true;
        public string Key { get; set; }
        public decimal? UnionArea { get; set; }
        public decimal? OverlapArea { get; set; }
        public string Notes { get; set; }
    }
    public sealed class SiteStatisticsInput
    {
        public string DocumentKey { get; set; }
        public string DocumentTitle { get; set; }
        public string SourcePath { get; set; }
        public SiteStatisticsSource[] Sources { get; set; } = new SiteStatisticsSource[0];
        public SiteStatisticsComponent[] Components { get; set; } = new SiteStatisticsComponent[0];
        public SiteStatisticsGeometry[] Geometry { get; set; } = new SiteStatisticsGeometry[0];
        public string[] Notes { get; set; } = new string[0];
    }
}
