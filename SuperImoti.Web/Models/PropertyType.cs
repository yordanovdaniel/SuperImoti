using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

/// <summary>Kind of property, e.g. "2-стаен", "Къща", "Офис".</summary>
public class PropertyType
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    public PropertyCategory Category { get; set; }

    public int SortOrder { get; set; }

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
