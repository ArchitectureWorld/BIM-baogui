using System;
using System.Globalization;

namespace BIMBaoGui.Stage01.Context
{
  public sealed class HBRSpatialReference
  {
    public HBRSpatialReference(
      string coordinateSystem,
      string elevationSystem,
      decimal baseX,
      decimal baseY,
      decimal baseElevation,
      decimal trueNorthAngleDegrees,
      string lengthUnit,
      string areaUnit,
      string angleUnit)
    {
      CoordinateSystem = coordinateSystem ?? string.Empty;
      ElevationSystem = elevationSystem ?? string.Empty;
      BaseX = baseX;
      BaseY = baseY;
      BaseElevation = baseElevation;
      TrueNorthAngleDegrees = trueNorthAngleDegrees;
      LengthUnit = lengthUnit ?? string.Empty;
      AreaUnit = areaUnit ?? string.Empty;
      AngleUnit = angleUnit ?? string.Empty;
    }

    public string CoordinateSystem { get; }
    public string ElevationSystem { get; }
    public decimal BaseX { get; }
    public decimal BaseY { get; }
    public decimal BaseElevation { get; }
    public decimal TrueNorthAngleDegrees { get; }
    public string LengthUnit { get; }
    public string AreaUnit { get; }
    public string AngleUnit { get; }

    public string ToSummary()
    {
      return CoordinateSystem + " / " + ElevationSystem
        + " / X=" + BaseX.ToString(CultureInfo.InvariantCulture)
        + " Y=" + BaseY.ToString(CultureInfo.InvariantCulture)
        + " Z=" + BaseElevation.ToString(CultureInfo.InvariantCulture)
        + " / 真北=" + TrueNorthAngleDegrees.ToString(CultureInfo.InvariantCulture) + "°";
    }
  }
}
