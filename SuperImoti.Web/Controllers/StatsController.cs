using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;

namespace SuperImoti.Web.Controllers;

/// <summary>Price statistics per district. The page is a Vue.js app that reads the JSON from <see cref="Districts"/>.</summary>
public class StatsController(ApplicationDbContext db, LookupService lookups) : Controller
{
    // GET: /Stats
    public async Task<IActionResult> Index(CancellationToken ct) => View(await lookups.GetAsync(ct));

    // GET: /Stats/Districts?deal=Sale&category=Apartment  →  JSON
    public async Task<IActionResult> Districts(
        DealKind deal = DealKind.Sale, PropertyCategory? category = null, int? type = null, CancellationToken ct = default)
    {
        var query = db.Listings.AsNoTracking().Where(l =>
            l.Status == ListingStatus.Active
            && l.DealKind == deal
            && l.Price != null
            && l.PricePerSqm != null
            && l.PricePerSqm > 0);

        if (type is int typeId)
        {
            query = query.Where(l => l.PropertyTypeId == typeId);
        }
        else if (category is PropertyCategory selected)
        {
            query = query.Where(l => l.PropertyType.Category == selected);
        }

        var rows = await query
            .GroupBy(l => new { l.DistrictId, l.District.Name })
            .Select(g => new DistrictStatsDto(
                g.Key.DistrictId,
                g.Key.Name,
                g.Count(),
                g.Average(l => l.Price),
                g.Average(l => l.PricePerSqm),
                g.Min(l => l.PricePerSqm),
                g.Max(l => l.PricePerSqm)))
            .ToListAsync(ct);

        return Json(rows.OrderByDescending(r => r.AvgPricePerSqm));
    }

    public sealed record DistrictStatsDto(
        int DistrictId,
        string District,
        int Count,
        decimal? AvgPrice,
        decimal? AvgPricePerSqm,
        decimal? MinPricePerSqm,
        decimal? MaxPricePerSqm);
}
