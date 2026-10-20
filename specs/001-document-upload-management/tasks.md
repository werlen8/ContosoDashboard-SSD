---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The plan (research R15, Complexity Tracking) requires an xUnit project to verify the FR-016 access matrix (SC-006), and constitution principle IV requires tests to pass when present. Within each story, write the tests first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: User story from spec.md (US1–US8)
- Paths are relative to the repository root. App code is in `ContosoDashboard/` and tests are in `ContosoDashboard.Tests/`.

## Conventions used by every task

- **Requesting user**: every `IDocumentService` method takes `UserContext user` and enforces FR-016 itself. Pages never use `ApplicationDbContext` directly.
- **Denials**: return a generic failure that looks the same as "not found", using the message "Document not found or you do not have access."
- **Messages**: user-facing error text must match the "User-facing messages" table in [contracts/ui-and-http.md](./contracts/ui-and-http.md) exactly.
- **Activity logging**: every mutating or reading file action writes one `DocumentActivity` row (FR-035) in the same `SaveChangesAsync` as the action. Downloads and previews write it just before the stream is returned.
- **Time**: all timestamps use `DateTime.UtcNow`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository and configuration scaffolding

- [ ] T001 Add the line `ContosoDashboard/AppData/` to `.gitignore` so uploaded files are never committed
- [ ] T002 [P] Add configuration sections to `ContosoDashboard/appsettings.json`: `"FileStorage": { "RootPath": "AppData/uploads" }` and `"MalwareScanning": { "BlockedPatterns": [] }`
- [ ] T003 [P] Create the xUnit test project `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj` targeting `net9.0`:
  - Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`.
  - `<ProjectReference Include="..\ContosoDashboard\ContosoDashboard.csproj" />`.
  - Verify that `dotnet test ContosoDashboard.Tests` runs with 0 tests.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, schema, identity, infrastructure abstractions, the `DocumentService` skeleton with the central authorization rules, DI registration and test helpers.

All four new entities are created here because they share one `EnsureCreated` schema (research R14). Creating them later would force developers to delete the database again for every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Entities & schema

- [ ] T004 [P] Create `ContosoDashboard/Models/DocumentCategories.cs`:
  - A static class with constants `ProjectDocuments = "Project Documents"`, `TeamResources = "Team Resources"`, `PersonalFiles = "Personal Files"`, `Reports = "Reports"`, `Presentations = "Presentations"`, `Other = "Other"`.
  - `IReadOnlyList<string> All` in that display order.
- [ ] T005 [P] Create `ContosoDashboard/Models/Document.cs` with these fields:

  | Field | Type and constraints |
  |-------|----------------------|
  | `DocumentId` | int PK |
  | `Title` | `[Required][MaxLength(200)]` |
  | `Description` | `[MaxLength(2000)]`, nullable |
  | `Category` | `[Required][MaxLength(50)]`, "must be one of `DocumentCategories.All`" |
  | `Tags` | `[MaxLength(600)]`, nullable, "normalized comma-joined list, ≤10 tags × ≤50 chars, lower-case, unique, no commas inside a tag" |
  | `OriginalFileName` | `[Required][MaxLength(255)]` |
  | `FileType` | `[Required][MaxLength(255)]`, canonical MIME |
  | `FileSizeBytes` | long, "1 … 26,214,400" |
  | `FilePath` | `[Required][MaxLength(500)]`, unique, relative `{uploaderId}/{projectId\|personal}/{guid}.{ext}` |
  | `UploadedByUserId` | int FK |
  | `ProjectId` | int? FK, "Null when Category = "Personal Files"" |
  | `UploadedDate` | DateTime |
  | `UpdatedDate` | DateTime |

  Navigations: `UploadedByUser`, `Project?`, `ICollection<DocumentShare> Shares`, `ICollection<TaskDocument> TaskDocuments`.
- [ ] T006 [P] Create `ContosoDashboard/Models/DocumentShare.cs`:
  - Fields: `DocumentShareId`, `DocumentId`, `SharedWithUserId`, `SharedByUserId`, `SharedDate`, `SharedViaDepartment` (`[MaxLength(100)]`, nullable).
  - Navigations: `Document`, `SharedWithUser`, `SharedByUser`.
- [ ] T007 [P] Create `ContosoDashboard/Models/TaskDocument.cs`:
  - Fields: `TaskDocumentId`, `TaskId`, `DocumentId`, `AttachedByUserId`, `AttachedDate`.
  - Navigations: `Task` (TaskItem), `Document`, `AttachedByUser`.
- [ ] T008 [P] Create `ContosoDashboard/Models/DocumentActivity.cs`:
  - Fields:
    - `DocumentActivityId`;
    - `DocumentId` (int, required, **no FK / no navigation**);
    - `DocumentTitle` (`[Required][MaxLength(200)]`);
    - `FileType` (`[Required][MaxLength(255)]`);
    - `UserId` (FK) with `User` navigation;
    - `Action` (`[Required][MaxLength(30)]`);
    - `Details` (`[MaxLength(500)]`, nullable);
    - `OccurredAt`.
  - Static class `DocumentActions` with constants `Upload`, `Download`, `Preview`, `EditMetadata`, `ReplaceFile`, `Delete`, `Share`, `RevokeShare`, `AttachToTask`.
- [ ] T009 [P] Append `DocumentShared` and `DocumentAdded` to the **end** of `enum NotificationType` in `ContosoDashboard/Models/Notification.cs`. Existing integer values must not change.
- [ ] T010 Add navigation collections in `ContosoDashboard/Models/User.cs`, `Project.cs`, `TaskItem.cs` (depends on T005, T007):
  - `ICollection<Document> UploadedDocuments` in `ContosoDashboard/Models/User.cs`;
  - `ICollection<Document> Documents` in `ContosoDashboard/Models/Project.cs`;
  - `ICollection<TaskDocument> TaskDocuments` in `ContosoDashboard/Models/TaskItem.cs`.
- [ ] T011 Update `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T004–T010):
  - **DbSets**: `Documents`, `DocumentShares`, `TaskDocuments`, `DocumentActivities`.
  - **Delete behavior**:
    - Document → User (`UploadedByUserId`): `Restrict`. Document → Project: `Restrict`.
    - DocumentShare → Document: `Cascade`. DocumentShare → SharedWithUser / SharedByUser: `Restrict`.
    - TaskDocument → TaskItem: `Cascade`. TaskDocument → Document: `Cascade`. TaskDocument → AttachedByUser: `Restrict`.
    - DocumentActivity → User: `Restrict`.
  - **Indexes**:
    - `Documents`: `UploadedByUserId`, `ProjectId`, `Category`, `UploadedDate`, unique `FilePath`.
    - `DocumentShares`: `SharedWithUserId`, unique `(DocumentId, SharedWithUserId)`.
    - `TaskDocuments`: `TaskId`, unique `(TaskId, DocumentId)`.
    - `DocumentActivities`: `OccurredAt`, `DocumentId`, `UserId`.
  - **Seed User 5**: `UserId = 5`, `Email = "avery.smith@contoso.com"`, `DisplayName = "Avery Smith"`, `Department = "Marketing"`, `JobTitle = "Marketing Specialist"`, `Role = UserRole.Employee`, and the same availability and notification defaults as the other seed users. No project membership. Existing seed rows must not change.

### Identity

- [ ] T012 [P] Create `ContosoDashboard/Services/UserContext.cs`:
  - `public sealed record UserContext(int UserId, UserRole Role, string? Department)` with a helper `bool IsAtLeast(UserRole role)` that follows the hierarchy Employee < TeamLead < ProjectManager < Administrator.
  - Static extension `ClaimsPrincipal.ToUserContext()` that reads `ClaimTypes.NameIdentifier`, `ClaimTypes.Role` (parsed into `UserRole`) and the `"Department"` claim (empty string → null). It throws `InvalidOperationException` when the user id is missing.
- [ ] T013 [P] Add `new Claim("Department", user.Department ?? string.Empty)` to the claims list in `ContosoDashboard/Pages/Login.cshtml.cs` (required for Team Lead rules, research R7)

### Infrastructure abstractions

- [ ] T014 [P] Create `ContosoDashboard/Services/Storage/IFileStorageService.cs` with exactly these signatures from [contracts/service-interfaces.md](./contracts/service-interfaces.md):
  - `Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default)`
  - `Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default)`
  - `Task DeleteAsync(string storagePath, CancellationToken ct = default)`
- [ ] T015 Implement `ContosoDashboard/Services/Storage/LocalFileStorageService.cs` (depends on T014):
  - **Root**: read `FileStorage:RootPath` from `IConfiguration` (default `AppData/uploads`) and resolve it against `IWebHostEnvironment.ContentRootPath`.
  - **Path safety**: build the full path with `Path.GetFullPath(Path.Combine(root, storagePath))` and throw `InvalidOperationException` if the result is not under the root.
  - **Writing**: create directories as needed; write with `FileMode.CreateNew`.
  - **Reading**: `DownloadAsync` returns a read-only `FileStream`, or throws `FileNotFoundException`.
  - **Deleting**: `DeleteAsync` does nothing if the file is missing.
  - **Return value**: the relative path with forward slashes.
  - Use only `System.IO`.
- [ ] T016 [P] Create `ContosoDashboard/Services/Scanning/IMalwareScanner.cs` with `Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)` and `public sealed record ScanResult(bool IsClean, string? Reason)`
- [ ] T017 Implement `ContosoDashboard/Services/Scanning/SignatureMalwareScanner.cs` (depends on T016):
  - Scan the stream bytes for the EICAR test string `X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*` and for every ASCII pattern in `MalwareScanning:BlockedPatterns` (`IConfiguration`).
  - Return `IsClean=false` with the matched reason.
  - Leave exceptions unhandled; the caller fails closed.
- [ ] T018 [P] Create the static class `ContosoDashboard/Services/DocumentFileRules.cs` (research R3):
  - `MaxFileSizeBytes = 26_214_400`.
  - Extension whitelist (`.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`), case-insensitive.
  - Canonical MIME map per extension; the OOXML types use the full `application/vnd.openxmlformats-officedocument.*` names.
  - `bool HasValidSignature(string extension, ReadOnlySpan<byte> header)` with these checks:
    - PDF: `%PDF-`
    - OOXML: `50 4B 03 04`
    - OLE2: `D0 CF 11 E0 A1 B1 1A E1`
    - JPEG: `FF D8 FF`
    - PNG: `89 50 4E 47 0D 0A 1A 0A`
    - TXT: no `0x00` byte in the first 8 KB
  - `IsPreviewable(mime)`: true only for `application/pdf`, `image/jpeg` and `image/png`.

### DocumentService skeleton & central authorization

- [ ] T019 Create `ContosoDashboard/Services/DocumentService.cs` with the shared types and the `IDocumentService` interface containing **only** `GetDocumentAsync` and `GetPermissionsAsync` for now (later stories add their methods to the interface).
  - **Records and enums**:
    - `DocumentPermissions(bool CanView, bool CanEditMetadata, bool CanReplace, bool CanDelete, bool CanShare)`
    - `DocumentListItem`, with the fields listed in [data-model.md](./data-model.md), including `UploadedByUserId` and `Permissions`
    - `DocumentListQuery` (`SortBy` = `Title | UploadedDate | Category | FileSize`, `Descending`, `Category?`, `ProjectId?`, `From?`, `To?`)
    - `UploadRequest`, `DocumentMetadataUpdate`
    - `DocumentFile(Stream Content, string ContentType, string FileName, bool Inline)`
    - `enum FileAccessMode { Download, Preview }`
    - `OperationResult` / `OperationResult<T>`, each with static `Ok` / `Fail(string error)`
    - `DocumentActivityReport`
  - **Class constructor**: `ApplicationDbContext`, `IFileStorageService`, `IMalwareScanner`, `INotificationService`, `ITaskService`, `ILogger<DocumentService>`.
  - **Constant**: `NotFoundMessage = "Document not found or you do not have access."`.
- [ ] T020 [P] Create the test helper `ContosoDashboard.Tests/TestDb.cs`:
  - Open a SQLite in-memory connection (`DataSource=:memory:`) and keep it open for the test's lifetime, then call `EnsureCreated`, which brings in seed users 1–5 and project 1.
  - Helper methods: `AddUser(role, department)`, `AddProject(managerId, memberIds)`, `AddDocument(...)` (inserts a row directly).
  - `UserContext For(int userId)`.
  - A `CreateDocumentService(IFileStorageService? storage = null, IMalwareScanner? scanner = null)` factory that wires the real `NotificationService` and `TaskService`.
- [ ] T021 [P] Create `ContosoDashboard.Tests/FakeFileStorageService.cs`:
  - An in-memory `Dictionary<string, byte[]>` implementing `IFileStorageService`.
  - Flags `ThrowOnUpload` and `ThrowOnDelete`.
  - Exposes `Files` so tests can make assertions about stored files.
- [ ] T022 Write `ContosoDashboard.Tests/DocumentAuthorizationTests.cs` (depends on T019–T021; write before T023 and confirm it fails). Cover the whole FR-016 table:
  - **Actors**: owner, project member, Project Manager of the document's project, Team Lead in the uploader's department (non-Personal document), Team Lead with a Personal Files document (denied), share recipient, Administrator, unrelated user (seed user 5).
  - **Assertions**: each actor gets exactly the expected `DocumentPermissions`, and the `AccessibleDocuments` filter returns exactly the visible set.
  - **Edge cases**: a Team Lead whose department is null sees no team documents; a ProjectManager role in the same department as the uploader gets the Team Lead rights (research R7).
- [ ] T023 Implement in `ContosoDashboard/Services/DocumentService.cs` (depends on T022):
  - **`private IQueryable<Document> AccessibleDocuments(UserContext user)`** — visible when any of these is true:
    - the user is an Administrator;
    - the user is the uploader;
    - the document's project is managed by the user or has the user as a member;
    - the document has a share to the user;
    - the user is at least TeamLead, `user.Department != null`, `Category != "Personal Files"`, and the uploader's `Department == user.Department`.
  - **`private DocumentPermissions GetPermissions(Document doc, UserContext user)`** following the FR-016 table:
    - owner: all rights;
    - Project Manager of the document's project, Team Lead per the rule above, and Administrator: view, edit metadata and delete;
    - project member and share recipient: view only;
    - everyone else: nothing.
  - **Helpers**:
    - `IsProjectMemberOrManagerAsync(projectId, userId)`;
    - `NormalizeTags(IEnumerable<string>)`: trim, lower-case, drop empty values, de-duplicate; reject more than 10 tags, any tag longer than 50 characters, or a tag containing a comma;
    - `AddActivity(Document doc, UserContext user, string action, string? details = null)`.
  - **Public methods**:
    - `GetDocumentAsync`: query through `AccessibleDocuments`, `Include` the Project and UploadedByUser;
    - `GetPermissionsAsync`: return all-false when the document is not visible.
  - Make T022 pass.
- [ ] T024 Register services and endpoints in `ContosoDashboard/Program.cs`:
  - `IFileStorageService → LocalFileStorageService` (singleton), `IMalwareScanner → SignatureMalwareScanner` (singleton), `IDocumentService → DocumentService` (scoped).
  - Add `app.MapRazorPages();` before `app.MapFallbackToPage("/_Host");` if it isn't there yet. The file endpoint (and Login) must be routable Razor Pages.
  - Do not change the security headers middleware.

**Checkpoint**:
- `dotnet build ContosoDashboard` has 0 errors.
- After `rm -f ContosoDashboard/ContosoDashboard.db*`, `dotnet run` creates the schema and seeds 5 users.
- `dotnet test ContosoDashboard.Tests` is green.

---

## Phase 3: User Story 1 - Upload and View My Documents (Priority: P1) 🎯 MVP

**Goal**: Employees upload one or more validated, scanned files with metadata and see them in a sortable, filterable "My Documents" list.

**Independent Test**: Quickstart V1–V6 and V8. As Ni Kang, upload files and see them listed. Invalid files are rejected with the exact messages and leave nothing behind. As Avery Smith, none of Ni Kang's documents are listed.

### Tests for User Story 1 ⚠️

- [ ] T025 [P] [US1] Write `ContosoDashboard.Tests/DocumentUploadTests.cs`. It uses `FakeFileStorageService` and a stub scanner.
  - **Valid upload**: creates exactly one `Document` row and one stored file whose path matches `^{uploaderId}/(personal|{projectId})/[0-9a-f]{32}\.pdf$`. `FileType` is the canonical MIME type even when the client claimed `text/html`. One `Upload` activity is written.
  - **Size limits**: 26,214,400 bytes is accepted. 26,214,401 bytes is rejected ("File exceeds the 25 MB limit."), as is 0 bytes ("File is empty.").
  - **Type checks**: `.exe` is rejected ("Unsupported file type. …"), and so is a `.pdf` whose bytes are `MZ…` ("File content does not match its type.").
  - **Malware scan**: EICAR content is rejected, and so is a scanner exception. Both give "File was rejected by the security scan."
  - **Project access**: upload to a project where the user is not a member → `NotFoundMessage`, nothing stored. A member's upload succeeds.
  - **Personal Files**: Personal Files plus a project → the Personal Files message.
  - **Metadata validation**: title empty or longer than 200 characters → rejected; 11 tags → rejected; tags are normalized.
  - **Rollback**: when the DB save fails (simulate by disposing the context or setting a title to force a constraint error), the stored file is deleted afterwards.
  - **Rejected uploads**: no row and no stored file are left behind.
- [ ] T026 [P] [US1] Write `ContosoDashboard.Tests/DocumentListTests.cs` for `GetMyDocumentsAsync`:
  - Only the user's own uploads are returned.
  - Sorting works for Title, UploadedDate, Category and FileSize, in both directions.
  - Filters work by category, project, and the `From`/`To` date range (inclusive).
  - `Permissions` is filled in on each item.

### Implementation for User Story 1

- [ ] T027 [US1] Add `Task<OperationResult<int>> UploadAsync(UploadRequest request, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`, in this order:
  1. Validate metadata: title trimmed, 1–200 characters; category in `DocumentCategories.All`; description ≤ 2000; tags via `NormalizeTags`.
  2. Apply the Personal Files rule: a Personal Files document cannot have a ProjectId (FR-010a).
  3. Check project access: unless the user is an Administrator, they must be the project's manager or a member (FR-010).
  4. Check the extension against the whitelist, the size (> 0 and ≤ `MaxFileSizeBytes`), and the file signature.
  5. Reset the stream position and scan it, treating an exception as rejection. Reset the position again afterwards.
  6. Build the path `$"{user.UserId}/{(projectId?.ToString() ?? "personal")}/{Guid.NewGuid():N}{ext}"`.
  7. Call `UploadAsync` on storage.
  8. Add the `Document` with the canonical MIME type and the `Upload` activity, then call `SaveChangesAsync`.
  9. In a `catch`, call `DeleteAsync(path)`, log the error, and return the failure message.

  Ignore `request.TaskId` for now; US6 implements it.
- [ ] T028 [US1] Add `Task<List<DocumentListItem>> GetMyDocumentsAsync(UserContext user, DocumentListQuery query)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Filter to `UploadedByUserId == user.UserId`, plus the optional Category, ProjectId and date-range filters.
  - Sort in SQL.
  - Project into `DocumentListItem` using a `Select` (no `Include` N+1).
  - Fill `Permissions` using `GetPermissions`.
- [ ] T029 [P] [US1] Create `ContosoDashboard/Shared/DocumentUploadDialog.razor` (Bootstrap modal).
  - **Parameters**: `int? FixedProjectId`, `int? TaskId`, `EventCallback OnUploaded`.
  - **Inputs**: `<InputFile multiple @key="inputKey" OnChange=…>`; a per-file editable title, defaulting to the file name without extension; shared fields Category (`DocumentCategories.All`, required), Description, Tags (comma-separated text) and Project.
    - Project options come from `IProjectService.GetUserProjectsAsync`. The field is locked when `FixedProjectId` is set.
    - Personal Files is disabled when a project is selected or fixed, and vice versa.
  - **Client checks**: before copying, flag files larger than 25 MB with the exact message.
  - **Upload loop**, one file at a time:
    1. Capture name, size and content type.
    2. Copy `OpenReadStream(DocumentFileRules.MaxFileSizeBytes)` into a `MemoryStream` in 81,920-byte chunks, updating that file's progress % and calling `StateHasChanged`.
    3. Set `Position = 0`, drop the `IBrowserFile` reference, and call `UploadAsync`.
  - **Result**: show a success or error message for each file. After the batch, regenerate `inputKey` and invoke `OnUploaded`.
  - **Click budget**: no more than 3 clicks — Upload → choose files → Submit (FR-011).
- [ ] T030 [P] [US1] Create `ContosoDashboard/Shared/DocumentList.razor`:
  - **Parameters**: `List<DocumentListItem> Items` plus show/hide flags for the Project, Uploader and Shared-by columns.
  - **Table**: Bootstrap responsive table with Title, Category, Uploaded (local date/time), Size (human-readable KB/MB), Project and Uploader.
  - **Empty state**: shows a configurable message.
  - **Action buttons**: leave an `ActionsTemplate` (`RenderFragment<DocumentListItem>`) slot; later stories fill it.
- [ ] T031 [US1] Create `ContosoDashboard/Pages/Documents.razor` with `@page "/documents"` and `@attribute [Authorize]`.
  - Resolve `UserContext` from `AuthenticationStateProvider` using `ToUserContext()`.
  - Add Bootstrap nav tabs. The "My Documents" tab has:
    - sort select and asc/desc toggle;
    - filters: Category, Project (user's projects), From/To dates;
    - an "Upload" button that opens `DocumentUploadDialog`;
    - a `DocumentList`.
  - Reload the list after upload or when filters change.
- [ ] T032 [P] [US1] Add a `NavLink href="documents"` entry labelled "Documents" with the `bi-file-earmark-text` icon to `ContosoDashboard/Shared/NavMenu.razor`, after Projects

**Checkpoint**: T025–T026 green. Quickstart V1–V6 and V8 pass (the search part of V6 waits for US4; use My Documents views instead).

---

## Phase 4: User Story 2 - Download, Preview, Edit, Replace, and Delete Documents (Priority: P1)

**Goal**: Users retrieve accessible documents through an authorized endpoint and manage them according to FR-016.

**Independent Test**: Quickstart V7 and V9–V11. Preview and download work for permitted users. Edit, replace and delete work for the owner. Avery Smith gets 404 from the direct URL.

### Tests for User Story 2 ⚠️

- [ ] T033 [P] [US2] Write `ContosoDashboard.Tests/DocumentManageTests.cs`:
  - **`OpenFileAsync`**:
    - returns content plus the original file name for viewers, and `null` for non-viewers;
    - `Preview` works for PDF/PNG/JPEG and returns `null` for DOCX;
    - each successful call writes a `Download` or `Preview` activity.
  - **`UpdateMetadataAsync`**:
    - allowed for the owner, Project Manager, Team Lead (non-Personal) and Administrator; denied for a project member and a share recipient;
    - only the owner may change `ProjectId`, and only under the FR-010 / FR-010a rules;
    - changing a project document's category to Personal Files is rejected while it has a project.
  - **`ReplaceFileAsync`**:
    - owner only;
    - goes through the same validation and scan as an upload (EICAR is rejected);
    - writes the new file under a new GUID path, deletes the old file, and updates size, type and `UpdatedDate`;
    - when the DB save fails, the new file is deleted and the old one is kept.
  - **`DeleteAsync`**:
    - allowed for the owner, Project Manager, Team Lead (non-Personal) and Administrator; denied for others;
    - cascades shares and task links, deletes the file, and keeps a `Delete` activity row.

### Implementation for User Story 2

- [ ] T034 [US2] Add `Task<DocumentFile?> OpenFileAsync(int documentId, UserContext user, FileAccessMode mode)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanView`.
  - `Preview` is allowed only when `DocumentFileRules.IsPreviewable(FileType)`.
  - Call `storage.DownloadAsync`; on `FileNotFoundException`, log it and return null.
  - Write a `Download` or `Preview` activity, save, then return a `DocumentFile` with `Inline = mode == Preview`.
- [ ] T035 [US2] Add `UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanEditMetadata`.
  - Validate with the same rules as an upload.
  - `ProjectId` changes are owner-only and must pass FR-010 and FR-010a.
  - Set `UpdatedDate` and write an `EditMetadata` activity.
- [ ] T036 [US2] Add `ReplaceFileAsync(int documentId, Stream content, string originalFileName, long size, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanReplace` (owner only).
  - Run the same file validation and scan as an upload. Reuse a private `ValidateAndScanAsync` extracted from T027.
  - Store the file under a new GUID path (same folder pattern), then update `FilePath`, `OriginalFileName`, `FileType`, `FileSizeBytes` and `UpdatedDate`, write a `ReplaceFile` activity, and save.
  - If the save fails, delete the new file. If it succeeds, delete the old file (best effort; log failures).
- [ ] T037 [US2] Add `DeleteAsync(int documentId, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanDelete`.
  - Write a `Delete` activity (title and type snapshot), remove the `Document` so shares and task links cascade, and save.
  - Then delete the stored file (best effort; log failures).
- [ ] T038 [P] [US2] Create `ContosoDashboard/Pages/DocumentFile.cshtml` and `DocumentFile.cshtml.cs`:
  - Route `@page "/documents/{id:int}/file"`, with `[Authorize]` on the PageModel.
  - **`OnGetAsync(int id, string? mode)`**:
    - `mode` is `null` or `"download"` → Download, `"preview"` → Preview, anything else → `BadRequest()`.
    - Call `IDocumentService.OpenFileAsync(id, User.ToUserContext(), mode)`.
    - If it returns null: give 400 when `mode=preview` targets an existing, viewable but non-previewable document (check with `GetPermissionsAsync` + `GetDocumentAsync`). Otherwise return `NotFound("Document not found or you do not have access.")`.
  - Set `Cache-Control: no-store, private`.
  - **Download**: `File(stream, contentType, fileName)`.
  - **Preview**: `File(stream, contentType)` with a `Content-Disposition: inline; filename*=UTF-8''…` header built via `ContentDispositionHeaderValue`.
  - Do not touch the global security headers.
- [ ] T039 [P] [US2] Create `ContosoDashboard/Shared/ConfirmDialog.razor`: a Bootstrap modal with parameters `string Message`, `bool Visible`, `EventCallback<bool> OnClose`, and Cancel / Delete buttons (FR-026)
- [ ] T040 [P] [US2] Create `ContosoDashboard/Shared/DocumentEditDialog.razor`:
  - **Parameters**: `int DocumentId`, `EventCallback OnSaved`.
  - **Loading**: fetch the document with `GetDocumentAsync`, and get `GetPermissionsAsync` for the current user.
  - **Edit form**: Title, Description, Category, Tags, plus Project (shown only when `CanReplace`, i.e. for the owner, and filtered by the Personal Files rule).
  - **"Replace file" section**: shown only when `CanReplace`. It uses a single `InputFile` with the same capture-metadata → `MemoryStream` → clear-reference pattern as T029.
  - **Errors**: show service errors verbatim.
- [ ] T041 [US2] Wire actions into `DocumentList` through an `ActionsTemplate` in `ContosoDashboard/Pages/Documents.razor` (depends on T034–T040). Each button is shown only when `item.Permissions` allows it:
  - **Preview**:
    - PDF → `<a target="_blank" rel="noopener" href="/documents/{id}/file?mode=preview">`;
    - JPEG/PNG → open an image modal containing `<img src="/documents/{id}/file?mode=preview">`.
  - **Download**: `<a href="/documents/{id}/file">`.
  - **Edit**: `DocumentEditDialog`.
  - **Delete**: `ConfirmDialog` → `DeleteAsync`.
  - After any change, reload the list and show the service message.

**Checkpoint**: T033 green. Quickstart V7 and V9–V11 pass. **MVP complete (US1 + US2).**

---

## Phase 5: User Story 3 - Project Documents (Priority: P2)

**Goal**: Project members see project documents. The Project Manager uploads and manages them. Members are notified of new project documents.

**Independent Test**: Quickstart V12–V14. Camille uploads to project 1, and Floris and Ni are notified and see the document. Ni cannot delete Camille's document. Avery Smith sees nothing.

### Tests for User Story 3 ⚠️

- [ ] T042 [P] [US3] Write `ContosoDashboard.Tests/ProjectDocumentTests.cs`:
  - **Listing**: `GetProjectDocumentsAsync` returns every project document for the PM, members and an Administrator, and an empty list for a non-member (Avery).
  - **Notifications**: an upload with `ProjectId` creates exactly one `DocumentAdded` notification for each of the project manager and members, excluding the uploader. Setting `ProjectId` through `UpdateMetadataAsync` notifies the same way.
  - **Delete rights**: the PM can delete a member's document; a member cannot delete another member's document.

### Implementation for User Story 3

- [ ] T043 [US3] Add `GetProjectDocumentsAsync(int projectId, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Return an empty list unless the user is an Administrator or the project's manager or a member.
  - Otherwise return `AccessibleDocuments(user).Where(d => d.ProjectId == projectId)` projected to `DocumentListItem`, with permissions.
- [ ] T044 [US3] Add `private Task NotifyProjectDocumentAddedAsync(Document doc, int actorUserId)` in `ContosoDashboard/Services/DocumentService.cs`:
  - Recipients: the project manager plus all `ProjectMembers`, excluding the actor, de-duplicated.
  - For each, call `INotificationService.CreateNotificationAsync` with `Type = NotificationType.DocumentAdded`, `Priority = Informational`, `Title = "New project document"` and `Message = $"\"{doc.Title}\" was added to {project.Name}."`.
  - Call it after a successful save in `UploadAsync` and in `UpdateMetadataAsync` when `ProjectId` changes from null or another value to a project.
- [ ] T045 [US3] Add a "Documents" card to `ContosoDashboard/Pages/ProjectDetails.razor`:
  - Inject `IDocumentService`.
  - List documents with `DocumentList` (Uploader column on).
  - Add an "Upload" button that opens `DocumentUploadDialog` with `FixedProjectId = projectId`; it is visible to the PM and members.
  - Add Preview, Download, Edit and Delete actions using the same pattern as T041.
  - Reload after changes.

**Checkpoint**: T042 green. Quickstart V12–V14 pass.

---

## Phase 6: User Story 4 - Search Documents (Priority: P2)

**Goal**: Users find accessible documents by title, description, tags, uploader name or project name within 2 seconds.

**Independent Test**: Quickstart V15 and the search half of V6. Ni finds documents tagged "budget"; Avery finds none of them.

### Tests for User Story 4 ⚠️

- [ ] T046 [P] [US4] Write `ContosoDashboard.Tests/DocumentSearchTests.cs`:
  - **Match fields**: the term matches Title, Description, Tags, uploader DisplayName and Project Name, case-insensitive.
  - **Access filtering**: results never include documents outside `AccessibleDocuments`. A Team Lead does not get a teammate's Personal Files document; Avery gets nothing.
  - **Special characters**: terms containing `%` or `_` are matched literally.
  - **Empty input**: a blank term returns an empty list.

### Implementation for User Story 4

- [ ] T047 [US4] Add `SearchAsync(string term, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Trim the term; return empty when it is blank.
  - Escape `\`, `%` and `_`, then match with `EF.Functions.Like(field, $"%{escaped}%", "\\")` over Title, Description, Tags, `UploadedByUser.DisplayName` and `Project.Name`.
  - Start from `AccessibleDocuments(user)`, sort by `UploadedDate` descending, project to `DocumentListItem` and fill permissions.
- [ ] T048 [US4] Add a "Search" tab to `ContosoDashboard/Pages/Documents.razor`:
  - A search input that submits on Enter or the button, and results in `DocumentList` with the Uploader and Project columns.
  - Reuse the T041 actions.
  - Empty state: "No documents match your search."

**Checkpoint**: T046 green. Quickstart V15 passes.

---

## Phase 7: User Story 5 - Share Documents (Priority: P3)

**Goal**: Owners share documents with users or teams (snapshot) and can revoke shares. Recipients are notified and see the documents in "Shared with Me".

**Independent Test**: Quickstart V16–V18. Ni shares with Avery and with the Marketing team; Avery is notified once and can view only. After revoke, Avery gets 404.

### Tests for User Story 5 ⚠️

- [ ] T049 [P] [US5] Write `ContosoDashboard.Tests/DocumentShareTests.cs`:
  - **Sharing with a user**: creates one share and one `DocumentShared` notification.
  - **Sharing with a department**: creates shares for current members only, with `SharedViaDepartment` set. A user added to that department afterwards has no access. A user who leaves the department keeps access.
  - **Duplicates**: the owner and existing recipients are skipped, with no duplicate notification. Sharing again gives a share count of 0.
  - **Non-owners**: they cannot share, including the PM and Administrator.
  - **Recipient rights**: a recipient cannot edit, delete, replace or share.
  - **Revoke**: removes access immediately and is owner-only.
  - **Shared with Me**: `GetSharedWithMeAsync` lists the shared documents with sharer name and date.

### Implementation for User Story 5

- [ ] T050 [US5] Add `ShareAsync(int documentId, IReadOnlyList<int> userIds, IReadOnlyList<string> departments, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanShare` (owner only).
  - Resolve departments to the users currently in them (snapshot) and merge them with `userIds`.
  - Exclude the owner and users who already have a share.
  - Insert `DocumentShare` rows. Set `SharedViaDepartment` when the user came from a department, and write one `Share` activity per recipient with the recipient's name in `Details`. Save.
  - Create a `DocumentShared` notification for each new recipient (`Title = "Document shared with you"`, `Message = $"{ownerName} shared \"{doc.Title}\" with you."`).
  - Return the number of shares created.
- [ ] T051 [US5] Add `RevokeShareAsync(int documentShareId, UserContext user)`, `GetSharesAsync(int documentId, UserContext user)` and `GetSharedWithMeAsync(UserContext user)` to `IDocumentService` and implement them in `ContosoDashboard/Services/DocumentService.cs`:
  - Revoke is owner-only and writes a `RevokeShare` activity.
  - `GetSharesAsync` is owner-only and includes `SharedWithUser`.
  - `GetSharedWithMeAsync` projects shares for `user.UserId`, with `SharedByName` and `SharedDate`, newest first.
- [ ] T052 [P] [US5] Create `ContosoDashboard/Shared/DocumentShareDialog.razor`:
  - **Parameter**: `int DocumentId`.
  - **Pickers**: a user multi-select from `IUserService.GetAllUsersAsync`, excluding the current user, and a department multi-select built from the distinct non-empty `User.Department` values.
  - **Share button**: shows "Shared with N new recipient(s)".
  - **Current shares**: listed from `GetSharesAsync`, each with a Revoke button.
- [ ] T053 [US5] Update `ContosoDashboard/Pages/Documents.razor`:
  - Add a "Share" action (shown when `Permissions.CanShare`) that opens `DocumentShareDialog`.
  - Add a "Shared with Me" tab with `DocumentList` (Shared-by column on) loaded from `GetSharedWithMeAsync`, with Preview and Download actions.

**Checkpoint**: T049 green. Quickstart V16–V18 pass.

---

## Phase 8: User Story 6 - Task Document Integration (Priority: P3)

**Goal**: Users see, upload and attach documents from a task detail page. Attached documents are tied to the task's project under the clarified rules.

**Independent Test**: Quickstart V19–V21. Ni uploads from task 3, and the document appears on the task and in project 1. A Personal Files document or one from another project cannot be attached. Avery cannot open `/tasks/1`.

### Tests for User Story 6 ⚠️

- [ ] T054 [P] [US6] Write `ContosoDashboard.Tests/TaskAttachmentTests.cs`:
  - **Attach with no project**: attaching a no-project document to a task in project 1 links it and sets `ProjectId = 1`.
  - **Same project**: attaching a document already in project 1 succeeds.
  - **Other project**: attaching a document from project 2 is rejected with "This document belongs to another project and cannot be attached to this task." and `ProjectId` stays unchanged.
  - **Personal Files**: attaching one is rejected with the Personal Files message.
  - **Task without a project**: accepts only no-project documents, and `ProjectId` stays null.
  - **No task access**: a user without access to the task (Avery) gets `NotFoundMessage`.
  - **Upload from a task**: `UploadAsync` with `TaskId` creates both the document and the `TaskDocument` with the task's project, and rejects Personal Files.
  - **Attachable list**: `GetAttachableDocumentsAsync` excludes Personal Files documents, documents from other projects and documents already attached.
  - **Duplicates**: attaching the same document twice is a no-op success.

### Implementation for User Story 6

- [ ] T055 [US6] Add `AttachToTaskAsync(int taskId, int documentId, UserContext user)`, `GetTaskDocumentsAsync(int taskId, UserContext user)` and `GetAttachableDocumentsAsync(int taskId, UserContext user)` to `IDocumentService` and implement them in `ContosoDashboard/Services/DocumentService.cs`:
  - Check task access with `ITaskService.GetTaskByIdAsync(taskId, user.UserId)`; null means `NotFoundMessage`.
  - The document must be visible to the user and its category must not be Personal Files.
  - The document's `ProjectId` must be null or equal to `task.ProjectId`. When it is null and the task has a project, set it and call `NotifyProjectDocumentAddedAsync`.
  - Insert the `TaskDocument` (skip if it already exists) and write an `AttachToTask` activity.
- [ ] T056 [US6] Extend `UploadAsync` in `ContosoDashboard/Services/DocumentService.cs` for `request.TaskId`:
  - Load the task through `ITaskService` (null means not found).
  - Force `ProjectId = task.ProjectId` and reject Personal Files.
  - Add the `TaskDocument` in the same `SaveChangesAsync` as the document.
- [ ] T057 [US6] Create `ContosoDashboard/Pages/TaskDetails.razor` with `@page "/tasks/{taskId:int}"` and `@attribute [Authorize]`:
  - Load the task with `ITaskService.GetTaskByIdAsync`. When it returns null, show "Task not found or you do not have access."
  - Show the task's title, description, status, priority, due date, project and assignee.
  - **"Documents" panel**:
    - `DocumentList` of `GetTaskDocumentsAsync` with Preview and Download actions;
    - an "Upload" button opening `DocumentUploadDialog` with `TaskId` and `FixedProjectId = task.ProjectId`;
    - an "Attach existing" select fed by `GetAttachableDocumentsAsync` → `AttachToTaskAsync`;
    - service messages shown verbatim.
- [ ] T058 [P] [US6] Replace the TODO in `ViewTaskDetails` in `ContosoDashboard/Pages/Tasks.razor` with `NavigationManager.NavigateTo($"/tasks/{taskId}")`, injecting `NavigationManager`

**Checkpoint**: T054 green. Quickstart V19–V21 pass.

---

## Phase 9: User Story 7 - Dashboard Integration (Priority: P4)

**Goal**: The dashboard shows a document count card and the five most recent uploads.

**Independent Test**: Quickstart V22. With 6 uploads the count is 6 and the widget shows the 5 newest; with 0 uploads both show empty or zero states.

### Tests for User Story 7 ⚠️

- [ ] T059 [P] [US7] Write `ContosoDashboard.Tests/DashboardDocumentTests.cs`:
  - `DashboardService.GetDashboardSummaryAsync(userId).DocumentCount` counts only documents uploaded by that user.
  - `GetRecentUploadsAsync(user, 5)` returns the 5 newest, newest first.

### Implementation for User Story 7

- [ ] T060 [P] [US7] Add `public int DocumentCount { get; set; }` to `DashboardSummary` and fill it with `CountAsync(d => d.UploadedByUserId == userId)` in `ContosoDashboard/Services/DashboardService.cs`
- [ ] T061 [US7] Add `GetRecentUploadsAsync(UserContext user, int count = 5)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`: the user's own uploads ordered by `UploadedDate` descending, `Take(count)`, projected with permissions
- [ ] T062 [US7] Update `ContosoDashboard/Pages/Index.razor`:
  - Add a fifth summary card "Documents" showing `summary.DocumentCount` with the `bi-file-earmark-text` icon.
  - Add a "Recent Documents" section listing `GetRecentUploadsAsync` results (title linked to download, category, date).
  - Empty state: "You haven't uploaded any documents yet." with a link to `/documents`.

**Checkpoint**: T059 green. Quickstart V22 passes.

---

## Phase 10: User Story 8 - Activity Tracking and Admin Reports (Priority: P4)

**Goal**: Every document action is logged. Administrators view usage reports for a date range.

**Independent Test**: Quickstart V23–V24. The Administrator's report reflects earlier activity, including deleted documents. Ni Kang is denied.

### Tests for User Story 8 ⚠️

- [ ] T063 [P] [US8] Write `ContosoDashboard.Tests/DocumentReportTests.cs`:
  - **Logging**: each of `Upload`, `Download`, `Preview`, `EditMetadata`, `ReplaceFile`, `Delete`, `Share`, `RevokeShare` and `AttachToTask` writes exactly one activity with the correct actor and action.
  - **Access**: `GetActivityReportAsync` fails for every role except Administrator.
  - **Report content**: shows uploads grouped by FileType, top 10 uploaders, top 10 documents by Download+Preview and per-user access counts, within an inclusive `[from, to]` range.
  - **Deleted documents**: they still appear by their title snapshot.

### Implementation for User Story 8

- [ ] T064 [US8] Add `GetActivityReportAsync(DateTime fromUtc, DateTime toUtc, UserContext user)` to `IDocumentService` and implement it in `ContosoDashboard/Services/DocumentService.cs`:
  - Return `Fail` unless `user.Role == Administrator`.
  - Query `DocumentActivities` in the range and build:
    - `UploadsByType`: Action = Upload, grouped by FileType;
    - `TopUploaders`: Action = Upload, grouped by user DisplayName, top 10;
    - `TopAccessedDocuments`: Download or Preview, grouped by DocumentId and DocumentTitle, top 10;
    - `AccessByUser`: Download or Preview, grouped by DisplayName.
  - Do the grouping in SQL where EF supports it.
- [ ] T065 [US8] Create `ContosoDashboard/Pages/DocumentReports.razor` with `@page "/documents/reports"` and `@attribute [Authorize(Policy = "Administrator")]`:
  - From/To date inputs, defaulting to the last 30 days.
  - Four Bootstrap tables, one per report list, each with an empty state.
- [ ] T066 [P] [US8] Add a "Document Reports" `NavLink href="documents/reports"` with the `bi-bar-chart` icon to `ContosoDashboard/Shared/NavMenu.razor`, wrapped in `<AuthorizeView Policy="Administrator">`

**Checkpoint**: T063 green. Quickstart V23–V24 pass.

---

## Phase 11: Polish & Cross-Cutting Concerns

- [ ] T067 [P] Add `ContosoDashboard.Tests/PerformanceSmokeTests.cs`: seed 500 accessible documents, then assert that `GetMyDocumentsAsync` and `SearchAsync` each complete in under 2 seconds (proxy for SC-002/SC-003)
- [ ] T068 [P] Update `README.md`:
  - **New pages and routes**: `/documents`, `/documents/reports`, `/tasks/{id}`, `/documents/{id}/file`.
  - **Roles and access summary**: the FR-016 table.
  - **Configuration**: `FileStorage:RootPath`, `MalwareScanning:BlockedPatterns`.
  - **Setup step**: "delete `ContosoDashboard/ContosoDashboard.db` once after upgrading".
  - **Running tests**: `dotnet test ContosoDashboard.Tests`.
  - **Training limitations**:
    - the signature scanner is not a real antivirus;
    - mock-auth claims, including Department, are captured at login;
    - files are stored locally, with the Azure swap done through `IFileStorageService`.
- [ ] T069 Security review pass over new code in `ContosoDashboard/Pages/`, `ContosoDashboard/Shared/`, `ContosoDashboard/Services/` (constitution I/III):
  - Every new routable page has `[Authorize]`, or the Administrator policy for reports.
  - No page or component references `ApplicationDbContext`.
  - Every `IDocumentService` method takes `UserContext`.
  - No user-supplied file name reaches a storage path.
  - The security headers block in `ContosoDashboard/Program.cs` is unchanged.

  Fix any finding in the file where it occurs.
- [ ] T070 Run the full validation per `specs/001-document-upload-management/quickstart.md`:
  - `dotnet build ContosoDashboard` gives 0 errors.
  - `rm -f ContosoDashboard/ContosoDashboard.db*` then `dotnet run` starts and seeds.
  - `dotnet test ContosoDashboard.Tests` is green.
  - Run quickstart V1–V26 from [quickstart.md](./quickstart.md).

  Record any deferred measurable (e.g., V25 or V26 on constrained hardware) with a justification in the PR description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational. Its UI wiring (T041) uses `Documents.razor` from US1 (T031), and `ReplaceFileAsync` reuses validation extracted from T027. The service methods themselves are independent.
- **US3, US4, US5, US7, US8**: depend on Foundational plus T027 (`UploadAsync`) to create test data. Their UI tasks reuse `DocumentList` (T030) and the action pattern from T041.
- **US6 (Phase 8)**: depends on T027 (it extends `UploadAsync`), T029 (upload dialog) and T044 (project notification helper from US3).
- **Polish (Phase 11)**: depends on every story you intend to ship.

### Story Completion Order

```text
Setup → Foundational → US1 → US2 ──┬─→ US3 ─→ US6
                                   ├─→ US4
                                   ├─→ US5
                                   ├─→ US7
                                   └─→ US8
                                          └─→ Polish
```

### Within Each Story

- Write the test file first and confirm it fails.
- Then service methods (all in `DocumentService.cs`, so they run sequentially), then components and pages.
- Finish at the story checkpoint with the quickstart scenarios.

### Shared-file caution

`ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Pages/Documents.razor` are edited by several stories. Tasks touching them are never marked [P], and stories that edit them must be merged one at a time.

---

## Parallel Examples

### Phase 2 (Foundational)

```text
Task: "T004 Create Models/DocumentCategories.cs"
Task: "T005 Create Models/Document.cs"
Task: "T006 Create Models/DocumentShare.cs"
Task: "T007 Create Models/TaskDocument.cs"
Task: "T008 Create Models/DocumentActivity.cs"
Task: "T009 Append enum values in Models/Notification.cs"
Task: "T012 Create Services/UserContext.cs"
Task: "T013 Add Department claim in Pages/Login.cshtml.cs"
Task: "T014 Create Services/Storage/IFileStorageService.cs"
Task: "T016 Create Services/Scanning/IMalwareScanner.cs"
Task: "T018 Create Services/DocumentFileRules.cs"
Task: "T020 Create ContosoDashboard.Tests/TestDb.cs"
Task: "T021 Create ContosoDashboard.Tests/FakeFileStorageService.cs"
```

### User Story 1

```text
Task: "T025 DocumentUploadTests.cs"
Task: "T026 DocumentListTests.cs"
# then, after T027/T028:
Task: "T029 Shared/DocumentUploadDialog.razor"
Task: "T030 Shared/DocumentList.razor"
Task: "T032 NavMenu Documents link"
```

### User Story 2

```text
Task: "T033 DocumentManageTests.cs"
Task: "T038 Pages/DocumentFile.cshtml(.cs)"
Task: "T039 Shared/ConfirmDialog.razor"
Task: "T040 Shared/DocumentEditDialog.razor"
```

### Across stories (after US2)

Test files for different stories can be written in parallel: T042, T046, T049, T054, T059, T063. The same goes for new components and pages in different files: T052, T057, T058, T060, T065, T066.

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Phase 1 Setup, then Phase 2 Foundational. Checkpoint: build, run, seed, tests green.
2. Phase 3 (US1). Validate V1–V6 and V8.
3. Phase 4 (US2). Validate V7 and V9–V11.
4. **Stop and demo**: secure upload, list, preview, download, edit, replace and delete. Both P1 stories are needed for a usable document lifecycle.

### Incremental Delivery

1. MVP (US1 + US2)
2. P2: US3 (project documents), then US4 (search)
3. P3: US5 (sharing), then US6 (tasks)
4. P4: US7 (dashboard), then US8 (reports)
5. Polish (T067–T070)

### Parallel Team Strategy

After the MVP, one developer owns `DocumentService.cs` and merges service tasks one story at a time. The others work in parallel on test files and on new components and pages (T052, T057, T065, etc.).

---

## Notes

- Every task names an exact file path. Constraints from data-model.md are quoted in the task text.
- [P] means a different file with no unfinished dependency.
- [USn] maps each task to a spec user story, for traceability.
- Commit after each task or logical group, and stop at any checkpoint to validate.
