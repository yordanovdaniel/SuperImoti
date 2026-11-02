using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.ViewModels;

/// <summary>Search, filter, sort and paging options of the listings page, bound from the query string.</summary>
public class ListingFilter : IValidatableObject
{
    public const int DefaultPageSize = 24;

    public static readonly int[] PageSizes = [12, 24, 48];

    [StringLength(100, ErrorMessage = "Търсеният текст е твърде дълъг (до {1} символа).")]
    public string? Q { get; set; }

    public DealKind? Deal { get; set; }

    public PropertyCategory? Category { get; set; }

    public int? Type { get; set; }

    public int? District { get; set; }

    public int? Seller { get; set; }

    [Display(Name = "Цена от")]
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Цената трябва да е положително число.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? MinPrice { get; set; }

    [Display(Name = "Цена до")]
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Цената трябва да е положително число.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? MaxPrice { get; set; }

    [Display(Name = "Площ от")]
    [Range(typeof(decimal), "0", "10000000", ErrorMessage = "Площта трябва да е положително число.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? MinArea { get; set; }

    [Display(Name = "Площ до")]
    [Range(typeof(decimal), "0", "10000000", ErrorMessage = "Площта трябва да е положително число.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? MaxArea { get; set; }

    public List<int> Features { get; set; } = [];

    /// <summary>Only listings with photos.</summary>
    public bool Photos { get; set; }

    /// <summary>Also show listings that are no longer active.</summary>
    public bool Inactive { get; set; }

    public string? Sort { get; set; }

    public int Page { get; set; } = 1;

    public int Size { get; set; } = DefaultPageSize;

    public int PageSize => PageSizes.Contains(Size) ? Size : DefaultPageSize;

    public string SortKey => ListingSort.Normalize(Sort);

    /// <summary>How many filters are set (shown on the "Филтри" button on phones).</summary>
    public int ActiveFilterCount =>
        new object?[] { Q, Deal, Category, Type, District, Seller, MinPrice, MaxPrice, MinArea, MaxArea }
            .Count(v => v is not null && !(v is string s && string.IsNullOrWhiteSpace(s)))
        + Features.Count + (Photos ? 1 : 0) + (Inactive ? 1 : 0);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult("Минималната цена е по-голяма от максималната.", [nameof(MinPrice)]);
        }

        if (MinArea > MaxArea)
        {
            yield return new ValidationResult("Минималната площ е по-голяма от максималната.", [nameof(MinArea)]);
        }
    }

    /// <summary>Builds "?deal=Sale&amp;district=5&amp;page=2" keeping every current filter.</summary>
    public string ToQueryString(int? page = null, string? sort = null, int? size = null)
    {
        var pairs = new List<KeyValuePair<string, string?>>();

        void Add(string key, object? value)
        {
            var text = value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                pairs.Add(new KeyValuePair<string, string?>(key, text.Trim()));
            }
        }

        Add("q", Q);
        Add("deal", Deal?.ToString());
        Add("category", Category?.ToString());
        Add("type", Type);
        Add("district", District);
        Add("seller", Seller);
        Add("minPrice", MinPrice);
        Add("maxPrice", MaxPrice);
        Add("minArea", MinArea);
        Add("maxArea", MaxArea);
        foreach (var featureId in Features.Distinct())
        {
            Add("features", featureId);
        }

        if (Photos)
        {
            Add("photos", "true");
        }

        if (Inactive)
        {
            Add("inactive", "true");
        }

        var sortKey = ListingSort.Normalize(sort ?? Sort);
        if (sortKey != ListingSort.Recommended)
        {
            Add("sort", sortKey);
        }

        var pageSize = size ?? PageSize;
        if (pageSize != DefaultPageSize)
        {
            Add("size", pageSize);
        }

        var pageNumber = page ?? Page;
        if (pageNumber > 1)
        {
            Add("page", pageNumber);
        }

        return QueryString.Create(pairs).ToUriComponent();
    }
}

/// <summary>Sort options of the listings page.</summary>
public static class ListingSort
{
    public const string Recommended = "recommended";
    public const string Newest = "newest";
    public const string PriceAsc = "price-asc";
    public const string PriceDesc = "price-desc";
    public const string PricePerSqmAsc = "ppsm-asc";
    public const string PricePerSqmDesc = "ppsm-desc";
    public const string AreaAsc = "area-asc";
    public const string AreaDesc = "area-desc";
    public const string MostViewed = "views";

    public static readonly IReadOnlyList<(string Key, string Label)> Options =
    [
        (Recommended, "Препоръчани"),
        (Newest, "Най-нови"),
        (PriceAsc, "Цена ↑"),
        (PriceDesc, "Цена ↓"),
        (PricePerSqmAsc, "Цена на м² ↑"),
        (PricePerSqmDesc, "Цена на м² ↓"),
        (AreaAsc, "Площ ↑"),
        (AreaDesc, "Площ ↓"),
        (MostViewed, "Най-гледани в imot.bg"),
    ];

    public static string Normalize(string? sort) =>
        Options.Any(o => o.Key == sort) ? sort! : Recommended;
}
