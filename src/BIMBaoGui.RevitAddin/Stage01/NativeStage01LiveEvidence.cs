namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01LiveEvidence
  {
    internal bool ProjectInformationAvailable { get; set; }
    internal string ProjectName { get; set; } = string.Empty;
    internal string ProjectNumber { get; set; } = string.Empty;
    internal bool ProjectPositionAvailable { get; set; }
    internal string BaseX { get; set; } = string.Empty;
    internal string BaseY { get; set; } = string.Empty;
    internal string BaseElevation { get; set; } = string.Empty;
    internal string TrueNorthAngle { get; set; } = string.Empty;
    internal bool GeoLocationAvailable { get; set; }
    internal string Longitude { get; set; } = string.Empty;
    internal string Latitude { get; set; } = string.Empty;
    internal bool UnitsAvailable { get; set; }
    internal string LengthUnit { get; set; } = string.Empty;
    internal string AreaUnit { get; set; } = string.Empty;
    internal string AngleUnit { get; set; } = string.Empty;

    internal NativeStage01LiveEvidence Clone()
    {
      return new NativeStage01LiveEvidence
      {
        ProjectInformationAvailable = ProjectInformationAvailable,
        ProjectName = ProjectName,
        ProjectNumber = ProjectNumber,
        ProjectPositionAvailable = ProjectPositionAvailable,
        BaseX = BaseX,
        BaseY = BaseY,
        BaseElevation = BaseElevation,
        TrueNorthAngle = TrueNorthAngle,
        GeoLocationAvailable = GeoLocationAvailable,
        Longitude = Longitude,
        Latitude = Latitude,
        UnitsAvailable = UnitsAvailable,
        LengthUnit = LengthUnit,
        AreaUnit = AreaUnit,
        AngleUnit = AngleUnit
      };
    }

    internal static NativeStage01LiveEvidence Create(
      bool projectInformationAvailable,
      string projectNumber,
      string projectName,
      bool projectPositionAvailable,
      string baseX,
      string baseY,
      string baseElevation,
      string trueNorthAngle,
      bool geoLocationAvailable,
      string longitude,
      string latitude,
      bool unitsAvailable,
      string lengthUnit,
      string areaUnit,
      string angleUnit)
    {
      return new NativeStage01LiveEvidence
      {
        ProjectInformationAvailable = projectInformationAvailable,
        ProjectNumber = projectNumber ?? string.Empty,
        ProjectName = projectName ?? string.Empty,
        ProjectPositionAvailable = projectPositionAvailable,
        BaseX = baseX ?? string.Empty,
        BaseY = baseY ?? string.Empty,
        BaseElevation = baseElevation ?? string.Empty,
        TrueNorthAngle = trueNorthAngle ?? string.Empty,
        GeoLocationAvailable = geoLocationAvailable,
        Longitude = longitude ?? string.Empty,
        Latitude = latitude ?? string.Empty,
        UnitsAvailable = unitsAvailable,
        LengthUnit = lengthUnit ?? string.Empty,
        AreaUnit = areaUnit ?? string.Empty,
        AngleUnit = angleUnit ?? string.Empty
      };
    }
  }
}
