using BMSL_Tracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Infrastructure;

/// <summary>
/// Brings the database up to date at startup:
///  - SQLite  : <c>EnsureCreated</c> (development, tests, single-file deployments; no migration history).
///  - SQL Server: applies pending EF Core migrations when <c>Database:ApplyMigrations=true</c>,
///    using the configured execution strategy so it survives transient SQL failures during rollout.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("BMSL_Tracker.DbInitializer");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var provider = configuration["Database:Provider"] ?? "SqlServer";
        var applyMigrations = configuration.GetValue("Database:ApplyMigrations", false);

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (applyMigrations)
            {
                var created = await db.Database.EnsureCreatedAsync(cancellationToken);
                logger.LogInformation(
                    created
                        ? "SQLite database created at {Path}."
                        : "SQLite database verified (existing schema left untouched).",
                    configuration.GetConnectionString("DefaultConnection"));
            }
        }
        else if (applyMigrations)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("EF Core migrations applied ({Provider}).", provider);
            });
        }
        else
        {
            logger.LogInformation(
                "Database:ApplyMigrations is disabled; ensure pending EF Core migrations are applied out of band.");
        }
    }
}
