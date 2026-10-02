using BMSL_Tracker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BMSL_Tracker.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserLocation> UserLocations => Set<UserLocation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserLocation>(entity =>
        {
            // Hot query: "latest location per user within a time window" and
            // "last saved sample for a user" — both served by this index.
            entity.HasIndex(l => new { l.UserId, l.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_UserLocations_UserId_Timestamp");

            entity.HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
