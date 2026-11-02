using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Districts with price statistics. Administrators can also create, rename and delete them.</summary>
public class DistrictsController(
    ApplicationDbContext db,
    ListingQueryService listingQueries,
    LookupService lookups,
    ILogger<DistrictsController> logger) : Controller
{
    // GET: /Districts
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await DistrictRows().OrderBy(d => d.Name).ToListAsync(ct));

    // GET: /Districts/Details/5?deal=Rent&page=2
    public async Task<IActionResult> Details(int? id, DealKind? deal, int page = 1, CancellationToken ct = default)
    {
        if (id is null)
        {
            return NotFound();
        }

        var stats = await DistrictRows().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (stats is null)
        {
            return NotFound();
        }

        var active = db.Listings.AsNoTracking().Where(l => l.DistrictId == id && l.Status == ListingStatus.Active);
        var typeBreakdown = await active
            .GroupBy(l => l.PropertyType.Name)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        var listings = deal is DealKind kind ? active.Where(l => l.DealKind == kind) : active;

        return View(new DistrictDetailsViewModel
        {
            Stats = stats,
            Deal = deal,
            Listings = await listingQueries.PageAsync(ListingQueryService.Sort(listings, null), page, 12, User.GetUserId(), ct),
            TypeBreakdown = typeBreakdown.Select(x => (x.Type, x.Count)).ToList(),
        });
    }

    // GET: /Districts/Create
    [Authorize(Roles = AppRoles.Administrator)]
    public IActionResult Create() => View(new DistrictFormModel());

    // POST: /Districts/Create
    [Authorize(Roles = AppRoles.Administrator)]
    [HttpPost]
    public async Task<IActionResult> Create(DistrictFormModel model, CancellationToken ct)
    {
        await CheckUniqueAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var district = new District { Name = model.Name!.Trim(), City = model.City!.Trim() };
        db.Districts.Add(district);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Could not create district {Name}.", model.Name);
            ModelState.AddModelError(string.Empty, "Кварталът не можа да бъде запазен. Възможно е вече да съществува.");
            return View(model);
        }

        lookups.Invalidate();
        TempData["Success"] = $"Кварталът „{district.Name}“ е добавен.";
        return RedirectToAction(nameof(Details), new { id = district.Id });
    }

    // GET: /Districts/Edit/5
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            return NotFound();
        }

        var district = await db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null)
        {
            return NotFound();
        }

        return View(new DistrictFormModel { Id = district.Id, Name = district.Name, City = district.City });
    }

    // POST: /Districts/Edit/5
    [Authorize(Roles = AppRoles.Administrator)]
    [HttpPost]
    public async Task<IActionResult> Edit(int id, DistrictFormModel model, CancellationToken ct)
    {
        if (model.Id != id)
        {
            return BadRequest();
        }

        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null)
        {
            return NotFound();
        }

        await CheckUniqueAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        district.Name = model.Name!.Trim();
        district.City = model.City!.Trim();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Could not update district {Id}.", id);
            ModelState.AddModelError(string.Empty, "Промените не можаха да бъдат запазени.");
            return View(model);
        }

        lookups.Invalidate();
        TempData["Success"] = "Кварталът е обновен.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: /Districts/Delete/5
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> Delete(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            return NotFound();
        }

        var district = await db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null)
        {
            return NotFound();
        }

        return View(new DistrictDeleteViewModel
        {
            District = district,
            ListingCount = await db.Listings.CountAsync(l => l.DistrictId == id, ct),
        });
    }

    // POST: /Districts/Delete/5
    [Authorize(Roles = AppRoles.Administrator)]
    [HttpPost]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null)
        {
            TempData["Error"] = "Кварталът вече е изтрит.";
            return RedirectToAction(nameof(Index));
        }

        var listingCount = await db.Listings.CountAsync(l => l.DistrictId == id, ct);
        if (listingCount > 0)
        {
            TempData["Error"] = $"Кварталът има {listingCount} обяви и не може да бъде изтрит.";
            return RedirectToAction(nameof(Delete), new { id });
        }

        db.Districts.Remove(district);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A listing may have been added between the check and the delete; the foreign key stops it.
            logger.LogWarning(ex, "Could not delete district {Id}.", id);
            TempData["Error"] = "Кварталът не може да бъде изтрит, защото към него има обяви.";
            return RedirectToAction(nameof(Delete), new { id });
        }

        lookups.Invalidate();
        TempData["Success"] = $"Кварталът „{district.Name}“ е изтрит.";
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<DistrictRowViewModel> DistrictRows() =>
        db.Districts.AsNoTracking().Select(d => new DistrictRowViewModel
        {
            Id = d.Id,
            Name = d.Name,
            City = d.City,
            SaleCount = d.Listings.Count(l => l.Status == ListingStatus.Active && l.DealKind == DealKind.Sale),
            RentCount = d.Listings.Count(l => l.Status == ListingStatus.Active && l.DealKind == DealKind.Rent),
            AvgApartmentSalePricePerSqm = d.Listings
                .Where(l => l.Status == ListingStatus.Active
                    && l.DealKind == DealKind.Sale
                    && l.PropertyType.Category == PropertyCategory.Apartment
                    && l.PricePerSqm != null)
                .Average(l => l.PricePerSqm),
            AvgApartmentRent = d.Listings
                .Where(l => l.Status == ListingStatus.Active
                    && l.DealKind == DealKind.Rent
                    && l.PropertyType.Category == PropertyCategory.Apartment
                    && l.Price != null)
                .Average(l => l.Price),
        });

    private async Task CheckUniqueAsync(DistrictFormModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.City))
        {
            return;
        }

        var name = model.Name.Trim();
        var city = model.City.Trim();
        var currentId = model.Id ?? 0;
        if (await db.Districts.AnyAsync(d => d.Name == name && d.City == city && d.Id != currentId, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "Вече има квартал с това име в този град.");
        }
    }
}
