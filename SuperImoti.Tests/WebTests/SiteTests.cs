using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SuperImoti.Web.Data;
using SuperImoti.Web.Services;

namespace SuperImoti.Tests.WebTests;

/// <summary>End-to-end tests: real HTTP requests through the whole MVC pipeline.</summary>
public class SiteTests(SiteFactory factory) : IClassFixture<SiteFactory>
{
    // A valid 1×1 PNG, used to test photo uploads.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("/")]
    [InlineData("/Listings")]
    [InlineData("/Listings?deal=Rent&q=лозенец&sort=price-asc")]
    [InlineData("/Districts")]
    [InlineData("/Sellers")]
    [InlineData("/Stats")]
    [InlineData("/Home/About")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task PublicPages_Load(string url)
    {
        var response = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SuperImoti", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Listings/Details/987654")]
    [InlineData("/Listings/Details/abc")]
    [InlineData("/Districts/Details/987654")]
    [InlineData("/no/such/page")]
    public async Task MissingPages_ShowTheFriendlyNotFoundPage(string url)
    {
        var response = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Страницата не е намерена", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Listings/Create")]
    [InlineData("/Favorites")]
    [InlineData("/Admin")]
    public async Task ProtectedPages_RedirectToLogin(string url)
    {
        var response = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task InvalidFilters_AreReportedInsteadOfFailing()
    {
        // Values that cannot be read are reported…
        var unreadable = await NewClient().GetStringAsync("/Listings?minPrice=abc");
        Assert.Contains("Стойността „abc“ не е валидна за „Цена от“.", Text(unreadable));

        // …and so are values that do not make sense together.
        var contradictory = await NewClient().GetStringAsync("/Listings?minArea=90&maxArea=10");
        Assert.Contains("Минималната площ е по-голяма от максималната.", Text(contradictory));
    }

    [Fact]
    public async Task StatsApi_ReturnsJson()
    {
        var response = await NewClient().GetAsync("/Stats/Districts?deal=Sale&category=Apartment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CsvExport_StartsWithBomAndHeader()
    {
        var response = await NewClient().GetAsync("/Listings/Export?deal=Sale");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("Id,imot.bg id,Заглавие", System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Register_RejectsInvalidData()
    {
        var client = NewClient();
        var response = await PostFormAsync(client, "/Account/Register", "/Account/Register", new()
        {
            ["DisplayName"] = "Б",
            ["Email"] = "not-an-email",
            ["Password"] = "abc",
            ["ConfirmPassword"] = "xyz",
        });

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Невалиден имейл адрес.", html);
        Assert.Contains("Паролите не съвпадат.", html);
    }

    [Fact]
    public async Task Listing_FullLifecycle_CreateEditCommentFavoriteDelete()
    {
        // A second user subscribed to e-mails about new listings.
        var subscriber = NewClient();
        await RegisterAsync(subscriber, "subscriber", notify: true);

        var owner = NewClient();
        await RegisterAsync(owner, "owner");
        var (districtId, typeId) = await FirstLookupsAsync();
        var marker = "Тестмаркер" + Guid.NewGuid().ToString("N")[..6];

        // Photos are optional, so the browser must not demand one.
        var createPage = await owner.GetStringAsync("/Listings/Create");
        var photoInput = Regex.Match(createPage, "<input[^>]*name=\"Input.NewPhotos\"[^>]*>").Value;
        Assert.NotEmpty(photoInput);
        Assert.DoesNotContain("data-val-required", photoInput);

        // Invalid form: stays on the page with Bulgarian validation messages.
        var invalid = await PostListingAsync(owner, "/Listings/Create", "/Listings/Create", new()
        {
            ["Input.Title"] = "",
            ["Input.DealKind"] = "0",
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Въведете заглавие.", await invalid.Content.ReadAsStringAsync());

        // Create with a photo.
        var created = await PostListingAsync(owner, "/Listings/Create", "/Listings/Create", new()
        {
            ["Input.Title"] = $"Светъл апартамент {marker}",
            ["Input.DealKind"] = "0",
            ["Input.PropertyTypeId"] = typeId.ToString(),
            ["Input.DistrictId"] = districtId.ToString(),
            ["Input.Price"] = "150000",
            ["Input.Currency"] = "0",
            ["Input.Area"] = "75.5",
            ["Input.Floor"] = "3",
            ["Input.TotalFloors"] = "6",
            ["Input.Description"] = "Тухлен апартамент с южно изложение и гледка към Витоша.",
            ["Input.ContactPhone"] = "0888 123 456",
        }, withPhoto: true);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var detailsUrl = created.Headers.Location!.OriginalString;
        var listingId = int.Parse(detailsUrl.Split('/')[^1]);

        var details = await owner.GetStringAsync(detailsUrl);
        Assert.Contains(marker, details);
        Assert.Contains("150 000 €", Text(details));
        var photoUrl = Regex.Match(details, "src=\"(/uploads/[^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(photoUrl));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(photoUrl)).StatusCode);

        // The full-text index picks up the new listing.
        Assert.Contains(marker, await owner.GetStringAsync($"/Listings?q={marker.ToLowerInvariant()}"));

        // The subscriber gets an e-mail (written to the pickup folder by the background sender).
        Assert.True(await WaitForEmailAsync(), "Expected an e-mail about the new listing.");

        // Edit: change the price.
        var editPage = await owner.GetStringAsync($"/Listings/Edit/{listingId}");
        var oldStamp = HiddenValue(editPage, "Input.ConcurrencyStamp");
        var editFields = new Dictionary<string, string>
        {
            ["Input.Id"] = listingId.ToString(),
            ["Input.ConcurrencyStamp"] = oldStamp,
            ["Input.Title"] = $"Светъл апартамент {marker}",
            ["Input.DealKind"] = "0",
            ["Input.PropertyTypeId"] = typeId.ToString(),
            ["Input.DistrictId"] = districtId.ToString(),
            ["Input.Price"] = "139000",
            ["Input.Currency"] = "0",
            ["Input.Area"] = "75.5",
            ["Input.Description"] = "Тухлен апартамент с южно изложение и гледка към Витоша.",
        };
        var edited = await PostListingAsync(owner, $"/Listings/Edit/{listingId}", $"/Listings/Edit/{listingId}", editFields);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var afterEdit = Text(await owner.GetStringAsync(detailsUrl));
        Assert.Contains("139 000 €", afterEdit);
        Assert.Contains("Цената е намалена", afterEdit);

        // Saving again with the old version stamp is a concurrent edit and must be refused.
        var stale = await PostListingAsync(owner, $"/Listings/Edit/{listingId}", $"/Listings/Edit/{listingId}", editFields);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Някой друг е променил обявата", await stale.Content.ReadAsStringAsync());

        // Another user can see the listing but cannot edit it.
        var stranger = NewClient();
        await RegisterAsync(stranger, "stranger");
        var forbidden = await stranger.GetAsync($"/Listings/Edit/{listingId}");
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", forbidden.Headers.Location!.PathAndQuery);

        // Comment and favourite.
        var comment = await PostFormAsync(stranger, detailsUrl, "/Comments/Create", new()
        {
            ["NewComment.ListingId"] = listingId.ToString(),
            ["NewComment.Text"] = "Има ли паркомясто?",
        });
        Assert.Equal(HttpStatusCode.Redirect, comment.StatusCode);
        Assert.Contains("Има ли паркомясто?", await stranger.GetStringAsync(detailsUrl));

        var favorite = await PostFormAsync(stranger, detailsUrl, "/Favorites/Toggle", new()
        {
            ["listingId"] = listingId.ToString(),
        }, acceptJson: true);
        Assert.Equal("{\"isFavorite\":true}", await favorite.Content.ReadAsStringAsync());
        Assert.Contains(marker, await stranger.GetStringAsync("/Favorites"));

        // Delete: the listing, its comments and its uploaded photo are gone.
        var deleted = await PostFormAsync(owner, $"/Listings/Delete/{listingId}", $"/Listings/Delete/{listingId}", new());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(detailsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(photoUrl)).StatusCode);
        Assert.DoesNotContain(marker, await owner.GetStringAsync($"/Listings?q={marker.ToLowerInvariant()}"));
    }

    [Fact]
    public async Task Admin_CanManageDistricts_ButNotDeleteOnesWithListings()
    {
        var admin = NewClient();
        await PostFormAsync(admin, "/Account/Login", "/Account/Login", new()
        {
            ["Email"] = SiteFactory.AdminEmail,
            ["Password"] = factory.AdminPassword,
        });
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin")).StatusCode);

        var name = "Тестов квартал " + Guid.NewGuid().ToString("N")[..6];
        var created = await PostFormAsync(admin, "/Districts/Create", "/Districts/Create", new()
        {
            ["Name"] = name,
            ["City"] = "София",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = int.Parse(created.Headers.Location!.OriginalString.Split('/')[^1]);

        var duplicate = await PostFormAsync(admin, "/Districts/Create", "/Districts/Create", new()
        {
            ["Name"] = name,
            ["City"] = "София",
        });
        Assert.Contains("Вече има квартал с това име", await duplicate.Content.ReadAsStringAsync());

        var deleted = await PostFormAsync(admin, $"/Districts/Delete/{id}", $"/Districts/Delete/{id}", new());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/Districts/Details/{id}")).StatusCode);
    }

    // ---------- helpers ----------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task RegisterAsync(HttpClient client, string name, bool notify = false)
    {
        var password = $"pw-{Guid.NewGuid():N}-1";
        var fields = new Dictionary<string, string>
        {
            ["DisplayName"] = name,
            ["Email"] = $"{name}-{Guid.NewGuid():N}@test.local",
            ["Password"] = password,
            ["ConfirmPassword"] = password,
        };
        if (notify)
        {
            fields["NotifyAboutNewListings"] = "true";
        }

        var response = await PostFormAsync(client, "/Account/Register", "/Account/Register", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<(int DistrictId, int TypeId)> FirstLookupsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Districts.Select(d => d.Id).FirstAsync(), await db.PropertyTypes.Select(t => t.Id).FirstAsync());
    }

    /// <summary>Loads the page with the form (for its anti-forgery token) and posts the fields.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formPage, string action, Dictionary<string, string> fields, bool acceptJson = false)
    {
        var token = Token(await client.GetStringAsync(formPage));
        var content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)));
        var request = new HttpRequestMessage(HttpMethod.Post, action) { Content = content };
        if (acceptJson)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostListingAsync(
        HttpClient client, string formPage, string action, Dictionary<string, string> fields, bool withPhoto = false)
    {
        var token = Token(await client.GetStringAsync(formPage));
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in fields)
        {
            content.Add(new StringContent(value), key);
        }

        content.Add(new StringContent(token), "__RequestVerificationToken");
        if (withPhoto)
        {
            var photo = new ByteArrayContent(TinyPng);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(photo, "Input.NewPhotos", "test.png");
        }

        return await client.PostAsync(action, content);
    }

    private async Task<bool> WaitForEmailAsync()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Directory.Exists(factory.MailFolder) && Directory.EnumerateFiles(factory.MailFolder, "*.eml").Any())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static string Token(string html) => HiddenValue(html, "__RequestVerificationToken");

    /// <summary>Decodes HTML entities and turns the non-breaking spaces of Bulgarian number formatting into spaces.</summary>
    private static string Text(string html) =>
        WebUtility.HtmlDecode(html).Replace((char)0x00A0, ' ').Replace((char)0x202F, ' ');

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, $"Hidden field {name} was not found.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
