using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Services;

/// <summary>Listing queries shared by the listings page, CSV export, home page, sellers, districts and favourites.</summary>
public class ListingQueryService(ApplicationDbContext db, PhotoUrlResolver photoUrls)
{
    /// <summary>Applies the search box and all filters.</summary>
    public IQueryable<Listing> Filter(ListingFilter filter)
    {
        var query = db.Listings.AsNoTracking();

        if (!filter.Inactive)
        {
            query = query.Where(l => l.Status == ListingStatus.Active);
        }

        if (filter.Deal is DealKind deal)
        {
            query = query.Where(l => l.DealKind == deal);
        }

        if (filter.Category is PropertyCategory category)
        {
            query = query.Where(l => l.PropertyType.Category == category);
        }

        if (filter.Type is int typeId)
        {
            query = query.Where(l => l.PropertyTypeId == typeId);
        }

        if (filter.District is int districtId)
        {
            query = query.Where(l => l.DistrictId == districtId);
        }

        if (filter.Seller is int sellerId)
        {
            query = query.Where(l => l.SellerId == sellerId);
        }

        if (filter.MinPrice is decimal minPrice)
        {
            query = query.Where(l => l.Price >= minPrice);
        }

        if (filter.MaxPrice is decimal maxPrice)
        {
            query = query.Where(l => l.Price <= maxPrice);
        }

        if (filter.MinArea is decimal minArea)
        {
            query = query.Where(l => l.Area >= minArea);
        }

        if (filter.MaxArea is decimal maxArea)
        {
            query = query.Where(l => l.Area <= maxArea);
        }

        if (filter.Photos)
        {
            query = query.Where(l => l.Photos.Any());
        }

        foreach (var featureId in filter.Features.Distinct().Take(10))
        {
            query = query.Where(l => l.Features.Any(f => f.FeatureId == featureId));
        }

        // Every word must appear in the title, address, district, seller or description. The search uses the
        // SQLite full-text index "ListingSearch", which database triggers keep in sync with Listings.SearchText.
        var ftsQuery = SearchTextBuilder.ToFtsQuery(filter.Q);
        if (ftsQuery is not null)
        {
            var matchingIds = db.Database.SqlQuery<int>(
                $"SELECT rowid AS \"Value\" FROM \"ListingSearch\" WHERE \"ListingSearch\" MATCH {ftsQuery}");
            query = query.Where(l => matchingIds.Contains(l.Id));
        }

        return query;
    }

    public static IQueryable<Listing> Sort(IQueryable<Listing> query, string? sort) => ListingSort.Normalize(sort) switch
    {
        ListingSort.Newest => query.OrderByDescending(l => l.CreatedUtc).ThenByDescending(l => l.Id),
        ListingSort.PriceAsc => query.OrderBy(l => l.Price == null).ThenBy(l => l.Price).ThenBy(l => l.Id),
        ListingSort.PriceDesc => query.OrderBy(l => l.Price == null).ThenByDescending(l => l.Price).ThenBy(l => l.Id),
        ListingSort.PricePerSqmAsc => query.OrderBy(l => l.PricePerSqm == null).ThenBy(l => l.PricePerSqm).ThenBy(l => l.Id),
        ListingSort.PricePerSqmDesc => query.OrderBy(l => l.PricePerSqm == null).ThenByDescending(l => l.PricePerSqm).ThenBy(l => l.Id),
        ListingSort.AreaAsc => query.OrderBy(l => l.Area).ThenBy(l => l.Id),
        ListingSort.AreaDesc => query.OrderByDescending(l => l.Area).ThenBy(l => l.Id),
        ListingSort.MostViewed => query.OrderByDescending(l => l.SourceViewCount ?? 0).ThenBy(l => l.Id),

        // "Recommended": listings with photos first, newest first.
        _ => query.OrderByDescending(l => l.Photos.Any()).ThenByDescending(l => l.CreatedUtc).ThenByDescending(l => l.Id),
    };

    public Task<PagedResult<ListingCardViewModel>> SearchAsync(ListingFilter filter, string? userId, CancellationToken ct) =>
        PageAsync(Sort(Filter(filter), filter.Sort), filter.Page, filter.PageSize, userId, ct);

    /// <summary>Counts the results, keeps the page number in range and loads one page of cards.</summary>
    public async Task<PagedResult<ListingCardViewModel>> PageAsync(
        IQueryable<Listing> sortedQuery, int page, int pageSize, string? userId, CancellationToken ct)
    {
        var total = await sortedQuery.CountAsync(ct);
        page = PagedResult<ListingCardViewModel>.ClampPage(page, total, pageSize);

        // Sort and skip using only the ids (small rows), then load the full cards for this one page.
        // Much faster on deep pages than sorting whole listings with their long descriptions.
        var ids = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => l.Id)
            .ToListAsync(ct);

        var items = await ByIdsAsync(ids, userId, ct);
        return new PagedResult<ListingCardViewModel>(items, page, pageSize, total);
    }

    public Task<IReadOnlyList<ListingCardViewModel>> LatestWithPhotosAsync(int count, string? userId, CancellationToken ct) =>
        LoadCardsAsync(
            db.Listings.AsNoTracking()
                .Where(l => l.Status == ListingStatus.Active && l.Photos.Any())
                .OrderByDescending(l => l.CreatedUtc).ThenByDescending(l => l.Id)
                .Take(count),
            userId,
            ct);

    public async Task<IReadOnlyList<ListingCardViewModel>> RecentPriceDropsAsync(int count, string? userId, CancellationToken ct)
    {
        var ids = await db.ListingEvents.AsNoTracking()
            .Where(e => e.Kind == ListingEventKind.PriceChange
                && e.OldPrice != null
                && e.Price < e.OldPrice
                && e.Listing.Status == ListingStatus.Active
                && e.Listing.Photos.Any())
            .OrderByDescending(e => e.OccurredUtc)
            .Select(e => e.ListingId)
            .Take(count * 5)
            .ToListAsync(ct);

        return await ByIdsAsync(ids.Distinct().Take(count).ToList(), userId, ct);
    }

    /// <summary>Same district, type and deal, with the closest price.</summary>
    public async Task<IReadOnlyList<ListingCardViewModel>> SimilarAsync(
        Listing listing, int count, string? userId, CancellationToken ct)
    {
        var query = db.Listings.AsNoTracking().Where(l =>
            l.Id != listing.Id
            && l.Status == ListingStatus.Active
            && l.DealKind == listing.DealKind
            && l.PropertyTypeId == listing.PropertyTypeId
            && l.DistrictId == listing.DistrictId);

        if (listing.DuplicateGroupId is int group)
        {
            query = query.Where(l => l.DuplicateGroupId == null || l.DuplicateGroupId != group);
        }

        if (listing.Price is not decimal price)
        {
            return await LoadCardsAsync(query.OrderByDescending(l => l.CreatedUtc).Take(count), userId, ct);
        }

        var min = price * 0.7m;
        var max = price * 1.3m;
        var candidates = await query
            .Where(l => l.Price >= min && l.Price <= max)
            .OrderByDescending(l => l.CreatedUtc)
            .Select(l => new { l.Id, l.Price })
            .Take(60)
            .ToListAsync(ct);

        var ids = candidates
            .OrderBy(c => Math.Abs(c.Price!.Value - price))
            .Take(count)
            .Select(c => c.Id)
            .ToList();

        return await ByIdsAsync(ids, userId, ct);
    }

    /// <summary>Other ads the scraper believes are the same property (re-posts or several agencies).</summary>
    public async Task<IReadOnlyList<ListingCardViewModel>> DuplicatesAsync(Listing listing, string? userId, CancellationToken ct)
    {
        if (listing.DuplicateGroupId is not int group)
        {
            return [];
        }

        return await LoadCardsAsync(
            db.Listings.AsNoTracking()
                .Where(l => l.DuplicateGroupId == group && l.Id != listing.Id)
                .OrderBy(l => l.Price == null).ThenBy(l => l.Price)
                .Take(12),
            userId,
            ct);
    }

    public async Task<IReadOnlyList<ListingCardViewModel>> ByIdsAsync(List<int> ids, string? userId, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var cards = await LoadCardsAsync(db.Listings.AsNoTracking().Where(l => ids.Contains(l.Id)), userId, ct);
        return cards.OrderBy(c => ids.IndexOf(c.Id)).ToList();
    }

    private async Task<IReadOnlyList<ListingCardViewModel>> LoadCardsAsync(
        IQueryable<Listing> query, string? userId, CancellationToken ct)
    {
        var cards = await ToCards(query, userId).ToListAsync(ct);
        foreach (var card in cards)
        {
            card.CoverUrl = photoUrls.Resolve(card.CoverHash, card.CoverFile);
        }

        return cards;
    }

    private static IQueryable<ListingCardViewModel> ToCards(IQueryable<Listing> query, string? userId) =>
        query.Select(l => new ListingCardViewModel
        {
            Id = l.Id,
            Title = l.Title,
            DealKind = l.DealKind,
            Status = l.Status,
            PropertyType = l.PropertyType.Name,
            District = l.District.Name,
            Price = l.Price,
            Currency = l.Currency,
            PricePerSqm = l.PricePerSqm,
            PreviousPrice = l.Events
                .Where(e => e.Kind == ListingEventKind.PriceChange)
                .OrderByDescending(e => e.OccurredUtc)
                .Select(e => e.OldPrice)
                .FirstOrDefault(),
            Area = l.Area,
            Floor = l.Floor,
            TotalFloors = l.TotalFloors,
            IsImported = l.ExternalId != null,
            SellerName = l.Seller != null ? l.Seller.Name : l.Owner != null ? l.Owner.DisplayName : null,
            PhotoCount = l.Photos.Count(),
            CoverHash = l.Photos.OrderBy(p => p.Position).Select(p => p.ContentHash).FirstOrDefault(),
            CoverFile = l.Photos.OrderBy(p => p.Position).Select(p => p.FileName).FirstOrDefault(),
            CreatedUtc = l.CreatedUtc,
            IsFavorite = userId != null && l.FavoritedBy.Any(f => f.UserId == userId),
        });
}
