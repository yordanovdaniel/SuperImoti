using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;

namespace SuperImoti.Web.ViewModels;

/// <summary>What a listing card in a grid needs (shown by the _ListingCard partial).</summary>
public class ListingCardViewModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DealKind DealKind { get; set; }

    public ListingStatus Status { get; set; }

    public string PropertyType { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public Currency Currency { get; set; }

    public decimal? PricePerSqm { get; set; }

    /// <summary>The price before the latest price change, if any.</summary>
    public decimal? PreviousPrice { get; set; }

    public decimal Area { get; set; }

    public int? Floor { get; set; }

    public int? TotalFloors { get; set; }

    public bool IsImported { get; set; }

    public string? SellerName { get; set; }

    public int PhotoCount { get; set; }

    public string? CoverHash { get; set; }

    public string? CoverFile { get; set; }

    public string CoverUrl { get; set; } = PhotoUrlResolver.Placeholder;

    public DateTime CreatedUtc { get; set; }

    public bool IsFavorite { get; set; }

    public bool HasPriceDrop => PreviousPrice is decimal before && Price is decimal now && now < before;
}

public class ListingIndexViewModel
{
    public required ListingFilter Filter { get; init; }

    public required PagedResult<ListingCardViewModel> Results { get; init; }

    public required LookupOptions Options { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record PhotoViewModel(int Id, string Url, bool IsUploaded);

public class ListingDetailsViewModel
{
    public required Listing Listing { get; init; }

    public required IReadOnlyList<PhotoViewModel> Photos { get; init; }

    public IReadOnlyList<ListingCardViewModel> Similar { get; init; } = [];

    public IReadOnlyList<ListingCardViewModel> Duplicates { get; init; } = [];

    public bool IsFavorite { get; init; }

    public int FavoriteCount { get; init; }

    public bool CanEdit { get; init; }

    public bool IsAdmin { get; init; }

    public string? CurrentUserId { get; init; }

    public CommentFormModel NewComment { get; init; } = new();

    public decimal? PreviousPrice =>
        Listing.Events
            .Where(e => e.Kind == ListingEventKind.PriceChange && e.OldPrice != null)
            .OrderByDescending(e => e.OccurredUtc)
            .Select(e => e.OldPrice)
            .FirstOrDefault();
}

public class ListingEditorViewModel
{
    public static readonly string[] ConstructionTypes = ["Тухла", "Панел", "ЕПК", "ПК", "Гредоред", "Сглобяема конструкция"];

    public static readonly string[] VatNotes = ["Не се начислява ДДС", "Цената е с включено ДДС", "Цената е без ДДС"];

    public required ListingFormModel Input { get; init; }

    public required LookupOptions Options { get; init; }

    public IReadOnlyList<PhotoViewModel> ExistingPhotos { get; init; } = [];

    public bool IsImported { get; init; }

    public bool IsEdit => Input.Id is not null;
}

/// <summary>Select-list data shared by the filters and the listing form.</summary>
public sealed class LookupOptions
{
    public IReadOnlyList<PropertyTypeItem> PropertyTypes { get; init; } = [];

    public IReadOnlyList<LookupItem> Districts { get; init; } = [];

    public IReadOnlyList<LookupItem> Features { get; init; } = [];

    public IEnumerable<IGrouping<PropertyCategory, PropertyTypeItem>> PropertyTypesByCategory =>
        PropertyTypes.GroupBy(t => t.Category).OrderBy(g => g.Key);
}

public sealed record LookupItem(int Id, string Name);

public sealed record PropertyTypeItem(int Id, string Name, PropertyCategory Category);

/// <summary>Model of the _Pagination partial.</summary>
public sealed class PaginationViewModel
{
    public required int Page { get; init; }

    public required int TotalPages { get; init; }

    public required Func<int, string> PageUrl { get; init; }

    /// <summary>Page numbers to show around the current page; null stands for "…".</summary>
    public IEnumerable<int?> Window()
    {
        const int radius = 2;
        var pages = new SortedSet<int> { 1, TotalPages };
        for (var p = Page - radius; p <= Page + radius; p++)
        {
            if (p >= 1 && p <= TotalPages)
            {
                pages.Add(p);
            }
        }

        int? previous = null;
        foreach (var page in pages)
        {
            if (previous is int prev && page - prev > 1)
            {
                yield return null;
            }

            yield return page;
            previous = page;
        }
    }
}
