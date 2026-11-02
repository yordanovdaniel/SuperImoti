using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>A neighbourhood ("квартал"). One district has many listings.</summary>
public class District
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(60)]
    public string City { get; set; } = "София";

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
