using SuperImoti.Web.Data.Import;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.ViewModels;

public class HomeIndexViewModel
{
    public int ActiveCount { get; init; }

    public int SaleCount { get; init; }

    public int RentCount { get; init; }

    public int SellerCount { get; init; }

    public int DistrictCount { get; init; }

    public int PhotoCount { get; init; }

    public decimal? AvgApartmentSalePricePerSqm { get; init; }

    public IReadOnlyList<ListingCardViewModel> Latest { get; init; } = [];

    public IReadOnlyList<ListingCardViewModel> PriceDrops { get; init; } = [];

    public IReadOnlyList<DistrictLinkViewModel> TopDistricts { get; init; } = [];

    public LookupOptions Options { get; init; } = new();
}

public sealed record DistrictLinkViewModel(int Id, string Name, int Count);

public class AboutViewModel
{
    public int ListingCount { get; init; }

    public int ImportedCount { get; init; }

    public int UserListingCount { get; init; }

    public int PhotoCount { get; init; }

    public int SellerCount { get; init; }

    public int DistrictCount { get; init; }

    public int UserCount { get; init; }

    public int CommentCount { get; init; }
}

public class SellerListItemViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Subdomain { get; init; }

    public string? MemberSince { get; init; }

    public string? OfficeAddress { get; init; }

    public int ActiveListings { get; init; }
}

public class SellerIndexViewModel
{
    public string? Q { get; init; }

    public string Sort { get; init; } = "listings";

    public required PagedResult<SellerListItemViewModel> Results { get; init; }
}

public class SellerDetailsViewModel
{
    public required Seller Seller { get; init; }

    public required PagedResult<ListingCardViewModel> Listings { get; init; }

    public int SaleCount { get; init; }

    public int RentCount { get; init; }

    public IReadOnlyList<DistrictLinkViewModel> TopDistricts { get; init; } = [];
}

public class DistrictRowViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public int SaleCount { get; init; }

    public int RentCount { get; init; }

    public decimal? AvgApartmentSalePricePerSqm { get; init; }

    public decimal? AvgApartmentRent { get; init; }

    public int TotalCount => SaleCount + RentCount;
}

public class DistrictDetailsViewModel
{
    public required DistrictRowViewModel Stats { get; init; }

    public required PagedResult<ListingCardViewModel> Listings { get; init; }

    public DealKind? Deal { get; init; }

    public IReadOnlyList<(string Type, int Count)> TypeBreakdown { get; init; } = [];
}

public class DistrictDeleteViewModel
{
    public required District District { get; init; }

    public int ListingCount { get; init; }
}

public class AdminIndexViewModel
{
    public required ImportSnapshot Import { get; init; }

    public string? SourceFolder { get; init; }

    public bool SourceDatabaseExists { get; init; }

    public bool SourceImagesExist { get; init; }

    public int ImportedListings { get; init; }

    public int UserListings { get; init; }

    public int Photos { get; init; }

    public int Users { get; init; }

    public int Comments { get; init; }

    public int Favorites { get; init; }

    public int QueuedEmailFiles { get; init; }

    public IReadOnlyList<AdminUserRow> LatestUsers { get; init; } = [];
}

public sealed record AdminUserRow(string Email, string DisplayName, DateTime CreatedUtc, int ListingCount, bool Notify);

public class ErrorViewModel
{
    public string? RequestId { get; init; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

public class StatusCodeViewModel
{
    public int Code { get; init; }

    public string Title => Code switch
    {
        400 => "Невалидна заявка",
        403 => "Нямате достъп",
        404 => "Страницата не е намерена",
        413 => "Файлът е твърде голям",
        _ => "Нещо се обърка",
    };

    public string Message => Code switch
    {
        400 => "Заявката съдържа невалидни данни. Проверете адреса или формата и опитайте отново.",
        403 => "Нямате права да видите или промените тази страница.",
        404 => "Обявата или страницата, която търсите, не съществува или е била изтрита.",
        413 => "Качените файлове са по-големи от позволеното. Опитайте с по-малки снимки.",
        _ => "Възникна неочакван проблем. Опитайте отново след малко.",
    };
}
