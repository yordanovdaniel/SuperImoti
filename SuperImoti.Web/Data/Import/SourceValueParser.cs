using System.Globalization;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Data.Import;

/// <summary>Small helpers that clean up values coming from the scraper database.</summary>
public static class SourceValueParser
{
    /// <summary>Scraper timestamps are UTC text: "YYYY-MM-DD HH:MM:SS".</summary>
    public static DateTime? ParseUtc(string? value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// A few imot.bg prices are labelled "лв." although the raw text shows euros ("511.29 €"),
    /// so only "$" is treated as a different currency.
    /// </summary>
    public static Currency ParseCurrency(string? value) =>
        value?.Trim() == "$" ? Currency.USD : Currency.EUR;

    /// <summary>"град София" → "София".</summary>
    public static string CleanCity(string? city)
    {
        var trimmed = city?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "София";
        }

        return trimmed.StartsWith("град ", StringComparison.Ordinal) ? trimmed[5..].Trim() : trimmed;
    }

    /// <summary>imot.bg has no titles, so one is built: "2-стаен за продажба, Надежда 3".</summary>
    public static string BuildTitle(string propertyType, DealKind deal, string district)
    {
        var dealText = deal == DealKind.Rent ? "под наем" : "за продажба";
        var title = $"{propertyType} {dealText}, {district}";
        return title.Length <= 150 ? title : title[..150];
    }

    /// <summary>Reads the old price from a history note such as "830 -> 790".</summary>
    public static decimal? ParseOldPrice(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var arrow = note.IndexOf("->", StringComparison.Ordinal);
        if (arrow <= 0)
        {
            return null;
        }

        return decimal.TryParse(note[..arrow].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Normalises a phone list to "0888123456, 0899123456". The scraper sometimes glued two
    /// 10-digit numbers together ("08984692670886416259"); those are split again.
    /// </summary>
    public static string? CleanPhones(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var phones = new List<string>();
        foreach (var token in raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length == 20 && token.All(char.IsAsciiDigit) && token[0] == '0' && token[10] == '0')
            {
                phones.Add(token[..10]);
                phones.Add(token[10..]);
            }
            else
            {
                phones.Add(token);
            }
        }

        return phones.Count == 0 ? null : string.Join(", ", phones.Distinct());
    }

    public static bool IsContentHash(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}
