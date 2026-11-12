using System;

namespace Hifc.Reference
{
    public sealed class BuildingAreaDocumentOption
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class BuildingAreaAmounts
    {
        public decimal? DeclaredProjectionArea { get; set; }
        public decimal? GeometricArea { get; set; }
        public decimal? CalculatedArea { get; set; }
        public decimal? FloorRatioArea { get; set; }
        public decimal? NonFloorRatioArea { get; set; }
        public decimal? DifferenceArea { get; set; }
        public decimal? DifferencePercent { get; set; }
        public bool ExceedsTolerance { get; set; }
        public bool Complete { get; set; }
        public string Notes { get; set; }
    }

    public sealed class BuildingAreaComponent : BuildingAreaAmounts
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string Slot { get; set; }
        public string PropertySet { get; set; }
        public string LevelUniqueId { get; set; }
        public string LevelName { get; set; }
        public decimal? ElevationM { get; set; }
        public string FloorKey { get; set; }
        public string MainFunction { get; set; }
        public string Function { get; set; }
        public decimal? CalculationFactor { get; set; }
        public decimal? FloorRatioFactor { get; set; }
        public bool Included { get; set; }
        public string Status { get; set; }
    }

    public sealed class BuildingAreaFloor : BuildingAreaAmounts
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public decimal? ElevationM { get; set; }
        public decimal? HeightM { get; set; }
        public string FunctionSummary { get; set; }
        public string SourceLevels { get; set; }
        public int[] ElementIds { get; set; } = new int[0];
        public bool IsTotal { get; set; }
    }

    public sealed class BuildingAreaComparison
    {
        public bool AttributeOnly { get; set; }
        public string Name { get; set; }
        public decimal? AttributeValue { get; set; }
        public decimal? CalculatedValue { get; set; }
        public decimal? Difference { get; set; }
        public decimal? DifferencePercent { get; set; }
        public bool ExceedsTolerance { get; set; }
        public string Notes { get; set; }
    }

    public sealed class BuildingAreaStatisticsReport
    {
        public string DocumentKey { get; set; }
        public string DocumentTitle { get; set; }
        public string SourcePath { get; set; }
        public string BuildingName { get; set; }
        public string BuildingNumber { get; set; }
        public string CapturedUtc { get; set; }
        public decimal TolerancePercent { get; set; } = 5m;
        public string ScopeDescription { get; set; }
        public string FormulaDescription { get; set; }
        public BuildingAreaFloor[] Floors { get; set; } = new BuildingAreaFloor[0];
        public BuildingAreaFloor Total { get; set; } = new BuildingAreaFloor { Name = "合计", IsTotal = true };
        public BuildingAreaComponent[] Components { get; set; } = new BuildingAreaComponent[0];
        public BuildingAreaComponent[] Pending { get; set; } = new BuildingAreaComponent[0];
        public BuildingAreaComparison[] Comparisons { get; set; } = new BuildingAreaComparison[0];
        public string[] Notes { get; set; } = new string[0];
        public bool Complete { get; set; }
    }
}
