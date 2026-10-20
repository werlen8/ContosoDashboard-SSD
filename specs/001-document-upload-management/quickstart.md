# Quickstart & Validation Guide: Document Upload and Management

Runnable scenarios proving the feature works end-to-end. Design details: [data-model.md](./data-model.md), [contracts/](./contracts/), [research.md](./research.md).

## Prerequisites

- .NET 9 SDK (`dotnet --version` ≥ 9.0)
- No internet or cloud services required.
- Test files (create locally):
  - `small.pdf` (any PDF ≤ 2 MB), `report.docx`, `photo.png`, `notes.txt`
  - `big.pdf` > 25 MB: `head -c 27000000 /dev/urandom > big.pdf`
  - `exactly25.pdf`: a valid PDF padded to 26,214,400 bytes (`truncate -s 26214400` on a copy of a PDF)
  - `empty.pdf`: `: > empty.pdf`
  - `fake.pdf` (renamed executable/text): `echo "MZ not a pdf" > fake.pdf`
  - `eicar.txt`: standard EICAR test string `X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*` (your OS antivirus may quarantine it — create inside an excluded folder or skip on such machines)
  - `tool.exe`: any file with `.exe` extension

## Setup

```bash
cd ContosoDashboard
rm -f ContosoDashboard.db*        # one-time: EnsureCreated does not alter an existing schema
dotnet build                      # must succeed with 0 errors
dotnet run                        # recreates and seeds DB; https://localhost:5001
```

Run automated tests (from repo root):

```bash
dotnet test ContosoDashboard.Tests
```

Expected: all tests pass, including the FR-016 permission matrix.

Seed users (login page dropdown): Administrator (IT), Camille Nicole — Project Manager (Engineering, manages project 1), Floris Kregel — Team Lead (Engineering, member of project 1), Ni Kang — Employee (Engineering, member of project 1), Avery Smith — Employee (Marketing, no projects).

## Validation scenarios

| # | Story | Steps | Expected |
|---|-------|-------|----------|
| V1 | US1 | Login **Ni Kang** → Documents → Upload `small.pdf`, title "Spec", category Reports → Submit | Progress shown; success; appears in My Documents with category, date, size, type `application/pdf` |
| V2 | US1 | Upload `report.docx`, `photo.png`, `notes.txt` together, category Team Resources | Three per-file successes; titles default to file names |
| V3 | US1 | Upload `big.pdf`; `empty.pdf`; `tool.exe`; `fake.pdf`; `eicar.txt` | Each rejected with its message from [contracts/ui-and-http.md](./contracts/ui-and-http.md); nothing listed; no new files under `ContosoDashboard/AppData/uploads` |
| V4 | US1 | Upload `exactly25.pdf` | Accepted; completes < 30 s (SC-001) |
| V5 | US1 | Choose category Personal Files + project "ContosoDashboard Development" | Blocked with Personal Files message |
| V6 | US1 | As Ni Kang upload `notes.txt` as **Personal Files** and `small.pdf` as **Reports** (no project). Login **Floris Kregel** (Team Lead, Engineering) → Search "notes" and "spec" | Sees the Reports doc (can edit metadata/delete), does **not** see the Personal Files doc |
| V7 | US1/US2 | Login **Avery Smith** → My Documents, Search; then open `/documents/{id}/file` for Ni Kang's doc ids | Nothing of Ni Kang's listed; direct URL returns 404 |
| V8 | US1 | As Ni Kang, sort My Documents by size, filter by category and date range | List order/filter correct |
| V9 | US2 | Preview `small.pdf` (opens new tab) and `photo.png` (modal); download `report.docx` | PDF renders in tab; image shows; docx downloads with original name and identical bytes (`sha256sum`) |
| V10 | US2 | Edit title/tags of a doc; replace `notes.txt` with a new `.txt` | Changes visible; size/date updated; old file gone from `AppData/uploads` |
| V11 | US2 | Delete a doc → Cancel; Delete → Confirm | Cancel: no change. Confirm: gone from lists, file removed from disk |
| V12 | US3 | Login **Camille Nicole** → Project 1 → Upload `small.pdf` | Listed in project Documents; Floris and Ni receive "document added" notification |
| V13 | US3 | Login **Ni Kang** → Project 1 → try delete Camille's doc | No delete action shown; service denies if forced |
| V14 | US3 | Login **Camille** → delete Ni's project doc | Deleted after confirmation |
| V15 | US4 | Tag a doc "budget"; search "budget", uploader name, project name as Ni Kang and as Avery | Ni sees matches < 2 s; Avery sees none |
| V16 | US5 | As Ni Kang share a doc with **Avery Smith**; share another with team **Marketing** | Avery notified; both in Avery's Shared with Me; Avery can preview/download but has no edit/delete/share actions |
| V17 | US5 | Share again with Avery | No duplicate share, no duplicate notification |
| V18 | US5 | Revoke Avery's share | Doc disappears from Avery's Shared with Me; direct URL → 404 |
| V19 | US6 | Login **Ni Kang** → Tasks → View task "Create UI mockups" → Upload `notes.txt` (category Other) | Attached to task; appears in Project 1 documents |
| V20 | US6 | Attach an existing no-project doc; try attaching a Personal Files doc | First attached and now in Project 1; second not offered/blocked |
| V21 | US6 | Login **Avery Smith** → `/tasks/1` | Access denied / not found |
| V22 | US7 | As Ni Kang open dashboard | Documents card = number uploaded; Recent Documents shows 5 newest |
| V23 | US8 | Login **Administrator** → Document Reports, range = today | Uploads by type, top uploaders, access counts reflect V1–V22 (including deleted docs' history) |
| V24 | US8 | Login **Ni Kang** → `/documents/reports` | Access denied |
| V25 | Perf | Seed ≥ 500 docs (test helper or repeated uploads) → open My Documents, search | List < 2 s (SC-002); search < 2 s (SC-003); preview < 3 s (SC-004) |
| V26 | Offline | Disconnect network → repeat V1, V9 | Works (FR-038) |

## Done criteria

- `dotnet build` 0 errors; `dotnet run` starts and seeds; `dotnet test ContosoDashboard.Tests` green.
- V1–V26 pass; any deferred measurable (e.g., V25 on slow hardware) recorded with justification in tasks/PR.
- README updated: Documents and Reports pages, `FileStorage`/`MalwareScanning` settings, "delete `ContosoDashboard.db` after upgrading", training limitations (signature scanner, mock auth claims).
