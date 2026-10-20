<!--
Sync Impact Report
==================
Version change: (unversioned template) → 1.0.0
Bump rationale: Initial ratification — all placeholders replaced with concrete governance.

Modified principles (template slot → new title):
  - [PRINCIPLE_1_NAME] → I. Security by Default (NON-NEGOTIABLE)
  - [PRINCIPLE_2_NAME] → II. Offline-First with Infrastructure Abstraction
  - [PRINCIPLE_3_NAME] → III. Layered Architecture & Separation of Concerns
  - [PRINCIPLE_4_NAME] → IV. Spec-Driven, Verifiable Delivery
  - [PRINCIPLE_5_NAME] → V. Simplicity & Training Clarity

Added sections:
  - Technology & Platform Constraints (was [SECTION_2_NAME])
  - Development Workflow & Quality Gates (was [SECTION_3_NAME])
  - Governance (filled)

Removed sections: none

Templates requiring updates (read constitution at runtime, not modified by this command):
  - .specify/templates/plan-template.md      ✅ compatible ("Constitution Check" gate consumes principles)
  - .specify/templates/spec-template.md      ✅ compatible
  - .specify/templates/tasks-template.md     ✅ compatible
  - .specify/templates/checklist-template.md ✅ compatible

Follow-up TODOs:
  - README.md states ASP.NET Core 8.0 while ContosoDashboard.csproj targets net9.0;
    align README in a separate change.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Security by Default (NON-NEGOTIABLE)

Every feature MUST preserve and extend the existing defense-in-depth security model.

- Every routable page/component that exposes user data MUST carry `[Authorize]` (or an
  equivalent policy); anonymous access is allowed only for `/login`.
- Every service method that reads or mutates user-owned data MUST receive the requesting user's
  identity and enforce authorization in the service layer (IDOR protection). UI-only checks are
  never sufficient.
- Role checks MUST follow the hierarchy Employee < TeamLead < ProjectManager < Administrator,
  using claims (NameIdentifier, Name, Email, Role, Department) issued at login.
- User input MUST be validated server-side. User-supplied values (e.g., filenames) MUST NEVER be
  used directly in file paths, SQL, or HTML; use GUID-based names, parameterized EF Core queries,
  and Razor encoding.
- Files and other protected resources MUST be stored outside `wwwroot` and served only through
  endpoints that perform authorization checks.
- Existing security headers (CSP, X-Frame-Options, etc.) MUST NOT be weakened without a
  documented justification in the plan's Complexity Tracking.

Rationale: The application's teaching value depends on demonstrating correct security patterns;
a feature that bypasses them actively teaches the wrong lesson.

### II. Offline-First with Infrastructure Abstraction

The application MUST run fully locally and offline with no cloud subscription or external
service.

- Persistence MUST use SQLite through EF Core; no feature may require another database engine.
- Every infrastructure dependency (file storage, email, messaging, scanning, etc.) MUST be
  accessed through an interface (e.g., `IFileStorageService`) registered via dependency
  injection, with a local implementation shipped in this repository.
- Business logic, pages, and the database schema MUST NOT reference a concrete infrastructure
  implementation or cloud SDK; swapping to a cloud implementation MUST require only a new
  implementation class plus DI/configuration changes.
- Stored references to external resources (e.g., file paths) MUST be relative and portable.

Rationale: Training must work anywhere, while students learn the abstraction pattern that makes
a later Azure migration (Azure SQL, Blob Storage, Entra ID) a configuration change.

### III. Layered Architecture & Separation of Concerns

Code MUST respect the existing layers: `Models` (entities) → `Data` (`ApplicationDbContext`) →
`Services` (business rules + authorization) → `Pages`/`Shared` (presentation).

- Pages and components MUST NOT access `ApplicationDbContext` directly; they call services.
- Services MUST be exposed through interfaces and registered in `Program.cs`.
- Data access MUST be async (`async`/`await`) and MUST avoid N+1 queries (use `.Include()` or
  projections); frequently filtered columns MUST be indexed.
- Entity keys MUST be integers, consistent with existing `User`, `Project`, and `TaskItem` keys.
- Multi-step operations that touch storage and database MUST order steps so failures cannot
  leave orphaned records (e.g., generate unique path → write file → save metadata).

Rationale: A consistent structure keeps the codebase predictable for learners and keeps security
enforcement centralized in the service layer.

### IV. Spec-Driven, Verifiable Delivery

All non-trivial changes MUST flow through the Spec Kit process: specification → plan → tasks →
implementation.

- Specifications MUST describe user value and testable acceptance criteria, free of
  implementation detail.
- Plans MUST pass the Constitution Check before design and again after design.
- Every user story MUST be independently testable; acceptance scenarios MUST include at least
  one authorization scenario (allowed and denied user) when the story touches protected data.
- The solution MUST build without errors (`dotnet build`) and the application MUST start and
  seed its database (`dotnet run`) before a feature is considered done. Automated tests, when
  present for a touched area, MUST pass.
- Measurable requirements stated in a spec (e.g., performance targets) MUST be verified or
  explicitly deferred with justification.

Rationale: The repository exists to teach Spec-Driven Development; traceability from
requirement to verified behavior is the core deliverable.

### V. Simplicity & Training Clarity

Solutions MUST be the simplest that satisfy the spec while remaining a correct example.

- New NuGet dependencies MUST be justified in the plan; prefer the .NET base class library and
  ASP.NET Core built-ins.
- No speculative abstractions: an interface is required for infrastructure (Principle II) and
  services (Principle III), not for every class.
- Features MUST fit the current architecture without major rewrites of existing modules.
- Known limitations and intentional training shortcuts (e.g., mock authentication) MUST be
  documented in `README.md` or the feature's spec rather than silently left in code.
- User-facing documentation (`README.md`) MUST be updated when a feature adds pages, routes,
  roles, configuration, or setup steps.

Rationale: Learners need to read and reason about the whole codebase; unnecessary complexity
obscures the patterns being taught.

## Technology & Platform Constraints

- **Runtime**: .NET 9 (`net9.0`), ASP.NET Core, Blazor Server; Razor Pages only for HTTP flows
  that require them (login/logout, file download endpoints).
- **Data**: Entity Framework Core with SQLite (`ContosoDashboard.db`); schema created via
  `EnsureCreated()` with seed data. Schema changes MUST keep seed data valid.
- **Authentication**: Cookie-based mock authentication with claims; this is a training-only
  mechanism and MUST NOT be presented as production-ready.
- **UI**: Bootstrap 5.3 and Bootstrap Icons; no additional front-end frameworks or JS build
  pipeline.
- **Blazor file handling**: Uploaded `IBrowserFile` streams MUST be copied to a `MemoryStream`
  before processing, metadata captured before opening the stream, and the reference cleared
  after use.
- **Scope**: This is a training application, not a production system. Production-only concerns
  (real identity providers, MFA, rate limiting, audit pipelines) MAY be documented as known
  limitations instead of implemented, unless a spec explicitly requires them.

## Development Workflow & Quality Gates

1. **Specify** (`/speckit.specify`) — capture requirements from stakeholder documents in
   `StakeholderDocs/`; resolve ambiguities with `/speckit.clarify`.
2. **Plan** (`/speckit.plan`) — produce design artifacts; the Constitution Check gate MUST pass
   or each violation MUST be recorded in Complexity Tracking with a justification and the simpler
   alternative rejected.
3. **Tasks** (`/speckit.tasks`) — tasks MUST be grouped by user story and reference exact file
   paths; run `/speckit.analyze` to confirm spec/plan/tasks consistency before implementing.
4. **Implement** (`/speckit.implement`) — follow task order; mark tasks complete only after
   verification.
5. **Review gate** — before merge, reviewers MUST confirm: build succeeds, app starts, service
   layer authorization covers new data paths, no infrastructure leaks into business logic, and
   documentation is updated.

## Governance

- This constitution supersedes conflicting conventions, templates, and ad-hoc practices in this
  repository. Spec Kit templates and agents read it at runtime and MUST be interpreted in
  accordance with it.
- **Amendments**: Proposed via `/speckit.constitution` with a pull request that includes the
  Sync Impact Report, the rationale for change, and any migration notes for in-flight specs or
  plans.
- **Versioning** (semantic):
  - MAJOR — removal or backward-incompatible redefinition of a principle or governance rule.
  - MINOR — new principle/section or materially expanded guidance.
  - PATCH — clarifications, wording, or typo fixes without semantic change.
- **Compliance**: Every plan MUST include a Constitution Check; every review MUST verify
  compliance with the principles above. Deviations require explicit, documented justification
  in the plan's Complexity Tracking table; unjustified deviations block merge.
- **Runtime guidance**: `README.md` and `.github/copilot-instructions.md` (when present) provide
  day-to-day development guidance and MUST remain consistent with this constitution.

**Version**: 1.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20
