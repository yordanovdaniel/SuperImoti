using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SuperImoti.Tests.WebTests;

/// <summary>The administrator password follows the configuration (user secrets) every time the site starts.</summary>
public class AdminSeedTests
{
    [Fact]
    public async Task AdminPassword_IsUpdatedFromConfigurationOnRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"superimoti-test-{Guid.NewGuid():N}.db");
        var firstPassword = $"First-{Guid.NewGuid():N}-1";
        var secondPassword = $"Second-{Guid.NewGuid():N}-2";

        // The first start creates the administrator with the first password.
        using (var first = new SiteFactory
        {
            DatabasePath = databasePath,
            AdminPassword = firstPassword,
            DeleteDatabaseOnDispose = false,
        })
        {
            Assert.True(await CanLogInAsync(first, firstPassword));
        }

        // The password is changed in the configuration and the site starts again on the same database.
        using var second = new SiteFactory { DatabasePath = databasePath, AdminPassword = secondPassword };
        Assert.True(await CanLogInAsync(second, secondPassword));
        Assert.False(await CanLogInAsync(second, firstPassword));
    }

    private static async Task<bool> CanLogInAsync(SiteFactory factory, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SiteFactory.AdminEmail,
            ["Password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
        }));

        // A successful login redirects; a failed one shows the form again.
        return response.StatusCode == HttpStatusCode.Redirect;
    }
}
