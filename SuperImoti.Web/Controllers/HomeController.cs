using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

public class HomeController(
    ApplicationDbContext db,
    ListingQueryService listingQueries,
    LookupService lookups) : Controller
{
    // GET: /
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var active = db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.Active);

        var topDistricts = await db.Districts.AsNoTracking()
            .Select(d => new { d.Id, d.Name, Count = d.Listings.Count(l => l.Status == ListingStatus.Active) })
            .Where(d => d.Count > 0)
            .OrderByDescending(d => d.Count)
            .Take(14)
            .ToListAsync(ct);

        var model = new HomeIndexViewModel
        {
            ActiveCount = await active.CountAsync(ct),
            SaleCount = await active.CountAsync(l => l.DealKind == DealKind.Sale, ct),
            RentCount = await active.CountAsync(l => l.DealKind == DealKind.Rent, ct),
            SellerCount = await db.Sellers.CountAsync(ct),
            DistrictCount = await db.Districts.CountAsync(d => d.Listings.Any(), ct),
            PhotoCount = await db.ListingPhotos.CountAsync(ct),
            AvgApartmentSalePricePerSqm = await active
                .Where(l => l.DealKind == DealKind.Sale
                    && l.PropertyType.Category == PropertyCategory.Apartment
                    && l.PricePerSqm != null)
                .AverageAsync(l => l.PricePerSqm, ct),
            Latest = await listingQueries.LatestWithPhotosAsync(8, userId, ct),
            PriceDrops = await listingQueries.RecentPriceDropsAsync(4, userId, ct),
            TopDistricts = topDistricts.Select(d => new DistrictLinkViewModel(d.Id, d.Name, d.Count)).ToList(),
            Options = await lookups.GetAsync(ct),
        };

        return View(model);
    }

    // GET: /Home/About
    public async Task<IActionResult> About(CancellationToken ct)
    {
        var model = new AboutViewModel
        {
            ListingCount = await db.Listings.CountAsync(ct),
            ImportedCount = await db.Listings.CountAsync(l => l.ExternalId != null, ct),
            UserListingCount = await db.Listings.CountAsync(l => l.ExternalId == null, ct),
            PhotoCount = await db.ListingPhotos.CountAsync(ct),
            SellerCount = await db.Sellers.CountAsync(ct),
            DistrictCount = await db.Districts.CountAsync(ct),
            UserCount = await db.Users.CountAsync(ct),
            CommentCount = await db.Comments.CountAsync(ct),
        };

        return View(model);
    }

    // Shown by UseExceptionHandler when an unhandled exception happens (outside Development).
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // Shown by UseStatusCodePagesWithReExecute for 404, 403, 400…
    [IgnoreAntiforgeryToken]
    [Route("Home/StatusCode/{code:int}")]
    public IActionResult StatusCodePage(int code) => View("StatusCode", new StatusCodeViewModel { Code = code });
}
