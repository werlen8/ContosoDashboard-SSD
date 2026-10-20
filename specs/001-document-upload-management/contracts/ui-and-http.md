# Contract: HTTP Endpoints and UI Routes

## HTTP endpoint — document file (`Pages/DocumentFile.cshtml`)

`GET /documents/{id:int}/file?mode={download|preview}`

| Aspect | Contract |
|--------|----------|
| Auth | `[Authorize]` (cookie). Anonymous → redirect to `/login`. |
| Parameters | `id` (route, int). `mode` (query, optional, default `download`; any other value → 400). |
| Authorization | Delegated to `IDocumentService.OpenFileAsync` (FR-016). |
| 200 download | Body = file bytes; `Content-Type` = stored canonical MIME; `Content-Disposition: attachment; filename="…"; filename*=UTF-8''…` (original name). |
| 200 preview | Only for `application/pdf`, `image/jpeg`, `image/png`; `Content-Disposition: inline`. |
| 400 | `mode=preview` for a non-previewable type, or invalid `mode`. |
| 404 | Document missing, file missing in storage, **or caller not authorized** (no existence disclosure). |
| Headers | Existing global security headers unchanged (`X-Frame-Options: DENY`, `nosniff`, CSP). `Cache-Control: no-store, private`. |
| Side effects | Writes `DocumentActivity` (`Download` or `Preview`). |

No upload HTTP endpoint: uploads flow through the Blazor circuit into `IDocumentService.UploadAsync` (research R6).

## UI routes (Blazor)

| Route | Page | Access | Content | Spec |
|-------|------|--------|---------|------|
| `/documents` | `Pages/Documents.razor` | `[Authorize]` | Tabs: **My Documents** (sort/filter, upload, edit, replace, delete, share, preview/download), **Shared with Me**, **Search** | US1, US2, US4, US5 |
| `/documents/reports` | `Pages/DocumentReports.razor` | `[Authorize(Policy = "Administrator")]` | Date range; uploads by type, top uploaders, access patterns | US8 |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (modified) | existing | New **Documents** card: list, preview/download, upload (members & PM), delete (PM/owner) | US3 |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` (new) | `[Authorize]` | Task info + **Documents** panel: list, upload, attach existing | US6 |
| `/tasks` | `Pages/Tasks.razor` (modified) | existing | "View" button navigates to `/tasks/{id}` | US6 |
| `/` | `Pages/Index.razor` (modified) | existing | **Documents** summary card (count) + **Recent Documents** widget (5) | US7 |
| nav | `Shared/NavMenu.razor` (modified) | — | "Documents" link (all); "Document Reports" link (Administrator only, `AuthorizeView Policy="Administrator"`) | — |

## Reusable components (`Shared/`)

| Component | Parameters | Behavior |
|-----------|------------|----------|
| `DocumentUploadDialog.razor` | `int? FixedProjectId`, `int? TaskId`, `EventCallback OnUploaded` | Multi-file select, per-file title (default = file name w/o extension), shared category/description/tags/project; Personal Files disabled when project fixed; per-file progress + result; resets `InputFile` via `@key`. |
| `DocumentList.razor` | `List<DocumentListItem> Items`, flags for which actions to show, callbacks | Responsive table: title, category, upload date, size, project, uploader; action buttons shown per `DocumentPermissions`. |
| `DocumentEditDialog.razor` | `int DocumentId`, `EventCallback OnSaved` | Edit title/description/category/tags (+ project for owner); replace file (owner). |
| `DocumentShareDialog.razor` | `int DocumentId` | Pick users and/or departments; list current shares with revoke. |
| `ConfirmDialog.razor` | `string Message`, `EventCallback<bool> OnClose` | Deletion confirmation (FR-026). |

## User-facing messages (stable, used in acceptance tests)

| Condition | Message |
|-----------|---------|
| Too large | "File exceeds the 25 MB limit." |
| Empty | "File is empty." |
| Unsupported type | "Unsupported file type. Allowed: PDF, Word, Excel, PowerPoint, TXT, JPEG, PNG." |
| Content mismatch | "File content does not match its type." |
| Malware / scan failure | "File was rejected by the security scan." |
| Personal Files + project/task | "Personal Files cannot be linked to a project or task. Choose another category." |
| Cross-project attach | "This document belongs to another project and cannot be attached to this task." |
| Not authorized / not found | "Document not found or you do not have access." |
