using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace SuperImoti.Web.Models;

/// <summary>A registered user of the site (ASP.NET Core Identity).</summary>
public class ApplicationUser : IdentityUser
{
    [Required]
    [StringLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>When true the user gets an e-mail every time someone publishes a new listing.</summary>
    public bool NotifyAboutNewListings { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();

    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
