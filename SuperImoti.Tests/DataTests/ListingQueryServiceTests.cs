using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperImoti.Web.Data;
using SuperImoti.Web.Data.Import;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Tests.DataTests;

/// <summary>
/// Runs the real queries against an in-memory SQLite database created by the real migrations
/// (including the full-text index and its triggers).
/// </summary>
public sealed class ListingQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly ListingQueryService _service;
    private readonly int _elevatorId;

    public ListingQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _service = new ListingQueryService(_db, new PhotoUrlResolver(Options.Create(new ScraperDataOptions())));

        var nadezhda = new District { Name = "Надежда 3" };
        var lozenets = new District { Name = "Лозенец" };
        var twoRoom = new PropertyType { Name = "2-стаен", Category = PropertyCategory.Apartment, SortOrder = 2 };
        var garage = new PropertyType { Name = "Гараж", Category = PropertyCategory.Parking, SortOrder = 43 };
        var elevator = new Feature { Name = "Асансьор" };

        var withPhoto = NewListing("A", DealKind.Sale, twoRoom, nadezhda, price: 100000, area: 60);
        withPhoto.Features.Add(new ListingFeature { Feature = elevator });
        withPhoto.Photos.Add(new ListingPhoto { FileName = "a.jpg", Position = 0 });

        _db.Listings.AddRange(
            withPhoto,
            NewListing("B", DealKind.Rent, garage, lozenets, price: 200, area: 20),
            NewListing("C", DealKind.Sale, twoRoom, lozenets, price: null, area: 70),
            NewListing("D", DealKind.Sale, twoRoom, lozenets, price: 250000, area: 90, status: ListingStatus.Inactive));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _elevatorId = elevator.Id;
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveForCyrillic()
    {
        Assert.Equal(["A"], await TitlesAsync(new ListingFilter { Q = "надежда" }));
        Assert.Equal(["A"], await TitlesAsync(new ListingFilter { Q = "НАДЕЖДА" }));
    }

    [Fact]
    public async Task Search_MatchesTheBeginningOfWordsAndNeedsEveryWord()
    {
        Assert.Equal(["B", "C"], (await TitlesAsync(new ListingFilter { Q = "лозен" })).Order().ToList());
        Assert.Equal(["C"], await TitlesAsync(new ListingFilter { Q = "лозенец 2-стаен" }));
        Assert.Empty(await TitlesAsync(new ListingFilter { Q = "бояна" }));
    }

    [Fact]
    public async Task Search_FollowsEditsThroughTheDatabaseTriggers()
    {
        var listing = await _db.Listings.SingleAsync(l => l.Title.EndsWith("B"));
        listing.SearchText = "гараж под наем в бояна";
        await _db.SaveChangesAsync();

        Assert.Equal(["B"], await TitlesAsync(new ListingFilter { Q = "бояна" }));
        Assert.Empty(await TitlesAsync(new ListingFilter { Q = "лозенец гараж" }));
    }

    [Fact]
    public async Task InactiveListings_AreHiddenUnlessAsked()
    {
        Assert.DoesNotContain("D", await TitlesAsync(new ListingFilter()));
        Assert.Contains("D", await TitlesAsync(new ListingFilter { Inactive = true }));
    }

    [Fact]
    public async Task Filters_CanBeCombined()
    {
        Assert.Equal(["B"], await TitlesAsync(new ListingFilter { Deal = DealKind.Rent }));
        Assert.Equal(["A"], await TitlesAsync(new ListingFilter { MinPrice = 50000 }));
        Assert.Equal(["A"], await TitlesAsync(new ListingFilter { Features = [_elevatorId] }));
        Assert.Equal(["A"], await TitlesAsync(new ListingFilter { Photos = true }));
        Assert.Equal(["B"], await TitlesAsync(new ListingFilter { Category = PropertyCategory.Parking }));
        Assert.Equal(["C"], await TitlesAsync(new ListingFilter { MinArea = 65, MaxArea = 80 }));
    }

    [Fact]
    public async Task PriceSort_PutsPriceOnRequestLast()
    {
        Assert.Equal(["B", "A", "C"], await TitlesAsync(new ListingFilter { Sort = ListingSort.PriceAsc }));
        Assert.Equal(["A", "B", "C"], await TitlesAsync(new ListingFilter { Sort = ListingSort.PriceDesc }));
    }

    [Fact]
    public async Task RecommendedSort_ShowsListingsWithPhotosFirst() =>
        Assert.Equal("A", (await TitlesAsync(new ListingFilter()))[0]);

    [Fact]
    public async Task Paging_ClampsPageNumberAndReportsTotals()
    {
        var result = await _service.SearchAsync(new ListingFilter { Page = 50, Size = 12 }, userId: null, CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task Cards_ResolvePhotoUrls()
    {
        var result = await _service.SearchAsync(new ListingFilter { Photos = true }, userId: null, CancellationToken.None);

        var card = Assert.Single(result.Items);
        Assert.Equal("/uploads/a.jpg", card.CoverUrl);
        Assert.Equal(1, card.PhotoCount);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<List<string>> TitlesAsync(ListingFilter filter)
    {
        var result = await _service.SearchAsync(filter, userId: null, CancellationToken.None);
        return result.Items.Select(c => c.Title[^1..]).ToList();
    }

    private static Listing NewListing(
        string code, DealKind deal, PropertyType type, District district, decimal? price, decimal area,
        ListingStatus status = ListingStatus.Active)
    {
        var title = $"{type.Name} {district.Name} {code}";
        return new Listing
        {
            Title = title,
            DealKind = deal,
            Status = status,
            PropertyType = type,
            District = district,
            Price = price,
            PriceOnRequest = price is null,
            PricePerSqm = price / area,
            Area = area,
            Description = "Описание",
            SearchText = SearchTextBuilder.Build(title, district.Name, type.Name),
            CreatedUtc = DateTime.UtcNow.AddMinutes(code[0]),
        };
    }
}
