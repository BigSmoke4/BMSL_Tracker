using BMSL_Tracker.Models;
using System.Collections.Concurrent;

namespace BMSL_Tracker.Services
{
    public interface ILocationService
    {
        bool ValidateAndSmooth(string userId, ref double lat, ref double lng, ref double accuracy);
    }

    public class LocationService : ILocationService
    {
        // Keep track of the last few locations for each user for smoothing
        private static readonly ConcurrentDictionary<string, List<UserLocation>> _userHistory = new();
        private const int MaxHistory = 5;
        private const double MaxSpeedMetersPerSecond = 300; // Impossible jump

        public bool ValidateAndSmooth(string userId, ref double lat, ref double lng, ref double accuracy)
        {
            // 1. Accuracy filtering (2000m is safer for initial detection on desktop/indoor)
            if (accuracy > 2000) return false; 

            var history = _userHistory.GetOrAdd(userId, _ => new List<UserLocation>());

            var now = DateTime.UtcNow;
            var newLocation = new UserLocation { Latitude = lat, Longitude = lng, Timestamp = now, Accuracy = accuracy };

            lock (history)
            {
                if (history.Count > 0)
                {
                    var last = history[^1];
                    var timeDiffSeconds = (now - last.Timestamp).TotalSeconds;
                    if (timeDiffSeconds < 0.1) timeDiffSeconds = 0.1; // Prevent division by zero or negative time

                    var distance = CalculateDistance(last.Latitude, last.Longitude, lat, lng);
                    var speed = distance / timeDiffSeconds;

                    // 2. Anti-spoofing: Ignore impossible jumps
                    if (speed > MaxSpeedMetersPerSecond) return false;
                }

                // 3. Movement Trail / Smoothing: Average last N coordinates
                history.Add(newLocation);
                if (history.Count > MaxHistory) history.RemoveAt(0);

                lat = history.Average(l => l.Latitude);
                lng = history.Average(l => l.Longitude);
            }

            return true;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3; // metres
            var phi1 = lat1 * Math.PI / 180;
            var phi2 = lat2 * Math.PI / 180;
            var deltaPhi = (lat2 - lat1) * Math.PI / 180;
            var deltaLambda = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                    Math.Cos(phi1) * Math.Cos(phi2) *
                    Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }
    }
}
