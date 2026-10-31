using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DietFlow.Web.Services;

public class StorageOptions
{
    public string Provider { get; set; } = "Local";
    public string? AzureBlobConnectionString { get; set; }
    public string AzureBlobContainer { get; set; } = "uploads";
}

public class AzureBlobFileStorage(BlobContainerClient container, LocalFileStorage local, ILogger<AzureBlobFileStorage> logger) : IFileStorage
{
    public async Task<string> SaveImageAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var ext = await ImageValidator.DetectExtensionAsync(file)
                  ?? throw new InvalidOperationException("Файл не є підтримуваним зображенням");

        var blob = container.GetBlobClient($"{folder}/{Guid.NewGuid():N}{ext}");
        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" },
                CacheControl = "public, max-age=31536000, immutable",
            },
        }, ct);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string? publicPath)
    {
        if (BlobName(publicPath) is not { } name)
        {
            await local.DeleteAsync(publicPath);
            return;
        }
        try
        {
            await container.DeleteBlobIfExistsAsync(name);
        }
        catch (Azure.RequestFailedException ex)
        {
            logger.LogWarning(ex, "Не вдалося видалити blob {Name}", name);
        }
    }

    public async Task<byte[]?> ReadAsync(string? publicPath, CancellationToken ct = default)
    {
        if (BlobName(publicPath) is not { } name) return await local.ReadAsync(publicPath, ct);
        try
        {
            var content = await container.GetBlobClient(name).DownloadContentAsync(ct);
            return content.Value.Content.ToArray();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private string? BlobName(string? path)
    {
        var prefix = container.Uri.ToString().TrimEnd('/') + "/";
        return path is not null && path.StartsWith(prefix, StringComparison.Ordinal) ? Uri.UnescapeDataString(path[prefix.Length..]) : null;
    }
}
