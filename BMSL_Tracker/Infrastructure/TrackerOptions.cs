using System.ComponentModel.DataAnnotations;

namespace BMSL_Tracker.Infrastructure;

/// <summary>
/// Tunable behaviour of the real-time tracking pipeline.
/// Bound from the "Tracker" configuration section and validated on startup.
/// </summary>
public sealed class TrackerOptions
{
    public const string SectionName = "Tracker";

    /// <summary>Minimum interval between two accepted samples per user (server-side throttle).</summary>
    [Range(100, 60_000)]
    public int MinBroadcastIntervalMs { get; set; } = 800;

    /// <summary>Number of recent points averaged ("anti-gravity" smoothing window).</summary>
    [Range(1, 60)]
    public int SmoothingWindow { get; set; } = 5;

    /// <summary>Samples with an accuracy radius worse than this many meters are rejected.</summary>
    [Range(1, 10_000)]
    public int MaxAccuracyMeters { get; set; } = 2000;

    /// <summary>Coordinates implying faster-than-physical movement are rejected as spoofing/errors.</summary>
    [Range(10, 10_000)]
    public double MaxValidSpeedMetersPerSecond { get; set; } = 300;

    /// <summary>Minimum minutes between database writes per user when the user is not moving.</summary>
    [Range(1, 240)]
    public int PersistenceIntervalMinutes { get; set; } = 5;

    /// <summary>Minimum displacement (meters) that forces a database write.</summary>
    [Range(1, 10_000)]
    public double PersistenceMinMoveMeters { get; set; } = 10;

    /// <summary>Historical rows older than this many days are pruned. 0 disables pruning.</summary>
    [Range(0, 3650)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>Cap on the number of in-memory per-user tracking states (protection against state bloat).</summary>
    [Range(1, 100_000)]
    public int MaxTrackedUsers { get; set; } = 5000;
}
