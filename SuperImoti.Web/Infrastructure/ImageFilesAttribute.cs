using System.ComponentModel.DataAnnotations;
using SuperImoti.Web.Services;

namespace SuperImoti.Web.Infrastructure;

/// <summary>Validates uploaded photos: how many, how big and which file types.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ImageFilesAttribute : ValidationAttribute
{
    public int MaxFiles { get; set; } = 10;

    public long MaxBytesPerFile { get; set; } = PhotoStorage.MaxFileBytes;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IEnumerable<IFormFile> files)
        {
            return ValidationResult.Success;
        }

        var list = files.Where(f => f.Length > 0).ToList();
        if (list.Count > MaxFiles)
        {
            return new ValidationResult($"Можете да качите най-много {MaxFiles} снимки наведнъж.");
        }

        foreach (var file in list)
        {
            if (!PhotoStorage.HasAllowedExtension(file.FileName))
            {
                return new ValidationResult($"Файлът „{file.FileName}“ не е снимка. Разрешени са JPG, PNG и WEBP.");
            }

            if (file.Length > MaxBytesPerFile)
            {
                return new ValidationResult($"Файлът „{file.FileName}“ е по-голям от {MaxBytesPerFile / 1024 / 1024} MB.");
            }
        }

        return ValidationResult.Success;
    }
}
