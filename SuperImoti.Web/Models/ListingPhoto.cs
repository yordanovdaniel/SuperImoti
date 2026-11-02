using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SuperImoti.Web.Models;

/// <summary>
/// A photo of a listing. Imported photos point into the scraper's image folder by <see cref="ContentHash"/>
/// (images/ab/cd/abcd….jpg); photos uploaded on this site are stored under App_Data/uploads by <see cref="FileName"/>.
/// </summary>
public class ListingPhoto
{
    public int Id { get; set; }

    public int ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public int Position { get; set; }

    [StringLength(64)]
    public string? ContentHash { get; set; }

    [StringLength(100)]
    public string? FileName { get; set; }

    [StringLength(300)]
    public string? SourceUrl { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public bool IsUploaded => FileName != null;
}
