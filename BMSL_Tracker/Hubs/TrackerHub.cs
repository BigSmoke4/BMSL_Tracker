using BMSL_Tracker.Data;
using BMSL_Tracker.Infrastructure;
using BMSL_Tracker.Models;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Hubs;

/// <summary>
/// Real-time geolocation hub. Clients join as authenticated Identity users; every accepted
/// sample is broadcast to all connected dashboards and durably logged through the
/// write-amplification guard. Invalid/throttled samples are dropped silently by design.
/// </summary>
[Authorize]
public sealed class TrackerHub : Hub
{
    /// <summary>Only this recent history is replayed to freshly connected clients.</summary>
    private static readonly TimeSpan InitialStateWindow = TimeSpan.FromHours(1);
    private const int MaxInitialStatesPerUser = 200;

    private readonly ApplicationDbContext _context;
    private readonly ILocationService _locations;
    private readonly ILogger<TrackerHub> _logger;

    public TrackerHub(
        ApplicationDbContext context,
        ILocationService locations,
        ILogger<TrackerHub> logger)
    {
        _context = context;
        _locations = locations;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        await Clients.Caller.SendAsync("YourId", userId ?? string.Empty);

        await SendRecentLocationsAsync(userId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Client invocation: raw GPS sample. Validation, smoothing, throttling and persistence
    /// policy are centralized in <see cref="ILocationService"/> (unit-tested, provider-independent).
    /// </summary>
    public async Task SendLocation(double lat, double lng, double accuracy, CancellationToken cancellationToken)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        if (!GeoMath.IsValidSample(lat, lng, accuracy))
        {
            return;
        }

        if (!_locations.TryValidateAndSmooth(userId, lat, lng, accuracy, out var location))
        {
            return;
        }

        var userName = Context.User?.Identity?.Name ?? "User";

        await Clients.All.SendAsync(
            "ReceiveLocation",
            location.UserId,
            userName,
            location.Latitude,
            location.Longitude,
            location.Accuracy,
            location.TimestampUtc,
            cancellationToken);

        if (!_locations.ShouldPersist(userId, location))
        {
            return;
        }

        _context.UserLocations.Add(new UserLocation
        {
            UserId = userId,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Accuracy = location.Accuracy,
            Timestamp = location.TimestampUtc,
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            _locations.MarkPersisted(userId, location);
        }
        catch (OperationCanceledException)
        {
            // Client went away mid-save; nothing to do.
        }
        catch (DbUpdateException ex)
        {
            // Never let a transient persistence error kill the live connection.
            _logger.LogError(ex, "Failed to persist location for user {UserId}.", userId);
        }
    }

    private async Task SendRecentLocationsAsync(string? currentUserId)
    {
        List<LatestLocation> latest;
        try
        {
            latest = await GetLatestPerUserAsync(currentUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not load recent locations for the new hub connection.");
            return;
        }

        foreach (var loc in latest)
        {
            await Clients.Caller.SendAsync(
                "ReceiveLocation", loc.UserId, loc.UserName, loc.Latitude, loc.Longitude, loc.Accuracy, loc.Timestamp);
        }
    }

    private async Task<List<LatestLocation>> GetLatestPerUserAsync(string? currentUserId)
    {
        // Fetch the window, then reduce in memory: avoids provider-specific GroupBy translation
        // quirks and is fast for the expected 100-1000 rows per hour of field activity.
        var since = DateTime.UtcNow - InitialStateWindow;

        var recent = await _context.UserLocations
            .AsNoTracking()
            .Where(l => l.Timestamp >= since)
            .OrderByDescending(l => l.Timestamp)
            .Take(MaxInitialStatesPerUser * 50)
            .Select(l => new { l.UserId, l.Latitude, l.Longitude, l.Accuracy, l.Timestamp })
            .ToListAsync(Context.CancellationToken);

        if (recent.Count == 0)
        {
            return [];
        }

        var latestPerUser = recent
            .GroupBy(l => l.UserId)
            .Select(g => g.First())
            .ToList();

        var userIds = latestPerUser.Select(l => l.UserId).Distinct().ToList();

        var names = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id!))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(Context.CancellationToken);

        var nameLookup = names.ToDictionary(u => u.Id!, u => u.UserName ?? "User");

        return latestPerUser
            .Select(l => new LatestLocation(
                l.UserId,
                nameLookup.GetValueOrDefault(l.UserId, "User"),
                l.Latitude,
                l.Longitude,
                l.Accuracy,
                l.Timestamp))
            .ToList();
    }

    private sealed record LatestLocation(
        string UserId,
        string UserName,
        double Latitude,
        double Longitude,
        double Accuracy,
        DateTime Timestamp);
}
