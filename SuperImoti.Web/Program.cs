using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using SuperImoti.Web.Data;
using SuperImoti.Web.Data.Import;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;

// ---------- Database: SQLite through Entity Framework Core ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options
        .UseSqlite(SqlitePaths.ResolveAgainst(connectionString, contentRoot))
        .AddInterceptors(new SqlitePerformanceInterceptor()));

// ---------- Users: ASP.NET Core Identity ----------
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddErrorDescriber<BulgarianIdentityErrorDescriber>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// ---------- MVC ----------
builder.Services.AddControllersWithViews(options =>
{
    // Every POST form must carry an anti-forgery token.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

    // Only fields marked [Required] are required (otherwise e.g. the optional photo upload would be).
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

    // Bulgarian messages for values that cannot be read at all (e.g. letters typed into a number field).
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"Стойността „{value}“ не е валидна за „{field}“.");
    messages.SetValueMustBeANumberAccessor(field => $"Полето „{field}“ трябва да е число.");
    messages.SetValueMustNotBeNullAccessor(field => $"Полето „{field}“ е задължително.");
    messages.SetValueIsInvalidAccessor(value => $"Стойността „{value}“ е невалидна.");
    messages.SetMissingBindRequiredValueAccessor(field => $"Липсва стойност за „{field}“.");
    messages.SetMissingKeyOrValueAccessor(() => "Липсва стойност.");
    messages.SetMissingRequestBodyRequiredValueAccessor(() => "Липсва съдържание на заявката.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"Стойността „{value}“ не е валидна.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Стойността не е валидна.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "Стойността трябва да е число.");
    messages.SetUnknownValueIsInvalidAccessor(field => $"Стойността за „{field}“ не е валидна.");
});
builder.Services.AddMemoryCache();

// Write Cyrillic text as-is instead of "&#x41D;…" entities (HTML-sensitive characters are still escaped).
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// ---------- Application services ----------
builder.Services.Configure<ScraperDataOptions>(builder.Configuration.GetSection(ScraperDataOptions.SectionName));
builder.Services.PostConfigure<ScraperDataOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.SourceFolder) && !Path.IsPathRooted(options.SourceFolder))
    {
        options.SourceFolder = Path.GetFullPath(Path.Combine(contentRoot, options.SourceFolder));
    }
});

builder.Services.AddScoped<ListingQueryService>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<ScraperImporter>();
builder.Services.AddSingleton<PhotoUrlResolver>();
builder.Services.AddSingleton<PhotoStorage>();
builder.Services.AddSingleton<ImportCoordinator>();
builder.Services.AddHostedService<ImportBackgroundService>();

var app = builder.Build();

// Create or upgrade the database, then seed lookup data, roles and the admin account.
await DbInitializer.InitializeAsync(app.Services);

// ---------- HTTP pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Friendly pages for 404 (missing listing), 403 (no access), 400…
app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");
app.UseHttpsRedirection();

// Forms and URLs use "." as the decimal separator; the views format numbers the Bulgarian way explicitly.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = [CultureInfo.InvariantCulture],
});

// Photos downloaded by the scraper are served straight from its folder.
// The files are content-addressed (named by their hash), so browsers may cache them forever.
var scraperData = app.Services.GetRequiredService<IOptions<ScraperDataOptions>>().Value;
if (scraperData.ImagesExist)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(scraperData.ImagesPath!),
        RequestPath = PhotoUrlResolver.ScrapedRequestPath,
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
    });
}

// Photos uploaded by users (App_Data/uploads).
var uploadsRoot = PhotoStorage.GetUploadsRoot(app.Environment.ContentRootPath);
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = PhotoUrlResolver.UploadsRequestPath,
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
