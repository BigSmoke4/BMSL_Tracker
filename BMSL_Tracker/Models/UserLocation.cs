using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace BMSL_Tracker.Models
{
    public class UserLocation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public virtual IdentityUser? User { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
