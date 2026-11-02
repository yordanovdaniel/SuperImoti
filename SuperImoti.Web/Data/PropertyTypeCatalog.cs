using SuperImoti.Web.Models;

namespace SuperImoti.Web.Data;

/// <summary>Turns imot.bg property types ("2-СТАЕН") into display names and groups them into categories.</summary>
public static class PropertyTypeCatalog
{
    // Listed in the order they should appear in drop-downs.
    private static readonly (string Name, PropertyCategory Category)[] Known =
    [
        ("1-стаен", PropertyCategory.Apartment),
        ("2-стаен", PropertyCategory.Apartment),
        ("3-стаен", PropertyCategory.Apartment),
        ("4-стаен", PropertyCategory.Apartment),
        ("Многостаен", PropertyCategory.Apartment),
        ("Мезонет", PropertyCategory.Apartment),
        ("Ателие, таван", PropertyCategory.Apartment),
        ("Стая", PropertyCategory.Apartment),

        ("Къща", PropertyCategory.House),
        ("Етаж от къща", PropertyCategory.House),
        ("Вила", PropertyCategory.House),
        ("Вилно селище", PropertyCategory.House),
        ("Самостоятелна сграда", PropertyCategory.House),

        ("Парцел", PropertyCategory.Land),
        ("Място", PropertyCategory.Land),
        ("Земеделска земя", PropertyCategory.Land),
        ("Ферма", PropertyCategory.Land),
        ("Соларен парк", PropertyCategory.Land),

        ("Офис", PropertyCategory.Commercial),
        ("Магазин", PropertyCategory.Commercial),
        ("Заведение", PropertyCategory.Commercial),
        ("Склад", PropertyCategory.Commercial),
        ("Хладилен склад", PropertyCategory.Commercial),
        ("Пром. помещение", PropertyCategory.Commercial),
        ("Фабрика/завод", PropertyCategory.Commercial),
        ("Хотел", PropertyCategory.Commercial),
        ("Търговски комплекс", PropertyCategory.Commercial),
        ("Клиника", PropertyCategory.Commercial),
        ("Лекарски кабинет", PropertyCategory.Commercial),
        ("Стоматологичен кабинет", PropertyCategory.Commercial),
        ("Аптека", PropertyCategory.Commercial),
        ("Банков офис", PropertyCategory.Commercial),
        ("Фитнес зала", PropertyCategory.Commercial),
        ("Фризьорски/козметичен салон", PropertyCategory.Commercial),
        ("Спа/соларно студио", PropertyCategory.Commercial),
        ("Фотографско студио", PropertyCategory.Commercial),
        ("Детски център", PropertyCategory.Commercial),
        ("Дом за възрастни хора", PropertyCategory.Commercial),
        ("Автосервиз", PropertyCategory.Commercial),
        ("Автомивка", PropertyCategory.Commercial),
        ("Бензиностанция/газстанция", PropertyCategory.Commercial),
        ("Аквапарк", PropertyCategory.Commercial),

        ("Гараж", PropertyCategory.Parking),
        ("Паркомясто", PropertyCategory.Parking),
        ("Гараж, паркомясто", PropertyCategory.Parking),
        ("Паркинг", PropertyCategory.Parking),
    ];

    /// <summary>"ПРОМ. ПОМЕЩЕНИЕ" → "Пром. помещение".</summary>
    public static string ToDisplayName(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var lower = trimmed.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    public static (PropertyCategory Category, int SortOrder) Classify(string displayName)
    {
        for (var i = 0; i < Known.Length; i++)
        {
            if (string.Equals(Known[i].Name, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return (Known[i].Category, i + 1);
            }
        }

        return (PropertyCategory.Other, 1000);
    }
}
