using BMSL_Tracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Services;

/// <summary>Deletes expired GPS history so location records are not kept indefinitely.</summary>
public sealed class LocationRetentionService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<LocationRetentionService> _logger;
    private readonly TimeSpan _retentionPeriod;

    public LocationRetentionService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IConfiguration configuration,
        ILogger<LocationRetentionService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        var retentionDays = configuration.GetValue<int?>("LocationData:RetentionDays") ?? 30;
        _retentionPeriod = TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 3_650));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db = await _contextFactory.CreateDbContextAsync(stoppingToken);
                var expiresBefore = DateTime.UtcNow.Subtract(_retentionPeriod);
                var deletedRows = await db.UserLocations
                    .Where(location => location.Timestamp < expiresBefore)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deletedRows > 0)
                {
                    _logger.LogInformation(
                        "Removed {Count} location records older than {RetentionDays} days.",
                        deletedRows,
                        _retentionPeriod.TotalDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Location history retention cleanup failed; it will retry later.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
