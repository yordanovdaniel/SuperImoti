namespace SuperImoti.Web.Data.Import;

/// <summary>What one import run changed.</summary>
public sealed class ImportResult
{
    public int ListingsAdded { get; set; }

    public int ListingsUpdated { get; set; }

    public int ListingsUnchanged { get; set; }

    public int ListingsSkipped { get; set; }

    public int PhotosAdded { get; set; }

    /// <summary>Photos marked as downloaded in the scraper database whose file is not in the images folder.</summary>
    public int PhotosWithoutFile { get; set; }

    public int EventsAdded { get; set; }

    public int DistrictsAdded { get; set; }

    public int PropertyTypesAdded { get; set; }

    public int FeaturesAdded { get; set; }

    public int SellersAdded { get; set; }

    public TimeSpan Duration { get; set; }

    public override string ToString() =>
        $"added {ListingsAdded}, updated {ListingsUpdated}, unchanged {ListingsUnchanged}, skipped {ListingsSkipped}, " +
        $"photos {PhotosAdded} (missing files {PhotosWithoutFile}), events {EventsAdded}, sellers {SellersAdded} " +
        $"in {Duration.TotalSeconds:F1}s";
}
