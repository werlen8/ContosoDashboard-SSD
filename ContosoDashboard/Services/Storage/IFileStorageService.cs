namespace ContosoDashboard.Services.Storage;

// Storage abstraction: local filesystem for training, swappable for Azure Blob Storage via DI.
public interface IFileStorageService
{
    // storagePath is always service-generated ("{userId}/{projectId|personal}/{guid}.{ext}"), never user input.
    Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default);

    // Throws FileNotFoundException when the file does not exist.
    Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default);

    // No-op when the file does not exist.
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
