using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Data.Import;

/// <summary>
/// Copies listings, photos and price history from the scraper's database (imot_imot.db) into the site's database.
/// Safe to run again and again: new listings are added, changed ones are updated, unchanged ones are skipped.
/// </summary>
public class ScraperImporter(
    ApplicationDbContext db,
    IOptions<ScraperDataOptions> options,
    ILogger<ScraperImporter> logger)
{
    private const int BatchSize = 1000;

    public async Task<ImportResult> RunAsync(Action<string, int, int>? report, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.DatabaseExists)
        {
            throw new FileNotFoundException("The scraper database was not found.", settings.DatabasePath);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = new ImportResult();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = settings.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var source = new SqliteConnection(connectionString);
        await source.OpenAsync(ct);

        // The database can list photos whose files never reached this computer, so only files that exist count.
        report?.Invoke("Проверка на снимките", 0, 0);
        var photoFiles = settings.ImagesExist ? ScanPhotoFiles(settings.ImagesPath!) : [];

        report?.Invoke("Справочници", 0, 0);
        var lookups = new ImportLookups
        {
            Districts = await SyncDistrictsAsync(source, result, ct),
            PropertyTypes = await SyncPropertyTypesAsync(source, result, ct),
            Features = await SyncFeaturesAsync(source, result, ct),
            Sellers = await SyncSellersAsync(source, result, ct),
            DuplicateGroups = await ReadDuplicateGroupsAsync(source, ct),
        };

        var existing = await db.Listings.AsNoTracking()
            .Where(l => l.ExternalId != null)
            .Select(l => new ExistingListing(l.ExternalId!, l.Id, l.SourceContentHash, l.Status, l.DuplicateGroupId))
            .ToDictionaryAsync(l => l.ExternalId, ct);

        // History rows are append-only with growing ids, so anything above the highest imported id is new.
        var lastEventSourceId = await db.ListingEvents.MaxAsync(e => e.SourceId, ct) ?? 0;

        var total = await CountListingsAsync(source, ct);
        var processed = 0;
        var after = string.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await ReadListingsAsync(source, after, ct);
            if (batch.Count == 0)
            {
                break;
            }

            var first = batch[0].ListingId;
            var last = batch[^1].ListingId;
            var photos = await ReadPhotosAsync(source, first, last, photoFiles, result, ct);
            var history = await ReadHistoryAsync(source, first, last, ct);

            await ImportBatchAsync(batch, photos, history, lookups, existing, lastEventSourceId, result, ct);

            after = last;
            processed += batch.Count;
            report?.Invoke("Обяви", processed, total);
        }

        result.Duration = stopwatch.Elapsed;
        logger.LogInformation("Scraper import finished: {Result}", result);
        return result;
    }

    private async Task ImportBatchAsync(
        List<SourceListing> batch,
        Dictionary<string, List<SourcePhoto>> photos,
        Dictionary<string, List<SourceHistory>> history,
        ImportLookups lookups,
        Dictionary<string, ExistingListing> existing,
        int lastEventSourceId,
        ImportResult result,
        CancellationToken ct)
    {
        var toUpdate = new Dictionary<int, (SourceListing Row, ListingRefs Refs)>();
        var existingInBatch = new List<(ExistingListing Known, SourceListing Row, ListingRefs Refs)>();

        foreach (var row in batch)
        {
            if (!lookups.TryResolve(row, out var refs))
            {
                result.ListingsSkipped++;
                continue;
            }

            var rowPhotos = photos.GetValueOrDefault(row.ListingId) ?? [];
            var rowHistory = history.GetValueOrDefault(row.ListingId) ?? [];

            if (!existing.TryGetValue(row.ListingId, out var known))
            {
                var listing = new Listing();
                Apply(row, refs, listing);

                foreach (var photo in BuildPhotos(rowPhotos, listing.CreatedUtc))
                {
                    listing.Photos.Add(photo);
                }

                foreach (var featureId in refs.FeatureIds)
                {
                    listing.Features.Add(new ListingFeature { FeatureId = featureId });
                }

                foreach (var item in rowHistory)
                {
                    listing.Events.Add(MapEvent(item));
                }

                db.Listings.Add(listing);
                result.ListingsAdded++;
                result.PhotosAdded += listing.Photos.Count;
                result.EventsAdded += listing.Events.Count;
                continue;
            }

            existingInBatch.Add((known, row, refs));
            if (known.ContentHash != row.ContentHash
                || known.Status != MapStatus(row.Status)
                || known.DuplicateGroupId != refs.DuplicateGroupId)
            {
                toUpdate[known.Id] = (row, refs);
            }

            foreach (var item in rowHistory.Where(h => h.Id > lastEventSourceId))
            {
                var listingEvent = MapEvent(item);
                listingEvent.ListingId = known.Id;
                db.ListingEvents.Add(listingEvent);
                result.EventsAdded++;
            }
        }

        // The scraper downloads photos separately, so photos can change while the listing itself does not.
        var photosChanged = await FindListingsWithChangedPhotosAsync(existingInBatch, photos, ct);
        foreach (var (known, row, refs) in existingInBatch.Where(e => photosChanged.Contains(e.Known.Id)))
        {
            toUpdate.TryAdd(known.Id, (row, refs));
        }

        if (toUpdate.Count > 0)
        {
            var ids = toUpdate.Keys.ToList();
            var listings = await db.Listings
                .Include(l => l.Features)
                .Include(l => l.Photos)
                .Where(l => ids.Contains(l.Id))
                .ToListAsync(ct);

            foreach (var listing in listings)
            {
                var (row, refs) = toUpdate[listing.Id];
                Apply(row, refs, listing);
                SyncFeatures(listing, refs.FeatureIds);

                if (photosChanged.Contains(listing.Id))
                {
                    result.PhotosAdded += ReplaceImportedPhotos(listing, photos.GetValueOrDefault(row.ListingId) ?? []);
                }
            }

            result.ListingsUpdated += listings.Count;
        }

        result.ListingsUnchanged += existingInBatch.Count - toUpdate.Count;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<HashSet<int>> FindListingsWithChangedPhotosAsync(
        List<(ExistingListing Known, SourceListing Row, ListingRefs Refs)> existingInBatch,
        Dictionary<string, List<SourcePhoto>> photos,
        CancellationToken ct)
    {
        var changed = new HashSet<int>();
        if (existingInBatch.Count == 0)
        {
            return changed;
        }

        var ids = existingInBatch.Select(e => e.Known.Id).ToList();
        var current = await db.ListingPhotos.AsNoTracking()
            .Where(p => ids.Contains(p.ListingId) && p.ContentHash != null)
            .OrderBy(p => p.ListingId).ThenBy(p => p.Position)
            .Select(p => new { p.ListingId, p.ContentHash })
            .ToListAsync(ct);

        var currentByListing = current
            .GroupBy(p => p.ListingId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ContentHash!).ToList());

        foreach (var (known, row, _) in existingInBatch)
        {
            var wanted = DistinctHashes(photos.GetValueOrDefault(row.ListingId) ?? []);
            var have = currentByListing.GetValueOrDefault(known.Id) ?? [];
            if (!wanted.SequenceEqual(have))
            {
                changed.Add(known.Id);
            }
        }

        return changed;
    }

    /// <summary>Copies the scraped values onto a listing (new or existing).</summary>
    private static void Apply(SourceListing row, ListingRefs refs, Listing listing)
    {
        var deal = row.DealKind == "Rent" ? DealKind.Rent : DealKind.Sale;

        listing.ExternalId = row.ListingId;
        listing.Title = SourceValueParser.BuildTitle(refs.PropertyTypeName, deal, refs.DistrictName);
        listing.DealKind = deal;
        listing.Status = MapStatus(row.Status);
        listing.PropertyTypeId = refs.PropertyTypeId;
        listing.DistrictId = refs.DistrictId;
        listing.Street = Clip(row.Street, 120);
        listing.Price = ToDecimal(row.Price);
        listing.PriceOnRequest = row.PriceOnRequest || row.Price is null;
        listing.Currency = SourceValueParser.ParseCurrency(row.Currency);
        listing.PricePerSqm = ToDecimal(row.PricePerSqm);
        listing.VatNote = Clip(row.VatNote, 60);
        listing.Area = ToDecimal(row.AreaSqm) ?? 0;
        listing.Floor = row.Floor;
        listing.TotalFloors = row.TotalFloors;
        listing.ConstructionType = Clip(row.ConstructionType, 60);
        listing.CompletionPeriod = Clip(row.CompletionPeriod, 40);
        listing.Description = Clip(row.Description, 6000);
        listing.ContactPhone = Clip(SourceValueParser.CleanPhones(row.SellerPhones), 120);
        listing.SellerId = refs.SellerId;
        listing.SourceUrl = Clip(row.CanonicalUrl, 300);
        listing.SourceContentHash = row.ContentHash;
        listing.SourceViewCount = row.ViewCount;
        listing.SourceUpdatedUtc = SourceValueParser.ParseUtc(row.LastUpdatedUtc);
        listing.DuplicateGroupId = refs.DuplicateGroupId;
        listing.CreatedUtc = SourceValueParser.ParseUtc(row.FirstSeenUtc) ?? DateTime.UtcNow;
        listing.UpdatedUtc = SourceValueParser.ParseUtc(row.LastChangedUtc) ?? listing.CreatedUtc;
        listing.InactiveSinceUtc = SourceValueParser.ParseUtc(row.GoneAtUtc);
        listing.SearchText = SearchTextBuilder.Build(
            listing.Title, listing.Street, refs.DistrictName, refs.PropertyTypeName,
            refs.SellerName, listing.Description, listing.ExternalId);
        listing.ConcurrencyStamp = Guid.NewGuid();
    }

    private static void SyncFeatures(Listing listing, IReadOnlyCollection<int> featureIds)
    {
        foreach (var link in listing.Features.Where(f => !featureIds.Contains(f.FeatureId)).ToList())
        {
            listing.Features.Remove(link);
        }

        foreach (var featureId in featureIds.Where(id => listing.Features.All(f => f.FeatureId != id)))
        {
            listing.Features.Add(new ListingFeature { FeatureId = featureId });
        }
    }

    /// <summary>Replaces the scraped photos of a listing; photos uploaded on this site are kept after them.</summary>
    private int ReplaceImportedPhotos(Listing listing, List<SourcePhoto> source)
    {
        db.ListingPhotos.RemoveRange(listing.Photos.Where(p => p.ContentHash != null).ToList());

        var fresh = BuildPhotos(source, DateTime.UtcNow);
        foreach (var photo in fresh)
        {
            listing.Photos.Add(photo);
        }

        var position = fresh.Count;
        foreach (var uploaded in listing.Photos.Where(p => p.FileName != null).OrderBy(p => p.Position))
        {
            uploaded.Position = position++;
        }

        return fresh.Count;
    }

    private static List<ListingPhoto> BuildPhotos(List<SourcePhoto> source, DateTime createdUtc) =>
        DistinctHashes(source)
            .Select((hash, index) => new ListingPhoto
            {
                ContentHash = hash,
                SourceUrl = Clip(source.First(p => p.ContentHash == hash).Url, 300),
                Position = index,
                CreatedUtc = createdUtc,
            })
            .ToList();

    /// <summary>The same photo can appear twice in one ad (different URLs, same file); keep it once.</summary>
    private static List<string> DistinctHashes(List<SourcePhoto> source) =>
        source.Select(p => p.ContentHash).Distinct().ToList();

    private static ListingEvent MapEvent(SourceHistory item) => new()
    {
        SourceId = item.Id,
        OccurredUtc = SourceValueParser.ParseUtc(item.ObservedUtc) ?? DateTime.UtcNow,
        Kind = item.Event switch
        {
            "price_change" => ListingEventKind.PriceChange,
            "status_change" => ListingEventKind.StatusChange,
            _ => ListingEventKind.Discovered,
        },
        Price = ToDecimal(item.Price),
        OldPrice = item.Event == "price_change" ? SourceValueParser.ParseOldPrice(item.Note) : null,
        Currency = item.Price is null ? null : SourceValueParser.ParseCurrency(item.Currency),
        Status = item.Status switch
        {
            "Gone" => ListingStatus.Inactive,
            "Active" => ListingStatus.Active,
            _ => null,
        },
        Note = Clip(item.Note, 200),
    };

    private static ListingStatus MapStatus(string? status) =>
        status == "Gone" ? ListingStatus.Inactive : ListingStatus.Active;

    private static decimal? ToDecimal(double? value) =>
        value is double d && double.IsFinite(d) ? (decimal)d : null;

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    // ---------- lookup tables ----------

    private async Task<Dictionary<string, (int Id, string Name)>> SyncDistrictsAsync(
        SqliteConnection source, ImportResult result, CancellationToken ct)
    {
        var known = await db.Districts.AsNoTracking()
            .ToDictionaryAsync(d => DistrictKey(d.City, d.Name), d => (d.Id, d.Name), StringComparer.OrdinalIgnoreCase, ct);

        var added = new Dictionary<string, District>(StringComparer.OrdinalIgnoreCase);
        await foreach (var record in QueryAsync(source, "SELECT DISTINCT city, district FROM listings WHERE district IS NOT NULL", ct))
        {
            var city = SourceValueParser.CleanCity(GetString(record, 0));
            var name = Clip(GetString(record, 1), 80);
            if (name is null)
            {
                continue;
            }

            var key = DistrictKey(city, name);
            if (!known.ContainsKey(key) && !added.ContainsKey(key))
            {
                added[key] = new District { City = Clip(city, 60)!, Name = name };
            }
        }

        if (added.Count > 0)
        {
            db.Districts.AddRange(added.Values);
            await db.SaveChangesAsync(ct);
            foreach (var (key, district) in added)
            {
                known[key] = (district.Id, district.Name);
            }

            result.DistrictsAdded = added.Count;
        }

        db.ChangeTracker.Clear();
        return known;
    }

    private async Task<Dictionary<string, (int Id, string Name)>> SyncPropertyTypesAsync(
        SqliteConnection source, ImportResult result, CancellationToken ct)
    {
        var known = await db.PropertyTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Name, t => (t.Id, t.Name), StringComparer.OrdinalIgnoreCase, ct);

        var added = new Dictionary<string, PropertyType>(StringComparer.OrdinalIgnoreCase);
        await foreach (var record in QueryAsync(source, "SELECT DISTINCT property_type FROM listings WHERE property_type IS NOT NULL", ct))
        {
            var name = Clip(PropertyTypeCatalog.ToDisplayName(GetString(record, 0) ?? string.Empty), 80);
            if (name is null || known.ContainsKey(name) || added.ContainsKey(name))
            {
                continue;
            }

            var (category, sortOrder) = PropertyTypeCatalog.Classify(name);
            added[name] = new PropertyType { Name = name, Category = category, SortOrder = sortOrder };
        }

        if (added.Count > 0)
        {
            db.PropertyTypes.AddRange(added.Values);
            await db.SaveChangesAsync(ct);
            foreach (var (key, type) in added)
            {
                known[key] = (type.Id, type.Name);
            }

            result.PropertyTypesAdded = added.Count;
        }

        db.ChangeTracker.Clear();
        return known;
    }

    private async Task<Dictionary<string, int>> SyncFeaturesAsync(
        SqliteConnection source, ImportResult result, CancellationToken ct)
    {
        var known = await db.Features.AsNoTracking()
            .ToDictionaryAsync(f => f.Name, f => f.Id, StringComparer.OrdinalIgnoreCase, ct);

        var added = new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase);
        await foreach (var record in QueryAsync(source, "SELECT DISTINCT features FROM listings WHERE features IS NOT NULL", ct))
        {
            foreach (var name in SplitFeatures(GetString(record, 0)))
            {
                if (!known.ContainsKey(name) && !added.ContainsKey(name))
                {
                    added[name] = new Feature { Name = name };
                }
            }
        }

        if (added.Count > 0)
        {
            db.Features.AddRange(added.Values);
            await db.SaveChangesAsync(ct);
            foreach (var (key, feature) in added)
            {
                known[key] = feature.Id;
            }

            result.FeaturesAdded = added.Count;
        }

        db.ChangeTracker.Clear();
        return known;
    }

    private async Task<Dictionary<string, (int Id, string Name)>> SyncSellersAsync(
        SqliteConnection source, ImportResult result, CancellationToken ct)
    {
        var sellers = await db.Sellers
            .Where(s => s.Subdomain != null)
            .ToDictionaryAsync(s => s.Subdomain!, StringComparer.OrdinalIgnoreCase, ct);

        // Ordered by last_seen_utc so the most recent details of each agency win.
        const string sql = """
            SELECT seller_subdomain, seller_name, seller_phones, seller_member_since, seller_office_address
            FROM listings
            WHERE seller_kind = 'Agency' AND seller_subdomain IS NOT NULL
            ORDER BY last_seen_utc
            """;

        await foreach (var record in QueryAsync(source, sql, ct))
        {
            var subdomain = Clip(GetString(record, 0), 80);
            if (subdomain is null)
            {
                continue;
            }

            if (!sellers.TryGetValue(subdomain, out var seller))
            {
                seller = new Seller { Subdomain = subdomain };
                db.Sellers.Add(seller);
                sellers[subdomain] = seller;
                result.SellersAdded++;
            }

            seller.Name = Clip(GetString(record, 1), 120) ?? subdomain;
            seller.NormalizedName = seller.Name.ToUpperInvariant();
            seller.Phones = Clip(SourceValueParser.CleanPhones(GetString(record, 2)), 200);
            seller.MemberSince = Clip(GetString(record, 3), 30);
            seller.OfficeAddress = Clip(GetString(record, 4), 250);
        }

        await db.SaveChangesAsync(ct);
        var map = sellers.ToDictionary(kv => kv.Key, kv => (kv.Value.Id, kv.Value.Name), StringComparer.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();
        return map;
    }

    private static async Task<Dictionary<string, int>> ReadDuplicateGroupsAsync(SqliteConnection source, CancellationToken ct)
    {
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);

        // The table is optional in the scraper database.
        await using (var check = source.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'listing_duplicates'";
            if (Convert.ToInt64(await check.ExecuteScalarAsync(ct)) == 0)
            {
                return groups;
            }
        }

        await foreach (var record in QueryAsync(source, "SELECT listing_id, group_id FROM listing_duplicates", ct))
        {
            groups[record.GetString(0)] = record.GetInt32(1);
        }

        return groups;
    }

    private static IEnumerable<string> SplitFeatures(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f.Length <= 80)
                .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string DistrictKey(string city, string name) => $"{city}\u001f{name}";

    // ---------- reading the scraper database ----------

    private static async Task<int> CountListingsAsync(SqliteConnection source, CancellationToken ct)
    {
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM listings";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<List<SourceListing>> ReadListingsAsync(SqliteConnection source, string after, CancellationToken ct)
    {
        const string sql = """
            SELECT listing_id, canonical_url, status, first_seen_utc, last_changed_utc, deal_kind, property_type,
                   city, district, street, price, currency, price_on_request, price_per_sqm, vat_note, area_sqm,
                   floor, total_floors, construction_type, completion_period, description, features, seller_kind,
                   seller_subdomain, seller_phones, view_count, last_updated_utc, content_hash, gone_at_utc
            FROM listings
            WHERE listing_id > $after
            ORDER BY listing_id
            LIMIT $take
            """;

        var rows = new List<SourceListing>(BatchSize);
        await foreach (var r in QueryAsync(source, sql, ct, ("$after", after), ("$take", BatchSize)))
        {
            rows.Add(new SourceListing(
                ListingId: r.GetString(0),
                CanonicalUrl: GetString(r, 1),
                Status: GetString(r, 2),
                FirstSeenUtc: GetString(r, 3),
                LastChangedUtc: GetString(r, 4),
                DealKind: GetString(r, 5),
                PropertyType: GetString(r, 6),
                City: GetString(r, 7),
                District: GetString(r, 8),
                Street: GetString(r, 9),
                Price: GetDouble(r, 10),
                Currency: GetString(r, 11),
                PriceOnRequest: GetInt(r, 12) == 1,
                PricePerSqm: GetDouble(r, 13),
                VatNote: GetString(r, 14),
                AreaSqm: GetDouble(r, 15),
                Floor: GetInt(r, 16),
                TotalFloors: GetInt(r, 17),
                ConstructionType: GetString(r, 18),
                CompletionPeriod: GetString(r, 19),
                Description: GetString(r, 20),
                Features: GetString(r, 21),
                SellerKind: GetString(r, 22),
                SellerSubdomain: GetString(r, 23),
                SellerPhones: GetString(r, 24),
                ViewCount: GetInt(r, 25),
                LastUpdatedUtc: GetString(r, 26),
                ContentHash: GetString(r, 27),
                GoneAtUtc: GetString(r, 28)));
        }

        return rows;
    }

    /// <summary>
    /// Lists the photo files that really exist: images/&lt;hash[0:2]&gt;/&lt;hash[2:4]&gt;/&lt;hash&gt;.jpg.
    /// Reading the folder once is much faster than checking 300 000 files one by one.
    /// </summary>
    private static HashSet<string> ScanPhotoFiles(string imagesRoot)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(imagesRoot, "*.jpg", SearchOption.AllDirectories))
        {
            var hash = Path.GetFileNameWithoutExtension(path);
            var folder = Path.GetDirectoryName(path)!;
            if (SourceValueParser.IsContentHash(hash)
                && Path.GetFileName(folder) == hash[2..4]
                && Path.GetFileName(Path.GetDirectoryName(folder)) == hash[..2])
            {
                hashes.Add(hash);
            }
        }

        return hashes;
    }

    private static async Task<Dictionary<string, List<SourcePhoto>>> ReadPhotosAsync(
        SqliteConnection source, string first, string last, HashSet<string> photoFiles, ImportResult result,
        CancellationToken ct)
    {
        // content_hash IS NULL means the photo has not been downloaded yet, so there is no file to show.
        const string sql = """
            SELECT listing_id, position, url, content_hash
            FROM listing_images
            WHERE listing_id BETWEEN $first AND $last AND content_hash IS NOT NULL
            ORDER BY listing_id, position
            """;

        var photos = new Dictionary<string, List<SourcePhoto>>(StringComparer.Ordinal);
        await foreach (var r in QueryAsync(source, sql, ct, ("$first", first), ("$last", last)))
        {
            var hash = GetString(r, 3);
            if (!SourceValueParser.IsContentHash(hash))
            {
                continue;
            }

            if (!photoFiles.Contains(hash!))
            {
                result.PhotosWithoutFile++;
                continue;
            }

            var listingId = r.GetString(0);
            if (!photos.TryGetValue(listingId, out var list))
            {
                photos[listingId] = list = [];
            }

            list.Add(new SourcePhoto(r.GetInt32(1), r.GetString(2), hash!));
        }

        return photos;
    }

    private static async Task<Dictionary<string, List<SourceHistory>>> ReadHistoryAsync(
        SqliteConnection source, string first, string last, CancellationToken ct)
    {
        const string sql = """
            SELECT id, listing_id, observed_utc, event, price, currency, status, note
            FROM listing_history
            WHERE listing_id BETWEEN $first AND $last
            ORDER BY id
            """;

        var history = new Dictionary<string, List<SourceHistory>>(StringComparer.Ordinal);
        await foreach (var r in QueryAsync(source, sql, ct, ("$first", first), ("$last", last)))
        {
            var listingId = r.GetString(1);
            if (!history.TryGetValue(listingId, out var list))
            {
                history[listingId] = list = [];
            }

            list.Add(new SourceHistory(
                Id: r.GetInt32(0),
                ObservedUtc: GetString(r, 2),
                Event: GetString(r, 3) ?? string.Empty,
                Price: GetDouble(r, 4),
                Currency: GetString(r, 5),
                Status: GetString(r, 6),
                Note: GetString(r, 7)));
        }

        return history;
    }

    private static async IAsyncEnumerable<SqliteDataReader> QueryAsync(
        SqliteConnection source,
        string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = source.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return reader;
        }
    }

    private static string? GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static double? GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static int? GetInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    // ---------- small types ----------

    private sealed record SourceListing(
        string ListingId, string? CanonicalUrl, string? Status, string? FirstSeenUtc, string? LastChangedUtc,
        string? DealKind, string? PropertyType, string? City, string? District, string? Street, double? Price,
        string? Currency, bool PriceOnRequest, double? PricePerSqm, string? VatNote, double? AreaSqm, int? Floor,
        int? TotalFloors, string? ConstructionType, string? CompletionPeriod, string? Description, string? Features,
        string? SellerKind, string? SellerSubdomain, string? SellerPhones, int? ViewCount, string? LastUpdatedUtc,
        string? ContentHash, string? GoneAtUtc);

    private sealed record SourcePhoto(int Position, string Url, string ContentHash);

    private sealed record SourceHistory(
        int Id, string? ObservedUtc, string Event, double? Price, string? Currency, string? Status, string? Note);

    private sealed record ExistingListing(
        string ExternalId, int Id, string? ContentHash, ListingStatus Status, int? DuplicateGroupId);

    private sealed record ListingRefs(
        int DistrictId, string DistrictName, int PropertyTypeId, string PropertyTypeName,
        int? SellerId, string? SellerName, IReadOnlyCollection<int> FeatureIds, int? DuplicateGroupId);

    private sealed class ImportLookups
    {
        public required Dictionary<string, (int Id, string Name)> Districts { get; init; }

        public required Dictionary<string, (int Id, string Name)> PropertyTypes { get; init; }

        public required Dictionary<string, int> Features { get; init; }

        public required Dictionary<string, (int Id, string Name)> Sellers { get; init; }

        public required Dictionary<string, int> DuplicateGroups { get; init; }

        public bool TryResolve(SourceListing row, out ListingRefs refs)
        {
            refs = null!;
            var districtName = row.District?.Trim();
            var typeName = PropertyTypeCatalog.ToDisplayName(row.PropertyType ?? string.Empty);
            if (string.IsNullOrEmpty(districtName)
                || !Districts.TryGetValue(DistrictKey(SourceValueParser.CleanCity(row.City), districtName), out var district)
                || !PropertyTypes.TryGetValue(typeName, out var type))
            {
                return false;
            }

            (int Id, string Name)? seller = null;
            if (row.SellerKind == "Agency" && row.SellerSubdomain is not null
                && Sellers.TryGetValue(row.SellerSubdomain.Trim(), out var found))
            {
                seller = found;
            }

            var featureIds = SplitFeatures(row.Features)
                .Select(name => Features.TryGetValue(name, out var id) ? id : 0)
                .Where(id => id != 0)
                .Distinct()
                .ToList();

            refs = new ListingRefs(
                district.Id,
                district.Name,
                type.Id,
                type.Name,
                seller?.Id,
                seller?.Name,
                featureIds,
                DuplicateGroups.TryGetValue(row.ListingId, out var group) ? group : null);
            return true;
        }
    }
}
