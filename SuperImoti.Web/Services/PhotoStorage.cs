namespace SuperImoti.Web.Services;

/// <summary>Saves and deletes photos uploaded by users (stored in App_Data/uploads, outside wwwroot).</summary>
public class PhotoStorage(IWebHostEnvironment env, ILogger<PhotoStorage> logger)
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    public const int MaxPhotosPerListing = 30;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public string UploadsRoot => GetUploadsRoot(env.ContentRootPath);

    public static string GetUploadsRoot(string contentRootPath) => Path.Combine(contentRootPath, "App_Data", "uploads");

    public static bool HasAllowedExtension(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>Looks at the first bytes of the file, so a renamed non-image file is rejected.</summary>
    public static async Task<bool> IsImageAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 4)
        {
            return false;
        }

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        var isWebp = read >= 12
            && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';

        return isJpeg || isPng || isWebp;
    }

    /// <summary>Stores the file under a new random name and returns that name.</summary>
    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct)
    {
        Directory.CreateDirectory(UploadsRoot);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension == ".jpeg")
        {
            extension = ".jpg";
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var output = File.Create(Path.Combine(UploadsRoot, fileName));
        await file.CopyToAsync(output, ct);
        return fileName;
    }

    public void Delete(IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames)
        {
            try
            {
                var path = Path.Combine(UploadsRoot, Path.GetFileName(name));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete uploaded photo {FileName}.", name);
            }
        }
    }
}
