using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Real-estate agencies and their listings (one seller → many listings).</summary>
public class SellersController(ApplicationDbContext db, ListingQueryService listingQueries) : Controller
{
    private const int PageSize = 24;

    // GET: /Sellers?q=адрес&sort=name&page=2
    public async Task<IActionResult> Index(string? q, string? sort, int page = 1, CancellationToken ct = default)
    {
        var sellers = db.Sellers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Names are compared upper-cased because SQLite cannot change the case of Cyrillic letters.
            var name = q.Trim().ToUpperInvariant();
            var subdomain = q.Trim().ToLowerInvariant();
            sellers = sellers.Where(s =>
                s.NormalizedName.Contains(name) || (s.Subdomain != null && s.Subdomain.Contains(subdomain)));
        }

        var rows = sellers.Select(s => new SellerListItemViewModel
        {
            Id = s.Id,
            Name = s.Name,
            Subdomain = s.Subdomain,
            MemberSince = s.MemberSince,
            OfficeAddress = s.OfficeAddress,
            ActiveListings = s.Listings.Count(l => l.Status == ListingStatus.Active),
        });

        var sortKey = sort == "name" ? "name" : "listings";
        rows = sortKey == "name"
            ? rows.OrderBy(s => s.Name)
            : rows.OrderByDescending(s => s.ActiveListings).ThenBy(s => s.Name);

        var total = await rows.CountAsync(ct);
        page = PagedResult<SellerListItemViewModel>.ClampPage(page, total, PageSize);
        var items = await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);

        return View(new SellerIndexViewModel
        {
            Q = q,
            Sort = sortKey,
            Results = new PagedResult<SellerListItemViewModel>(items, page, PageSize, total),
        });
    }

    // GET: /Sellers/Details/5
    public async Task<IActionResult> Details(int? id, int page = 1, CancellationToken ct = default)
    {
        if (id is null)
        {
            return NotFound();
        }

        var seller = await db.Sellers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (seller is null)
        {
            return NotFound();
        }

        var active = db.Listings.AsNoTracking().Where(l => l.SellerId == seller.Id && l.Status == ListingStatus.Active);
        var topDistricts = await active
            .GroupBy(l => new { l.DistrictId, l.District.Name })
            .Select(g => new { g.Key.DistrictId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(ct);

        return View(new SellerDetailsViewModel
        {
            Seller = seller,
            Listings = await listingQueries.PageAsync(
                active.OrderByDescending(l => l.CreatedUtc).ThenByDescending(l => l.Id), page, 12, User.GetUserId(), ct),
            SaleCount = await active.CountAsync(l => l.DealKind == DealKind.Sale, ct),
            RentCount = await active.CountAsync(l => l.DealKind == DealKind.Rent, ct),
            TopDistricts = topDistricts.Select(d => new DistrictLinkViewModel(d.DistrictId, d.Name, d.Count)).ToList(),
        });
    }
}
