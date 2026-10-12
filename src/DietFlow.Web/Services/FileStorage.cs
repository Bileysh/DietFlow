namespace DietFlow.Web.Services;


public interface IFileStorage
{
    Task<string> SaveImageAsync(IFormFile file, string folder, CancellationToken ct = default);

    Task DeleteAsync(string? publicPath);
}

public class LocalFileStorage(IWebHostEnvironment env, ILogger<LocalFileStorage> logger) : IFileStorage
{
    private const string UploadsRoot = "uploads";

    public async Task<string> SaveImageAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var ext = await ImageValidator.DetectExtensionAsync(file)
                  ?? throw new InvalidOperationException("Файл не є підтримуваним зображенням");

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var dir = Path.Combine(env.WebRootPath, UploadsRoot, folder);
        Directory.CreateDirectory(dir);

        await using (var stream = File.Create(Path.Combine(dir, fileName)))
            await file.CopyToAsync(stream, ct);

        return $"/{UploadsRoot}/{folder}/{fileName}";
    }

    public Task DeleteAsync(string? publicPath)
    {
        if (string.IsNullOrEmpty(publicPath) || !publicPath.StartsWith($"/{UploadsRoot}/", StringComparison.Ordinal))
            return Task.CompletedTask;

        var fullPath = Path.GetFullPath(Path.Combine(env.WebRootPath, publicPath.TrimStart('/')));
        var uploadsDir = Path.GetFullPath(Path.Combine(env.WebRootPath, UploadsRoot));

        if (!fullPath.StartsWith(uploadsDir, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Відхилено спробу видалити файл поза uploads: {Path}", publicPath);
            return Task.CompletedTask;
        }

        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}

public static class ImageValidator
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static async Task<string?> DetectExtensionAsync(IFormFile file)
    {
        var header = new byte[12];
        await using var s = file.OpenReadStream();
        var read = await s.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (read < 12) return null;

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
        if (header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    public static async Task<string?> ValidateAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > MaxBytes) return "Фото має бути не більше 5 МБ";
        return await DetectExtensionAsync(file) is null ? "Підтримуються лише JPEG, PNG або WebP" : null;
    }
}
