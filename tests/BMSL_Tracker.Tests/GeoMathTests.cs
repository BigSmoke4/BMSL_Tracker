using BMSL_Tracker.Infrastructure;
using Xunit;

namespace BMSL_Tracker.Tests;

public class GeoMathTests
{
    [Fact]
    public void DistanceMeters_SamePoint_IsZero()
    {
        Assert.Equal(0, GeoMath.DistanceMeters(23.81, 90.41, 23.81, 90.41), 6);
    }

    [Fact]
    public void DistanceMeters_OneHundredthLatitudeDegree_IsAboutElevenHundredMeters()
    {
        // 0.01° of latitude ≈ 1,111.9 m (mean Earth radius).
        var meters = GeoMath.DistanceMeters(0, 0, 0.01, 0);
        Assert.InRange(meters, 1105, 1119);
    }

    [Theory]
    [InlineData(23.81, 90.41, 12.5, true)]
    [InlineData(91, 0, 5, false)]
    [InlineData(-90.0001, 0, 5, false)]
    [InlineData(0, 180.5, 5, false)]
    [InlineData(0, 0, -1, false)]
    [InlineData(double.NaN, 0, 5, false)]
    [InlineData(0, double.PositiveInfinity, 5, false)]
    public void IsValidSample_ChecksBoundsAndFiniteValues(
        double lat, double lng, double accuracy, bool expected)
    {
        Assert.Equal(expected, GeoMath.IsValidSample(lat, lng, accuracy));
    }
}
