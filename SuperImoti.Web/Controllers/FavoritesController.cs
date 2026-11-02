using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;

namespace SuperImoti.Web.Controllers;

/// <summary>Listings a user saved (many-to-many between users and listings).</summary>
[Authorize]
public class FavoritesController(ApplicationDbContext db, ListingQueryService listingQueries) : Controller
{
    // GET: /Favorites
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var userId = User.GetUserId()!;
        var query = db.Listings.AsNoTracking()
            .Where(l => l.FavoritedBy.Any(f => f.UserId == userId))
            .OrderByDescending(l => l.FavoritedBy.Where(f => f.UserId == userId).Select(f => f.CreatedUtc).FirstOrDefault())
            .ThenByDescending(l => l.Id);

        return View(await listingQueries.PageAsync(query, page, 12, userId, ct));
    }

    // POST: /Favorites/Toggle — adds or removes a listing. Answers with JSON when called from JavaScript.
    [HttpPost]
    public async Task<IActionResult> Toggle(int listingId, string? returnUrl, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == listingId, ct))
        {
            return NotFound();
        }

        var userId = User.GetUserId()!;
        var favorite = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ListingId == listingId, ct);
        var isFavorite = favorite is null;
        if (favorite is null)
        {
            db.Favorites.Add(new Favorite { UserId = userId, ListingId = listingId });
        }
        else
        {
            db.Favorites.Remove(favorite);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A double click can add the same favourite twice; the first click already did the job.
        }

        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { isFavorite });
        }

        TempData["Success"] = isFavorite ? "Обявата е добавена в любими." : "Обявата е премахната от любими.";
        return Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Details", "Listings", new { id = listingId });
    }
}
