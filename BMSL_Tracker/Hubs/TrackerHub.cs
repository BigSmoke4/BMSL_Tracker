using BMSL_Tracker.Data;
using BMSL_Tracker.Models;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Hubs;

[Authorize]
public sealed class TrackerHub : Hub
{
    private static readonly TimeSpan InitialLocationWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumDatabaseSaveInterval = TimeSpan.FromMinutes(5);
    private const double MinimumDistanceToSaveMeters = 10;

    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILocationService _locationService;
    private readonly ILogger<TrackerHub> _logger;

    public TrackerHub(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILocationService locationService,
        ILogger<TrackerHub> logger)
    {
        _contextFactory = contextFactory;
        _locationService = locationService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var userId = Context.UserIdentifier;
        await Clients.Caller.SendAsync("YourId", userId, Context.ConnectionAborted);

        var cutoff = DateTime.UtcNow.Subtract(InitialLocationWindow);
        await using var db = await _contextFactory.CreateDbContextAsync(Context.ConnectionAborted);

        // Only expose each active teammate's most recent point to a newly connected client.
        var recentLocations = await db.UserLocations
            .AsNoTracking()
            .Where(location => location.Timestamp >= cutoff)
            .Select(location => new
            {
                location.UserId,
                location.Latitude,
                location.Longitude,
                location.Accuracy,
                location.Timestamp
            })
            .ToListAsync(Context.ConnectionAborted);

        var latestPerUser = recentLocations
            .GroupBy(location => location.UserId)
            .Select(group => group.MaxBy(location => location.Timestamp))
            .Where(location => location is not null)
            .ToList();

        if (latestPerUser.Count == 0)
        {
            return;
        }

        var userIds = latestPerUser
            .Select(location => location!.UserId)
            .Distinct()
            .ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName })
            .ToDictionaryAsync(user => user.Id, user => user.UserName, Context.ConnectionAborted);

        foreach (var location in latestPerUser)
        {
            if (location is null)
            {
                continue;
            }

            var name = users.GetValueOrDefault(location.UserId) ?? "Team member";
            await Clients.Caller.SendAsync(
                "ReceiveLocation",
                location.UserId,
                name,
                location.Latitude,
                location.Longitude,
                location.Accuracy,
                DateTime.SpecifyKind(location.Timestamp, DateTimeKind.Utc),
                Context.ConnectionAborted);
        }
    }

    public async Task StopSharing()
    {
        if (!string.IsNullOrWhiteSpace(Context.UserIdentifier))
        {
            await Clients.All.SendAsync("LocationStopped", Context.UserIdentifier, Context.ConnectionAborted);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!string.IsNullOrWhiteSpace(Context.UserIdentifier))
        {
            await Clients.Others.SendAsync("LocationStopped", Context.UserIdentifier);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendLocation(double latitude, double longitude, double accuracy)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        if (!_locationService.ValidateAndSmooth(userId, ref latitude, ref longitude, ref accuracy))
        {
            return;
        }

        var timestamp = DateTime.UtcNow;
        var userName = Context.User?.Identity?.Name ?? "Team member";

        await using var db = await _contextFactory.CreateDbContextAsync(Context.ConnectionAborted);
        var lastSaved = await db.UserLocations
            .AsNoTracking()
            .Where(location => location.UserId == userId)
            .OrderByDescending(location => location.Timestamp)
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        var shouldSave = lastSaved is null
            || timestamp - lastSaved.Timestamp >= MinimumDatabaseSaveInterval
            || CalculateDistance(lastSaved.Latitude, lastSaved.Longitude, latitude, longitude) > MinimumDistanceToSaveMeters;

        if (shouldSave)
        {
            db.UserLocations.Add(new UserLocation
            {
                UserId = userId,
                Latitude = latitude,
                Longitude = longitude,
                Accuracy = accuracy,
                Timestamp = timestamp
            });

            try
            {
                await db.SaveChangesAsync(Context.ConnectionAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Could not persist a location update for user {UserId}.", userId);
                throw;
            }
        }

        // All tracker clients must be authenticated; the app's privacy notice explains that
        // signed-in teammates can see live location while sharing is active.
        await Clients.All.SendAsync(
            "ReceiveLocation",
            userId,
            userName,
            latitude,
            longitude,
            accuracy,
            timestamp,
            Context.ConnectionAborted);
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
