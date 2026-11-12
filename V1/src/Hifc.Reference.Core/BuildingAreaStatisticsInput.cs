namespace Hifc.Reference
{
    public sealed class BuildingAreaLevelInput
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public decimal? ElevationM { get; set; }
        public decimal? HeightM { get; set; }
        public bool BuildingFootprint { get; set; }
        public bool ExplicitBuildingLevel { get; set; }
        public bool ExplicitTopBoundary { get; set; }
        public string Notes { get; set; }
    }

    public sealed class BuildingAreaGeometryInput
    {
        public string FloorKey { get; set; }
        public decimal? UnionArea { get; set; }
        public decimal? ComponentAreaSum { get; set; }
        public decimal? OverlapArea { get; set; }
        public string Notes { get; set; }
    }

    public sealed class BuildingAreaStatisticsInput
    {
        public string DocumentKey { get; set; }
        public string DocumentTitle { get; set; }
        public string SourcePath { get; set; }
        public string BuildingName { get; set; }
        public string BuildingNumber { get; set; }
        public string CapturedUtc { get; set; }
        public decimal? DeclaredBuildingArea { get; set; }
        public decimal? DeclaredFootprintArea { get; set; }
        public BuildingAreaLevelInput[] Levels { get; set; } = new BuildingAreaLevelInput[0];
        public BuildingAreaComponent[] Components { get; set; } = new BuildingAreaComponent[0];
        public BuildingAreaGeometryInput[] Geometry { get; set; } = new BuildingAreaGeometryInput[0];
        public string[] Notes { get; set; } = new string[0];
    }
}
