using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>A real-estate agency that publishes listings on imot.bg. One seller has many listings.</summary>
public class Seller
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-cased name used for case-insensitive search (SQLite cannot upper-case Cyrillic).</summary>
    [Required]
    [StringLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>The agency's page on imot.bg, e.g. "citadela63.imot.bg". Unique.</summary>
    [StringLength(80)]
    public string? Subdomain { get; set; }

    [StringLength(200)]
    public string? Phones { get; set; }

    [StringLength(30)]
    public string? MemberSince { get; set; }

    [StringLength(250)]
    public string? OfficeAddress { get; set; }

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
