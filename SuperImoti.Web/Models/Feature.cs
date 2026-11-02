using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>An amenity such as "Асансьор" or "Обзаведен". Many-to-many with listings.</summary>
public class Feature
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    public ICollection<ListingFeature> Listings { get; set; } = new List<ListingFeature>();
}

/// <summary>Join entity of the many-to-many relation between listings and features.</summary>
public class ListingFeature
{
    public int ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public int FeatureId { get; set; }

    public Feature Feature { get; set; } = null!;
}
