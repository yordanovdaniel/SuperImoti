using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SuperImoti.Web.Data;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Services;

/// <summary>Loads the drop-down data (property types, districts, features) and caches it for a few minutes.</summary>
public class LookupService(ApplicationDbContext db, IMemoryCache cache)
{
    private const string CacheKey = "lookup-options";

    public async Task<LookupOptions> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out LookupOptions? cached) && cached is not null)
        {
            return cached;
        }

        var options = new LookupOptions
        {
            PropertyTypes = await db.PropertyTypes.AsNoTracking()
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .Select(t => new PropertyTypeItem(t.Id, t.Name, t.Category))
                .ToListAsync(ct),

            Districts = await db.Districts.AsNoTracking()
                .OrderBy(d => d.Name)
                .Select(d => new LookupItem(d.Id, d.City == "София" ? d.Name : d.Name + " (" + d.City + ")"))
                .ToListAsync(ct),

            // Most common features first, so the filter shows the useful ones at the top.
            Features = await db.Features.AsNoTracking()
                .OrderByDescending(f => f.Listings.Count()).ThenBy(f => f.Name)
                .Select(f => new LookupItem(f.Id, f.Name))
                .ToListAsync(ct),
        };

        cache.Set(CacheKey, options, TimeSpan.FromMinutes(10));
        return options;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
