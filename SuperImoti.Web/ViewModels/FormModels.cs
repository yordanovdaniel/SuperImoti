using System.ComponentModel.DataAnnotations;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.ViewModels;

/// <summary>The fields of the "publish / edit listing" form, with validation rules.</summary>
public class ListingFormModel : IValidatableObject
{
    public const string PhonePattern = @"^[0-9+()\-\s,]{6,120}$";

    public int? Id { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    [Required(ErrorMessage = "Въведете заглавие.")]
    [StringLength(150, MinimumLength = 10, ErrorMessage = "Заглавието трябва да е между {2} и {1} символа.")]
    [Display(Name = "Заглавие")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Изберете вид сделка.")]
    [Display(Name = "Вид сделка")]
    public DealKind? DealKind { get; set; }

    [Required(ErrorMessage = "Изберете вид имот.")]
    [Display(Name = "Вид имот")]
    public int? PropertyTypeId { get; set; }

    [Required(ErrorMessage = "Изберете квартал.")]
    [Display(Name = "Квартал")]
    public int? DistrictId { get; set; }

    [StringLength(120, ErrorMessage = "Адресът може да е до {1} символа.")]
    [Display(Name = "Улица / адрес")]
    public string? Street { get; set; }

    [Range(typeof(decimal), "1", "100000000", ErrorMessage = "Цената трябва да е между {1} и {2}.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [Display(Name = "Цена")]
    public decimal? Price { get; set; }

    [Display(Name = "Валута")]
    public Currency Currency { get; set; } = Currency.EUR;

    [Display(Name = "Цена при запитване")]
    public bool PriceOnRequest { get; set; }

    [StringLength(60)]
    [Display(Name = "ДДС")]
    public string? VatNote { get; set; }

    [Required(ErrorMessage = "Въведете площ.")]
    [Range(typeof(decimal), "1", "1000000", ErrorMessage = "Площта трябва да е между {1} и {2} м².",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [Display(Name = "Площ (м²)")]
    public decimal? Area { get; set; }

    [Range(-5, 100, ErrorMessage = "Етажът трябва да е между {1} и {2}.")]
    [Display(Name = "Етаж")]
    public int? Floor { get; set; }

    [Range(1, 100, ErrorMessage = "Етажността трябва да е между {1} и {2}.")]
    [Display(Name = "Етажност на сградата")]
    public int? TotalFloors { get; set; }

    [StringLength(60, ErrorMessage = "Видът строителство може да е до {1} символа.")]
    [Display(Name = "Строителство")]
    public string? ConstructionType { get; set; }

    [StringLength(40, ErrorMessage = "Годината/периодът може да е до {1} символа.")]
    [Display(Name = "Година на строителство")]
    public string? CompletionPeriod { get; set; }

    [Required(ErrorMessage = "Въведете описание.")]
    [StringLength(5000, MinimumLength = 20, ErrorMessage = "Описанието трябва да е между {2} и {1} символа.")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [RegularExpression(PhonePattern, ErrorMessage = "Въведете валиден телефонен номер.")]
    [Display(Name = "Телефон за контакт")]
    public string? ContactPhone { get; set; }

    [Display(Name = "Особености")]
    public List<int> FeatureIds { get; set; } = [];

    [ImageFiles(MaxFiles = 10)]
    [Display(Name = "Добави снимки")]
    public List<IFormFile> NewPhotos { get; set; } = [];

    /// <summary>Photos ticked for removal on the edit page.</summary>
    public List<int> RemovePhotoIds { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PriceOnRequest && Price is null)
        {
            yield return new ValidationResult(
                "Въведете цена или отбележете „Цена при запитване“.", [nameof(Price)]);
        }

        if (Floor is int floor && TotalFloors is int total && floor > total)
        {
            yield return new ValidationResult(
                "Етажът не може да е по-висок от етажността на сградата.", [nameof(Floor)]);
        }
    }
}

public class CommentFormModel
{
    public int ListingId { get; set; }

    [Required(ErrorMessage = "Напишете коментар.")]
    [StringLength(1000, MinimumLength = 2, ErrorMessage = "Коментарът трябва да е между {2} и {1} символа.")]
    [Display(Name = "Коментар")]
    public string? Text { get; set; }
}

public class DistrictFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Въведете име на квартала.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Името трябва да е между {2} и {1} символа.")]
    [Display(Name = "Име на квартала")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Въведете град.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Градът трябва да е между {2} и {1} символа.")]
    [Display(Name = "Град")]
    public string? City { get; set; } = "София";
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Въведете име.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Името трябва да е между {2} и {1} символа.")]
    [Display(Name = "Име")]
    public string? DisplayName { get; set; }

    [Required(ErrorMessage = "Въведете имейл.")]
    [EmailAddress(ErrorMessage = "Невалиден имейл адрес.")]
    [StringLength(256)]
    [Display(Name = "Имейл")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Въведете парола.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Паролата трябва да е между {2} и {1} символа.")]
    [DataType(DataType.Password)]
    [Display(Name = "Парола")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "Повторете паролата.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Паролите не съвпадат.")]
    [Display(Name = "Повторете паролата")]
    public string? ConfirmPassword { get; set; }

    [Display(Name = "Искам имейл при всяка нова обява")]
    public bool NotifyAboutNewListings { get; set; }
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Въведете имейл.")]
    [EmailAddress(ErrorMessage = "Невалиден имейл адрес.")]
    [Display(Name = "Имейл")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Въведете парола.")]
    [DataType(DataType.Password)]
    [Display(Name = "Парола")]
    public string? Password { get; set; }

    [Display(Name = "Запомни ме")]
    public bool RememberMe { get; set; }
}

public class ProfileViewModel
{
    [Required(ErrorMessage = "Въведете име.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Името трябва да е между {2} и {1} символа.")]
    [Display(Name = "Име")]
    public string? DisplayName { get; set; }

    [RegularExpression(ListingFormModel.PhonePattern, ErrorMessage = "Въведете валиден телефонен номер.")]
    [StringLength(30, ErrorMessage = "Телефонът може да е до {1} символа.")]
    [Display(Name = "Телефон")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Искам имейл при всяка нова обява")]
    public bool NotifyAboutNewListings { get; set; }
}
