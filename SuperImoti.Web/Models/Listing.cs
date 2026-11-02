using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SuperImoti.Web.Models;

/// <summary>
/// A property listing. Imported listings come from the imot.bg scraper and have an <see cref="ExternalId"/>;
/// listings published on this site have an <see cref="Owner"/> instead.
/// </summary>
public class Listing
{
    public int Id { get; set; }

    /// <summary>The imot.bg id, e.g. "1b178549737475110". Null for listings published on this site.</summary>
    [StringLength(32)]
    public string? ExternalId { get; set; }

    [Required]
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;

    public DealKind DealKind { get; set; }

    public ListingStatus Status { get; set; } = ListingStatus.Active;

    public int PropertyTypeId { get; set; }

    public PropertyType PropertyType { get; set; } = null!;

    public int DistrictId { get; set; }

    public District District { get; set; } = null!;

    [StringLength(120)]
    public string? Street { get; set; }

    /// <summary>Null when the price is given only on request.</summary>
    public decimal? Price { get; set; }

    public Currency Currency { get; set; } = Currency.EUR;

    public bool PriceOnRequest { get; set; }

    public decimal? PricePerSqm { get; set; }

    [StringLength(60)]
    public string? VatNote { get; set; }

    /// <summary>Area in square metres.</summary>
    public decimal Area { get; set; }

    /// <summary>0 is the ground floor, negative numbers are basements.</summary>
    public int? Floor { get; set; }

    public int? TotalFloors { get; set; }

    [StringLength(60)]
    public string? ConstructionType { get; set; }

    /// <summary>Year or period of construction as shown on imot.bg, e.g. "2028 г." or "1980 - 1989 г.".</summary>
    [StringLength(40)]
    public string? CompletionPeriod { get; set; }

    [StringLength(6000)]
    public string? Description { get; set; }

    /// <summary>Phone for private sellers and for listings published on this site.</summary>
    [StringLength(120)]
    public string? ContactPhone { get; set; }

    /// <summary>The agency that published the listing on imot.bg (null for private sellers).</summary>
    public int? SellerId { get; set; }

    public Seller? Seller { get; set; }

    /// <summary>The user who published the listing on this site.</summary>
    public string? OwnerId { get; set; }

    public ApplicationUser? Owner { get; set; }

    [StringLength(300)]
    public string? SourceUrl { get; set; }

    /// <summary>Hash of the scraped content; the importer skips listings whose hash has not changed.</summary>
    [StringLength(64)]
    public string? SourceContentHash { get; set; }

    public int? SourceViewCount { get; set; }

    public DateTime? SourceUpdatedUtc { get; set; }

    /// <summary>Listings the scraper thinks are re-posts of the same property share a group id.</summary>
    public int? DuplicateGroupId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? InactiveSinceUtc { get; set; }

    /// <summary>
    /// Lower-cased text used by the search box. SQLite cannot lower-case Cyrillic letters,
    /// so the text is lower-cased in .NET when the listing is saved.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>Changes on every save so two people editing the same listing cannot overwrite each other.</summary>
    [ConcurrencyCheck]
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<ListingPhoto> Photos { get; set; } = new List<ListingPhoto>();

    public ICollection<ListingFeature> Features { get; set; } = new List<ListingFeature>();

    public ICollection<ListingEvent> Events { get; set; } = new List<ListingEvent>();

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public ICollection<Favorite> FavoritedBy { get; set; } = new List<Favorite>();

    [NotMapped]
    public bool IsImported => ExternalId != null;
}
