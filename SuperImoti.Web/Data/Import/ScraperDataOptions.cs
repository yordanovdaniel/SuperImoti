namespace SuperImoti.Web.Data.Import;

/// <summary>Where the imot.bg scraper keeps its data. Bound from the "ScraperData" configuration section.</summary>
public class ScraperDataOptions
{
    public const string SectionName = "ScraperData";

    /// <summary>The scraper's "imot.bg" folder, which contains imot_imot.db and the images/ folder.</summary>
    public string? SourceFolder { get; set; }

    public string DatabaseFileName { get; set; } = "imot_imot.db";

    public string ImagesFolderName { get; set; } = "images";

    /// <summary>Synchronise with the scraper database every time the site starts (runs in the background).</summary>
    public bool SyncOnStartup { get; set; } = true;

    public string? DatabasePath =>
        string.IsNullOrWhiteSpace(SourceFolder) ? null : Path.Combine(SourceFolder, DatabaseFileName);

    public string? ImagesPath =>
        string.IsNullOrWhiteSpace(SourceFolder) ? null : Path.Combine(SourceFolder, ImagesFolderName);

    public bool DatabaseExists => DatabasePath is not null && File.Exists(DatabasePath);

    public bool ImagesExist => ImagesPath is not null && Directory.Exists(ImagesPath);
}
