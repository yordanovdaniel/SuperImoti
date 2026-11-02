using System.Globalization;
using System.Text;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Services;

/// <summary>One row of the CSV export.</summary>
public sealed record ListingCsvRow(
    int Id,
    string? ExternalId,
    string Title,
    DealKind DealKind,
    string PropertyType,
    string District,
    string? Street,
    decimal? Price,
    Currency Currency,
    decimal? PricePerSqm,
    decimal Area,
    int? Floor,
    int? TotalFloors,
    string? ConstructionType,
    string? CompletionPeriod,
    string? Seller,
    ListingStatus Status,
    DateTime CreatedUtc,
    string? SourceUrl);

/// <summary>Writes listings as CSV (RFC 4180, UTF-8 with BOM so Excel shows Cyrillic correctly).</summary>
public static class ListingCsvExporter
{
    private static readonly string[] Header =
    [
        "Id", "imot.bg id", "Заглавие", "Сделка", "Вид имот", "Квартал", "Улица", "Цена", "Валута",
        "Цена на м²", "Площ (м²)", "Етаж", "Етажност", "Строителство", "Година", "Агенция", "Статус",
        "Добавена", "Линк в SuperImoti", "Линк в imot.bg",
    ];

    public static async Task WriteAsync(
        Stream output, IAsyncEnumerable<ListingCsvRow> rows, Func<int, string> detailsUrl, CancellationToken ct)
    {
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), ct);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(string.Join(',', Header.Select(Escape)));

        await foreach (var row in rows.WithCancellation(ct))
        {
            string?[] cells =
            [
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.ExternalId,
                row.Title,
                row.DealKind.GetDisplayName(),
                row.PropertyType,
                row.District,
                row.Street,
                Number(row.Price),
                row.Price is null ? null : row.Currency.ToString(),
                Number(row.PricePerSqm),
                Number(row.Area),
                row.Floor?.ToString(CultureInfo.InvariantCulture),
                row.TotalFloors?.ToString(CultureInfo.InvariantCulture),
                row.ConstructionType,
                row.CompletionPeriod,
                row.Seller,
                row.Status.GetDisplayName(),
                Fmt.Date(row.CreatedUtc),
                detailsUrl(row.Id),
                row.SourceUrl,
            ];

            await writer.WriteLineAsync(string.Join(',', cells.Select(Escape)));
        }

        await writer.FlushAsync(ct);
    }

    /// <summary>Quotes a cell when needed and stops spreadsheets from running text as a formula.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@'
            && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string? Number(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);
}
