# Research: Document Upload and Management

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-20

All Technical Context unknowns are resolved below. Each entry: Decision → Rationale → Alternatives considered.

---

## R1. File storage abstraction and layout

**Decision**: Add `IFileStorageService` with `UploadAsync`, `DownloadAsync`, `DeleteAsync`, implemented by `LocalFileStorageService` (BCL `System.IO` only). The storage root comes from configuration `FileStorage:RootPath` (default `AppData/uploads`, resolved against the content root, outside `wwwroot`). `DocumentService` generates the relative storage path `{uploaderUserId}/{projectId|personal}/{Guid:N}.{ext}` **before** calling storage. The path is stored in the database with forward slashes, so it can be reused as an Azure blob name.

`LocalFileStorageService` combines the root and the relative path, normalizes the result with `Path.GetFullPath`, and rejects any path that resolves outside the root. This second layer guards against path traversal even though callers never pass user input.

**Rationale**: Meets FR-012/013/039 and constitution principles I and II. The GUID file name means user-supplied names never reach the filesystem, and the original name is kept only as metadata.

**Alternatives considered**:
- Storing under `wwwroot`: rejected, because files would be publicly reachable (violates FR-012 and principle I).
- Storing file bytes in SQLite: rejected. It bloats the DB and defeats the cloud-migration pattern the stakeholders want to teach.
- **`GetUrlAsync(path, expiration)` from the stakeholder sketch: omitted.** Direct or pre-signed URLs would bypass the app's per-request authorization check and the download/preview audit log (FR-035). Every retrieval goes through the authorized endpoint via `DownloadAsync`. A future Azure implementation can add it if direct-to-blob delivery is ever needed. This deviation is recorded in plan.md under Complexity Tracking.

## R2. Upload ordering and failure handling (FR-014)

**Decision**:
- **Upload**: validate → scan → generate GUID path → `UploadAsync` → insert `Document` (plus activity record) in one `SaveChangesAsync`. If the DB save throws, call `DeleteAsync(path)` and rethrow as a user-facing failure.
- **Replace**: write the new file to a new GUID path, then update the record. If the update fails, delete the new file; if it succeeds, delete the old file (best effort, logged).
- **Delete**: remove the DB record first (shares and task links cascade), then delete the file (best effort, logged).

**Rationale**: This follows constitution principle III ("generate path → write file → save metadata"). An orphaned *record* is never possible. An orphaned *file* can only be left by a failed best-effort cleanup, and it is never reachable because nothing references it.

**Alternatives considered**: DB-first then file write. Rejected: if the write fails, the record is left with no file — the exact orphan-record bug the stakeholder doc warns about.

## R3. File type validation (FR-002, FR-004)

**Decision**: A static `DocumentFileRules` class holds an extension whitelist, a canonical MIME type for each extension, and a magic-byte check for each family:

| Extensions | Canonical MIME | Signature check |
|------------|----------------|-----------------|
| `.pdf` | `application/pdf` | starts with `%PDF-` |
| `.docx`, `.xlsx`, `.pptx` | OOXML types (e.g., `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, 71 chars) | ZIP header `50 4B 03 04` |
| `.doc`, `.xls`, `.ppt` | `application/msword`, `application/vnd.ms-excel`, `application/vnd.ms-powerpoint` | OLE2 header `D0 CF 11 E0 A1 B1 1A E1` |
| `.txt` | `text/plain` | no NUL bytes in the first 8 KB |
| `.jpg`, `.jpeg` | `image/jpeg` | `FF D8 FF` |
| `.png` | `image/png` | `89 50 4E 47 0D 0A 1A 0A` |

Size rules: reject 0 bytes or more than 25 MB (26,214,400 bytes). `Document.FileType` stores the **canonical** MIME type from this table, never the browser-reported one.

**Rationale**: The BCL is enough, so no new dependency is needed (principle V). Serving the canonical type stops a client-declared `text/html` type from being rendered inline (stored XSS). The `FileType` column stays 255 characters, as the stakeholders require.

**Alternatives considered**:
- MIME-detection NuGet packages: rejected as an unnecessary dependency.
- Trusting the browser's `ContentType`: rejected, because the client can spoof it.

## R4. Malware scanning offline (FR-005, Clarification Q1)

**Decision**: `IMalwareScanner.ScanAsync(Stream) → ScanResult { IsClean, Reason }`, implemented by `SignatureMalwareScanner`. It searches the bytes for the EICAR test string and for any extra patterns listed in `MalwareScanning:BlockedPatterns` (appsettings). Exceptions inside the scanner are treated as "scan could not be completed" and the upload is rejected (fail closed). The scanner is registered through DI, so a ClamAV or Defender implementation can replace it later.

**Rationale**: Matches the clarified answer. It works offline with no install, the EICAR check makes the step testable, and it follows the principle II abstraction pattern.

**Alternatives considered**: Requiring a real AV engine (rejected in clarification); skipping scanning (rejected in clarification).

## R5. Serving files: download and preview (FR-012, FR-022, FR-023)

**Decision**: A Razor Page `Pages/DocumentFile.cshtml` at `GET /documents/{id:int}/file?mode=download|preview` with `[Authorize]`. Its handler builds a `UserContext` from claims and calls `IDocumentService.OpenFileAsync(id, user, mode)`. That method authorizes the request, logs the Download or Preview activity, and returns the stream plus metadata.
- **Download**: `Content-Disposition: attachment` with the original file name (ASP.NET Core `File(stream, mime, fileDownloadName)` encodes it safely).
- **Preview**: allowed only for `application/pdf`, `image/jpeg`, `image/png`, sent with `Content-Disposition: inline`. Requesting preview for any other type returns 400.
- Unauthorized or missing documents return **404** in both cases, so a guessed ID does not reveal whether the document exists.

**UI**: images are previewed in an `<img>` tag inside a modal (`img-src 'self'` is already allowed). PDFs open in a new browser tab pointing at the preview URL.

**Rationale**:
- Blazor Server cannot send file responses directly. The constitution names Razor Pages for file download endpoints.
- A new tab avoids iframes, so **`X-Frame-Options: DENY` and the CSP stay unchanged** (principle I).
- `X-Content-Type-Options: nosniff` (already set) plus the canonical MIME type prevent content sniffing.

**Alternatives considered**:
- Minimal API endpoint: equivalent, but the constitution specifies Razor Pages for this.
- `<iframe>`/`<embed>` PDF preview: rejected because it would require relaxing X-Frame-Options and adding `frame-src`.
- Base64 data URLs through SignalR: rejected (25 MB over the circuit, and memory pressure).

## R6. Blazor upload mechanics and progress (FR-001, FR-006, SC-001)

**Decision**: A reusable `Shared/DocumentUploadDialog.razor` uses `<InputFile multiple @key="inputKey">`. For each selected file, sequentially:
1. Capture the name, size and content type first.
2. Reject files over 25 MB on the client side (the server re-validates).
3. Copy `OpenReadStream(26_214_400)` into a `MemoryStream` in 81,920-byte chunks, updating that file's progress percentage and calling `StateHasChanged`.
4. Clear the `IBrowserFile` reference and call `IDocumentService.UploadAsync`.

After a successful batch, `@key` is regenerated to reset the input. Results are shown per file (success, or the specific error).

**Rationale**: Follows the constitution's Blazor file-handling rule and the stakeholder pattern. Chunked copying gives a real progress indicator with no JavaScript, and processing files one at a time caps memory at one file.

**Alternatives considered**: A JS `fetch` upload to an HTTP endpoint with XHR progress events. Rejected: it adds a JS pipeline and a second, antiforgery-protected endpoint.

## R7. Authorization model (FR-015, FR-016, Clarification Q2)

**Decision**: Services receive a `UserContext(int UserId, UserRole Role, string? Department)` built from claims by `ClaimsPrincipal.ToUserContext()`. `Login.cshtml.cs` adds the missing `Department` claim (as the stakeholder doc requires). All rules live in `DocumentService`:
- **Visibility, as one composable `IQueryable` filter** (`AccessibleDocuments(user)`), used by lists, search, the dashboard and point lookups. A document is visible when any of these is true:
  - the user is an Administrator;
  - the user uploaded it;
  - it belongs to a project the user manages or is a member of;
  - it has a share to the user;
  - the user is Team Lead or above, has a department, the document's category is not "Personal Files", and the uploader is in the same department.
- **Action rights**: one `GetPermissions(document, user)` method that returns `{CanView, CanEditMetadata, CanReplace, CanDelete, CanShare}` from the FR-016 table. Every mutating method checks it before doing anything.
- **Roles**: Team Lead rights apply to roles ≥ TeamLead (TeamLead, ProjectManager), following the constitution's role hierarchy. Administrators match every rule.
- **Denials**: a "not found or not allowed" outcome is returned as one indistinguishable result.

**Rationale**:
- Centralizing the rules in the service layer is IDOR protection (principle I).
- A single query filter guarantees that lists, search and counts can never disagree with point access (SC-006).
- Claims follow principle I. With mock login, claims mirror the DB at sign-in, which is acceptable for training and documented as a limitation.

**Alternatives considered**:
- ASP.NET resource-based `IAuthorizationHandler`: more ceremony, and it does not compose into LINQ for list and search filtering.
- EF global query filters: they hide data from admin reports and complicate the shared DbContext.

## R8. Tags storage

**Decision**: Store tags in one `Document.Tags` text column (max 600 characters) as a normalized, comma-joined list: trimmed, lower-cased, de-duplicated, at most 10 tags of at most 50 characters, with commas not allowed inside a tag. Search uses `LIKE` on this column.

**Rationale**: The spec only needs substring search and editing, not tag browsing or tag statistics. One column avoids an extra table, join and update logic (principle V).

**Alternatives considered**: A `DocumentTag` child table. It would be justified only by tag-level queries such as a tag cloud or exact-match facets, and none are in scope.

## R9. Search and list performance (SC-002, SC-003)

**Decision**:
- **Search**: `AccessibleDocuments(user)` combined with `EF.Functions.Like(field, %term%)` over Title, Description, Tags, uploader DisplayName and project Name, with a projection to a list DTO (no N+1).
- **Indexes**: `Documents(UploadedByUserId)`, `(ProjectId)`, `(Category)`, `(UploadedDate)`, `DocumentShares(SharedWithUserId)`, unique `(DocumentId, SharedWithUserId)`, `TaskDocuments(TaskId)`, unique `(TaskId, DocumentId)`, `DocumentActivities(OccurredAt)`, `(DocumentId)`.
- **Volume**: lists are not paged at the 500-document target. Sorting and filtering run in SQL.

**Rationale**: SQLite `LIKE` over at most a few thousand rows easily meets the 2-second target. Full-text search (FTS5) is unnecessary at training scale.

**Alternatives considered**: SQLite FTS5 virtual table. Rejected: `EnsureCreated` cannot manage it cleanly, and it is overkill at this scale.

## R10. Sharing (FR-027–FR-029, Clarification Q3)

**Decision**:
- **Team shares**: sharing with a team (department) expands immediately into one `DocumentShare` row per current department member, excluding the owner and users who already have a share. Each row records `SharedViaDepartment`. Revoking removes a single row.
- **Notifications**: one `DocumentShared` notification per newly created row.
- **Picker**: users come from `IUserService.GetAllUsersAsync`; departments are the distinct `User.Department` values.

**Rationale**: Implements snapshot semantics exactly, keeps the access check a simple `Shares.Any(s => s.SharedWithUserId == id)`, and makes the "already has access → no duplicate notification" edge case free through the unique index.

**Alternatives considered**: A team-level share row resolved at read time (live semantics). Rejected in clarification.

## R11. Notifications

**Decision**:
- Append `DocumentShared` and `DocumentAdded` to the end of the `NotificationType` enum, so existing integer values are unchanged.
- **Project document added**: notify the project manager and all members except the uploader, through the existing `INotificationService.CreateNotificationAsync`.
- Notification text includes the document title. Following the link to a deleted document shows a "document no longer available" message (the edge case in the spec).

**Rationale**: Reuses the existing notification mechanism (spec Dependencies). Appending to the enum keeps stored values valid.

## R12. Task integration (FR-031, FR-032, Clarification Q4/Q5)

**Decision**:
- Add `Pages/TaskDetails.razor` at `/tasks/{taskId:int}`. The page does not exist yet; `Tasks.razor` has a TODO in `ViewTaskDetails`, which will now navigate to it.
- The page shows task information and a "Documents" panel: the attached documents, "Upload" (reusing `DocumentUploadDialog` with the project fixed to the task's project and "Personal Files" disabled), and "Attach existing" (a picker of accessible documents whose `ProjectId` is null or equals the task's project, excluding "Personal Files").
- `DocumentService.AttachToTaskAsync` re-checks task access through the existing `ITaskService.GetTaskByIdAsync`, along with the project and category rules. A task with no project accepts only documents with no project and does not set a project.

**Rationale**: The task detail page is the minimal surface the spec needs. Reusing `ITaskService` for task authorization avoids duplicating its rules.

## R13. Activity log and reports (FR-035–FR-037)

**Decision**:
- **`DocumentActivity` table** with no foreign key to `Document`, so rows survive deletion. It stores a `DocumentId` value plus title and file-type snapshots, and an FK to the acting `User`.
- **Logging**: written in the same `SaveChangesAsync` as the action it records; for downloads and previews, just before the stream is returned.
- **Reports** (`GetActivityReportAsync(from, to, user)`, Administrator only, enforced in the service):
  - uploads grouped by FileType;
  - the top 10 uploaders;
  - the top 10 documents by downloads plus previews;
  - per-user access counts.
- **Page**: `Pages/DocumentReports.razor` with `[Authorize(Policy = "Administrator")]`.

**Rationale**: Meets the audit requirements with the existing stack. The policy on the page and the check in the service give defense in depth.

**Alternatives considered**: `ILogger`-based audit with no DB table. Rejected because reports need queryable data.

## R14. Schema creation with `EnsureCreated`

**Decision**:
- **New entities**: add them to `ApplicationDbContext`. No document seed rows are needed. One extra seed user (Avery Smith, Marketing, Employee) is added so the "different team / non-member → denied" scenarios can be tested; existing seed rows are unchanged, so seed data stays valid.
- **Existing databases**: `EnsureCreated` will not alter an existing `ContosoDashboard.db`. The quickstart and README tell developers to delete the DB file once, and the app recreates and re-seeds it.
- **Upload folder**: add `ContosoDashboard/AppData/` to `.gitignore`.

**Rationale**: Consistent with the constitution's "schema via EnsureCreated() with seed data". Introducing EF migrations would be a major change out of this feature's scope.

**Alternatives considered**: Adopting EF migrations now. Rejected as a cross-cutting rewrite (principle V).

## R15. Automated tests

**Decision**:
- **New project**: `ContosoDashboard.Tests` (xUnit, net9.0) referencing the web project.
- **Test setup**: SQLite in-memory (`DataSource=:memory:`, connection kept open), plus an in-memory fake `IFileStorageService` that can be told to throw.
- **Coverage**:
  - the FR-016 permission matrix (allowed and denied for every role);
  - list and search filtering, including Team Lead vs Personal Files;
  - validation (size, type, signature, EICAR);
  - the Personal Files and cross-project attach rules;
  - rollback when the DB save fails;
  - team-share snapshot behavior.

**Rationale**:
- SC-006 demands that 100% of unauthorized attempts are denied, and a test matrix is the only practical way to verify that.
- Principle IV requires tests to pass when present.
- The app project itself gains no new packages; the test-only packages are justified in Complexity Tracking.

**Alternatives considered**: Manual testing only via the quickstart. Rejected: it cannot cover the access matrix reliably.
