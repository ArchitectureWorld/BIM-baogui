using System;
using System.Globalization;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeGeoLocationValue
  {
    internal double LongitudeRadians { get; set; }
    internal double LatitudeRadians { get; set; }
  }

  internal static class NativeStage01GeoLocationPolicy
  {
    internal static NativeGeoLocationValue Parse(
      string longitudeDegrees,
      string latitudeDegrees)
    {
      double longitude = ParseFinite(longitudeDegrees, nameof(longitudeDegrees));
      double latitude = ParseFinite(latitudeDegrees, nameof(latitudeDegrees));
      if (longitude < -180.0 || longitude > 180.0)
      {
        throw new ArgumentException(
          "Longitude must be within [-180, 180] degrees.",
          nameof(longitudeDegrees));
      }
      if (latitude < -90.0 || latitude > 90.0)
      {
        throw new ArgumentException(
          "Latitude must be within [-90, 90] degrees.",
          nameof(latitudeDegrees));
      }
      return new NativeGeoLocationValue
      {
        LongitudeRadians = DegreesToRadians(longitude),
        LatitudeRadians = DegreesToRadians(latitude)
      };
    }

    internal static double DegreesToRadians(double degrees)
    {
      if (double.IsNaN(degrees) || double.IsInfinity(degrees))
        throw new ArgumentException("Degree value must be finite.", nameof(degrees));
      return degrees * Math.PI / 180.0;
    }

    internal static double RadiansToDegrees(double radians)
    {
      if (double.IsNaN(radians) || double.IsInfinity(radians))
        throw new ArgumentException("Radian value must be finite.", nameof(radians));
      return radians * 180.0 / Math.PI;
    }

    internal static string FormatDegrees(double radians)
    {
      double rounded = Math.Round(
        RadiansToDegrees(radians),
        12,
        MidpointRounding.AwayFromZero);
      if (rounded == 0.0) rounded = 0.0;
      return rounded.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static double ParseFinite(string value, string name)
    {
      double parsed;
      if (string.IsNullOrWhiteSpace(value)
        || !double.TryParse(
          value.Trim(),
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out parsed)
        || double.IsNaN(parsed)
        || double.IsInfinity(parsed))
      {
        throw new ArgumentException(
          "Geo-location value must be a finite invariant number.",
          name);
      }
      return parsed;
    }
  }
}
