namespace BMSL_Tracker.Infrastructure;

/// <summary>
/// Shared geodesy helpers used by the hub and the location service.
/// </summary>
public static class GeoMath
{
    /// <summary>Mean Earth radius in meters (WGS-84).</summary>
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>
    /// Great-circle distance between two WGS-84 coordinates in meters (Haversine).
    /// </summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = ToRadians(lat1);
        var phi2 = ToRadians(lat2);
        var deltaPhi = ToRadians(lat2 - lat1);
        var deltaLambda = ToRadians(lon2 - lon1);

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2)
                + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Basic sanity checks for a raw GPS sample before it is accepted from the wire.
    /// </summary>
    public static bool IsValidSample(double latitude, double longitude, double accuracyMeters)
        => !double.IsNaN(latitude) && !double.IsInfinity(latitude)
           && !double.IsNaN(longitude) && !double.IsInfinity(longitude)
           && !double.IsNaN(accuracyMeters) && !double.IsInfinity(accuracyMeters)
           && latitude is >= -90 and <= 90
           && longitude is >= -180 and <= 180
           && accuracyMeters >= 0;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
