using System.Collections.Concurrent;
using BMSL_Tracker.Models;

namespace BMSL_Tracker.Services;

public interface ILocationService
{
    bool ValidateAndSmooth(string userId, ref double latitude, ref double longitude, ref double accuracy);
}

public sealed class LocationService : ILocationService
{
    private readonly ConcurrentDictionary<string, List<UserLocation>> _userHistory = new();
    private const int MaxHistory = 5;
    private const double MaxSpeedMetersPerSecond = 300;
    private const double MaxAcceptedAccuracyMeters = 2_000;
    private static readonly TimeSpan MinimumUpdateInterval = TimeSpan.FromSeconds(5);

    public bool ValidateAndSmooth(string userId, ref double latitude, ref double longitude, ref double accuracy)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || !double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || !double.IsFinite(accuracy)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180
            || accuracy is < 0 or > MaxAcceptedAccuracyMeters)
        {
            return false;
        }

        var history = _userHistory.GetOrAdd(userId, _ => new List<UserLocation>());
        var now = DateTime.UtcNow;
        var newLocation = new UserLocation
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = now,
            Accuracy = accuracy
        };

        lock (history)
        {
            if (history.Count > 0)
            {
                var lastLocation = history[^1];
                var elapsed = now - lastLocation.Timestamp;
                if (elapsed < MinimumUpdateInterval)
                {
                    return false;
                }

                var distance = CalculateDistance(
                    lastLocation.Latitude,
                    lastLocation.Longitude,
                    latitude,
                    longitude);
                if (distance / elapsed.TotalSeconds > MaxSpeedMetersPerSecond)
                {
                    return false;
                }
            }

            history.Add(newLocation);
            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            latitude = history.Average(location => location.Latitude);
            longitude = history.Average(location => location.Longitude);
        }

        return true;
    }

    private static double CalculateDistance(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        var latitude1Radians = latitude1 * Math.PI / 180;
        var latitude2Radians = latitude2 * Math.PI / 180;
        var latitudeDelta = (latitude2 - latitude1) * Math.PI / 180;
        var longitudeDelta = (longitude2 - longitude1) * Math.PI / 180;

        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(latitude1Radians) * Math.Cos(latitude2Radians)
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        var boundedA = Math.Clamp(a, 0, 1);
        var centralAngle = 2 * Math.Atan2(Math.Sqrt(boundedA), Math.Sqrt(1 - boundedA));
        return earthRadiusMeters * centralAngle;
    }
}
