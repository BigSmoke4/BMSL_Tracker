using BMSL_Tracker.Infrastructure;
using BMSL_Tracker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BMSL_Tracker.Tests;

/// <summary>
/// Deterministic tests for the validation / smoothing / throttling / persistence pipeline.
/// Time-based guards are neutralised (MinBroadcastIntervalMs = 0) so assertions are stable;
/// the "impossible jump" rule uses the 0.1 s minimum elapsed time, which makes a huge jump
/// reject deterministically.
/// </summary>
public class LocationServiceTests
{
    private static LocationService CreateService(Action<TrackerOptions>? configure = null)
    {
        var options = new TrackerOptions
        {
            MinBroadcastIntervalMs = 0,
            SmoothingWindow = 5,
            MaxAccuracyMeters = 2000,
            MaxValidSpeedMetersPerSecond = 300,
            PersistenceIntervalMinutes = 5,
            PersistenceMinMoveMeters = 10,
            MaxTrackedUsers = 100,
        };
        configure?.Invoke(options);
        return new LocationService(Options.Create(options), NullLogger<LocationService>.Instance);
    }

    [Fact]
    public void TryValidateAndSmooth_AcceptsFirstValidSample()
    {
        var service = CreateService();

        var accepted = service.TryValidateAndSmooth("user-1", 23.81, 90.41, 10, out var location);

        Assert.True(accepted);
        Assert.Equal(23.81, location.Latitude, 6);
        Assert.Equal(90.41, location.Longitude, 6);
    }

    [Theory]
    [InlineData(double.NaN, 90.41, 10)]
    [InlineData(91, 90.41, 10)]
    [InlineData(23.81, 181, 10)]
    [InlineData(23.81, 90.41, -1)]
    public void TryValidateAndSmooth_RejectsInvalidSamples(double lat, double lng, double accuracy)
    {
        var service = CreateService();

        Assert.False(service.TryValidateAndSmooth("user-1", lat, lng, accuracy, out _));
    }

    [Fact]
    public void TryValidateAndSmooth_RejectsPoorAccuracy()
    {
        var service = CreateService(o => o.MaxAccuracyMeters = 200);

        Assert.False(service.TryValidateAndSmooth("user-1", 23.81, 90.41, accuracyMeters: 5000, out _));
    }

    [Fact]
    public void TryValidateAndSmooth_RejectsImpossibleJump()
    {
        var service = CreateService();

        // First fix near Dhaka.
        Assert.True(service.TryValidateAndSmooth("user-1", 23.81, 90.41, 10, out _));

        // Same instant, 1000 km away -> "airplane-speed" spoof.
        Assert.False(service.TryValidateAndSmooth("user-1", 30.0, 100.0, 10, out _));
    }

    [Fact]
    public void TryValidateAndSmooth_SmoothsOverWindow()
    {
        var service = CreateService(o => o.SmoothingWindow = 2);

        Assert.True(service.TryValidateAndSmooth("user-1", 10.0, 20.0, 5, out var first));
        Assert.Equal(10.0, first.Latitude, 6);

        // Second sample ~22 m north (lat +0.0002) stays below the impossible-speed guard.
        Assert.True(service.TryValidateAndSmooth("user-1", 10.0002, 20.0, 6, out var second));
        Assert.Equal(10.0001, second.Latitude, 6); // average of the 2-point window
        Assert.Equal(20.0, second.Longitude, 6);
    }

    [Fact]
    public void TryValidateAndSmooth_KeepsUsersIndependent()
    {
        var service = CreateService();

        Assert.True(service.TryValidateAndSmooth("user-1", 23.81, 90.41, 10, out _));
        Assert.True(service.TryValidateAndSmooth("user-2", -33.87, 151.21, 10, out var other));

        Assert.Equal(-33.87, other.Latitude, 6);
    }

    [Fact]
    public void ShouldPersist_FirstAcceptedSampleYes_ThenOnlyOnMoveOrInterval()
    {
        // SmoothingWindow = 1 -> persisted coordinates are the raw samples, which makes the
        // distance-based write-gate easy to reason about deterministically.
        var service = CreateService(o => o.SmoothingWindow = 1);

        Assert.True(service.TryValidateAndSmooth("user-1", 23.81, 90.41, 10, out var loc));
        Assert.True(service.ShouldPersist("user-1", loc));

        service.MarkPersisted("user-1", loc);

        // Still: same spot, no time elapsed -> no write.
        Assert.True(service.TryValidateAndSmooth("user-1", 23.81, 90.41, 10, out var idle));
        Assert.False(service.ShouldPersist("user-1", idle));

        // Moved ~22 m -> write again even inside the interval.
        Assert.True(service.TryValidateAndSmooth("user-1", 23.8102, 90.41, 10, out var moved));
        Assert.True(service.ShouldPersist("user-1", moved));
    }

    [Fact]
    public void ShouldPersist_UnknownUser_ReturnsFalse()
    {
        var service = CreateService();
        var loc = new ValidatedLocation("ghost", 1, 1, 5, DateTime.UtcNow);
        Assert.False(service.ShouldPersist("ghost", loc));
    }
}
