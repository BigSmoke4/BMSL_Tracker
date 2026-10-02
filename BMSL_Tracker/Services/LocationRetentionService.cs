using BMSL_Tracker.Data;
using BMSL_Tracker.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BMSL_Tracker.Services;

/// <summary>
/// Background retention worker: trims <c>UserLocations</c> rows older than
/// <c>Tracker:RetentionDays</c> in bounded batches (0 disables pruning).
/// Keeps the history table — and the map's initial-load queries — fast forever.
/// </summary>
public sealed class LocationRetentionService : BackgroundService
{
    private const int BatchSize = 2_000;
    private const int MaxBatchesPerRun = 50;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TrackerOptions _options;
    private readonly ILogger<LocationRetentionService> _logger;

    public LocationRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<TrackerOptions> options,
        ILogger<LocationRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            _logger.LogInformation("Location retention is disabled (Tracker:RetentionDays = 0).");
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await PruneOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Retention: removed {Count} location rows older than {Days} days.",
                        deleted, _options.RetentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention pass failed; will retry at the next interval.");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<int> PruneOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var totalDeleted = 0;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var staleRows = await db.UserLocations
                .AsNoTracking()
                .Where(l => l.Timestamp < cutoff)
                .OrderBy(l => l.Timestamp)
                .Take(BatchSize)
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            if (staleRows.Count == 0)
            {
                break;
            }

            await db.UserLocations
                .Where(l => staleRows.Contains(l.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += staleRows.Count;

            if (staleRows.Count < BatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }
}
