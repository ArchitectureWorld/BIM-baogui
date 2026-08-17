using System;
using System.Globalization;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01GeoLocationPolicyTests
  {
    [Theory]
    [InlineData("114.300000", "30.600000")]
    [InlineData("-180", "-90")]
    [InlineData("180", "90")]
    public void LongitudeAndLatitudeRoundTripNumerically(
      string longitude,
      string latitude)
    {
      NativeGeoLocationValue value =
        NativeStage01GeoLocationPolicy.Parse(longitude, latitude);

      Assert.Equal(
        double.Parse(longitude, CultureInfo.InvariantCulture),
        NativeStage01GeoLocationPolicy.RadiansToDegrees(
          value.LongitudeRadians),
        10);
      Assert.Equal(
        double.Parse(latitude, CultureInfo.InvariantCulture),
        NativeStage01GeoLocationPolicy.RadiansToDegrees(
          value.LatitudeRadians),
        10);
    }

    [Theory]
    [InlineData(114.3, "114.3")]
    [InlineData(0.0, "0")]
    [InlineData(-90.0, "-90")]
    public void DegreeFormatIsCanonicalNotRawTextPreserving(
      double degrees,
      string expected)
    {
      Assert.Equal(
        expected,
        NativeStage01GeoLocationPolicy.FormatDegrees(
          NativeStage01GeoLocationPolicy.DegreesToRadians(degrees)));
    }

    [Theory]
    [InlineData("180.0001", "0")]
    [InlineData("0", "90.0001")]
    [InlineData("NaN", "0")]
    [InlineData("0", "Infinity")]
    [InlineData("", "0")]
    public void InvalidOrOutOfRangeCoordinatesAreRejected(
      string longitude,
      string latitude)
    {
      Assert.Throws<ArgumentException>(() =>
        NativeStage01GeoLocationPolicy.Parse(longitude, latitude));
    }
  }
}
