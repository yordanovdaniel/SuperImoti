using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Listings: search and filters, details, CSV export and full CRUD (create, edit, delete).</summary>
public class ListingsController(
    ApplicationDbContext db,
    ListingQueryService listingQueries,
    LookupService lookups,
    PhotoStorage photoStorage,
    PhotoUrlResolver photoUrls,
    NewListingNotifier notifier,
    ILogger<ListingsController> logger) : Controller
{
    // Up to 10 photos of 5 MB each, plus the form fields.
    private const long MaxUploadBytes = 60L * 1024 * 1024;

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

    // GET: /Listings/Mine
    [Authorize]
    public async Task<IActionResult> Mine(int page = 1, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var query = db.Listings.AsNoTracking()
            .Where(l => l.OwnerId == userId)
            .OrderByDescending(l => l.CreatedUtc).ThenByDescending(l => l.Id);

        return View(await listingQueries.PageAsync(query, page, 12, userId, ct));
    }

    // GET: /Listings/Create
    [Authorize]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var phone = await db.Users.Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
        var input = new ListingFormModel { DealKind = DealKind.Sale, ContactPhone = phone };

        return View(await BuildEditorAsync(input, ct));
    }

    // POST: /Listings/Create
    [Authorize]
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ListingFormModel input, CancellationToken ct)
    {
        var (district, propertyType) = await ValidateLookupsAsync(input, ct);
        await ValidateNewPhotosAsync(input, existingPhotoCount: 0, ct);
        if (!ModelState.IsValid)
        {
            return View(await BuildEditorAsync(input, ct));
        }

        var now = DateTime.UtcNow;
        var listing = new Listing
        {
            OwnerId = User.GetUserId(),
            Status = ListingStatus.Active,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        Apply(input, listing, district!, propertyType!);
        listing.Events.Add(new ListingEvent
        {
            Kind = ListingEventKind.Published,
            OccurredUtc = now,
            Price = listing.Price,
            Currency = listing.Currency,
            Status = ListingStatus.Active,
        });

        var savedFiles = new List<string>();
        try
        {
            await AddPhotosAsync(listing, input.NewPhotos, savedFiles, ct);
            db.Listings.Add(listing);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException)
        {
            logger.LogError(ex, "Could not save a new listing.");
            photoStorage.Delete(savedFiles);
            ModelState.AddModelError(string.Empty, "Обявата не можа да бъде запазена. Моля, опитайте отново.");
            return View(await BuildEditorAsync(input, ct));
        }

        var url = Url.Action(nameof(Details), "Listings", new { id = listing.Id }, Request.Scheme)!;
        var notified = await notifier.NotifyAsync(listing, district!.Name, url, ct);

        TempData["Success"] = notified > 0
            ? $"Обявата е публикувана. {notified} абонирани потребители ще получат имейл."
            : "Обявата е публикувана.";
        return RedirectToAction(nameof(Details), new { id = listing.Id });
    }

    // GET: /Listings/Edit/5
    [Authorize]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            return NotFound();
        }

        var listing = await db.Listings.AsNoTracking()
            .Include(l => l.Features)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
        {
            return NotFound();
        }

        if (!CanModify(listing))
        {
            return Forbid();
        }

        return View(await BuildEditorAsync(ToForm(listing), ct));
    }

    // POST: /Listings/Edit/5
    [Authorize]
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] ListingFormModel input, CancellationToken ct)
    {
        if (input.Id != id)
        {
            return BadRequest();
        }

        var listing = await db.Listings
            .Include(l => l.Features)
            .Include(l => l.Photos)
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
        {
            return NotFound();
        }

        if (!CanModify(listing))
        {
            return Forbid();
        }

        var (district, propertyType) = await ValidateLookupsAsync(input, ct);
        var removedPhotos = listing.Photos.Where(p => input.RemovePhotoIds.Contains(p.Id)).ToList();
        await ValidateNewPhotosAsync(input, listing.Photos.Count - removedPhotos.Count, ct);
        if (!ModelState.IsValid)
        {
            return View(await BuildEditorAsync(input, ct));
        }

        // The stamp from the form is the version the user started editing. If someone saved
        // the listing in the meantime, EF Core throws DbUpdateConcurrencyException below.
        db.Entry(listing).Property(l => l.ConcurrencyStamp).OriginalValue = input.ConcurrencyStamp;
        listing.ConcurrencyStamp = Guid.NewGuid();

        var oldPrice = listing.Price;
        Apply(input, listing, district!, propertyType!);
        listing.UpdatedUtc = DateTime.UtcNow;

        foreach (var photo in removedPhotos)
        {
            listing.Photos.Remove(photo);
        }

        var savedFiles = new List<string>();
        try
        {
            await AddPhotosAsync(listing, input.NewPhotos, savedFiles, ct);
            RenumberPhotos(listing);

            listing.Events.Add(oldPrice != listing.Price
                ? new ListingEvent
                {
                    Kind = ListingEventKind.PriceChange,
                    Price = listing.Price,
                    OldPrice = oldPrice,
                    Currency = listing.Currency,
                    Note = "Цената е променена в SuperImoti.",
                }
                : new ListingEvent { Kind = ListingEventKind.Edited, Price = listing.Price, Currency = listing.Currency });

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            photoStorage.Delete(savedFiles);
            ModelState.AddModelError(string.Empty,
                "Някой друг е променил обявата, докато я редактирахте. Отворете я отново, за да видите последната версия.");
            return View(await BuildEditorAsync(input, ct));
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException)
        {
            logger.LogError(ex, "Could not update listing {ListingId}.", id);
            photoStorage.Delete(savedFiles);
            ModelState.AddModelError(string.Empty, "Промените не можаха да бъдат запазени. Моля, опитайте отново.");
            return View(await BuildEditorAsync(input, ct));
        }

        photoStorage.Delete(removedPhotos.Where(p => p.FileName != null).Select(p => p.FileName!));
        TempData["Success"] = "Промените са запазени.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: /Listings/Delete/5
    [Authorize]
    public async Task<IActionResult> Delete(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            return NotFound();
        }

        var listing = await db.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null)
        {
            return NotFound();
        }

        if (!CanModify(listing))
        {
            return Forbid();
        }

        var card = (await listingQueries.ByIdsAsync([listing.Id], User.GetUserId(), ct)).Single();
        return View(new ListingDeleteViewModel
        {
            Card = card,
            IsImported = listing.IsImported,
            CommentCount = await db.Comments.CountAsync(c => c.ListingId == listing.Id, ct),
            FavoriteCount = await db.Favorites.CountAsync(f => f.ListingId == listing.Id, ct),
        });
    }

    // POST: /Listings/Delete/5
    [Authorize]
    [HttpPost]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken ct)
    {
        var listing = await db.Listings.Include(l => l.Photos).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null)
        {
            TempData["Error"] = "Обявата вече е изтрита.";
            return RedirectToAction(nameof(Index));
        }

        if (!CanModify(listing))
        {
            return Forbid();
        }

        var uploadedFiles = listing.Photos.Where(p => p.FileName != null).Select(p => p.FileName!).ToList();
        db.Listings.Remove(listing);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Could not delete listing {ListingId}.", id);
            TempData["Error"] = "Обявата не можа да бъде изтрита. Моля, опитайте отново.";
            return RedirectToAction(nameof(Details), new { id });
        }

        photoStorage.Delete(uploadedFiles);
        TempData["Success"] = "Обявата е изтрита.";
        return listing.OwnerId == User.GetUserId() ? RedirectToAction(nameof(Mine)) : RedirectToAction(nameof(Index));
    }

    private bool CanModify(Listing listing) =>
        User.IsAdmin() || (listing.OwnerId is not null && listing.OwnerId == User.GetUserId());

    /// <summary>Checks that the chosen district and type exist and drops unknown feature ids.</summary>
    private async Task<(District? District, PropertyType? Type)> ValidateLookupsAsync(ListingFormModel input, CancellationToken ct)
    {
        District? district = null;
        if (input.DistrictId is int districtId)
        {
            district = await db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == districtId, ct);
            if (district is null)
            {
                ModelState.AddModelError("Input.DistrictId", "Изберете квартал от списъка.");
            }
        }

        PropertyType? propertyType = null;
        if (input.PropertyTypeId is int typeId)
        {
            propertyType = await db.PropertyTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct);
            if (propertyType is null)
            {
                ModelState.AddModelError("Input.PropertyTypeId", "Изберете вид имот от списъка.");
            }
        }

        if (input.FeatureIds.Count > 0)
        {
            var requested = input.FeatureIds.Distinct().ToList();
            input.FeatureIds = await db.Features.Where(f => requested.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        }

        return (district, propertyType);
    }

    /// <summary>Limits the number of photos and rejects files that are not really images.</summary>
    private async Task ValidateNewPhotosAsync(ListingFormModel input, int existingPhotoCount, CancellationToken ct)
    {
        var files = input.NewPhotos.Where(f => f.Length > 0).ToList();
        if (existingPhotoCount + files.Count > PhotoStorage.MaxPhotosPerListing)
        {
            ModelState.AddModelError("Input.NewPhotos",
                $"Една обява може да има най-много {PhotoStorage.MaxPhotosPerListing} снимки.");
        }

        foreach (var file in files.Where(f => PhotoStorage.HasAllowedExtension(f.FileName)))
        {
            if (!await PhotoStorage.IsImageAsync(file, ct))
            {
                ModelState.AddModelError("Input.NewPhotos", $"Файлът „{file.FileName}“ не е валидна снимка.");
            }
        }
    }

    private async Task AddPhotosAsync(Listing listing, IEnumerable<IFormFile> files, List<string> savedFiles, CancellationToken ct)
    {
        var position = listing.Photos.Count == 0 ? 0 : listing.Photos.Max(p => p.Position) + 1;
        foreach (var file in files.Where(f => f.Length > 0))
        {
            var fileName = await photoStorage.SaveAsync(file, ct);
            savedFiles.Add(fileName);
            listing.Photos.Add(new ListingPhoto { FileName = fileName, Position = position++, CreatedUtc = DateTime.UtcNow });
        }
    }

    private static void RenumberPhotos(Listing listing)
    {
        var position = 0;
        foreach (var photo in listing.Photos.OrderBy(p => p.Position))
        {
            photo.Position = position++;
        }
    }

    /// <summary>Copies the form values onto the entity.</summary>
    private static void Apply(ListingFormModel input, Listing listing, District district, PropertyType propertyType)
    {
        listing.Title = input.Title!.Trim();
        listing.DealKind = input.DealKind!.Value;
        listing.PropertyTypeId = propertyType.Id;
        listing.DistrictId = district.Id;
        listing.Street = Clean(input.Street);
        listing.PriceOnRequest = input.PriceOnRequest;
        listing.Price = input.PriceOnRequest ? null : input.Price;
        listing.Currency = input.Currency;
        listing.VatNote = Clean(input.VatNote);
        listing.Area = input.Area!.Value;
        listing.PricePerSqm = listing.Price is decimal price && listing.Area > 0
            ? Math.Round(price / listing.Area, 2)
            : null;
        listing.Floor = input.Floor;
        listing.TotalFloors = input.TotalFloors;
        listing.ConstructionType = Clean(input.ConstructionType);
        listing.CompletionPeriod = Clean(input.CompletionPeriod);
        listing.Description = input.Description!.Trim();
        listing.ContactPhone = Clean(input.ContactPhone);

        // Many-to-many: keep the ticked features and remove the rest.
        foreach (var link in listing.Features.Where(f => !input.FeatureIds.Contains(f.FeatureId)).ToList())
        {
            listing.Features.Remove(link);
        }

        foreach (var featureId in input.FeatureIds.Where(id => listing.Features.All(f => f.FeatureId != id)))
        {
            listing.Features.Add(new ListingFeature { FeatureId = featureId });
        }

        listing.SearchText = SearchTextBuilder.Build(
            listing.Title, listing.Street, district.Name, propertyType.Name,
            listing.Seller?.Name, listing.Description, listing.ExternalId);
    }

    private static ListingFormModel ToForm(Listing listing) => new()
    {
        Id = listing.Id,
        ConcurrencyStamp = listing.ConcurrencyStamp,
        Title = listing.Title,
        DealKind = listing.DealKind,
        PropertyTypeId = listing.PropertyTypeId,
        DistrictId = listing.DistrictId,
        Street = listing.Street,
        Price = listing.Price,
        Currency = listing.Currency,
        PriceOnRequest = listing.PriceOnRequest,
        VatNote = listing.VatNote,
        Area = listing.Area,
        Floor = listing.Floor,
        TotalFloors = listing.TotalFloors,
        ConstructionType = listing.ConstructionType,
        CompletionPeriod = listing.CompletionPeriod,
        Description = listing.Description,
        ContactPhone = listing.ContactPhone,
        FeatureIds = listing.Features.Select(f => f.FeatureId).ToList(),
    };

    private async Task<ListingEditorViewModel> BuildEditorAsync(ListingFormModel input, CancellationToken ct)
    {
        IReadOnlyList<PhotoViewModel> photos = [];
        var isImported = false;

        if (input.Id is int id)
        {
            var stored = await db.ListingPhotos.AsNoTracking()
                .Where(p => p.ListingId == id)
                .OrderBy(p => p.Position)
                .Select(p => new { p.Id, p.ContentHash, p.FileName })
                .ToListAsync(ct);

            photos = stored
                .Select(p => new PhotoViewModel(p.Id, photoUrls.Resolve(p.ContentHash, p.FileName), p.FileName != null))
                .ToList();
            isImported = await db.Listings.AnyAsync(l => l.Id == id && l.ExternalId != null, ct);
        }

        return new ListingEditorViewModel
        {
            Input = input,
            Options = await lookups.GetAsync(ct),
            ExistingPhotos = photos,
            IsImported = isImported,
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
