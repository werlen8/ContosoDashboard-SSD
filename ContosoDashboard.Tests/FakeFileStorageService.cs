using ContosoDashboard.Services.Storage;

namespace ContosoDashboard.Tests;

public sealed class FakeFileStorageService : IFileStorageService
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public bool ThrowOnUpload { get; set; }
    public bool ThrowOnDelete { get; set; }

    public async Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default)
    {
        if (ThrowOnUpload) throw new IOException("Simulated storage failure.");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Files[storagePath] = ms.ToArray();
        return storagePath;
    }

    public Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default) =>
        Files.TryGetValue(storagePath, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(storagePath);

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        if (ThrowOnDelete) throw new IOException("Simulated delete failure.");
        Files.Remove(storagePath);
        return Task.CompletedTask;
    }
}
