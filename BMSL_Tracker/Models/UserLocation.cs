using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace BMSL_Tracker.Models;

/// <summary>
/// One persisted geolocation sample for a user. Rows are written through the
/// write-amplification guard in <see cref="Services.ILocationService"/>, and pruned
/// according to <c>Tracker:RetentionDays</c>.
/// </summary>
public class UserLocation
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;


    [ForeignKey(nameof(UserId))]
    public virtual IdentityUser? User { get; set; }

    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    /// <summary>GPS accuracy radius in meters reported by the device.</summary>
    [Range(0, double.MaxValue)]
    public double Accuracy { get; set; }

    /// <summary>Always UTC (server receive time).</summary>
    public DateTime Timestamp { get; set; }
}
