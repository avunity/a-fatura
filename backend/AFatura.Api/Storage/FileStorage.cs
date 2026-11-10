using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;

namespace AFatura.Api.Storage;

public class StorageOptions
{
    public string Root { get; set; } = "/data/files";
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>Stores documents on a Docker volume: {root}/{orgId}/{yyyy}/{MM}/{fileId}{ext}.</summary>
public class FileStorage(Microsoft.Extensions.Options.IOptions<StorageOptions> options, AppDbContext db)
{
    private readonly StorageOptions _o = options.Value;

    public static readonly HashSet<string> AllowedInvoiceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif", "image/tiff"
    };

    public static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".tif" or ".tiff" => "image/tiff",
        ".csv" => "text/csv",
        ".xml" => "application/xml",
        _ => "application/octet-stream"
    };

    public async Task<StoredFile> SaveAsync(Guid orgId, string fileName, string? contentType, byte[] data, CancellationToken ct = default)
    {
        if (data.Length > _o.MaxUploadBytes) throw new AppException(413, "Ficheiro demasiado grande");
        var file = new StoredFile
        {
            OrganizationId = orgId,
            FileName = Path.GetFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream"
                ? GuessContentType(fileName) : contentType,
            Size = data.Length,
            Sha256 = Text.Sha256(data),
        };
        var now = DateTime.UtcNow;
        file.RelativePath = Path.Combine(orgId.ToString(), now.ToString("yyyy"), now.ToString("MM"),
            file.Id + Path.GetExtension(file.FileName).ToLowerInvariant());
        var full = Path.Combine(_o.Root, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data, ct);
        db.Files.Add(file);
        return file;
    }

    public string FullPath(StoredFile f) => Path.Combine(_o.Root, f.RelativePath);
    public Task<byte[]> ReadAsync(StoredFile f, CancellationToken ct = default) => File.ReadAllBytesAsync(FullPath(f), ct);
    public Stream OpenRead(StoredFile f) => File.OpenRead(FullPath(f));
}
