using System;
using System.Linq;
using Hifc.Reference;

namespace Hifc.Web.Contracts
{
    public sealed class StatisticsSourceCapture
    {
        public SiteStatisticsSource Source { get; set; }
        public string PlacementIdentity { get; set; }
        public ModelSettings Settings { get; set; } = new ModelSettings();
        public ElementRecord[] Elements { get; set; } = new ElementRecord[0];
        public ParameterValue[] Values { get; set; } = new ParameterValue[0];
    }
    public sealed class StatisticsRequest
    {
        public string RuleVersion { get; set; }
        public string Kind { get; set; }
        public string DocumentKey { get; set; }
        public string DocumentTitle { get; set; }
        public string SourcePath { get; set; }
        public StatisticsSourceCapture[] Sources { get; set; } = new StatisticsSourceCapture[0];
        public string CaptureHash { get; set; }
        public StatisticsGeometryResult[] Geometry { get; set; } = new StatisticsGeometryResult[0];
    }
    public sealed class StatisticsSourcePlan
    {
        public string SourceKey { get; set; }
        public CaptureTarget[] Targets { get; set; } = new CaptureTarget[0];
    }
    public sealed class StatisticsCapturePlan
    {
        public string RuleVersion { get; set; }
        public StatisticsSourcePlan[] Sources { get; set; } = new StatisticsSourcePlan[0];
    }
    public sealed class StatisticsGeometryMember
    {
        public string SourceKey { get; set; }
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public decimal? ElevationM { get; set; }
        public decimal? GeometricArea { get; set; }
    }
    public sealed class StatisticsGeometryGroup
    {
        public string Key { get; set; }
        public StatisticsGeometryMember[] Members { get; set; } = new StatisticsGeometryMember[0];
    }
    public sealed class StatisticsGeometryPlan
    {
        public string CaptureHash { get; set; }
        public StatisticsGeometryGroup[] Groups { get; set; } = new StatisticsGeometryGroup[0];
    }
    public sealed class StatisticsGeometryResult
    {
        public string Key { get; set; }
        public bool Verified { get; set; }
        public decimal? UnionArea { get; set; }
        public decimal? ComponentAreaSum { get; set; }
        public decimal? OverlapArea { get; set; }
        public string Notes { get; set; }
    }
    public sealed class StatisticsResponse
    {
        public string CaptureHash { get; set; }
        public BuildingAreaStatisticsReport Building { get; set; }
        public SiteStatisticsReport Site { get; set; }
    }
    public sealed class StatisticsWorkbook
    {
        public string CaptureHash { get; set; }
        public string FileName { get; set; }
        public string ContentBase64 { get; set; }
    }
    public static class StatisticsIdentity
    {
        public static string Capture(StatisticsRequest request)
        {
            var sources = request.Sources.OrderBy(s => s.Source.Key, StringComparer.Ordinal).Select(s => new {
                s.Source, s.PlacementIdentity,
                SettingsHash = Canonical.ConfigurationHash(s.Settings, new ProjectDataRecord[0]),
                InventoryHash = Canonical.InventoryHash(s.Elements), ValuesHash = Canonical.ValuesHash(s.Values)
            }).ToArray();
            return Canonical.DataHash(new { request.RuleVersion, request.Kind, request.DocumentKey, request.DocumentTitle, request.SourcePath, Sources = sources });
        }
    }
}
