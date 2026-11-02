using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperImoti.Web.Data;
using SuperImoti.Web.Data.Import;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Administration: data sync with the scraper, site totals and newest users.</summary>
[Authorize(Roles = AppRoles.Administrator)]
public class AdminController(
    ApplicationDbContext db,
    ImportCoordinator importCoordinator,
    IOptions<ScraperDataOptions> scraperOptions,
    SmtpEmailSender emailSender) : Controller
{
    // GET: /Admin
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = scraperOptions.Value;
        var latestUsers = await db.Users.AsNoTracking()
            .OrderByDescending(u => u.CreatedUtc)
            .Take(20)
            .Select(u => new AdminUserRow(
                u.Email ?? string.Empty, u.DisplayName, u.CreatedUtc, u.Listings.Count(), u.NotifyAboutNewListings))
            .ToListAsync(ct);

        return View(new AdminIndexViewModel
        {
            Import = importCoordinator.Snapshot(),
            SourceFolder = settings.SourceFolder,
            SourceDatabaseExists = settings.DatabaseExists,
            SourceImagesExist = settings.ImagesExist,
            ImportedListings = await db.Listings.CountAsync(l => l.ExternalId != null, ct),
            UserListings = await db.Listings.CountAsync(l => l.ExternalId == null, ct),
            Photos = await db.ListingPhotos.CountAsync(ct),
            Users = await db.Users.CountAsync(ct),
            Comments = await db.Comments.CountAsync(ct),
            Favorites = await db.Favorites.CountAsync(ct),
            QueuedEmailFiles = Directory.Exists(emailSender.PickupDirectory)
                ? Directory.EnumerateFiles(emailSender.PickupDirectory, "*.eml").Count()
                : 0,
            LatestUsers = latestUsers,
        });
    }

    // POST: /Admin/Import — starts a sync with the scraper database in the background.
    [HttpPost]
    public IActionResult Import()
    {
        if (!scraperOptions.Value.DatabaseExists)
        {
            TempData["Error"] = "Базата данни на скрейпъра не е намерена. Проверете настройката ScraperData:SourceFolder.";
        }
        else if (importCoordinator.TryRequestRun())
        {
            TempData["Success"] = "Синхронизацията започна. Прогресът се вижда на тази страница.";
        }
        else
        {
            TempData["Error"] = "В момента вече тече синхронизация.";
        }

        return RedirectToAction(nameof(Index));
    }
}
