# PHASES.md — PGSH Development Roadmap

**Project:** Plateforme de Gestion des Stages Hospitaliers
**Stack:** .NET 9 · ASP.NET Core Minimal API · EF Core 9 · PostgreSQL · Keycloak · React 19 · .NET Aspire

---

## ✅ Phase 0 — Foundation & Architecture

**Status: Complete**

- Clean Architecture (Domain / Application / Infrastructure / API / SharedKernel)
- .NET Aspire orchestration: PostgreSQL, Keycloak, Redis, API, MigrationService, Frontend
- Keycloak authentication (realm `pgsh`): JWT Bearer, role transformer, UserContext sync
- MediatR CQRS pipeline with `ValidationPipelineBehavior` + `RequestLoggingPipelineBehavior`
- `IEndpoint` auto-discovery pattern (reflection-based registration)
- `Result<T>` / `Error` / `ValidationError` pattern in SharedKernel
- Domain events: raised on entities, published after `SaveChangesAsync`
- EF Core 9 + Npgsql, configurations via `IEntityTypeConfiguration<T>` per domain folder
- Scalar UI + Swagger UI with Keycloak OAuth2 PKCE at `/scalar/v1`
- Serilog structured logging with correlation ID middleware
- `SyncUserMiddleware`: links Keycloak identity to local user profile on first request
- Permission-based authorization via `HasPermission` attribute + `PermissionAuthorizationHandler`
- `JsonStringEnumConverter` globally: all enums serialize as strings in API responses

---

## ✅ Phase 1 — Hospital Infrastructure

**Status: Complete**

Entities: `Center → Hospital → Service`

- Full CRUD endpoints for Centers, Hospitals, Services
- `Service.Staff`: Employee many-to-many (shadow join table)
- `Service.ServiceChef`: nullable FK to Employee with domain validation (`AssignChef` requires the employee to be in Staff and have `Position.ServiceChef`)
- `Localization` owned value object (GPS x/y/z) on both Center and Hospital
- `CenterType`, `HospitalType`, `ServiceType` enums

---

## ✅ Phase 2 — Academic Structure

**Status: Complete**

Entities: `AcademicYear → AcademicGroup`, `Level`

- `AcademicYear`: label (unique), start/end dates, `IsCurrent` flag
- `AcademicGroup`: scoped to a year, `GroupNumber` + `Label` both unique per year, `GeographicZone` for clustering
- `Level`: `(Year, AcademicProgram)` unique constraint, used as FK by both Stage and Registration
- Auto-arrange endpoint: distributes unassigned registrations into groups of configurable size
- `GET /academic-years` endpoint added: returns all years ordered by StartDate descending (`GetAcademicYearsQuery` + handler + endpoint)

---

## ✅ Phase 3 — Student & Registration Management

**Status: Complete**

Entities: `Student`, `Registration`, `History`

- `Student` extends `User` (TPH): CNE (unique), Appogee (unique), AccessGrade, BacSeries, Academy, Province, Ranking
- `Registration`: links Student ↔ AcademicYear ↔ Level ↔ AcademicGroup
- `RegistrationStatus` enum: `Pending → Active → Validated / Failed / Withdrawn`
- `FailureReasons` owned value object: Description, Notes (jsonb array), Cheat flag
- Domain methods on `Student`: `AddRegistration`, `UpdateRegistration`, `RemoveRegistration` — enforce duplicate-year constraint and block deletion of Validated registrations
- `History` audit trail with `HistoryType` enum and free-form `Metadata` (jsonb)
- Bulk registration endpoint (`CreateManyRegistrationsCommand`): O(1) duplicate detection with HashSet
- `IX_Registration_Student_Year` composite index
- **Business rule — Program mismatch**: `CreateRegistration` and `UpdateRegistration` reject a level whose `AcademicProgram` doesn't match the student's program (`RegistrationErrors.ProgramMismatch`)
- **Business rule — Chronological consistency**: `CreateRegistration` and `UpdateRegistration` reject any (level, year) pair that contradicts the student's existing registration progression — higher level must always be in a later academic year (`RegistrationErrors.ChronologicalInconsistency`)
- **`searchTerm` extended**: `GetStudentsQueryHandler` now searches CNE, Appogee, and CIN in addition to name/email

---

## ✅ Phase 4 — Internship Framework (Planning)

**Status: Complete**

Entities: `Stage`, `StageObjective`, `Cohort`, `StageSlot`, `CohortSlotAssignment`

- `Stage`: ties a curriculum unit to a `Level` with `DurationInDays` and `Coefficient`
- `StageObjective`: weighted evaluation criteria per Stage (`Weight`, `IsMandatory`)
- `Cohort`: groups an `AcademicGroup` with a `Stage` for a rotation cycle; `Label` required
- `StageSlot`: time period column (P1, P2...) belonging to a Stage — `PeriodNumber`, `Label?`, `StartDate`, `EndDate`; unique on `(StageId, PeriodNumber)`
- `CohortSlotAssignment`: grid cell — maps one Cohort to one Service in one StageSlot; unique on `(CohortId, StageSlotId)`
- Full CRUD endpoints for Stages, Levels, Cohorts
- Schedule grid endpoints: `GET /stages/{id}/schedule`, slot CRUD (`POST/PUT/DELETE /stages/{id}/slots`), assignment CRUD (`PUT/DELETE /stages/{id}/slots/{slotId}/cohorts/{cohortId}`)
- Publish/unpublish: `POST/DELETE /cohorts/{id}/publish-schedule` — creates/removes ServicePeriods with capacity check
- Migration: `ScheduleGridRework` (2026-05-23) — replaced `RotationPlans`/`RotationPlanSlots` with `StageSlots`/`CohortSlotAssignments`

---

## ✅ Phase 5 — Internship Execution & Evaluation

**Status: Complete — entities & configuration done, business logic pending**

Entities: `InternshipAssignment`, `CohortMembership`, `ServicePeriod`, `ServiceEvaluation`, `ObjectiveScore`, `AttendanceRecord`

- `InternshipAssignment`: links Registration ↔ Cohort; tracks `InternshipStatus` and `StageAssignmentResult`
- `CohortMembership`: transfer history — records every cohort a student belonged to with dates and reason
- `ServicePeriod`: actual rotation execution (real dates, `IsComplete`); linked to a `CohortRotationTemplate` if planned, NULL if ad-hoc
- `ServiceEvaluation`: one-to-one with ServicePeriod; `TotalScore` (decimal) + `SupervisorComment`
- `ObjectiveScore`: per-criterion score linked to both Evaluation and StageObjective
- `AttendanceRecord`: daily presence per ServicePeriod; `(ServicePeriodId, Date)` unique constraint
- `InternshipAssignment.FinalScore`: stored derived value — **not yet computed** (see Phase 7)

**Still needed:**
- Endpoints to create/update InternshipAssignments, ServicePeriods, Evaluations, Attendance
- Business logic to transition `InternshipStatus` through its lifecycle
- **Revalidation support**: a student who receives `Result = NonValidé` continues through their academic years and can be assigned to a future cohort doing the same stage. A new `InternshipAssignment` is created for that attempt. The old failed assignment is preserved as history. A `History(Revalidation)` record marks the start of the revalidation. Queries for "has the student passed Stage X" must check for any `Validé` result across all their assignments for that stage, not just the latest.

---

## ✅ Phase 6 — Code Quality & Full-Stack Cleanup

**Status: Complete**

### 6a — Domain & Infrastructure Hardening

- Deleted 16 orphan/dead files: `HospitalServices/` folder, `Profile.cs`, `Nature.cs`, `TokenProvider.cs`, `PasswordHasher.cs`, `IPasswordHasher.cs`, `ITokenProvider.cs`, `LoginUserCommand`, `RegisterUserCommand` (and handlers/validators)
- `RegistrationStatus` promoted from `string` to enum throughout all layers (domain → application → API → endpoints)
- `InternshipAssignment.GroupNumber` removed (redundant with Cohort → AcademicGroup.GroupNumber)
- `Cohort.Label` made required (non-nullable)
- `ServicePeriod.CohortRotationTemplateId` FK added (nullable) — tracks planned vs. ad-hoc rotations
- Fixed shadow FK notation in `RegistrationConfiguration` (`"LevelId"` → `r => r.LevelId`)
- Removed duplicate `CohortMembership` relationship definition (was defined on both sides)
- `FailureReasons.Notes` promoted to `jsonb` column type
- `PermissionAuthorizationHandler` cleaned: removed unused `IServiceScopeFactory` and commented block
- `DependencyInjection.cs` stripped of all commented legacy blocks
- `ApplicationDbContext` stripped of unused imports
- **10 new indexes:** `IX_Student_CNE` (unique), `IX_Student_Appogee` (unique, null filter), `IX_AttendanceRecord_Period_Date` (unique), `IX_Level_Year_Program` (unique), `IX_Registration_Student_Year`, `IX_InternshipAssignment_RegistrationId`, `IX_InternshipAssignment_CohortId`, `IX_ServicePeriod_ServiceId`, `IX_ServicePeriod_AssignmentId`, `IX_CohortMembership_AssignmentId`
- Migration: `Domain_Cleanup_And_Schema_Improvements`

### 6b — SharedKernel Cleanup

- Stripped redundant `using System.*` from `PaginatedResponse.cs`
- Removed redundant `public` from `IDateTimeProvider` interface member
- Fixed `BulkItemResult.IsSuccess` null check order (`Error is null || Error == Error.None`)
- Fixed `CLAUDE.md` `IEndpoint` location reference

### 6c — Application Layer Cleanup & Optimization

- **Todo deleted entirely**: Domain/Todos, Application/Todos, Infrastructure/Todos, API/Endpoints/Todos — removed from `IApplicationDbContext`, `ApplicationDbContext`, `Tags.cs`. Migration: `Remove_Todo`
- **Duplicate response types merged**: `UserResponse` → `Application/Users/UserResponse.cs` (deleted 2 duplicates). Empty `GetCohortByStageIdResponse` deleted.
- **`LevelResponse` moved** from `Students/GetById/StudentResponse.cs` to `Stages/Levels/LevelResponse.cs` — fixed the cross-domain import (`StageResponse.cs` was importing from `Students.GetById`)
- **`ToPaginatedResponseAsync` extension** added at `Application/Extensions/QueryableExtensions.cs` — eliminates the 5-step `CountAsync + Skip + Take + Select + ToListAsync + new PaginatedResponse` boilerplate. Applied to 5 GetMany handlers (Hospitals, Centers, Stages, Students, Levels)
- **`LocalizationMapper.FromCoordinates`** extracted to `Application/Hospitals/LocalizationMapper.cs` — replaces the inline ternary in 4 handlers (CreateHospital, UpdateHospital, CreateCenter, UpdateCenter)
- **`GetLevelsQuery.AcademicProgram`** fixed from `int?` to `AcademicProgram?` enum — eliminates unsafe `(int)` cast in handler
- **`GetStudentsQuery`** Appogee and CIN filters wired up in handler (were declared but silently ignored)
- `GetStagesQueryHandler` sealed
- Noise step-comments stripped from 15 handlers
- Redundant `using System.*` removed from validators

### 6d — API Layer Cleanup

- **Deleted 3 dead files**: `Users/Login.cs`, `Demo/GetDemo.cs`, `Users/Permissions.cs`
- **POST endpoints → direct Command binding**: `Hospitals/Create`, `Centers/Create`, `Services/Create`, `Students/Create` — removed inner `Request` records and all `(EnumType)int` casts; Commands are now the HTTP contract
- **PUT endpoints → proper enum types**: `Hospitals/Update`, `Centers/Update`, `Services/Update` — replaced `int HospitalType/CenterType/ServiceType` with actual enum types in Request records
- **`CreateStudentCommand`** — stripped redundant `System.*` usings, fixed `string CIN` → `string? CIN` to match domain nullability
- **Structural fixes**: sealed `Stages/GetById`, `Stages/Delete`, `Cohorts/GetByStageId`; converted 4 files to file-scoped namespaces; fixed missing `CancellationToken` in `Stages/GetById`; fixed `ISender Sender` → `ISender sender` parameter casing
- **Route consistency**: removed leading slashes from 3 routes; fixed `/api/` prefix in `Cohorts/Create` Created URL
- **`GlobalExceptionHandler`** — now matches on `DomainException` base class using its `StatusCode`/`Title` properties; automatically handles all future `DomainException` subclasses
- **`CustomResults.Problem`** — collapsed 4 redundant switch arms; fixed `GetErrors` to return `validationError.Errors` array instead of the outer error object
- Comment noise stripped from `Students/Create`, `Students/GetCurrent`, `Centers/Update`, `Stages/GetMany`

---

## ✅ Phase 7 — Scheduling Automation

**Status: Complete**

- ✅ `POST /cohorts/{id}/publish-schedule`: creates `ServicePeriod` records for each student in the cohort × each slot assignment. Capacity check removed (grid badge already warns the user; blocking publish after first cohort breaks bulk publish for all subsequent cohorts assigned to the same service).
- ✅ `DELETE /cohorts/{id}/publish-schedule`: removes all published ServicePeriods for the cohort.
- ✅ `POST /stages/{id}/schedule/auto-arrange`: capacity-proportional cyclic rotation. Each allowed service is allocated a fixed number of cohorts per period proportional to its capacity (largest-remainder method). A service queue of length N (= num cohorts) is built from these allocations. In each period the queue is read with offset `period × (N / numPeriods)`, giving every cohort a different service block each period — matching the real faculty rotation documents. No saturation: each service gets the same cohort count every period. Clears existing unpublished assignments before rewriting. Returns `{ assigned: int }`.
- ✅ **RotationGroup / Partition system** added to `AcademicGroup` (EF migration `AddRotationGroupToAcademicGroup`): persistent `string? RotationGroup` label (A, B, C…) shared across all stages in an academic year. Auto-arrange assigns labels on first run, respects them on subsequent runs (never overwrites existing labels). Optional `partitionCount` query param overrides the default (= number of allowed services). Cohorts sorted by `(RotationGroup, GroupNumber)` before building the queue so each partition occupies a contiguous block — cyclic shift moves the entire partition to a different service section each period.
- ✅ `InternshipAssignment.FinalScore` computation: `RecomputeFinalScore()` aggregates `ObjectiveScore.Score × StageObjective.Weight` inline inside `SubmitEvaluation()`.
- ✅ `InternshipStatus` lifecycle transitions: `Start`, `Validate`, `Reject` domain methods with guard rules.
- ✅ Batch attendance generation: `GenerateAttendanceCommand` creates one `AttendanceRecord` per working day for a ServicePeriod.
- ✅ `AssignmentValidatedEventHandler`: writes `History(ValidationStage)` on validation.

---

## ✅ Phase 7.1 — Partition Macro Planning & Flexible Scheduling

**Status: Complete**

Made planning and affectation work for *all groups or a single partition / window*, with a one-click macro orchestrator. Mirrors the faculty rotation sheets (`example_stage_assignement/`).

- **Shared planning services** (`Application/Stages/Planning/`, DI-registered): `PartitionAllocator`, `RotationArranger`, `StudentAffectationService`, `SchedulePublisher`, `CohortProvisioner`. Command handlers and the orchestrator share one source of truth (no nested MediatR).
- **Partition + window scoping**: `AutoArrangeStageScheduleCommand` and `AssignAllStudentsByStageCommand` take optional `PartitionLabels` + `PeriodNumbers`. New `PublishStageScheduleCommand` (stage+partition+window). Removal in auto-arrange is scoped to `targetCohorts ∩ targetSlots`, so arranging one partition's window never wipes another's.
- **`GenerateMacroPlanCommand`** (`POST /stages/macro-plan`): fans out per `(RotationGroup, StageId, PeriodNumbers)` → create cohorts → affect → arrange → optionally publish. Lenient when a stage's window has no slots yet.
- **Partitions scoped per (year, level)**: `AssignRotationGroupsCommand.LevelId`; `CohortProvisioner` matches groups to each stage's level (a label reused across levels never creates cross-level cohorts).
- **Capacity-aware allocation**: `RotationArranger` weight = `floor(capacity / avgStudents)` (whole groups a service can hold); services smaller than one group are excluded instead of force-overflowed. Saturation counted from actual per-cell load. No artificial saturation when per-period capacity ≥ demand.
- **Frontend**: `ScheduleGridModal` per-partition/window auto-arrange + scoped publish, stacking-guard alert, saturation banner listing real offending cells + full-report Drawer. `GroupsPage` Macro Plan tab: per-level partition setup, partition×stage matrix with per-cell period windows, one-click "Générer le plan" with step toggles. Per-row "Vider le groupe" + "Vider toutes" (`EmptyAllYearGroupsCommand`, `DELETE /groups/all/students`). Debounce added to the two remaining un-debounced searches (service combobox, group student search).

---

## 🔲 Phase 7.5 — Planning UX & Capacity Correctness

**Status: Planned** — upgrades surfaced while building the macro planner.

### Correctness (priority)

- **Global service capacity across stages/time** *(DONE 2026-06-03)*: occupancy was computed **per stage**, grouped by `(StageSlotId, ServiceId)`, so the same physical service used by partition A in stage X and partition B in stage Y over overlapping dates was counted separately → silent over-booking. Fixed with `ServiceOccupancyCalculator` (`Application/Stages/Planning/`): load = students on a service over any **overlapping** slot window, across all stages. Wired into the grid display (`GetStageScheduleQueryHandler`), auto-arrange saturation (`RotationArranger`), and a new **pre-publish guard** (`SchedulePublisher.EnsureCapacityAsync` → `StageErrors.CapacityExceeded`, which was previously defined-but-unused — there was no capacity check at publish at all). See NOTES.md "Capacity is measured GLOBALLY across stages".
  - **Opt-in override** *(DONE 2026-06-04)*: an `AllowOverCapacity` flag on the publish commands/endpoints (and `GenerateMacroPlanCommand`) skips the guard when explicitly enabled. Surfaced as an "Autoriser le dépassement de capacité" checkbox in the publish confirm dialogs. Default off (guard enforced).

### Published-data integrity (fixed 2026-06-03)

The "published is locked" rule was enforced inconsistently across the planning grid:

- **`DeleteStageSlot` had no published guard** — deleting a period column cascaded to delete its `CohortSlotAssignment`s and `SetNull`-orphaned the already-published `ServicePeriod`s (turning them into ad-hoc periods detached from their planning origin → silent integrity drift, risk of duplicate re-publish). Fixed: the handler now blocks with `StageErrors.SlotPublished` when any cell on the slot is published; the admin must unpublish first.
- **Bulk `ClearSlotAssignments` silently skipped published cells** and returned `{ cleared }` with HTTP 200, so the UI showed success while nothing changed (the single-cell clear already failed loudly). Fixed: the handler now returns `{ cleared, skipped }` so the UI reports "X vidés, Y ignorés (publiés)" instead of false success.

### Robustness

- **Long-running operations run to completion in the background**: mutations (e.g. "démarrer", publish, macro plan) are not aborted by navigation or by other requests, and there is no client timeout. For large cohorts, move heavy operations to a background job (or chunk + progress) and make handlers idempotent so an accidental re-trigger is safe. Consider an `AbortSignal`/timeout on `fetchBaseQuery` for read queries.

### UX

- **Per-stage capacity fit gauge**: before arranging, show "demande par période X / capacité par période Y" so admins size services up front instead of discovering saturation after.
- **One-click "ajuster les capacités"**: from the saturation drawer, bump each saturated service to its "Requis" value in one action.
- **Window picker chips** in the macro matrix (click P1/P2…) instead of typing `"1,2"`; auto-suggest the free window per partition to avoid stacking.
- **Validate referenced periods exist** per stage in the macro tab (today, periods with no matching slot are silently skipped) — flag stages whose slots aren't defined yet.
- **Student stage status — "Non planifié" vs "Planifié"** *(fixed 2026-06-03)*: `StageListPage.tsx` bucketed a stage with **no** `InternshipAssignment` into the same "Planifié" group as a genuinely `Planned` assignment. Now a stage without an assignment shows a distinct **"À venir / Non planifié"** state, separate from "Planifié" (assignment exists, status `Planned`).
- **Save a macro plan as a reusable template** across years (the A→Méd[1,2]/Chir[3,4] pattern repeats yearly).
- **Partial-group placement** *(model change)*: allow splitting a group across services (as the faculty sheet does — 9–11 + 12) to eliminate wasted seats from atomic whole-group placement. Largest impact, largest effort.

---

## 🔣 Phase 7.6 — Stage Timeline / Calendar Visualization

**Status: Phase A complete (2026-06-04) · Phase B (drag-to-edit) planned** — a Gantt/Teams-style
calendar to *see* the plan over time, drilling Year → Level → Stage → Partition.
Detailed UI breakdown lives in `PGSH.Frontend/PHASES.md`.

### Data model note (important)
A `Stage` has **no explicit dates** — only `DurationInDays`. Every date on the timeline is
**derived from `StageSlot.StartDate/EndDate`**:
- A **stage's** span (for a level/year) = `min(slot.StartDate)` … `max(slot.EndDate)` over its slots.
- A **partition's** span within a stage = min/max slot dates over the slots its cohorts occupy
  (`CohortSlotAssignment` → `StageSlot`), i.e. the period window that partition runs (A→P1–2, B→P3–4).
- No schema change needed for the read-only viewer.

### Phase A — read-only viewer (backend + frontend) — ✅ DONE 2026-06-04
- **Endpoint** `GET /academic-years/{id}/timeline?levelId=` (`GetYearTimelineQuery` in
  `Application/Stages/Timeline/`) returns the nested tree: `Level → Stage (derived start/end, slot
  count, cohort/partition count, hasSaturation) → Partition (label, derived window, cohort+student
  count, saturated)`. Built from existing `StageSlot` + `CohortSlotAssignment` +
  `AcademicGroup.RotationGroup` (year reached via `AcademicGroup.AcademicYearId`); reuses
  `ServiceOccupancyCalculator` for the saturation flag. **No schema change.**
- **Frontend** `StageTimelinePage` (route `/admin/timeline`, nav "Calendrier"): custom CSS Gantt
  (date→% offset, no heavy dep), Year picker, sticky month axis, collapsible Level rows, Stage bars
  → click opens a partition-window Drawer; saturation flagged; horizontal scroll on small screens.
- Deeper drill (partition → micro rotation per service) still reuses the existing schedule grid.

### Phase B — editable (drag to reschedule) — later
- Drag/resize a stage or partition bar → writes back to `StageSlot.StartDate/EndDate`.
- Must re-run the cross-stage capacity check (`ServiceOccupancyCalculator`) on the new dates and
  block/warn on overlap-induced over-booking; confirm before persisting; ideally undo.

### Interactive range date picker (from → to) — ✅ DONE 2026-06-04
- Added `@mantine/dates` (+ `dayjs`), CSS imported in `main.tsx`, app wrapped in `DatesProvider`
  (`locale="fr"`, Monday first). The `StageSlot` start/end inputs in `ScheduleGridModal` are now a
  single **`DatePickerInput type="range"`** (two-month popover, returns `"YYYY-MM-DD"` strings — no
  conversion needed for the `DateOnly` backend). Macro-window range selection can adopt the same control.

---

## 🔲 Phase 8 — Permission System

**Status: Stub exists — not implemented**

> ⚠ **Blocking security items found in the 2026-08-06 audit — fix these before or with Phase 8.**
> Authorization in this codebase is enforced in the **Application layer** via `ExecutionAuthorizer`, not at the
> endpoint (every route in Stages / InternshipAssignments / ServiceEvaluations is a bare `.RequireAuthorization()`).
> That is a legitimate choice, but several handlers were never wired to it:
> - 🔴 **`DelocalizeStudentCommandHandler` has no authorization at all.** A student can POST their own
>   `registrationId` with `outcome: Validated` and self-validate a stage end-to-end. Needs a policy decision
>   (Scolarité + SuperUser only?) then an `ExecutionAuthorizer` check.
> - 🔴 **IDOR on `GET internship-assignments/{id}/record` and `/fiche`** — no ownership or role check, and
>   `GET internship-assignments` is unscoped, so a student can enumerate ids and read classmates' marks,
>   supervisor comments and attendance.
> - 🟠 Assignment `Start`/`Validate`/`Reject` and the whole schedule/planning surface are likewise unchecked.
> **None of this is testable at unit level** — it needs the `WebApplicationFactory` functional suite below.

Current state: `PermissionAuthorizationHandler` checks Keycloak roles only. `PermissionProvider.GetForUserIdAsync` returns an empty set.

- Design a `Permissions` table (or derive from roles) backing `PermissionProvider`
- Map Keycloak realm roles → granular permission strings
- Wire `PermissionAuthorizationHandler` to call `PermissionProvider` instead of `IsInRole`
- Define permission constants (e.g., `students:write`, `stages:manage`, `evaluations:submit`)

---

## 🔲 Phase 9 — Notifications & Workflows

**Status: Not started**

- `UserRegisteredDomainEventHandler`: send email verification on account creation
- `StudentRegisteredDomainEventHandler`: notify coordinator on new registration
- `RegistrationUpdatedDomainEventHandler`: notify student when status changes to `Validated` or `Failed`
- Decide on transport: in-process MediatR notifications vs. outbox pattern for reliability

---

## 🔲 Phase 10 — Reporting & Analytics

**Status: Not started**

- Student internship transcript (all stages, evaluations, attendance summary)
- Cohort attendance statistics per ServicePeriod
- Hospital/Service workload view (how many students per period)
- Academic year completion report per Level
- Export to PDF/Excel

---

## ✅ Phase 11 — Frontend

**Status: Substantially complete (admin + student zones)**

### Admin zone — complete
- Global `AcademicYearContext` wraps `AdminLayout`; all pages auto-filter by selected year via `useAcademicYear()` hook. Year selector in header.
- `AdminDashboardPage` — placeholder stats
- `StudentListPage` — search, paginated table
- `AdminStudentDetailPage` — Inscriptions + Profile tabs, CreateRegistrationModal
- `AcademicYearsPage`, `LevelsPage`, `GroupsPage`, `GroupDetailPage` — full CRUD. `GroupDetailPage` has a "Vider le groupe" button (header, only visible when the group has students) that calls `DELETE /groups/{id}/students`, unassigning all students back to the unassigned pool so they can be re-arranged. `GroupsPage` table shows a `RotationGroup` badge (violet dot) per row; `EditGroupModal` includes a "Groupe de rotation" text input to override the label manually.
- `StagesPage` — search, CRUD, objectives drawer, allowed-services management
- `StageDetailPage` — stage info + objectives + allowed services + cohort management. Actions: "Tout affecter", "Grille de planning", "Publier toutes" (shows when any cohort has configured unpublished schedule), "Dépublier toutes".
- `ScheduleGridModal` — full schedule grid with `ServicePicker` per cell, slot CRUD, per-cohort publish/unpublish buttons, "Répartition auto." button. Optional `partitionCount` input next to the button. Partition filter chips appear above the grid when at least one cohort has a `rotationGroup` label — clicking a chip filters rows to that partition. Rotation group badge shown inline in the cohort label column.
- `InfrastructurePage` — Centers/Hospitals/Services tabs with CRUD and service staff/chef management
- `EmployeesPage` — full CRUD
- `AssignmentsPage` — sidebar cohort list + bulk actions (Start/Complete/Validate) + assignment table with academic year filter
- `AttendancePage` — stage→cohort→period selection + record attendance

### Student zone — complete
- Dashboard, Profile, History, Demands (stub)
- `StageListPage` — lists all stages for current registration level
- `StageDetailsPage` — stage info + objectives + assignment status + per-service-period cards (attendance summary, collapsible evaluation). Service names are clickable links to `ServiceDetailPage`.
- `ServiceDetailPage` — NEW (`/student/services/:serviceId`): service type/description, hospital info + city, OpenStreetMap embed (when GPS coordinates present), chef card, staff list.

### Employee zone — stubs
Employee shell + routing exists. `EmployeeDashboardPage`, `EmployeeProfilePage`, `EmployeeServicesPage` are placeholder stubs — not yet wired to backend endpoints.

---

## 🔲 Phase 11.4 — Test infrastructure (integration + functional)

**Status: unit suite done (260 tests, `PGSH.Tests`); integration + functional NOT started**

The current suite is 100% unit-level over `UseInMemoryDatabase`, which ignores FK constraints, unique indexes,
`OnDelete` behaviour and SQL translatability — so a whole class of defect is invisible, and authorization cannot
be covered at all. Two additions, in order:

1. **Integration — Testcontainers over real Postgres.** Spin up `postgres:17.2`, run the real migrations, move
   the handler tests off InMemory. Catches FK/constraint/translation defects. Would have caught the chef
   worklist year-filter directly (that bug was found by querying the live DB by hand, not by a test).
2. **Functional — `WebApplicationFactory` + stubbed `IUserContext` per role.** The only way to assert
   "a student gets 403 from `stages/delocalize`" and to stop the Phase 8 authorization gaps from regressing.
   Requires referencing `PGSH.API` from the test project (it currently references only Domain/Application/Infrastructure).

---

## 🔲 Phase 11.5 — Performance Audit

**Status: Not started — to be run before Phase 12**

A mandatory optimization pass before production deployment.

### Backend
- **EF Core query audit**: run `EnableSensitiveDataLogging` + `LogTo` in dev and capture all SQL; look for N+1 patterns, missing `.AsNoTracking()`, unneeded `.Include()`, and queries inside loops
- **Projection completeness**: ensure every `GetMany` handler uses `.ToPaginatedResponseAsync` with a `Select` projection (never loads full entities for list views)
- **Index coverage**: verify all FK columns used in `WHERE` / `JOIN` have indexes; check slow queries with `EXPLAIN ANALYZE` in PostgreSQL
- **Compiled queries**: consider `EF.CompileAsyncQuery` for the hottest read paths (student list, registration list)
- **Connection pooling**: verify Npgsql pool size is appropriate for expected concurrency
- **`SaveChangesAsync` batching**: multiple entity modifications in one handler should all save in a single call, never multiple `SaveChangesAsync` per request

### Frontend
- **RTK Query tag hygiene**: audit `providesTags` / `invalidatesTags` — over-broad invalidation (e.g., `['Student']` wiping all students on a single update) causes unnecessary refetches
- **Debounce coverage**: all search inputs use `useDebouncedValue(search, 350)` — verify no direct query params without debounce
- **Pagination discipline**: no endpoint called with `pageSize: 999` "load all" patterns; large reference lists (hospitals for select, levels for select) should be cached and paginated or fetched once and memoized
- **Code splitting**: verify all page-level components are `lazy()`-loaded — no accidental eager imports in `routes/index.tsx`
- **Mantine component imports**: use direct imports (`import { Button } from '@mantine/core'`) not barrel re-exports to keep bundle splits clean

---

## 🔲 Phase 12 — Production Readiness

**Status: Not started**

- Tighten CORS policy (replace `AllowAllForDev` with explicit origins)
- Implement health checks (`/health` endpoint, Npgsql + Keycloak probes)
- Enable Redis distributed cache (`builder.AddRedisDistributedCache("cache")` already stubbed)
- Environment-specific `appsettings.Production.json`
- Aspire deployment manifest for container orchestration
- Structured log shipping (Seq or equivalent)
- CI/CD pipeline

---

## 🔲 Phase 13 — Performance Hardening

**Status: Not started — run after Phase 12, before first large cohort**

Known issues identified during design review. All are low-risk at current scale (~360 students) but must be resolved before scaling.

### Confirmed defects from the 2026-08-06 audit (reproduced with probes, not yet fixed)

- **Editing an evaluation with objectives throws `DbUpdateConcurrencyException`** —
  `UpdateServiceEvaluationCommandHandler.cs:63` pre-sets `Id` on `ObjectiveScore` children of a tracked
  evaluation → EF marks them `Modified`, not `Added`. Only fires when `ObjectiveScores` is non-empty, so
  "Valider le stage" works while the other two modes break. Fix: drop the pre-set `Id`, mutate in place.
- **`MidStageTransferRescheduler.RerouteAsync` NREs on a slot-less period** — the missing-slots guard drops
  nulls, so a period with `CohortSlotAssignmentId == null` (any ad-hoc/délocalisé rotation) passes the guard and
  crashes at `:66`.
- **`RerouteAsync:79` start date is not clamped** — should be `date > target.StartDate ? date : target.StartDate`;
  currently produces periods starting before their own slot opens. `:72` and `MaterializeAtTargetAsync:168` also
  set `EndDate` with no floor at `StartDate`.
- **`ResumePeriod` shifts completed and interrupted periods** (`InternshipAssignment.cs:137` has no filter),
  back-dating closed rotations and terminal history rows.
- **Evaluation updates bypass the aggregate** — no domain event, so a mark change leaves no audit trail.
- **`EvaluationSubmittedDomainEvent` carries `null`** for validate-only modes; should publish
  `StageScoring.PeriodMark(evaluation)`.
- **Objective ids are never validated against the period's stage** — a foreign id is silently weighted 1, a
  nonexistent one dies on the FK as a 500.
- **`CompletePeriod` has no `IsStarted` guard** (unlike `PausePeriod`) — a rotation that never ran can be closed
  and then evaluated. Left deliberately uncovered by tests pending a ruling.

### Critical (data correctness)

- **`FinalScore` computation**: `InternshipAssignment.FinalScore` is declared but never written. Add a domain method `RecomputeFinalScore(IEnumerable<ServiceEvaluation>)` that aggregates `ObjectiveScore.Score × StageObjective.Weight`. Call it from a domain event handler whenever `ObjectiveScore` records change, and persist via `SaveChangesAsync`. Until this is done, score-based sorting and transcript generation return null.

- **`IsCurrent` uniqueness on `AcademicYear`**: Nothing prevents two years having `IsCurrent = true` simultaneously. Add a PostgreSQL partial unique index:
  ```sql
  CREATE UNIQUE INDEX IX_AcademicYear_IsCurrent ON "AcademicYears" (true) WHERE "IsCurrent" = true;
  ```
  Also add a guard in `UpdateAcademicYearCommandHandler` (if built) mirroring the `POST` logic.

### High (FK index gaps)

- **`Registration.LevelId` missing index**: Used in every `WHERE` clause of auto-arrange and registration queries. Add in a migration:
  ```csharp
  builder.HasIndex(r => r.LevelId).HasDatabaseName("IX_Registration_LevelId");
  ```
- **`Registration.AcademicGroupId` missing index**: Same — used in auto-arrange unassigned-student query and any group membership report.
  ```csharp
  builder.HasIndex(r => r.AcademicGroupId).HasDatabaseName("IX_Registration_AcademicGroupId");
  ```

### Medium (correctness under concurrency)

- **`GenerateScheduleCommandHandler` occupancy tracker is not concurrency-safe**: The in-memory `_occupancy` dictionary is populated from a DB snapshot at request start. Two simultaneous schedule-generation requests can both see sufficient capacity and both over-assign the same service. Fix: use `SELECT ... FOR UPDATE` on the relevant `ServicePeriods` rows, or add a post-save capacity validation step, or serialize schedule generation via a distributed lock (Redis `IDistributedLock`).

### Low (query efficiency at scale)

- **TPH table growth**: `Users` table holds Student, Employee, and base User rows with discriminator. At 10 k+ rows consider Table-Per-Type (TPT) migration to separate `Students` and `Employees` tables. Low priority at faculty scale (<1 000 students).
- **`GetStageScheduleQueryHandler` occupancy map**: The query handler loads all `ServicePeriods` with a non-null `CohortSlotAssignmentId` for the stage to build an in-memory occupancy count per cell. At large scale, filter to only periods within the requested stage's slot date range.
- **`UserContext.SyncAsync` memory cache is process-local**: In a multi-instance deployment each instance maintains a separate cache. Migrate the sync cache key to the Redis distributed cache already provisioned in `AppHost`.
