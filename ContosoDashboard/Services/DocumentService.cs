using System.Linq.Expressions;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services.Scanning;
using ContosoDashboard.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<Document?> GetDocumentAsync(int documentId, UserContext user);
    Task<DocumentPermissions> GetPermissionsAsync(int documentId, UserContext user);
    Task<OperationResult<int>> UploadAsync(UploadRequest request, UserContext user);
    Task<List<DocumentListItem>> GetMyDocumentsAsync(UserContext user, DocumentListQuery query);
    Task<DocumentFile?> OpenFileAsync(int documentId, UserContext user, FileAccessMode mode);
    Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, UserContext user);
    Task<OperationResult> ReplaceFileAsync(int documentId, Stream content, string originalFileName, UserContext user);
    Task<OperationResult> DeleteAsync(int documentId, UserContext user);
    Task<List<DocumentListItem>> GetProjectDocumentsAsync(int projectId, UserContext user);
}

public enum FileAccessMode { Download, Preview }

public sealed record DocumentFile(Stream Content, string ContentType, string FileName, bool Inline);

public sealed record DocumentMetadataUpdate(string Title, string? Description, string Category, IReadOnlyList<string> Tags, int? ProjectId);

public static class DocumentMessages
{
    public const string NotFound = "Document not found or you do not have access.";
    public const string AttachedToTasks = "This document is attached to tasks, so its project or category cannot be changed this way.";
    public const string DeleteFailed = "The document could not be deleted. Please try again.";
    public const string NotPermitted = "You do not have permission to perform this action.";
    public const string TooLarge = "File exceeds the 25 MB limit.";
    public const string Empty = "File is empty.";
    public const string UnsupportedType = "Unsupported file type. Allowed: PDF, Word, Excel, PowerPoint, TXT, JPEG, PNG.";
    public const string ContentMismatch = "File content does not match its type.";
    public const string ScanRejected = "File was rejected by the security scan.";
    public const string PersonalFilesNotLinkable = "Personal Files cannot be linked to a project or task. Choose another category.";
    public const string StorageFailed = "The file could not be saved. Please try again.";
    public const string TitleRequired = "Title is required.";
    public const string TitleTooLong = "Title must be 200 characters or fewer.";
    public const string DescriptionTooLong = "Description must be 2000 characters or fewer.";
    public const string InvalidCategory = "Please choose a valid category.";
    public const string TooManyTags = "A document can have at most 10 tags.";
    public const string InvalidTag = "Each tag must be 50 characters or fewer and cannot contain commas.";
}

public class OperationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }

    public static OperationResult Ok() => new() { Succeeded = true };
    public static OperationResult Fail(string error) => new() { Error = error };
}

public sealed class OperationResult<T> : OperationResult
{
    public T? Value { get; init; }

    public static OperationResult<T> Ok(T value) => new() { Succeeded = true, Value = value };
    public new static OperationResult<T> Fail(string error) => new() { Error = error };
}

public sealed class UploadRequest
{
    // Seekable stream with the full file content (Blazor copies IBrowserFile into a MemoryStream).
    public required Stream Content { get; init; }
    public required string OriginalFileName { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Category { get; init; }
    public int? ProjectId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

public enum DocumentSortField { Title, UploadedDate, Category, FileSize }

public sealed class DocumentListQuery
{
    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedDate;
    public bool Descending { get; init; } = true;
    public string? Category { get; init; }
    public int? ProjectId { get; init; }
    public DateTime? From { get; init; } // inclusive, UTC
    public DateTime? To { get; init; }   // inclusive, UTC
}

public sealed class DocumentListItem
{
    public int DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DateTime UploadedDate { get; init; }
    public long FileSizeBytes { get; init; }
    public string FileType { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int UploadedByUserId { get; init; }
    public string UploadedByName { get; init; } = string.Empty;
    public string? Tags { get; init; }
    public DocumentPermissions Permissions { get; init; } = DocumentPermissions.None;
}

public sealed record DocumentPermissions(bool CanView, bool CanEditMetadata, bool CanReplace, bool CanDelete, bool CanShare)
{
    public static readonly DocumentPermissions None = new(false, false, false, false, false);
    public static readonly DocumentPermissions Owner = new(true, true, true, true, true);
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanner _scanner;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IMalwareScanner scanner,
        INotificationService notificationService,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _storage = storage;
        _scanner = scanner;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Document?> GetDocumentAsync(int documentId, UserContext user)
    {
        return await AccessibleDocuments(user)
            .AsNoTracking()
            .Include(d => d.Project)
            .Include(d => d.UploadedByUser)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);
    }

    public async Task<DocumentPermissions> GetPermissionsAsync(int documentId, UserContext user)
    {
        var row = await LoadAccessRowAsync(documentId, user);
        return row == null ? DocumentPermissions.None : ComputePermissions(row, user);
    }

    public async Task<OperationResult<int>> UploadAsync(UploadRequest request, UserContext user)
    {
        var metadata = ValidateMetadata(request.Title, request.Description, request.Category, request.Tags);
        if (metadata.Error != null)
            return OperationResult<int>.Fail(metadata.Error);

        if (request.Category == DocumentCategories.PersonalFiles && request.ProjectId != null)
            return OperationResult<int>.Fail(DocumentMessages.PersonalFilesNotLinkable);

        if (request.ProjectId is int projectId && !await CanLinkProjectAsync(projectId, user))
            return OperationResult<int>.Fail(DocumentMessages.NotFound);

        var content = await EnsureSeekableAsync(request.Content);
        var fileName = CleanFileName(request.OriginalFileName);
        var file = await ValidateAndScanAsync(content, fileName);
        if (file.Error != null)
            return OperationResult<int>.Fail(file.Error);

        // Upload sequence: generate unique path -> save file -> save metadata (no orphaned records).
        var storagePath = BuildStoragePath(user.UserId, request.ProjectId, file.Extension);
        if (!await TryStoreAsync(content, storagePath, file.MimeType))
            return OperationResult<int>.Fail(DocumentMessages.StorageFailed);

        var now = DateTime.UtcNow;
        var document = new Document
        {
            Title = metadata.Title,
            Description = metadata.Description,
            Category = request.Category,
            Tags = metadata.Tags,
            OriginalFileName = fileName,
            FileType = file.MimeType,
            FileSizeBytes = content.Length,
            FilePath = storagePath,
            UploadedByUserId = user.UserId,
            ProjectId = request.ProjectId,
            UploadedDate = now,
            UpdatedDate = now
        };

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            _context.Documents.Add(document);
            await _context.SaveChangesAsync();
            AddActivity(document, user, DocumentActions.Upload);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving document metadata failed for user {UserId}; removing stored file", user.UserId);
            DiscardChanges(document);
            await TryDeleteFileAsync(storagePath);
            return OperationResult<int>.Fail(DocumentMessages.StorageFailed);
        }

        if (document.ProjectId != null)
            await NotifyProjectDocumentAddedAsync(document, user.UserId);

        return OperationResult<int>.Ok(document.DocumentId);
    }

    public async Task<List<DocumentListItem>> GetMyDocumentsAsync(UserContext user, DocumentListQuery query)
    {
        var documents = _context.Documents.AsNoTracking().Where(d => d.UploadedByUserId == user.UserId);

        if (!string.IsNullOrEmpty(query.Category))
            documents = documents.Where(d => d.Category == query.Category);
        if (query.ProjectId != null)
            documents = documents.Where(d => d.ProjectId == query.ProjectId);
        if (query.From != null)
            documents = documents.Where(d => d.UploadedDate >= query.From);
        if (query.To != null)
            documents = documents.Where(d => d.UploadedDate <= query.To);

        documents = (query.SortBy, query.Descending) switch
        {
            (DocumentSortField.Title, false) => documents.OrderBy(d => d.Title),
            (DocumentSortField.Title, true) => documents.OrderByDescending(d => d.Title),
            (DocumentSortField.Category, false) => documents.OrderBy(d => d.Category),
            (DocumentSortField.Category, true) => documents.OrderByDescending(d => d.Category),
            (DocumentSortField.FileSize, false) => documents.OrderBy(d => d.FileSizeBytes),
            (DocumentSortField.FileSize, true) => documents.OrderByDescending(d => d.FileSizeBytes),
            (_, false) => documents.OrderBy(d => d.UploadedDate),
            _ => documents.OrderByDescending(d => d.UploadedDate),
        };

        return await ToListItemsAsync(documents, user);
    }

    public async Task<DocumentFile?> OpenFileAsync(int documentId, UserContext user, FileAccessMode mode)
    {
        var row = await LoadAccessRowAsync(documentId, user);
        if (row == null || !ComputePermissions(row, user).CanView)
            return null;

        var document = row.Document;
        if (mode == FileAccessMode.Preview && !DocumentFileRules.IsPreviewable(document.FileType))
            return null;

        Stream content;
        try
        {
            content = await _storage.DownloadAsync(document.FilePath);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Stored file for document {DocumentId} is missing", documentId);
            return null;
        }

        AddActivity(document, user, mode == FileAccessMode.Preview ? DocumentActions.Preview : DocumentActions.Download);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            await content.DisposeAsync();
            DiscardChanges(document);
            throw;
        }

        return new DocumentFile(content, document.FileType, document.OriginalFileName, mode == FileAccessMode.Preview);
    }

    public async Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, UserContext user)
    {
        var (row, denied) = await LoadForActionAsync(documentId, user, p => p.CanEditMetadata);
        if (denied != null) return denied;

        var metadata = ValidateMetadata(update.Title, update.Description, update.Category, update.Tags);
        if (metadata.Error != null)
            return OperationResult.Fail(metadata.Error);

        var document = row!.Document;
        var projectChanged = update.ProjectId != document.ProjectId;

        if (update.Category == DocumentCategories.PersonalFiles && update.ProjectId != null)
            return OperationResult.Fail(DocumentMessages.PersonalFilesNotLinkable);

        if (projectChanged)
        {
            if (!row.IsOwner)
                return OperationResult.Fail(DocumentMessages.NotPermitted);
            if (update.ProjectId is int projectId && !await CanLinkProjectAsync(projectId, user))
                return OperationResult.Fail(DocumentMessages.NotFound);
        }

        // Task attachments require a non-personal document in the task's project (clarifications Q4/Q5).
        if ((projectChanged || update.Category == DocumentCategories.PersonalFiles)
            && await _context.TaskDocuments.AnyAsync(td => td.DocumentId == documentId))
            return OperationResult.Fail(DocumentMessages.AttachedToTasks);

        document.Title = metadata.Title;
        document.Description = metadata.Description;
        document.Category = update.Category;
        document.Tags = metadata.Tags;
        document.ProjectId = update.ProjectId;
        document.UpdatedDate = DateTime.UtcNow;
        AddActivity(document, user, DocumentActions.EditMetadata);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Updating metadata of document {DocumentId} failed", documentId);
            DiscardChanges(document);
            return OperationResult.Fail(DocumentMessages.StorageFailed);
        }

        if (projectChanged && document.ProjectId != null)
            await NotifyProjectDocumentAddedAsync(document, user.UserId);

        return OperationResult.Ok();
    }

    public async Task<OperationResult> ReplaceFileAsync(int documentId, Stream content, string originalFileName, UserContext user)
    {
        var (row, denied) = await LoadForActionAsync(documentId, user, p => p.CanReplace);
        if (denied != null) return denied;

        content = await EnsureSeekableAsync(content);
        var fileName = CleanFileName(originalFileName);
        var file = await ValidateAndScanAsync(content, fileName);
        if (file.Error != null)
            return OperationResult.Fail(file.Error);

        var document = row!.Document;
        var oldPath = document.FilePath;
        var newPath = BuildStoragePath(document.UploadedByUserId, document.ProjectId, file.Extension);
        if (!await TryStoreAsync(content, newPath, file.MimeType))
            return OperationResult.Fail(DocumentMessages.StorageFailed);

        document.FilePath = newPath;
        document.OriginalFileName = fileName;
        document.FileType = file.MimeType;
        document.FileSizeBytes = content.Length;
        document.UpdatedDate = DateTime.UtcNow;
        AddActivity(document, user, DocumentActions.ReplaceFile);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replacing file of document {DocumentId} failed; removing new file", documentId);
            DiscardChanges(document);
            await TryDeleteFileAsync(newPath);
            return OperationResult.Fail(DocumentMessages.StorageFailed);
        }

        await TryDeleteFileAsync(oldPath);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(int documentId, UserContext user)
    {
        var (row, denied) = await LoadForActionAsync(documentId, user, p => p.CanDelete);
        if (denied != null) return denied;

        var document = row!.Document;
        AddActivity(document, user, DocumentActions.Delete);
        _context.Documents.Remove(document); // shares and task links cascade

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting document {DocumentId} failed", documentId);
            DiscardChanges(document);
            return OperationResult.Fail(DocumentMessages.DeleteFailed);
        }

        await TryDeleteFileAsync(document.FilePath);
        return OperationResult.Ok();
    }

    public async Task<List<DocumentListItem>> GetProjectDocumentsAsync(int projectId, UserContext user)
    {
        if (!await CanLinkProjectAsync(projectId, user))
            return new List<DocumentListItem>();

        var documents = AccessibleDocuments(user)
            .Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.UploadedDate);
        return await ToListItemsAsync(documents, user);
    }

    // FR-030: notify the project manager and members (except the actor) when a document joins a project.
    private async Task NotifyProjectDocumentAddedAsync(Document document, int actorUserId)
    {
        try
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Where(p => p.ProjectId == document.ProjectId)
                .Select(p => new { p.Name, p.ProjectManagerId, MemberIds = p.ProjectMembers.Select(pm => pm.UserId).ToList() })
                .FirstOrDefaultAsync();
            if (project == null) return;

            var recipients = project.MemberIds.Append(project.ProjectManagerId).Where(id => id != actorUserId).Distinct();
            foreach (var recipientId in recipients)
            {
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = recipientId,
                    Title = "New project document",
                    Message = $"\"{document.Title}\" was added to {project.Name}.",
                    Type = NotificationType.DocumentAdded,
                    Priority = NotificationPriority.Informational
                });
            }
        }
        catch (Exception ex)
        {
            // The document is already saved; a notification failure must not undo it.
            _logger.LogError(ex, "Sending project notifications for document {DocumentId} failed", document.DocumentId);
        }
    }

    // Loads a tracked document and checks one permission. Invisible documents and unknown ids look the same.
    private async Task<(AccessRow? Row, OperationResult? Denied)> LoadForActionAsync(
        int documentId, UserContext user, Func<DocumentPermissions, bool> required)
    {
        var row = await LoadAccessRowAsync(documentId, user);
        var permissions = row == null ? DocumentPermissions.None : ComputePermissions(row, user);
        if (!permissions.CanView)
            return (null, OperationResult.Fail(DocumentMessages.NotFound));
        if (!required(permissions))
            return (null, OperationResult.Fail(DocumentMessages.NotPermitted));
        return (row, null);
    }

    // ---------------------------------------------------------------------------------------------
    // Validation, storage and audit helpers
    // ---------------------------------------------------------------------------------------------

    private sealed record MetadataCheck(string? Error, string Title = "", string? Description = null, string? Tags = null);

    private sealed record FileCheck(string? Error, string Extension = "", string MimeType = "");

    private static MetadataCheck ValidateMetadata(string? title, string? description, string category, IEnumerable<string>? tags)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0) return new(DocumentMessages.TitleRequired);
        if (trimmedTitle.Length > 200) return new(DocumentMessages.TitleTooLong);

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > 2000) return new(DocumentMessages.DescriptionTooLong);

        if (!DocumentCategories.All.Contains(category)) return new(DocumentMessages.InvalidCategory);

        var normalized = (tags ?? Enumerable.Empty<string>())
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (normalized.Count > 10) return new(DocumentMessages.TooManyTags);
        if (normalized.Any(t => t.Length > 50 || t.Contains(','))) return new(DocumentMessages.InvalidTag);

        return new(null, trimmedTitle, trimmedDescription, normalized.Count == 0 ? null : string.Join(",", normalized));
    }

    private async Task<FileCheck> ValidateAndScanAsync(Stream content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!DocumentFileRules.IsAllowedExtension(extension)) return new(DocumentMessages.UnsupportedType);
        if (content.Length == 0) return new(DocumentMessages.Empty);
        if (content.Length > DocumentFileRules.MaxFileSizeBytes) return new(DocumentMessages.TooLarge);

        content.Position = 0;
        var header = new byte[Math.Min(DocumentFileRules.SignatureHeaderLength, content.Length)];
        await content.ReadExactlyAsync(header);
        if (!DocumentFileRules.HasValidSignature(extension, header)) return new(DocumentMessages.ContentMismatch);

        content.Position = 0;
        try
        {
            var scan = await _scanner.ScanAsync(content);
            if (!scan.IsClean)
            {
                _logger.LogWarning("Upload rejected by malware scan: {Reason}", scan.Reason);
                return new(DocumentMessages.ScanRejected);
            }
        }
        catch (Exception ex)
        {
            // Fail closed: an incomplete scan never lets a file through.
            _logger.LogError(ex, "Malware scan could not be completed");
            return new(DocumentMessages.ScanRejected);
        }
        finally
        {
            content.Position = 0;
        }

        return new(null, extension, DocumentFileRules.GetCanonicalMimeType(extension));
    }

    // User-supplied names are kept for display/download only; directory parts are stripped.
    private static string CleanFileName(string originalFileName)
    {
        var name = originalFileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        if (name.Length > 255) name = name[^255..];
        return name.Length == 0 ? "file" : name;
    }

    private static string BuildStoragePath(int userId, int? projectId, string extension) =>
        $"{userId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():N}{extension}";

    private static async Task<Stream> EnsureSeekableAsync(Stream content)
    {
        if (content.CanSeek) return content;
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private async Task<bool> TryStoreAsync(Stream content, string storagePath, string mimeType)
    {
        try
        {
            await _storage.UploadAsync(content, storagePath, mimeType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writing file {StoragePath} failed", storagePath);
            await TryDeleteFileAsync(storagePath);
            return false;
        }
    }

    private async Task TryDeleteFileAsync(string storagePath)
    {
        try
        {
            await _storage.DeleteAsync(storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting file {StoragePath} failed; it is unreferenced and can be removed manually", storagePath);
        }
    }

    private async Task<bool> CanLinkProjectAsync(int projectId, UserContext user) =>
        await _context.Projects.AnyAsync(p => p.ProjectId == projectId
            && (user.IsAdministrator
                || p.ProjectManagerId == user.UserId
                || p.ProjectMembers.Any(pm => pm.UserId == user.UserId)));

    private DocumentActivity AddActivity(Document document, UserContext user, string action, string? details = null)
    {
        var activity = new DocumentActivity
        {
            DocumentId = document.DocumentId,
            DocumentTitle = document.Title,
            FileType = document.FileType,
            UserId = user.UserId,
            Action = action,
            Details = details,
            OccurredAt = DateTime.UtcNow
        };
        _context.DocumentActivities.Add(activity);
        return activity;
    }

    // The DbContext is scoped to the Blazor circuit, so failed changes must not linger in the change tracker.
    private void DiscardChanges(Document document)
    {
        _context.Entry(document).State = EntityState.Detached;
        foreach (var entry in _context.ChangeTracker.Entries<DocumentActivity>().Where(e => e.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
    }

    private async Task<List<DocumentListItem>> ToListItemsAsync(IQueryable<Document> documents, UserContext user)
    {
        var rows = await documents.AsNoTracking().Select(ToAccessRow(user)).ToListAsync();
        return rows.Select(r => new DocumentListItem
        {
            DocumentId = r.Document.DocumentId,
            Title = r.Document.Title,
            Category = r.Document.Category,
            UploadedDate = r.Document.UploadedDate,
            FileSizeBytes = r.Document.FileSizeBytes,
            FileType = r.Document.FileType,
            ProjectId = r.Document.ProjectId,
            ProjectName = r.ProjectName,
            UploadedByUserId = r.Document.UploadedByUserId,
            UploadedByName = r.UploadedByName,
            Tags = r.Document.Tags,
            Permissions = ComputePermissions(r, user)
        }).ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Authorization (FR-016). Visibility and per-action rights are defined only here.
    // ---------------------------------------------------------------------------------------------

    private IQueryable<Document> AccessibleDocuments(UserContext user)
    {
        if (user.IsAdministrator)
            return _context.Documents;

        var uid = user.UserId;
        var dept = user.Department;
        var teamLead = IsTeamLeadScope(user);

        return _context.Documents.Where(d =>
            d.UploadedByUserId == uid
            || (d.Project != null && (d.Project.ProjectManagerId == uid || d.Project.ProjectMembers.Any(pm => pm.UserId == uid)))
            || d.Shares.Any(s => s.SharedWithUserId == uid)
            || (teamLead && d.Category != DocumentCategories.PersonalFiles && d.UploadedByUser.Department == dept));
    }

    // Team Lead rights apply to roles >= TeamLead that belong to a department (research R7).
    private static bool IsTeamLeadScope(UserContext user) =>
        user.IsAtLeast(UserRole.TeamLead) && user.Department != null;

    private static Expression<Func<Document, AccessRow>> ToAccessRow(UserContext user)
    {
        var uid = user.UserId;
        var dept = user.Department;
        var teamLead = IsTeamLeadScope(user);

        return d => new AccessRow
        {
            Document = d,
            ProjectName = d.Project != null ? d.Project.Name : null,
            UploadedByName = d.UploadedByUser.DisplayName,
            IsOwner = d.UploadedByUserId == uid,
            IsProjectManager = d.Project != null && d.Project.ProjectManagerId == uid,
            IsProjectMember = d.Project != null && d.Project.ProjectMembers.Any(pm => pm.UserId == uid),
            IsShareRecipient = d.Shares.Any(s => s.SharedWithUserId == uid),
            IsTeamLead = teamLead && d.Category != DocumentCategories.PersonalFiles && d.UploadedByUser.Department == dept
        };
    }

    private static DocumentPermissions ComputePermissions(AccessRow row, UserContext user)
    {
        if (row.IsOwner)
            return DocumentPermissions.Owner;

        var canManage = user.IsAdministrator || row.IsProjectManager || row.IsTeamLead;
        var canView = canManage || row.IsProjectMember || row.IsShareRecipient;
        return new DocumentPermissions(canView, canManage, false, canManage, false);
    }

    private async Task<AccessRow?> LoadAccessRowAsync(int documentId, UserContext user) =>
        await _context.Documents
            .Where(d => d.DocumentId == documentId)
            .Select(ToAccessRow(user))
            .FirstOrDefaultAsync();

    private sealed class AccessRow
    {
        public Document Document { get; init; } = null!;
        public string? ProjectName { get; init; }
        public string UploadedByName { get; init; } = string.Empty;
        public bool IsOwner { get; init; }
        public bool IsProjectManager { get; init; }
        public bool IsProjectMember { get; init; }
        public bool IsShareRecipient { get; init; }
        public bool IsTeamLead { get; init; }
    }
}
