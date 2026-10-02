using System.Collections.Concurrent;
using BMSL_Tracker.Infrastructure;
using Microsoft.Extensions.Options;

namespace BMSL_Tracker.Services;

/// <summary>A location sample that passed validation and smoothing and is safe to broadcast/persist.</summary>
public readonly record struct ValidatedLocation(
    string UserId,
    double Latitude,
    double Longitude,
    double Accuracy,
    DateTime TimestampUtc);

public interface ILocationService
{
    /// <summary>
    /// Throttles, validates (accuracy + anti-spoofing) and smooths a raw GPS sample.
    /// Returns <c>false</c> when the sample must be dropped.
    /// </summary>
    bool TryValidateAndSmooth(string userId, double latitude, double longitude, double accuracyMeters, out ValidatedLocation location);

    /// <summary>
    /// Write-amplification guard: a validated sample only needs persisting when the user moved
    /// far enough since the last write, or enough time has passed.
    /// </summary>
    bool ShouldPersist(string userId, in ValidatedLocation location);

    /// <summary>Records that a location was successfully persisted for the user.</summary>
    void MarkPersisted(string userId, in ValidatedLocation location);
}

/// <summary>
/// Stateful, in-memory validation/smoothing pipeline shared by all hub connections.
/// Thread-safe: invocations for the same user can arrive concurrently (multiple tabs/devices).
/// </summary>
public sealed class LocationService : ILocationService
{
    private readonly ConcurrentDictionary<string, UserTrackingState> _userStates = new(StringComparer.Ordinal);
    private readonly TrackerOptions _options;
    private readonly ILogger<LocationService> _logger;
    private int _stateCapWarningLogged;

    public LocationService(IOptions<TrackerOptions> options, ILogger<LocationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    public bool TryValidateAndSmooth(
        string userId, double latitude, double longitude, double accuracyMeters, out ValidatedLocation location)
    {
        location = default;

        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        if (!GeoMath.IsValidSample(latitude, longitude, accuracyMeters))
        {
            return false;
        }

        // Accuracy filter: very coarse fixes (deep indoors, tunnels, blocked browser prompts) are noise.
        if (accuracyMeters > _options.MaxAccuracyMeters)
        {
            return false;
        }

        var now = DateTime.UtcNow;

        if (!_userStates.TryGetValue(userId, out var state))
        {
            // Protect against unbounded state growth: above the soft cap, samples are still
            // broadcast (unsmoothed) but never persisted.
            if (_userStates.Count >= _options.MaxTrackedUsers)
            {
                WarnStateCapOnce();
                location = new ValidatedLocation(userId, latitude, longitude, accuracyMeters, now);
                return true;
            }

            state = _userStates.GetOrAdd(userId, _ => new UserTrackingState());
        }

        lock (state.Sync)
        {
            // Server-side per-user throttle: bounds broadcast volume regardless of client behaviour.
            if (state.LastAcceptedUtc.HasValue &&
                (now - state.LastAcceptedUtc.Value).TotalMilliseconds < _options.MinBroadcastIntervalMs)
            {
                return false;
            }

            // Anti-spoofing: reject impossible jumps relative to the last accepted sample.
            if (state.LastAcceptedUtc.HasValue)
            {
                var elapsedSeconds = (now - state.LastAcceptedUtc.Value).TotalSeconds;
                if (elapsedSeconds < 0.1)
                {
                    elapsedSeconds = 0.1; // clock coalescing guard, prevents divide-by-zero speeds
                }

                var distance = GeoMath.DistanceMeters(
                    state.LastAcceptedLat, state.LastAcceptedLng, latitude, longitude);

                if (distance / elapsedSeconds > _options.MaxValidSpeedMetersPerSecond)
                {
                    _logger.LogDebug(
                        "Rejected implausible jump for user {UserId} ({Distance:F0} m in {Elapsed:F1} s).",
                        userId, distance, elapsedSeconds);
                    return false;
                }
            }

            // "Anti-gravity" smoothing: average the last N accepted points to dampen jitter.
            state.Buffer.Add((latitude, longitude));
            if (state.Buffer.Count > _options.SmoothingWindow)
            {
                state.Buffer.RemoveAt(0);
            }

            var smoothLat = 0d;
            var smoothLng = 0d;
            foreach (var point in state.Buffer)
            {
                smoothLat += point.Lat;
                smoothLng += point.Lng;
            }

            smoothLat /= state.Buffer.Count;
            smoothLng /= state.Buffer.Count;

            state.LastAcceptedUtc = now;
            state.LastAcceptedLat = latitude;
            state.LastAcceptedLng = longitude;

            location = new ValidatedLocation(userId, smoothLat, smoothLng, accuracyMeters, now);
            return true;
        }
    }

    public bool ShouldPersist(string userId, in ValidatedLocation location)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return false;
        }

        lock (state.Sync)
        {
            if (state.LastPersistedUtc is null)
            {
                return true;
            }

            var minutesSinceWrite = (location.TimestampUtc - state.LastPersistedUtc.Value).TotalMinutes;
            if (minutesSinceWrite >= _options.PersistenceIntervalMinutes)
            {
                return true;
            }

            var moved = GeoMath.DistanceMeters(
                state.LastPersistedLat, state.LastPersistedLng, location.Latitude, location.Longitude);

            return moved > _options.PersistenceMinMoveMeters;
        }
    }

    public void MarkPersisted(string userId, in ValidatedLocation location)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return;
        }

        lock (state.Sync)
        {
            state.LastPersistedUtc = location.TimestampUtc;
            state.LastPersistedLat = location.Latitude;
            state.LastPersistedLng = location.Longitude;
        }
    }

    private void WarnStateCapOnce()
    {
        if (Interlocked.Exchange(ref _stateCapWarningLogged, 1) == 0)
        {
            _logger.LogWarning(
                "Per-user tracking state cap ({MaxTrackedUsers}) reached. Extra users are broadcast " +
                "unsmoothed and never persisted. Increase Tracker:MaxTrackedUsers if this is unexpected.",
                _options.MaxTrackedUsers);
        }
    }

    private sealed class UserTrackingState
    {
        public readonly object Sync = new();

        /// <summary>Smoothing buffer of recent raw points (lat/lng pairs).</summary>
        public List<(double Lat, double Lng)> Buffer { get; } = new();

        public DateTime? LastAcceptedUtc { get; set; }
        public double LastAcceptedLat { get; set; }
        public double LastAcceptedLng { get; set; }

        public DateTime? LastPersistedUtc { get; set; }
        public double LastPersistedLat { get; set; }
        public double LastPersistedLng { get; set; }
    }
}
