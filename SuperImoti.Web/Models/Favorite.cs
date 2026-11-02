namespace SuperImoti.Web.Models;

/// <summary>Join entity of the many-to-many relation "user saved a listing to favourites".</summary>
public class Favorite
{
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public int ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
