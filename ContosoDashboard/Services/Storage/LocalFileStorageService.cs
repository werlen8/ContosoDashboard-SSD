namespace ContosoDashboard.Services.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["FileStorage:RootPath"];
        var rootPath = string.IsNullOrWhiteSpace(configured) ? "AppData/uploads" : configured;
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, rootPath));
    }

    public async Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);

        return storagePath.Replace('\\', '/');
    }

    public Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Stored file not found.", storagePath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storagePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    // Defense in depth against path traversal: the resolved path must stay under the storage root.
    private string ResolvePath(string storagePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Storage path resolves outside the storage root.");
        return fullPath;
    }
}
