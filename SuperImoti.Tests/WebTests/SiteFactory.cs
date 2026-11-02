using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace SuperImoti.Tests.WebTests;

/// <summary>Starts the whole site in memory with its own temporary SQLite database and mail folder.</summary>
public sealed class SiteFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";

    /// <summary>A fresh random password for each test run, so no password is stored in the repository.</summary>
    public string AdminPassword { get; } = $"Adm-{Guid.NewGuid():N}-7";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"superimoti-test-{Guid.NewGuid():N}.db");

    public string MailFolder { get; } = Path.Combine(Path.GetTempPath(), $"superimoti-mail-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_databasePath}");
        builder.UseSetting("ScraperData:SourceFolder", string.Empty);
        builder.UseSetting("ScraperData:SyncOnStartup", "false");
        builder.UseSetting("AdminUser:Email", AdminEmail);
        builder.UseSetting("AdminUser:Password", AdminPassword);
        builder.UseSetting("Email:SmtpHost", string.Empty);
        builder.UseSetting("Email:PickupDirectory", MailFolder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file still in use is left in the temp folder.
            }
        }

        if (Directory.Exists(MailFolder))
        {
            Directory.Delete(MailFolder, recursive: true);
        }
    }
}
