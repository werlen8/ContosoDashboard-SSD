# Contract: Service Interfaces

Internal C# contracts consumed by Blazor pages, the file endpoint, and tests. Types referenced are defined in [data-model.md](../data-model.md). Every method that touches document data takes the requesting `UserContext` and enforces FR-016 itself (constitution principle I). "Denied" and "not found" are indistinguishable to callers (`null` / failed result with generic message).

## IFileStorageService (`Services/Storage/IFileStorageService.cs`)

```csharp
public interface IFileStorageService
{
    // storagePath is service-generated: "{userId}/{projectId|personal}/{guid}.{ext}" — never user input.
    Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default);   // FileNotFoundException if missing
    Task DeleteAsync(string storagePath, CancellationToken ct = default);             // no-op if missing
}
```

- Returns the stored relative path (forward slashes).
- `LocalFileStorageService`: root from `FileStorage:RootPath` (default `AppData/uploads`, relative to content root); creates directories as needed; rejects any resolved path outside the root (`InvalidOperationException`).
- Future `AzureBlobStorageService` implements the same interface; swap via DI in `Program.cs`. `GetUrlAsync` intentionally omitted (research R1).

## IMalwareScanner (`Services/Scanning/IMalwareScanner.cs`)

```csharp
public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default); // must not consume caller position: caller resets
}
```

- `SignatureMalwareScanner`: flags EICAR test string and `MalwareScanning:BlockedPatterns` (string[]). Any exception → caller treats as "scan could not be completed" → reject.

## IDocumentService (`Services/DocumentService.cs`)

```csharp
public interface IDocumentService
{
    // Upload & files
    Task<OperationResult<int>> UploadAsync(UploadRequest request, UserContext user);
    Task<OperationResult> ReplaceFileAsync(int documentId, Stream content, string originalFileName, UserContext user); // size taken from the stream
    Task<DocumentFile?> OpenFileAsync(int documentId, UserContext user, FileAccessMode mode); // logs Download/Preview; null = not found/denied/preview-not-allowed

    // Read
    Task<Document?> GetDocumentAsync(int documentId, UserContext user);          // includes Project, UploadedByUser
    Task<DocumentPermissions> GetPermissionsAsync(int documentId, UserContext user);
    Task<List<DocumentListItem>> GetMyDocumentsAsync(UserContext user, DocumentListQuery query);
    Task<List<DocumentListItem>> GetSharedWithMeAsync(UserContext user);
    Task<List<DocumentListItem>> GetProjectDocumentsAsync(int projectId, UserContext user);
    Task<List<DocumentListItem>> GetTaskDocumentsAsync(int taskId, UserContext user);
    Task<List<DocumentListItem>> GetAttachableDocumentsAsync(int taskId, UserContext user);
    Task<List<DocumentListItem>> SearchAsync(string term, UserContext user);
    Task<List<DocumentListItem>> GetRecentUploadsAsync(UserContext user, int count = 5);

    // Manage
    Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, UserContext user);
    Task<OperationResult> DeleteAsync(int documentId, UserContext user);

    // Share
    Task<OperationResult<int>> ShareAsync(int documentId, IReadOnlyList<int> userIds, IReadOnlyList<string> departments, UserContext user); // value = shares created
    Task<OperationResult> RevokeShareAsync(int documentShareId, UserContext user);
    Task<List<DocumentShare>> GetSharesAsync(int documentId, UserContext user);  // owner only

    // Tasks
    Task<OperationResult> AttachToTaskAsync(int taskId, int documentId, UserContext user);

    // Reports (Administrator only; others → empty report / failed result)
    Task<OperationResult<DocumentActivityReport>> GetActivityReportAsync(DateTime fromUtc, DateTime toUtc, UserContext user);
}

public enum FileAccessMode { Download, Preview }
public sealed record DocumentFile(Stream Content, string ContentType, string FileName, bool Inline);
public sealed record DocumentMetadataUpdate(string Title, string? Description, string Category, IReadOnlyList<string> Tags, int? ProjectId);
```

### Behavioral guarantees

| Method | Guarantees | Spec refs |
|--------|-----------|-----------|
| `UploadAsync` | Validates size/type/signature, scans, enforces project-membership and Personal-Files rules, writes file then record; deletes file if record save fails; logs `Upload`; if `ProjectId` set, notifies project manager + members except uploader (`DocumentAdded`); if `TaskId` set, attaches to task (task's project enforced). | FR-001–FR-010a, FR-014, FR-030, FR-032 |
| `ReplaceFileAsync` | Owner only; same validation/scan; new GUID path; record updated; old file deleted after success; logs `ReplaceFile`. | FR-025 |
| `OpenFileAsync` | Requires `CanView`; `Preview` only for PDF/JPEG/PNG; logs activity before returning stream. | FR-022, FR-023, FR-035 |
| `UpdateMetadataAsync` | Requires `CanEditMetadata`; ProjectId change only by owner and only under FR-010/FR-010a rules; logs `EditMetadata`. | FR-024 |
| `DeleteAsync` | Requires `CanDelete`; removes record (cascade shares/task links), then file; logs `Delete`. UI confirms before calling. | FR-026 |
| `ShareAsync` | Owner only; expands departments to current members (snapshot); skips owner + existing recipients; notifies each new recipient (`DocumentShared`); logs `Share`. | FR-027, FR-029 |
| `RevokeShareAsync` | Owner only; removes row; logs `RevokeShare`. | FR-028 |
| `AttachToTaskAsync` | Task access via `ITaskService`; doc view access; not Personal Files; doc project null or equal to task project; sets project when null; logs `AttachToTask`. | FR-031, FR-032 |
| List/search methods | Results always ⊆ `AccessibleDocuments(user)`; projections only (no N+1). | FR-017–FR-021, SC-006 |
| `GetActivityReportAsync` | Administrator only. | FR-036, FR-037 |

## Changes to existing services

| Service | Change |
|---------|--------|
| `IDashboardService.GetDashboardSummaryAsync` | `DashboardSummary.DocumentCount` = documents uploaded by user (FR-034). |
| `Login.cshtml.cs` | Add `Department` claim (empty string if null). |
| `Program.cs` | Register `IFileStorageService → LocalFileStorageService` (singleton), `IMalwareScanner → SignatureMalwareScanner` (singleton), `IDocumentService → DocumentService` (scoped); bind `FileStorage` / `MalwareScanning` options. |
