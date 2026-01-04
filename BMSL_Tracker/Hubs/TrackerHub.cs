using Microsoft.AspNetCore.SignalR;
using BMSL_Tracker.Models;
using BMSL_Tracker.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Hubs
{
    [Authorize]
    public class TrackerHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly Services.ILocationService _locationService;

        public TrackerHub(ApplicationDbContext context, Services.ILocationService locationService)
        {
            _context = context;
            _locationService = locationService;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("YourId", Context.UserIdentifier);

            // Fetch locations from the last 1 hour
            // Materialize list first to avoid EF Core 9.0 GroupBy translation bugs
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            
            var locationsInLastHour = await _context.UserLocations
                .AsNoTracking()
                .Where(l => l.Timestamp >= oneHourAgo)
                .Select(l => new { l.UserId, l.Latitude, l.Longitude, l.Accuracy, l.Timestamp })
                .ToListAsync();

            if (!locationsInLastHour.Any()) return;

            // Grouping in-memory is safe and fast for 100-1000 records
            var latestPerUser = locationsInLastHour
                .GroupBy(l => l.UserId)
                .Select(g => g.OrderByDescending(l => l.Timestamp).FirstOrDefault())
                .Where(l => l != null)
                .ToList();

            var userIds = latestPerUser.Select(l => l!.UserId).Distinct().ToList();
            
            // Fetch user names safely
            var userList = await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName })
                .ToListAsync();

            var userMap = userList.ToDictionary(u => u.Id, u => u.UserName);

            foreach (var loc in latestPerUser)
            {
                if (loc == null) continue;
                var userName = userMap.GetValueOrDefault(loc.UserId) ?? "User";
                await Clients.Caller.SendAsync("ReceiveLocation", loc.UserId, userName, loc.Latitude, loc.Longitude, loc.Accuracy, loc.Timestamp);
            }

            await base.OnConnectedAsync();
        }

        public async Task SendLocation(double lat, double lng, double accuracy)
        {
            var userId = Context.UserIdentifier;
            var userName = Context.User?.Identity?.Name;

            if (string.IsNullOrEmpty(userId)) return;

            // Apply Anti-Gravity smoothing/validation
            if (!_locationService.ValidateAndSmooth(userId, ref lat, ref lng, ref accuracy))
            {
                return; // Invalid or too inaccurate
            }

            // Broadcast to all clients (for 100 users visible to everyone)
            await Clients.All.SendAsync("ReceiveLocation", userId, userName, lat, lng, accuracy, DateTime.UtcNow);

            // Optimization: Only save to database if they moved significantly (>10m)
            // or if it's been more than 5 minutes since the last save
            var lastSaved = await _context.UserLocations
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync();

            bool shouldSave = false;
            if (lastSaved == null)
            {
                shouldSave = true;
            }
            else
            {
                var timeSinceLastSave = (DateTime.UtcNow - lastSaved.Timestamp).TotalMinutes;
                if (timeSinceLastSave >= 5)
                {
                    shouldSave = true;
                }
                else
                {
                    // Haversine distance check
                    var distance = CalculateDistance(lastSaved.Latitude, lastSaved.Longitude, lat, lng);
                    if (distance > 10) // 10 meters
                    {
                        shouldSave = true;
                    }
                }
            }

            if (shouldSave)
            {
                var location = new UserLocation
                {
                    UserId = userId,
                    Latitude = lat,
                    Longitude = lng,
                    Accuracy = accuracy,
                    Timestamp = DateTime.UtcNow
                };

                _context.UserLocations.Add(location);
                await _context.SaveChangesAsync();
            }
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
