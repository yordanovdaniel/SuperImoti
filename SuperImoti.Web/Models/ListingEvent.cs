using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>One entry in a listing's history: discovered, price changed, went inactive, edited…</summary>
public class ListingEvent
{
    public int Id { get; set; }

    public int ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

    public ListingEventKind Kind { get; set; }

    public decimal? Price { get; set; }

    /// <summary>The price before a price change.</summary>
    public decimal? OldPrice { get; set; }

    public Currency? Currency { get; set; }

    public ListingStatus? Status { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    /// <summary>Id of the row in the scraper's listing_history table, so a re-import never duplicates events.</summary>
    public int? SourceId { get; set; }
}
