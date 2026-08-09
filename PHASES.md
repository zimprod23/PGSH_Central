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

---

## 🔲 Phase 14 — Records, printing & the admin student file

**Status: Not started — specified 2026-08-08, agreed with the user. Build in the order below.**

The parcours read model (`GET /students/{id}/parcours`, session 10) already returns every registration
a student holds with its stage attempts, and it is the backbone of all three items here. Read
["Student Parcours" in HANDOFF.md](HANDOFF.md) before starting.

### 14.1 — Printable relevé de stages (year and full cursus)

Today the only printable artefact is the **fiche de validation**, which covers **one stage**
(`GetFicheDeValidationQuery`). The student and the scolarité both need the two levels above it.

| Scope | Contents | Route (proposed) |
|---|---|---|
| **One academic year** | every stage of that registration: stage, cohort/group, rotation dates, per-period marks, final note, verdict; header with student identity, level, year, group | `GET /students/{id}/registrations/{registrationId}/releve` |
| **Whole cursus** | one block per year, most recent first, plus a summary of stages owed vs. validated across the whole course | `GET /students/{id}/releve` |

Notes and constraints:
- **Reuse `StageScoring`** for every mark shown — never recompute inline (see CLAUDE.md). The parcours
  handler's `finalNoteOf` rule applies: a mark is only a *final* note once `AllPeriodsEvaluated`; a
  partial mean must be labelled provisional or omitted from a printed document.
- **A retake must be visible as a retake.** `ParcoursStage.attemptNumber` already carries this; a
  relevé that silently prints only the passing attempt is a falsified record.
- **A stage carries its own level, not the registration's** — a cross-level revalidation printed under
  the wrong year is the single easiest way to get this wrong.
- **Read scope**: the same as the parcours — `EnsureCanReadStudentDossierAsync` (administration, or the
  student themselves). A chef must not be able to print a student's cursus.
- **Output format: client-side print** *(settled 2026-08-08)*. A React view plus a `@media print`
  stylesheet — **no server-side PDF library**. The signed artefact is produced by the signature
  service (below), not by the API.
- **No average, anywhere** *(settled 2026-08-08)*. The relevé prints each stage's own note and
  nothing else. The **only** mean in the domain is *within* a stage — the mean of its periods, which
  `StageScoring` already computes — because a stage holds several periods. There is no year average,
  no cursus average, no coefficient-weighted roll-up. `Stage.Coefficient` is **not** to be used to
  invent one. This closes the open question carried since session 10.
- Tests: happy path per scope, plus the guards (unknown student, unknown registration, forbidden
  caller), plus a student with a retake and one with a cross-level revalidation.

### 14.1b — How a signed relevé is actually obtained

*(User, 2026-08-08 — the delivery flow, already agreed in an earlier discussion.)*

The printable view is only half of it. A relevé that leaves the faculty has to be **signed**, and the
signature is the job of a **separate digital-signature microservice**, not of PGSH:

```
student requests a relevé
      → the request lands in the demandes queue          (Phase 5 — demandes)
      → scolarité generates the document                 (14.1)
      → the signature microservice signs it
      → the signed document comes back as the response to the demande
```

Consequences for how 14.1 is built:
- The **demande** is the unit of work, not the document. `DemandeType` needs a relevé variant, with a
  scope (one year / whole cursus) as its payload.
- The generated document must be **reproducible and addressable** — the response attached to a demande
  has to be the exact artefact that was signed. Decide early whether the signed blob is stored or the
  document is regenerated on demand and hashed; marks can be amended after the fact, so a regenerated
  relevé may not match the one that was signed.
- PGSH's side of the boundary is: produce the document + hand it to the service + attach what comes
  back. Do not build signing into the API.
- ⚠ **Phase 5 (demandes) is a prerequisite.** Until it exists, 14.1 ships as an
  admin/student-facing *print view* only, with no signature and no queue.

### Still open on 14.1 (blocking layout work only)

- **Header / footer assets**: the user has an official template but describes it as "quite messed
  up", and will supply **logos and the exact header/footer texts**; layout is otherwise ours to
  design. The `example_stage_assignement/` images show the house style — institution block top-left,
  document title centred, academic year top-right.
- **"Délivré le" date and the stamp/signature block** — confirmed needed in principle; exact
  placement waits on the assets above.

### 14.2 — Admin student file: all stages + all events

The student portal gained the full parcours; the **admin** side still shows a student without it.
`AdminStudentDetailPage` needs the same two views the student now has, from the scolarité's angle:

- **Stages** — every attempt across every registration, grouped by year, with state and marks. The
  `parcours` endpoint already serves exactly this and is already authorized for administrative
  callers; the admin page can consume it as-is. Reuse `ParcoursRecord` / `stageStateOf` rather than
  re-deriving the state buckets — CLAUDE.md's "change one, change both" rule applies to that mapping.
- **Events** — the student's `History` timeline (`GET /students/{id}/history`), which the admin page
  does not show at all today. The student portal's `HistoryPage` timeline and `historyConfig` are
  reusable; they currently live under `features/student/` and would need lifting to a shared place.
- Entry points to the printable relevés from 14.1 belong on this page.
- ⚠ `GetStudentHistoryQuery` currently has **no read scoping** (unlike the parcours) — it is only
  behind `RequireAuthorization()`. Scope it before exposing it more widely.

### 14.3 — Settling `Registration.Status` for past years

PGSH is not linked to the pedagogical side of the faculty, so no registration is ever closed and every
past year reads "En cours" in both portals. The inference rule agreed with the user — *a registration
is failed when a later registration exists at the same level; otherwise validated; the latest is in
progress* — plus the four cases that still need a ruling before any code is written, are documented in
[NOTES.md → "`Registration.Status` is unmanaged"](NOTES.md).

Do not start this before those cases are answered. In particular: an inferred verdict must not be
written into `Registration.Status` in a way that makes it indistinguishable from a known one, or a
later link to the pedagogical system will overwrite facts with guesses.

### 14.4 — Répartition annuelle des stages (printable level planning matrix)

**Status: Built 2026-08-08. Reference output: [`example_stage_assignement/`](example_stage_assignement/) (`Med3.png`, `Med6.png`).**

⚠ **The database holds 0 `StageSlots` and 0 `CohortSlotAssignments`**, so every level's répartition is
empty until a planning is authored — see *No periods in legacy* in NOTES.md. Verified 2026-08-08
against a temporary Med3-shaped fixture (inserted, checked, deleted): the table matched `Med3.png`,
including the row order, and the export was self-contained.

**Two defects found in that pass blocked authoring a planning at all:**
1. ✅ **`SlotOverlapGuard` forbade the published Med3 planning** — fixed 2026-08-08. It is now
   per-stage, and double-booking is enforced per group by `GroupScheduleConflictGuard` where a cohort
   is actually placed. This unlocks the real model: *periods-per-stage × partitions* periods on one
   shared axis, with the partitions crossing over (A→Médecine P1-P2 + Chirurgie P3-P4, B mirrored).
   Verified on level 3 — 320 cells, 26 rows × 4 columns, **zero empty cells**, zero double-booked
   groups. Details in NOTES.md.
2. ⚠ **25 of 27 stages have an empty `AllowedServices`**, which `RotationArranger` refuses outright.
   Only the misleading UI copy was corrected (it claimed empty meant "all services allowed"); the
   stage→service mapping itself still has to be entered per stage.

Shipped as an **export**, not a public endpoint — the faculty uploads a file to its own site rather
than PGSH serving the page. `GET /levels/{levelId}/repartition` stays behind
`RequireAuthorization()` like everything else.

- `GetLevelRepartitionQuery` + handler, `GroupNumberRanges`, `PeriodAxis` in `Application/Stages/Repartition/`
- `RepartitionPage` (admin → Formation → Répartition annuelle) previews it; the same DOM node is
  serialized into a standalone `.html` and printed to PDF, so preview, print and uploaded file are
  one document by construction rather than three implementations agreeing.

**Answers to the three open questions below**, settled during the build:

- **Do all stages share one period axis?** No — the axis is the **finest partition present**. A
  window that strictly contains another stage's window is a composite of it and is dropped, leaving
  the atoms. `Med3` (every stage on the same four periods) drops nothing; `Med6` keeps ten monthly
  columns and the two-month stages repeat their cell across each pair, carrying one `SlotId` so a
  renderer may merge them. A period claims a column by **midpoint containment**, not bare overlap —
  otherwise a window spilling a few days past a boundary seizes the next column from the stage that
  really runs there.
- **Rows with no assignment in a period?** Blank, hatched, and **counted**: `Summary.EmptyCells`
  drives an orange banner on the page. A hole is a planning gap to review, not a shorter row.
- **Is the chef printed live or frozen?** Resolved from `ServiceChefAssignment` **as of the first
  column's start date**, falling back to the sitting chef where no tenure covers it (the legacy
  import carried no trail). A répartition reprinted years later keeps naming the chef it was
  published with.

Row **and** stage order both fall out of one rule: sort by the lowest group number each line opens
on. That reproduces `Med3` (Médecine 1-40 above Chirurgie 41-80) and `Med6` (Chirurgie 1-20, ANES REA
21-30, URGS-TRAUMA 31-40…) exactly, and keeps the rotation cycle readable down the page.

The one artefact the faculty actually publishes today, and the one PGSH cannot yet produce. It is a
**planning** document: it shows *who goes where and when*, before any rotation runs. **No marks, no
results, no execution state.**

#### What it looks like

Header: institution block left, `«3ème année médecine» / «Répartition annuelle des stages»` centred,
`Année universitaire: 2025/2026` right. Then one wide table:

- **Two stacked date header rows** — period start dates, then period end dates.
- **Row identity: `Stage | Service (Chef de Service)`** — the service cell reads
  `«HMIMV: Chirurgie A - Pr.M.Bouchentouf»`, i.e. `Hospital.Name`: `Service.Name` - the service chef.
  Rows are grouped by stage (Médecine, Chirurgie, ANES REA, URGS-TRAUMA, Gynécologie Obst, Pédiatrie…).
- **Cells: collapsed group-number ranges** — `47-50` means academic groups 47, 48, 49 **and** 50 are
  in that service for that period. Single groups print bare (`27`), and Med6 shows non-contiguous
  runs are simply not merged.
- **Colour banding by rotation partition** — in `Med3` the 80 groups form four blocks of 20 and the
  bands track which partition a row's rotation belongs to. This is the existing
  `AcademicGroup.RotationGroup` / `PartitionLabels` concept, not decoration.

#### The data is already there — this is a pivot, not new modelling

Every cell is a `CohortSlotAssignment` = (Cohort, StageSlot, Service), and `Cohort → AcademicGroup`
carries the `GroupNumber` that gets printed. What is missing is only the **orientation**:

| | rows | columns | cell |
|---|---|---|---|
| existing `GetStageScheduleQuery` | cohorts | slots of **one** stage | the service |
| **needed (14.4)** | (stage, service, chef) across **the whole level** | the level's periods | group numbers |

So: a new level-scoped query that transposes `CohortSlotAssignment`, groups rows by (stage, service),
and collapses each cell's `GroupNumber`s into ranges. Proposed
`GET /levels/{levelId}/repartition?academicYearId=…` → `GetLevelRepartitionQuery`.

Notes and constraints:
- **Scope by academic year server-side.** A level's cohorts exist per year; an unscoped query returns
  every year the level ever ran (the 681-row trap in CLAUDE.md).
- **Range collapsing belongs in one place** — a small pure helper, unit-tested against the two sample
  images. `47,48,49,50 → "47-50"`; `47,48,50 → "47-48, 50"`.
- **Do not paginate**, but bound it: rows are services of one level (tens), columns are periods
  (≤ ~10). Assert that shape rather than assuming it.
- Print via the same client-side `@media print` route as 14.1 — the table is **landscape** and wide;
  that is a stylesheet decision, not a data one.

#### Why it exists, and when it dies

Students are currently emailed only their **group number**; they then consult this published table to
find their year's rotations. So 14.4 also ships as a **public, read-only web view** of the same
matrix — "enter/see your group, read your year" — not just a print.

⚠ **This is deliberately transitional.** Once every student uses the portal, the group-number lookup
is redundant: the parcours (session 10) already answers "where am I and when" per student. Build it
so it can be **switched off**, and do not let the public view accrete features that belong in the
portal.

#### Still open on 14.4

- **The reference images could not be checked against real `StageSlot` rows** — there are none. The
  axis builder handles both readings of `Med6` (five two-month slots on a shared monthly axis, or ten
  monthly ones), so the design does not depend on which it is; but the first real planning authored
  is the moment to confirm it, and the moment the whole page gets its first end-to-end pass.
- **The letterhead is hardcoded** in `RepartitionDocument.tsx` — there is no institution entity in
  the schema and one faculty publishes these. Becomes wrong the day a second one does.
- **Nothing records that a répartition was published**, so "the chef at publication" is approximated
  by "the chef when the first period starts". Close enough while the two dates are weeks apart;
  wrong if a planning is drawn up a year ahead.

---

## ✅ Phase 15 — CNPN versioning (Médecine 7 ans → 6 ans)

**Done 2026-08-08.** Arrêté **1650.25** (BO 7422, 17 juillet 2025) shortens the Médecine doctorate to
six years with effect from 2024-2025, while article 2 keeps every student registered before that year
under arrêté 2174.18 *in its pre-2175.22 form*. Two texts therefore run side by side for years, and
from 2026-2027 a single (level, year) holds students of both.

Entities: `CnpnVersion`, re-keyed `Curriculum`, `Student.CnpnVersionId`

- `Curriculum` moves from `(LevelId, AcademicYearId)` to **`(CnpnVersionId, LevelId)`** — the year
  cannot identify a requirement set once two texts govern it.
- `CnpnAssignment` resolves a student's text from their **first registration**, never from the level
  they currently sit in; the stamp is sticky and only `Student.AssignCnpnVersion` writes it.
- Migration `20260808135315_CnpnVersioning` creates the four texts, attributes each recorded
  curriculum to the text governing the intake that reached its level, **unions** the years that
  collapse onto one version, and stamps 10,185 students. Dry-run on a clone first.
- Planning became CNPN-aware: `AutoArrangeGroupsCommandHandler` splits groups by
  (year, level, version) so no group mixes texts; `CohortProvisioner` refuses a cohort for a stage the
  group's text does not require, standing aside where no set is recorded.
- API: `GET /cnpn-versions`; the curriculum routes take `{cnpnVersionId}` where they took
  `{academicYearId}`.

### ✅ Phase 15.05 — targeting: who a text binds

**Done 2026-08-08.** The first implementation hard-coded one reading of "who does this arrêté bind"
(entry year). That fits 1650.25 and nothing else — a text can equally target a programme, a level
band, or a cluster. The rule is now authored.

`Application/Stages/Cnpn/Targeting/` — `CnpnTargetCriteria` (programme + `MaxLevelYear` +
as-of year + `IncludeEntryContradictions`), `CnpnTargetPlanner` shared by
`PreviewCnpnTargetQuery` and `ApplyCnpnTargetCommand`, so the dry run is literally the plan.
API: `POST cnpn-versions/{id}/target/preview` and `POST cnpn-versions/{id}/target`.

Four properties, each load-bearing:

- **No `CnpnTargetRule` entity.** A stored rule re-evaluated later re-targets people, which defeats
  stickiness. The rule is applied once; the membership plus the audit entry are what survive.
- **Selector + standing rule.** The selector catches today's students; future intakes stay the
  version's `AppliesToEntrantsFromAcademicYearId`. Both halves are needed.
- **Bulk never overwrites a confirmed stamp** — it reports `ConfirmedOnAnotherText`. Upgrading an
  inferred stamp *is* allowed, which is how the ~2,200 deduced assignments get confirmed.
- **Disagreements are reported, not resolved.** `EntryPredatesText` is the repeater the arrêté
  excludes but "année ≤ N" catches; the faculty ticks a box or does not.

Frontend: `CnpnTargetingPanel` on the CNPN page — rule, *Simuler*, counts + the rows needing a
decision, then *Rattacher*.

### ✅ Phase 15.06 — recording the texts themselves

**Done 2026-08-08.** Layer 1 of a CNPN — the text's own identity — had no screen and no endpoint: the
four existing rows were inserted by migration, so adding an arrêté, renaming `PHARM-LEGACY` or fixing
a wrong `TotalYears` all required SQL. That was the actual blocker on 15.2.

`Application/Stages/Cnpn/Manage/` — `CreateCnpnVersionCommand`, `UpdateCnpnVersionCommand`,
`DeleteCnpnVersionCommand`, `CloneCnpnCurriculaCommand`. API: `POST cnpn-versions`,
`PUT cnpn-versions/{id}`, `DELETE cnpn-versions/{id}`, `POST cnpn-versions/{id}/clone-curricula`.

**Deletion is gated on students, not curricula.** `Users → CnpnVersions` is `NO ACTION` and
`Curriculums → CnpnVersions` is `CASCADE`, so an ungated delete either 500s on a foreign key or
silently destroys authored requirement sets. It refuses while any student is stamped (inferred stamps
count), and otherwise returns how many requirement sets the cascade removed. That is safe precisely
*because* of the gate: a text nobody follows has nobody who could owe anything. Meant for the mistyped
row — a superseded arrêté stays, because the students who followed it stay.

**« 1650.25 reprend 2174.18 »** clones every level in one act, skipping levels the target already has
(a hand edit is never overwritten) and counting levels outside its span (a 7ᵉ année has nowhere to go
in a six-year text). That turns setting up a text from six clone actions into one, then editing only
the years the arrêté actually changes — which is how an arrêté reads.

### 🔲 Phase 15.1 — the semester model (the deferred half)

The new CNPN organises **12 semesters**, not 6 years, and types its placements:

| Semesters | Placement | Credits |
|---|---|---|
| S1–S4 | immersion in the health system + nursing activities | — |
| S5–S8 | part-time clinical | 10 / semester |
| S9–S10 | full-time clinical | 20 / semester |
| S11–S12 | full-time + médecine de famille | 30 / semester |

Plus 8 "horizontal units", one per semester S1–S8.

PGSH models `Level.Year` and a free `CurriculumStage.Coefficient`, so 1650.25's requirements can only
be recorded **approximately** until this lands. Three pieces, best done together:

- **Semester granularity** on `Curriculum`/`CurriculumStage` (or a `Semester` between Level and Stage).
- **Placement type** — immersion / nursing / part-time clinical / full-time / family medicine.
- ⚠ **`Stage.LevelId` must stop pinning a stage to one level.** The new text moves stages between
  years ("les stages du 7e glissent vers le 6e"), so one `Stage` row needs two levels depending on the
  text. `CurriculumStage` already expresses `(version, level) → stage`; `Stage.LevelId` should become
  advisory and `CohortProvisioner` should read the curriculum for the level instead. Deferred with
  15.1 deliberately — solving it alone would conflict with the semester work.
- ⚠ **`Stage.Coefficient` / `Stage.DurationInDays` are a second source of truth.** The catalogue
  carries a weight and duration, and so does every `CurriculumStage` for the same stage. They agree
  today only because the reconstruction seeded one from the other. The first text that reweights a
  stage makes them disagree — and the **Stages page renders the catalogue value**, which no CNPN
  necessarily states. The page is also not year- or CNPN-scoped, so switching the navbar year there
  changes nothing. Either drop the catalogue columns in favour of `CurriculumStage`, or annotate the
  Stages page with the text each figure comes from. Same change as the two items above.

### 🔲 Phase 15.2 — data entry (blocks scolarité, not code)

- **1650.25 has zero recorded requirements.** Nothing historical maps to it; the stage lists must be
  entered from `cnpn/CNPN Diplôme de Docteur en Médecine.pdf`. Six-year students have no CNPN content
  until then.
- **`PHARM-LEGACY` is a placeholder** created so Pharmacie's 13 existing curricula had a text to
  belong to. Replace its code, label and reference with the real Pharmacie arrêté.
- **"Médecine de famille" does not exist as a `Stage`.** The new CNPN requires it in S11–S12.
- **~2,200 students carry `CnpnAssignmentIsInferred`** — entry deduced from their current level
  because the legacy import never carried it. The single assumption is that the 1,013 at level 2 did
  not repeat an unrecorded first year. Confirm or correct in bulk.
