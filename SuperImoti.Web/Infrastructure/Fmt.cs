using System.Globalization;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Infrastructure;

/// <summary>Formats numbers and dates the Bulgarian way ("135 000 €", "18.09.2026") for the views.</summary>
public static class Fmt
{
    public static readonly CultureInfo Bg = CultureInfo.GetCultureInfo("bg-BG");

    private static readonly TimeZoneInfo Sofia = FindSofiaTimeZone();

    public static string Money(decimal? amount, Currency currency = Currency.EUR)
    {
        if (amount is not decimal value)
        {
            return "При запитване";
        }

        var number = value.ToString(value % 1 == 0 ? "N0" : "N2", Bg);
        return currency == Currency.USD ? $"{number} $" : $"{number} €";
    }

    public static string PerSqm(decimal? amount, Currency currency = Currency.EUR) =>
        amount is null ? string.Empty : $"{Money(amount, currency)}/м²";

    public static string Area(decimal area) =>
        $"{area.ToString(area % 1 == 0 ? "N0" : "N1", Bg)} м²";

    public static string Number(int value) => value.ToString("N0", Bg);

    public static string Number(decimal? value, int decimals = 0) =>
        value is decimal d ? d.ToString("N" + decimals, Bg) : "—";

    public static string Floor(int? floor, int? totalFloors)
    {
        if (floor is not int f)
        {
            return totalFloors is int t ? $"{t} етажа" : "—";
        }

        var text = f switch
        {
            0 => "Партер",
            < 0 => "Сутерен",
            _ => $"Етаж {f}",
        };

        return totalFloors is int total ? $"{text} от {total}" : text;
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sofia);

    public static string Date(DateTime? utc) => utc is DateTime d ? ToLocal(d).ToString("dd.MM.yyyy", Bg) : "—";

    public static string DateAndTime(DateTime? utc) =>
        utc is DateTime d ? ToLocal(d).ToString("dd.MM.yyyy HH:mm", Bg) : "—";

    /// <summary>"днес", "вчера", "преди 5 дни", otherwise the date.</summary>
    public static string Ago(DateTime utc)
    {
        var days = (int)(DateTime.UtcNow - DateTime.SpecifyKind(utc, DateTimeKind.Utc)).TotalDays;
        return days switch
        {
            <= 0 => "днес",
            1 => "вчера",
            < 30 => $"преди {days} дни",
            _ => Date(utc),
        };
    }

    private static TimeZoneInfo FindSofiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
