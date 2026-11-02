using System.ComponentModel.DataAnnotations;

namespace SuperImoti.Web.Models;

public enum DealKind
{
    [Display(Name = "Продажба")]
    Sale = 0,

    [Display(Name = "Наем")]
    Rent = 1,
}

public enum ListingStatus
{
    [Display(Name = "Активна")]
    Active = 0,

    [Display(Name = "Неактивна")]
    Inactive = 1,
}

public enum Currency
{
    [Display(Name = "EUR (€)")]
    EUR = 0,

    [Display(Name = "USD ($)")]
    USD = 1,
}

public enum ListingEventKind
{
    [Display(Name = "Открита в imot.bg")]
    Discovered = 0,

    [Display(Name = "Промяна на цената")]
    PriceChange = 1,

    [Display(Name = "Промяна на статуса")]
    StatusChange = 2,

    [Display(Name = "Публикувана")]
    Published = 3,

    [Display(Name = "Редактирана")]
    Edited = 4,
}

public enum PropertyCategory
{
    [Display(Name = "Апартаменти")]
    Apartment = 0,

    [Display(Name = "Къщи и вили")]
    House = 1,

    [Display(Name = "Парцели и земя")]
    Land = 2,

    [Display(Name = "Бизнес имоти")]
    Commercial = 3,

    [Display(Name = "Гаражи и паркоместа")]
    Parking = 4,

    [Display(Name = "Други")]
    Other = 5,
}
