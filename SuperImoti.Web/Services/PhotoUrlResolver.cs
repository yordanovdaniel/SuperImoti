using Microsoft.Extensions.Options;
using SuperImoti.Web.Data.Import;

namespace SuperImoti.Web.Services;

/// <summary>Turns a stored photo (scraped hash or uploaded file name) into the URL the browser loads.</summary>
public class PhotoUrlResolver(IOptions<ScraperDataOptions> options)
{
    /// <summary>The scraper's images folder is served under this path (see Program.cs).</summary>
    public const string ScrapedRequestPath = "/photos";

    /// <summary>Photos uploaded on this site (App_Data/uploads) are served under this path.</summary>
    public const string UploadsRequestPath = "/uploads";

    public const string Placeholder = "/img/placeholder.svg";

    private readonly bool _scrapedImagesAvailable = options.Value.ImagesExist;

    public string Resolve(string? contentHash, string? fileName)
    {
        if (fileName is not null)
        {
            return $"{UploadsRequestPath}/{Uri.EscapeDataString(fileName)}";
        }

        // The scraper stores files content-addressed: images/<hash[0:2]>/<hash[2:4]>/<hash>.jpg
        if (contentHash is { Length: 64 } && _scrapedImagesAvailable)
        {
            return $"{ScrapedRequestPath}/{contentHash[..2]}/{contentHash[2..4]}/{contentHash}.jpg";
        }

        return Placeholder;
    }
}
