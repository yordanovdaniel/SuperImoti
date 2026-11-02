using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>A comment left by a user under a listing.</summary>
public class Comment
{
    public int Id { get; set; }

    public int ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    [Required]
    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser Author { get; set; } = null!;

    [Required]
    [StringLength(1000, MinimumLength = 2)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
