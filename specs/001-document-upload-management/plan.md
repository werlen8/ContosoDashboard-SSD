# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add secure document upload, organization, sharing, and management to ContosoDashboard. Employees upload PDF/Office/text/image files (≤ 25 MB) with title, category, optional project and tags; browse them in **My Documents**, **Shared with Me**, project and task views; search; preview/download through an authorized endpoint; edit, replace, delete, and share them. Administrators get activity reports.

Technical approach (see [research.md](./research.md)): a new `DocumentService` in the existing service layer owns all validation and authorization (one composable "accessible documents" query + a per-document permission matrix). Files are written to a local folder outside `wwwroot` through a new `IFileStorageService` (local implementation now, Azure later), after magic-byte validation and an offline `IMalwareScanner` (EICAR/signature scanner). Files are served by a `[Authorize]` Razor Page endpoint that returns 404 for unauthorized callers and logs every download/preview. Uploads use Blazor `InputFile` with chunked copy into `MemoryStream` for progress. Four new entities (Document, DocumentShare, TaskDocument, DocumentActivity) are added to the existing SQLite `EnsureCreated` schema. A new xUnit test project verifies the access matrix.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`, SDK 9.0.203)  
**Primary Dependencies**: ASP.NET Core 9 (Blazor Server + Razor Pages), EF Core 9 SQLite (existing), Bootstrap 5.3 + Bootstrap Icons (existing CDN). **No new app NuGet packages.** Test project: xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk.  
**Storage**: SQLite `ContosoDashboard.db` via EF Core `EnsureCreated()` for metadata; local filesystem `ContosoDashboard/AppData/uploads` (configurable `FileStorage:RootPath`) for file bytes  
**Testing**: New `ContosoDashboard.Tests` (xUnit, SQLite in-memory, fake storage) + manual scenarios in [quickstart.md](./quickstart.md)  
**Target Platform**: Local dev machine (Windows/Linux/macOS), browser over HTTPS `localhost:5001`; fully offline  
**Project Type**: Single ASP.NET Core web application (Blazor Server)  
**Performance Goals**: 25 MB upload < 30 s; lists ≤ 500 docs < 2 s; search < 2 s; preview < 3 s (SC-001–SC-004)  
**Constraints**: Offline, no cloud SDKs; files outside `wwwroot`; GUID storage names; no weakening of CSP / `X-Frame-Options: DENY`; integer keys; category stored as text; MIME column 255 chars; ≤ 3 clicks to upload  
**Scale/Scope**: Training app — a handful of users, up to ~500 documents per user view; 8 user stories, 39+ FRs; ~5 new pages/components, 1 endpoint, 4 entities, 3 new services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle / Rule | Pre-design | Post-design evidence |
|---|------------------|-----------|----------------------|
| I.1 | `[Authorize]` on every routable page exposing data | ✅ Planned | `/documents`, `/tasks/{id}`, `/documents/{id}/file` → `[Authorize]`; `/documents/reports` → `Policy="Administrator"` ([contracts/ui-and-http.md](./contracts/ui-and-http.md)) |
| I.2 | Service-layer authorization with requesting user (IDOR) | ✅ Planned | Every `IDocumentService` method takes `UserContext`; `AccessibleDocuments` + `GetPermissions` (research R7); 404 on denial |
| I.3 | Role hierarchy via claims incl. Department | ✅ Planned | `Department` claim added at login; Team Lead rules apply to roles ≥ TeamLead (R7) |
| I.4 | Server-side validation; no user filenames in paths | ✅ Planned | Whitelist + magic bytes + size + scan server-side; GUID paths; storage rejects paths escaping root (R1, R3) |
| I.5 | Files outside `wwwroot`, served via authorized endpoint | ✅ Planned | `AppData/uploads`; `DocumentFile.cshtml` endpoint (R5) |
| I.6 | Security headers not weakened | ✅ Planned | PDF preview opens in new tab, images via `<img>`; CSP/X-Frame-Options unchanged (R5) |
| II.1 | SQLite via EF Core only | ✅ | New entities in existing `ApplicationDbContext` |
| II.2 | Infrastructure behind interfaces with local impl | ✅ | `IFileStorageService`/`LocalFileStorageService`, `IMalwareScanner`/`SignatureMalwareScanner` |
| II.3 | No concrete infra/cloud SDK in business logic or schema | ✅ | `DocumentService` depends on interfaces only; `FilePath` relative, forward slashes |
| II.4 | Portable relative paths | ✅ | `{userId}/{projectId\|personal}/{guid}.{ext}` |
| III.1 | Pages don't touch DbContext | ✅ | Pages call `IDocumentService` / `IDashboardService`; file endpoint calls service |
| III.2 | Services behind interfaces, registered in `Program.cs` | ✅ | See [contracts/service-interfaces.md](./contracts/service-interfaces.md) |
| III.3 | Async, no N+1, indexed filters | ✅ | Projections to `DocumentListItem`; indexes listed in [data-model.md](./data-model.md) |
| III.4 | Integer keys | ✅ | All new PKs `int` |
| III.5 | No orphaned records on multi-step ops | ✅ | path → file → record; compensating delete on failure (R2) |
| IV.1 | Spec → plan → tasks flow | ✅ | spec clarified (5 Qs); this plan; `/speckit.tasks` next |
| IV.2 | Allowed + denied authorization scenario per story | ✅ | Spec stories; quickstart V6, V7, V13, V18, V21, V24; automated matrix (R15) |
| IV.3 | Build, run, seed; tests pass | ✅ Planned | Quickstart "Done criteria" |
| IV.4 | Measurable requirements verified or deferred | ✅ Planned | Quickstart V4, V15, V25 |
| V.1 | New NuGet dependencies justified | ⚠️ Justified | App: none. Test project packages → Complexity Tracking |
| V.2 | No speculative abstractions | ✅ | Interfaces only for infra + services; `DocumentFileRules` is a static class; tags as one column (R8); `GetUrlAsync` omitted (R1) |
| V.3 | Fits current architecture | ✅ | Additive; one new page per surface; reuse `ITaskService`, `INotificationService` |
| V.4 | Training shortcuts documented | ✅ Planned | README: signature scanner, mock-auth claims, delete-DB-on-upgrade |
| V.5 | README updated for new pages/routes/config/setup | ✅ Planned | Task in implementation |
| Tech | Blazor file handling (metadata first, MemoryStream, clear ref) | ✅ | R6 |
| Tech | Razor Pages only for HTTP flows needing them | ✅ | Only the file endpoint |
| Tech | Bootstrap only, no JS pipeline | ✅ | No JS added |

**Gate result**: PASS (pre-design and post-design). One justified item in Complexity Tracking; no unjustified violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── spec.md              # Feature spec (clarified)
├── plan.md              # This file
├── research.md          # Phase 0 decisions R1–R15
├── data-model.md        # Phase 1 entities, rules, indexes
├── quickstart.md        # Phase 1 validation guide (V1–V26)
├── contracts/
│   ├── service-interfaces.md   # IFileStorageService, IMalwareScanner, IDocumentService
│   └── ui-and-http.md          # File endpoint, routes, components, messages
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs                 # NEW
│   ├── DocumentCategories.cs       # NEW (static constants)
│   ├── DocumentShare.cs            # NEW
│   ├── TaskDocument.cs             # NEW
│   ├── DocumentActivity.cs         # NEW (+ DocumentActions constants)
│   ├── Notification.cs             # MOD: append DocumentShared, DocumentAdded
│   ├── Project.cs, TaskItem.cs, User.cs   # MOD: navigation collections
├── Data/
│   └── ApplicationDbContext.cs     # MOD: DbSets, relationships, indexes, seed user 5
├── Services/
│   ├── DocumentService.cs          # NEW: IDocumentService + impl, DTOs/records
│   ├── DocumentFileRules.cs        # NEW: whitelist, MIME map, signatures, size
│   ├── UserContext.cs              # NEW: record + ClaimsPrincipal.ToUserContext()
│   ├── Storage/
│   │   ├── IFileStorageService.cs  # NEW
│   │   └── LocalFileStorageService.cs  # NEW
│   ├── Scanning/
│   │   ├── IMalwareScanner.cs      # NEW
│   │   └── SignatureMalwareScanner.cs  # NEW
│   └── DashboardService.cs         # MOD: DocumentCount
├── Pages/
│   ├── Documents.razor             # NEW: My Documents / Shared with Me / Search
│   ├── DocumentReports.razor       # NEW: admin reports
│   ├── DocumentFile.cshtml(.cs)    # NEW: authorized download/preview endpoint
│   ├── TaskDetails.razor           # NEW: task detail + documents panel
│   ├── Tasks.razor                 # MOD: navigate to task details
│   ├── ProjectDetails.razor        # MOD: Documents card
│   ├── Index.razor                 # MOD: count card + Recent Documents
│   └── Login.cshtml.cs             # MOD: Department claim
├── Shared/
│   ├── DocumentUploadDialog.razor  # NEW
│   ├── DocumentList.razor          # NEW
│   ├── DocumentEditDialog.razor    # NEW
│   ├── DocumentShareDialog.razor   # NEW
│   ├── ConfirmDialog.razor         # NEW
│   └── NavMenu.razor               # MOD: Documents, Document Reports links
├── Program.cs                      # MOD: DI registrations, options binding
├── appsettings.json                # MOD: FileStorage, MalwareScanning sections
└── AppData/uploads/                # RUNTIME (git-ignored)

ContosoDashboard.Tests/             # NEW xUnit project
├── ContosoDashboard.Tests.csproj
├── TestDb.cs                       # SQLite in-memory context + seed helpers
├── FakeFileStorageService.cs
├── DocumentAuthorizationTests.cs   # FR-016 matrix, list/search filtering
├── DocumentUploadTests.cs          # validation, scan, rollback, Personal Files rule
├── DocumentShareTests.cs           # snapshot team share, revoke, duplicates
└── TaskAttachmentTests.cs          # cross-project + Personal Files rules

.gitignore                          # MOD: ContosoDashboard/AppData/
README.md                           # MOD: feature docs, config, setup, limitations
```

**Structure Decision**: Keep the single existing web project and its `Models → Data → Services → Pages/Shared` layering (constitution III). Infrastructure abstractions live in `Services/Storage` and `Services/Scanning` sub-folders. A sibling `ContosoDashboard.Tests` project is added for automated verification; no solution file is required (`dotnet test ContosoDashboard.Tests`).

## Implementation Phasing (input for /speckit.tasks)

1. **Foundation** (blocks all stories): entities + DbContext + seed user, `DocumentCategories`, `UserContext` + Department claim, `IFileStorageService`/local, `IMalwareScanner`/signature, `DocumentFileRules`, DI + config, `.gitignore`, test project scaffold.
2. **US1 + US2 (P1, MVP)**: `DocumentService` upload/list/get/permissions/edit/replace/delete/open-file; `DocumentFile` endpoint; `Documents.razor` My Documents tab; upload/list/edit/confirm components; nav link; tests for matrix/validation/rollback.
3. **US3 + US4 (P2)**: project documents + `DocumentAdded` notifications in `ProjectDetails.razor`; search tab.
4. **US5 + US6 (P3)**: share/revoke + Shared with Me + notifications; `TaskDetails.razor` + attach rules.
5. **US7 + US8 (P4)**: dashboard count + recent widget; activity report page.
6. **Polish**: README, quickstart run-through (V1–V26), performance check (V25).

## Complexity Tracking

| Violation / Deviation | Why Needed | Simpler Alternative Rejected Because |
|-----------------------|------------|-------------------------------------|
| New test project with NuGet packages (xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk) | SC-006 requires 100% of unauthorized attempts denied across a 7-actor × 5-action matrix plus list/search filtering; constitution IV requires tests to pass when present | Manual quickstart checks alone cannot reliably cover the matrix or regressions; packages are test-only, app project gains zero dependencies |
| `IFileStorageService.GetUrlAsync` from stakeholder sketch omitted | Direct/pre-signed URLs would bypass per-request authorization and download/preview audit logging (FR-012, FR-035) | Keeping it would be a speculative, unused method (constitution V) that teaches an insecure path; can be added in a future Azure implementation if needed |
