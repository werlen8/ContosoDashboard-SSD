# Data Model: Document Upload and Management

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

All keys are `int` identity columns (constitution principle III). All timestamps are UTC. Schema is created by `EnsureCreated()` (see research R14).

## Entity Relationship Overview

```text
User 1───* Document *───0..1 Project
              │ 1
              ├──* DocumentShare *───1 User (recipient), *───1 User (sharer)
              └──* TaskDocument  *───1 TaskItem, *───1 User (attacher)

DocumentActivity *───1 User (actor)        (no FK to Document — survives deletion)
Notification (existing) + NotificationType.DocumentShared, DocumentAdded
```

---

## Document (new — `Models/Document.cs`)

| Field | Type | Constraints | Source / Notes |
|-------|------|-------------|----------------|
| DocumentId | int | PK, identity | |
| Title | string | required, max 200 | FR-007 |
| Description | string? | max 2000 | FR-008 |
| Category | string | required, max 50, must be one of `DocumentCategories.All` | Stored as text (stakeholder constraint) |
| Tags | string? | max 600; normalized comma-joined list, ≤10 tags × ≤50 chars, lower-case, unique, no commas inside a tag | FR-008, research R8 |
| OriginalFileName | string | required, max 255 | Display/download name only; never used in paths |
| FileType | string | required, max 255 | Canonical MIME from whitelist (research R3) |
| FileSizeBytes | long | required, 1 … 26,214,400 | FR-003 |
| FilePath | string | required, max 500, **unique** | Relative `{uploaderId}/{projectId\|personal}/{guid}.{ext}`; generated before insert |
| UploadedByUserId | int | FK → User, required, `Restrict` delete | FR-009 |
| ProjectId | int? | FK → Project, `Restrict` delete | Null when Category = "Personal Files" (FR-010a) |
| UploadedDate | DateTime | required | Set on create |
| UpdatedDate | DateTime | required | Set on create, metadata edit, file replace |

**Navigation**: `UploadedByUser`, `Project`, `Shares` (ICollection<DocumentShare>), `TaskDocuments` (ICollection<TaskDocument>).

**Indexes**: `UploadedByUserId`, `ProjectId`, `Category`, `UploadedDate`, unique `FilePath`.

**Validation rules** (enforced in `DocumentService`, server-side):
- Title trimmed, 1–200 chars; Category ∈ `DocumentCategories.All`.
- If `Category == "Personal Files"` then `ProjectId` MUST be null (upload and edit) — FR-010a.
- Changing an existing project document's Category to "Personal Files" is rejected while `ProjectId` is set.
- `ProjectId` may be set only if requester is the project's manager or a member (Administrators exempt) — FR-010.
- File: extension in whitelist, signature matches, 0 < size ≤ 25 MB, scanner clean — FR-002–FR-005.

**Lifecycle**:

```text
(none) ──upload──▶ Active ──edit metadata / replace file──▶ Active
                     │
                     └──delete (confirmed)──▶ (removed; shares + task links cascade; file removed; activity retained)
```

## DocumentCategories (new — static class, `Models/DocumentCategories.cs`)

Constant strings: `ProjectDocuments = "Project Documents"`, `TeamResources = "Team Resources"`, `PersonalFiles = "Personal Files"`, `Reports = "Reports"`, `Presentations = "Presentations"`, `Other = "Other"`, and `All` (read-only list in display order). Not a table — fixed list (spec Key Entities).

## DocumentShare (new — `Models/DocumentShare.cs`)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| DocumentShareId | int | PK | |
| DocumentId | int | FK → Document, **Cascade** delete | |
| SharedWithUserId | int | FK → User, `Restrict` | Recipient |
| SharedByUserId | int | FK → User, `Restrict` | Always the owner (FR-016) |
| SharedDate | DateTime | required | |
| SharedViaDepartment | string? | max 100 | Set when created by a team share (snapshot, research R10) |

**Indexes**: `SharedWithUserId`; unique `(DocumentId, SharedWithUserId)` — prevents duplicate access/notifications.

**Rules**: recipient ≠ owner; only owner may create/revoke; recipients get view/download only.

## TaskDocument (new — `Models/TaskDocument.cs`)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| TaskDocumentId | int | PK | |
| TaskId | int | FK → TaskItem, **Cascade** delete | |
| DocumentId | int | FK → Document, **Cascade** delete | |
| AttachedByUserId | int | FK → User, `Restrict` | |
| AttachedDate | DateTime | required | |

**Indexes**: `TaskId`; unique `(TaskId, DocumentId)`.

**Rules** (FR-031/FR-032, clarifications Q4/Q5): requester must have task access (via `ITaskService.GetTaskByIdAsync`) and view access to the document; document Category ≠ "Personal Files"; document `ProjectId` must be null or equal task's `ProjectId`; if null and task has a project, document `ProjectId` is set to it (triggers project "document added" notification).

## DocumentActivity (new — `Models/DocumentActivity.cs`)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| DocumentActivityId | int | PK | |
| DocumentId | int | required, **no FK** | Survives document deletion (FR-035) |
| DocumentTitle | string | required, max 200 | Snapshot at time of action |
| FileType | string | required, max 255 | Snapshot (for "uploads by type" report) |
| UserId | int | FK → User, `Restrict` | Actor |
| Action | string | required, max 30 | One of `DocumentActions`: `Upload`, `Download`, `Preview`, `EditMetadata`, `ReplaceFile`, `Delete`, `Share`, `RevokeShare`, `AttachToTask` |
| Details | string? | max 500 | e.g., recipient name for Share |
| OccurredAt | DateTime | required | |

**Indexes**: `OccurredAt`, `DocumentId`, `UserId`. Immutable (insert-only).

## Changes to existing types

| Type | Change | Notes |
|------|--------|-------|
| `NotificationType` (enum) | Append `DocumentShared`, `DocumentAdded` at the end | Keeps existing stored integer values |
| `User` | Add navigation `ICollection<Document> UploadedDocuments` | Optional convenience |
| `Project` | Add navigation `ICollection<Document> Documents` | For project document list |
| `TaskItem` | Add navigation `ICollection<TaskDocument> TaskDocuments` | |
| `DashboardSummary` | Add `int DocumentCount` | Count of documents uploaded by the user (FR-034) |
| `ApplicationDbContext` | Add `DbSet`s: `Documents`, `DocumentShares`, `TaskDocuments`, `DocumentActivities`; configure FKs/delete behavior/indexes above | No document seed rows |
| Seed data | Add `User` 5: `avery.smith@contoso.com`, "Avery Smith", Department **Marketing**, Role Employee, not a member of project 1 | All existing seed users except the admin are in Engineering; a user in another department with no project membership is needed to verify the "denied" access scenarios (US1 #10, US3 #4, SC-006). Existing seed rows unchanged. |

## Non-persisted types (in `Services/`)

- **UserContext** `record(int UserId, UserRole Role, string? Department)` — built from claims (`NameIdentifier`, `Role`, `Department`) via `ClaimsPrincipal.ToUserContext()`.
- **DocumentPermissions** `record(bool CanView, bool CanEditMetadata, bool CanReplace, bool CanDelete, bool CanShare)`.
- **DocumentListItem** projection: `DocumentId, Title, Category, UploadedDate, FileSizeBytes, FileType, ProjectId, ProjectName, UploadedByUserId, UploadedByName, Tags, SharedByName?, SharedDate?, Permissions` (`DocumentPermissions`, computed by the service for the requesting user so the UI shows only allowed actions).
- **DocumentListQuery**: `SortBy (Title|UploadedDate|Category|FileSize)`, `Descending`, `Category?`, `ProjectId?`, `From?`, `To?`.
- **UploadRequest**: `Stream Content, string OriginalFileName, long Size, string Title, string? Description, string Category, int? ProjectId, IReadOnlyList<string> Tags, int? TaskId`.
- **OperationResult / OperationResult<T>**: `Succeeded`, `Error` (user-facing message), `Value`.
- **ScanResult** `record(bool IsClean, string? Reason)`.
- **DocumentActivityReport**: `UploadsByType`, `TopUploaders`, `TopAccessedDocuments`, `AccessByUser` (each a list of `(Label, Count)`).
