using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Listings: search and filters, details and CSV export.</summary>
public class ListingsController(
    ApplicationDbContext db,
    ListingQueryService listingQueries,
    LookupService lookups,
    PhotoUrlResolver photoUrls) : Controller
{
    // GET: /Listings?deal=Sale&district=12&sort=price-asc&page=2
    public async Task<IActionResult> Index([FromQuery] ListingFilter filter, CancellationToken ct)
    {
        var model = new ListingIndexViewModel
        {
            Filter = filter,
            Results = await listingQueries.SearchAsync(filter, User.GetUserId(), ct),
            Options = await lookups.GetAsync(ct),
            Warnings = ModelState.IsValid
                ? []
                : ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct().ToList(),
        };

        return View(model);
    }

    // GET: /Listings/Export?… (same filters as Index) — downloads the results as CSV.
    public async Task<IActionResult> Export([FromQuery] ListingFilter filter, CancellationToken ct)
    {
        var rows = ListingQueryService.Sort(listingQueries.Filter(filter), filter.Sort)
            .Select(l => new ListingCsvRow(
                l.Id, l.ExternalId, l.Title, l.DealKind, l.PropertyType.Name, l.District.Name, l.Street,
                l.Price, l.Currency, l.PricePerSqm, l.Area, l.Floor, l.TotalFloors, l.ConstructionType,
                l.CompletionPeriod, l.Seller != null ? l.Seller.Name : null, l.Status, l.CreatedUtc, l.SourceUrl))
            .AsAsyncEnumerable();

        var detailsBase = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/Listings/Details/";
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"superimoti-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv\"";

        await ListingCsvExporter.WriteAsync(Response.Body, rows, id => detailsBase + id, ct);
        return new EmptyResult();
    }

    // GET: /Listings/Details/5
    public async Task<IActionResult> Details(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            return NotFound();
        }

        var listing = await db.Listings.AsNoTracking()
            .Include(l => l.District)
            .Include(l => l.PropertyType)
            .Include(l => l.Seller)
            .Include(l => l.Owner)
            .Include(l => l.Photos.OrderBy(p => p.Position))
            .Include(l => l.Features).ThenInclude(f => f.Feature)
            .Include(l => l.Events.OrderByDescending(e => e.OccurredUtc))
            .Include(l => l.Comments.OrderByDescending(c => c.CreatedUtc)).ThenInclude(c => c.Author)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
        {
            return NotFound();
        }

        var userId = User.GetUserId();
        var model = new ListingDetailsViewModel
        {
            Listing = listing,
            Photos = listing.Photos
                .Select(p => new PhotoViewModel(p.Id, photoUrls.Resolve(p.ContentHash, p.FileName), p.IsUploaded))
                .ToList(),
            Similar = await listingQueries.SimilarAsync(listing, 4, userId, ct),
            Duplicates = await listingQueries.DuplicatesAsync(listing, userId, ct),
            IsFavorite = userId != null
                && await db.Favorites.AnyAsync(f => f.ListingId == listing.Id && f.UserId == userId, ct),
            FavoriteCount = await db.Favorites.CountAsync(f => f.ListingId == listing.Id, ct),
            CanEdit = CanModify(listing),
            IsAdmin = User.IsAdmin(),
            CurrentUserId = userId,
            NewComment = new CommentFormModel { ListingId = listing.Id, Text = TempData["CommentDraft"] as string },
        };

        return View(model);
    }

    private bool CanModify(Listing listing) =>
        User.IsAdmin() || (listing.OwnerId is not null && listing.OwnerId == User.GetUserId());
}
