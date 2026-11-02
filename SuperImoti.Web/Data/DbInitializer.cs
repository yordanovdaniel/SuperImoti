using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Data;

/// <summary>Creates/updates the database on startup and seeds lookup data, roles and the admin account.</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var env = provider.GetRequiredService<IWebHostEnvironment>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        await db.Database.MigrateAsync();

        // WAL mode lets pages read the database while the background import is writing to it.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        await SeedLookupsAsync(db, env, logger);
        await SeedRolesAndAdminAsync(provider, logger);
    }

    /// <summary>
    /// Districts, property types and features, so listings can be published even before
    /// any data is imported from the scraper.
    /// </summary>
    private static async Task SeedLookupsAsync(ApplicationDbContext db, IWebHostEnvironment env, ILogger logger)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "Seed", "lookups.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Seed file {Path} was not found; lookup tables were not seeded.", path);
            return;
        }

        var seed = JsonSerializer.Deserialize<LookupSeed>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LookupSeed();

        if (!await db.Districts.AnyAsync())
        {
            db.Districts.AddRange(seed.Districts.Select(d => new District { City = d.City, Name = d.Name }));
        }

        if (!await db.PropertyTypes.AnyAsync())
        {
            db.PropertyTypes.AddRange(seed.PropertyTypes.Select(name =>
            {
                var (category, sortOrder) = PropertyTypeCatalog.Classify(name);
                return new PropertyType { Name = name, Category = category, SortOrder = sortOrder };
            }));
        }

        if (!await db.Features.AnyAsync())
        {
            db.Features.AddRange(seed.Features.Select(name => new Feature { Name = name }));
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAndAdminAsync(IServiceProvider provider, ILogger logger)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(AppRoles.Administrator))
        {
            await roleManager.CreateAsync(new IdentityRole(AppRoles.Administrator));
        }

        var configuration = provider.GetRequiredService<IConfiguration>();
        var email = configuration["AdminUser:Email"];
        var password = configuration["AdminUser:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("AdminUser is not configured, so no administrator account was created.");
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = configuration["AdminUser:DisplayName"] ?? "Администратор",
            };

            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Could not create the administrator account: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.Administrator))
        {
            await userManager.AddToRoleAsync(admin, AppRoles.Administrator);
        }
    }

    private sealed class LookupSeed
    {
        public List<DistrictSeed> Districts { get; set; } = [];

        public List<string> PropertyTypes { get; set; } = [];

        public List<string> Features { get; set; } = [];
    }

    private sealed class DistrictSeed
    {
        public string City { get; set; } = "София";

        public string Name { get; set; } = string.Empty;
    }
}
