using Microsoft.Extensions.Options;

namespace SuperImoti.Web.Data.Import;

/// <summary>
/// Runs scraper imports in the background so the site starts immediately.
/// One import runs on startup (if enabled); admins can start more from the admin page.
/// </summary>
public sealed class ImportBackgroundService(
    ImportCoordinator coordinator,
    IServiceScopeFactory scopeFactory,
    IOptions<ScraperDataOptions> options,
    ILogger<ImportBackgroundService> logger) : BackgroundService
{
    // Task.Run keeps the first (synchronous) part of the work off the startup path.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.SyncOnStartup && settings.DatabaseExists)
        {
            coordinator.TryRequestRun();
        }
        else if (!settings.DatabaseExists)
        {
            logger.LogWarning(
                "Scraper database not found at {Path}. Set ScraperData:SourceFolder to import listings.",
                settings.DatabasePath ?? "(not configured)");
        }

        try
        {
            await foreach (var _ in coordinator.Requests.ReadAllAsync(stoppingToken))
            {
                await RunImportAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The site is shutting down.
        }
    }

    private async Task RunImportAsync(CancellationToken stoppingToken)
    {
        coordinator.MarkStarted();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<ScraperImporter>();
            var result = await importer.RunAsync(coordinator.Report, stoppingToken);

            coordinator.MarkCompleted(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            coordinator.MarkFailed("Импортът беше прекъснат при спиране на сайта.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scraper import failed.");
            coordinator.MarkFailed(ex.Message);
        }
    }
}
