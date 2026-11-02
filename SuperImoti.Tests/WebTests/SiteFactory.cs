using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace SuperImoti.Tests.WebTests;

/// <summary>Starts the whole site in memory with its own temporary SQLite database and mail folder.</summary>
public sealed class SiteFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";

    /// <summary>A fresh random password for each test run, so no password is stored in the repository.</summary>
    public string AdminPassword { get; init; } = $"Adm-{Guid.NewGuid():N}-7";

    /// <summary>Two factories can share one database to test what happens when the site restarts.</summary>
    public string DatabasePath { get; init; } = Path.Combine(Path.GetTempPath(), $"superimoti-test-{Guid.NewGuid():N}.db");

    public bool DeleteDatabaseOnDispose { get; init; } = true;

    public string MailFolder { get; } = Path.Combine(Path.GetTempPath(), $"superimoti-mail-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath}");
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

        if (DeleteDatabaseOnDispose)
        {
            foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
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
        }

        if (Directory.Exists(MailFolder))
        {
            Directory.Delete(MailFolder, recursive: true);
        }
    }
}
