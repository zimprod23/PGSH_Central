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

**Status: unit suite done (1 017 tests, `PGSH.Tests`); functional done (`Integration/ApiFactory`);
Testcontainers NOT started — but the *translation* half of its case is now closed on the macro-plan path.**

The suite runs over `UseInMemoryDatabase`, which ignores FK constraints, unique indexes,
`OnDelete` behaviour and SQL translatability — so a whole class of defect is invisible.

⚠ **Translation no longer needs a database, and the macro-plan path is swept** (2026-08-26).
`SqlTranslationTests` compiles each named query against the Npgsql provider through
`ToQueryString()`; twelve cases cover `CohortProvisioner`, `StudentAffectationService`,
`RotationArranger`, `GroupScheduleConflictGuard`, `ServiceOccupancyCalculator` and
`SchedulePublisher`, plus the shape that broke the plan, asserted still-refused. All of them compile:
the sweep found no second defect. It narrows what Testcontainers is still owed for — **rows, FKs,
unique indexes and `OnDelete`** — rather than replacing it.

Two additions, in order:

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

### Confirmed defects from the 2026-08-06 audit — ✅ ALL CLOSED (verified in code 2026-08-16)

Every item below was fixed in the sessions that followed; the list is kept because each one names a
shape worth recognising again. Where the fix lives now:

| Defect | Where it was closed |
|---|---|
| `UpdateServiceEvaluation` pre-set `Id` on `ObjectiveScore` | goes through the aggregate — `InternshipAssignment.AmendEvaluation`, no pre-set key |
| `RerouteAsync` NRE on a slot-less period | refused up front: `StageErrors.CannotRerouteAdHocPeriod` |
| `RerouteAsync` start date not clamped | `RemainingWindowStart(date, target.StartDate, target.EndDate)` |
| `ResumePeriod` shifts completed/interrupted periods | filtered — `!p.IsComplete && !p.IsInterrupted` |
| Evaluation updates bypass the aggregate | `AmendEvaluation` raises the event and recomputes |
| `EvaluationSubmittedDomainEvent` carries `null` | carries `StageScoring.PeriodMark(evaluation)` |
| Objective ids never validated against the stage | `EvaluationObjectiveResolver.ResolveAsync` |
| `CompletePeriod` has no `IsStarted` guard | `StageErrors.PeriodNotStarted` |

<details><summary>The original descriptions</summary>

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

</details>

### Critical (data correctness)

- ✅ **`FinalScore` computation** *(closed)*: `InternshipAssignment.RecomputeFinalScore` is called from
  `SubmitEvaluation` / `AmendEvaluation` / `RemovePublishedPeriods`.

- ✅ **`IsCurrent` uniqueness on `AcademicYear`** *(closed 2026-08-16, migration
  `PartitionScopeAndIndexGaps`)*: a partial unique index — `unique` on `IsCurrent` with filter
  `"IsCurrent"` — so at most one row can be flagged. The migration demotes any extras first, keeping
  the highest `Id`, which is what `CreateAcademicYear` would have left standing. There is no update
  handler to guard; the index is the invariant, not the write path.

### ~~High (FK index gaps)~~ — never real

- ⚠ **`Registration.LevelId` and `Registration.AcademicGroupId` were never missing an index.** EF Core
  creates one per foreign key by convention: `IX_Registrations_LevelId` and
  `IX_Registrations_AcademicGroupId` are both in the model snapshot and both in the database.
  Declaring them explicitly only *renames* the existing index — checked 2026-08-16 by scaffolding the
  migration, which produced a `RenameIndex` and nothing else. `IX_Registration_Year_Level` (added
  later, composite) is the one that was genuinely worth adding.

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

### ✅ 14.3a — Closing a year by declaration (the déliberation canvas)

**Built 2026-08-09.** The answer to "who cleared the year" is not something PGSH can compute and was
never going to be: there is no exam, no TP, no note de module and no jury in the system. So the
faculty states it, in the shape it already works in — a canvas per promotion, filled from the PV de
déliberation and uploaded, exactly like the évaluation import.

`Application/Students/Registrations/Deliberation/` — `GetDeliberationTemplateQuery`,
`PreviewDeliberationQuery`, `ApplyDeliberationCommand`, all three sharing `DeliberationPlanner` so the
dry run *is* the plan. API: `GET|POST deliberation[/template|/preview]` — ⚠ **re-scoped to the year in
14.3d below**, `levelId` now a filter rather than the route.

- **One canvas is one promotion — (academic year, level).** *(Superseded by 14.3d: the scope is the
  year, and a level narrows it. The reasoning below is why matching had to be scoped at all.)* That is how a jury sits, and it is what
  makes the identifier index mean anything: a CNE is unique within a promotion, and matching across
  years turns a legitimate row into an ambiguous one.
- **`RegistrationStatus` gained `Graduated` and `Excluded`.** *Exclu* is not *redouble* — one ends the
  cursus, the other repeats the year — and the réinscription below is the thing that must tell them
  apart. `Diplômé` is separate from `Admis` for the same reason: there is no level above it.
- **`Registration.RecordYearOutcome` is the only writer**, and it stamps
  `OutcomeSource` (`Declared` | `Inferred`) plus `OutcomeRecordedOn`. Re-declaring is allowed — a jury
  corrects itself — but **an inferred verdict may never overwrite a declared one**. That guard is what
  makes 14.3b safe to build afterwards.
- **All-or-nothing.** One unreadable decision refuses the whole file, because the file is not stored:
  a promotion half closed could not be reconstructed afterwards.
- ⚠ **A contradiction against PGSH's own stage record is reported, never enforced.** An *Admis* whose
  stages are not all validated is flagged and counted, and the import proceeds. The jury deliberates
  on the whole year and PGSH sees only the stages — and with 0 authored periods in the base, an
  unmarked stage is currently the norm, so enforcing would block every import.
- ⚠ **`Diplômé` off the final year is refused** where the student's CNPN is known, and **stands aside**
  where it is not: ~2,200 stamps are inferred and 19 students carry none, so refusing on absence would
  make the feature unusable on the real data. Same standing-aside rule as `CohortProvisioner`'s.
- `NotCovered` counts the registrations no row mentions — a promotion of 688 closed with a 200-row
  file is worth seeing *before* applying.

### ✅ 14.3b — Réinscription: next year from the verdicts

**Built 2026-08-09.** `Application/Students/Registrations/Reinscription/` —
`PreviewReinscriptionQuery`, `ApplyReinscriptionCommand`, sharing `ReinscriptionPlanner`.
API: `GET reinscription/preview`, `POST reinscription` — ⚠ **re-scoped to the year in 14.3d**, `levelId` now an optional filter rather than the route.

*Admis → niveau + 1. Redoublant → même niveau. Diplômé / Exclu / Abandon → rien.*

- **A separate act from the déliberation, deliberately.** Deliberation is July, re-registration is
  September, and not every admis comes back. One combined act would invent registrations for students
  who abandoned, and would require next year's `AcademicYear` row to exist in July.
- ⚠ **Idempotent and additive, not all-or-nothing** — the opposite of 14.3a, and for a reason worth
  keeping straight: a student already registered in the target year is *skipped*, so the rollover is
  re-run after the odd verdicts are corrected. Refusing 690 rows over three anomalies would buy
  nothing when re-running is safe. The déliberation cannot work that way; its file is not stored.
- **`NextLevelMissing`** is *Admis* with no level above — almost always a PV that should have read
  *Diplômé*. Reported, never guessed into a graduation.
- New registrations are `Active` and carry **no group**: nothing in the app filters planning by
  `Registration.Status`, so a `Pending` row would be grouped and planned exactly like an active one
  while claiming not to be enrolled. Grouping is `AutoArrangeGroupsCommand`'s job and runs next — these
  students are the "Non réparti" bucket it reads from.

### ✅ 14.3d — The exceptions canvas, and the flexibility around it

**Built 2026-08-18.** 14.3a and 14.3b were complete server-side and **unreachable**: there was no UI
for either, so nobody in the running app could close a year. That gap is what made re-shaping the
canvas cheap — the redesign landed before the screen was written rather than after.

Four changes, one act of scolarité:

**1 · The canvas is a list of exceptions, and one file covers the year.** Scolarité types only the
students the year went badly for; everyone the file does not name is *Admis*. Read [CLAUDE.md → "The
canvas is a list of exceptions"](CLAUDE.md) for the rules; the ones that decide the design:
- Year-wide matching is safe because a student holds one registration per year — it is *cross-year*
  matching that is ambiguous, and that is still impossible.
- ⚠ **Superseded the same day by 14.3e:** this shipped as *Admis, or Diplômé where the year is the
  last of the student's own CNPN*, with an unstamped student on a possibly-final year **blocking** the
  file. The first run against real data killed both halves — the default now promotes and never
  graduates, and the blocking case disappeared with it. Read 14.3e before touching the planner.
- The default never overwrites a verdict already recorded, which is what makes the import re-runnable.
- `ConfirmedDefaultCount` is echoed back from the preview and refused on a mismatch — a registration
  created between the two calls is exactly what a checkbox would have waved through.
- `DeliberationTemplateMode.Full` keeps the old nominative canvas; both modes produce the same decision
  sheet, so the parser never learns which one it is reading.

**2 · The réinscription runs year-wide too** — `levelId` optional, each student moving up from his own
level. Rows are ordered attention-first under a cap, so a bounded report can never hide a row somebody
must act on.

**3 · One student at a time** — `Registrations/Outcome/`, `POST registrations/{id}/outcome[/reopen]`.
Required by the exceptions file, not merely convenient: re-uploading a promotion's file must never be
the way to fix one row. ⚠ This also closed a real defect — `UpdateRegistrationCommand` wrote `Status`
directly and left `OutcomeSource` null, so the edit form showed « Admis » while the réinscription
reported « aucune décision enregistrée ». Reopening reports `LaterRegistrationExists` and deletes
nothing.

**4 · Joining a roster after the schedule is published** — `AcademicGroups/Join/`,
`POST groups/assign-student`. The transfer path silently did nothing for a student who had no group.
`LateArrivalScheduler` materialises only windows that have not closed; a stage the roster already
finished is owed and unserved (`StagesAlreadyOver`), never invented.

**Frontend.** New `YearClosurePage` (admin → Académique → « Clôture & réinscription ») drives both acts
on one screen. `AdminStudentDetailPage` gained the per-student verdict control, « Rouvrir l'année » and
« Affecter à un groupe ». ⚠ `RegistrationStatus` on the frontend was still the pre-14.3a five-value
union — `Graduated` and `Excluded` were missing, and both status maps would have rendered a graduate
blank. Fixed, and the type error is what found them.

**Tests: 897 green** (859 + 38, including 8 endpoint tests through the real pipeline). Four guards
were each verified by breaking them and confirming the right tests fail: the default not overwriting a
recorded verdict, `ConfirmedDefaultCount`, the closed-window rule, and the binding of
`defaultUnlistedToAdmis`.

⚠ **The final-year rule changed after the first run against real data** — see 14.3e.

### ✅ 14.3e — The default promotes, it never graduates

**Changed 2026-08-18, the same day, after the first run against the real base.** 14.3d shipped with
« silence = Admis, ou Diplômé en dernière année ». The live data says the second half is wrong:

| | in the final year | of whom, there before |
|---|---|---|
| 7ᵉ année Médecine | 1 657 | **855** (550 twice, 173 three times, 132 four times) |
| 6ᵉ année Pharmacie | 356 | **74** |

The final year is the **thesis year**. Students sit in it until they defend, PGSH holds no record of a
defence, and so "still there" and "finished" are both perfectly ordinary — the exact situation where a
default must not choose. Applying it would have graduated **~930 students who were simply still
enrolled**, and that is a floor.

So `MayBeAFinalYear` replaces `DefaultOutcomeFor`: anyone who may be in his last year is counted
(`FinalYearUndecided`, per level and in total) and **left untouched**. The faculty names its graduates
instead, which costs it nothing — the defence roll is the list it already has.

Two things fell out of the change rather than being added:
- **`DefaultIssues` is gone.** It existed to block the file when an unstamped student sat on a year
  that might be his last. Nobody in a possible final year is decided for any more, so that student
  needs no special case and the import stops having a blocking condition it did not need.
- **`DefaultedGraduations` is gone** — the default writes exactly one outcome now, so the report says
  so with one number.

⚠ **The general lesson, worth keeping:** an exceptions file works where the exception is the rare case.
Check that assumption per promotion before inverting a default — here it holds for years 1–5 and
reverses completely in the last one.

### 🔲 14.3c — Inferring the imported years (still open)

Nobody will upload a canvas for 2019-2025, so those six years still read "En cours". The inference
rule agreed with the user — *a registration is failed when a later registration exists at the same
level; otherwise validated; the latest is in progress* — plus the four cases that still need a ruling,
are documented in [NOTES.md → "`Registration.Status` is unmanaged"](NOTES.md).

Do not start before those cases are answered. The half that *is* now settled: an inferred verdict
writes `OutcomeSource = Inferred`, which `RecordYearOutcome` refuses to let overwrite a declared one —
so a later link to the pedagogical system can never have its facts replaced by guesses, and every
reader can tell which is which.

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

### ✅ Phase 15.07 — unequal stage durations on one rotation axis

**Done 2026-08-09.** `Stages/RotationCycle/` generalised from one `periodsPerStage` per block to a period
count **per stage**, which is what the 6th year needs: four stages of two periods and two of one.

The counting identity does the work. A partition needs `T = Σkₛ` columns to visit every stage, and if `Lₛ`
partitions sit in stage *s* at once then `Lₛ·T = P·kₛ`. So `Lₛ = P·kₛ/T`, and `P` must be a multiple of
`T / gcd(kₛ)`. The real 6th year: `k = [2,2,2,2,1,1]` → `T = 10`, `P = 10`, `L = [2,2,2,2,1,1]` — the ten
monthly columns of `Med6.png`.

- ⚠ **A period is one service, not one stage.** Every stage carries a slot per axis column; a partition
  takes a run of `kₛ` consecutive ones, i.e. `kₛ` different services.
- ⚠ **The closed-form Latin square is gone** — it only exists for equal durations. `RotationTiling` solves
  an exact cover, backtracking across partitions and columns together.
- ⚠ **Some mixes are impossible, and the refusal is a proof.** Stages of 2 and 1 give `T = 3`, where a
  two-column run always covers column 2. The search is exhaustive, so `NoFeasibleArrangement` means no
  arrangement exists at any `P`.
- **Dates are entered once** for the block, at its finest granularity; every stage's slots are cut from
  that single list, so stages of different lengths cannot drift apart.
- `P` is **taken and validated**, not derived — a level's partitioning is shared across its blocks, and
  deriving would silently re-cut it. The refusal names the multiples that would work.

### ✅ Phase 15.08 — jours ouvrables, and undoing a partitioning

**Done 2026-08-13.** Three things the planning screens could not do: state a column in worked days,
state it in *n* weeks, and take back a partitioning entered by mistake.

**`Domain/Calendar/`** — `Holiday` (a dated span, `Kind` = National | Religious | Academic,
`IsConfirmed`) and `WorkingDayCalendar`, pure and immutable: calendar days minus `WorkingWeek.Moroccan`
(Sat + Sun) minus every declared holiday. `MoroccanPublicHolidays.FixedFor(year)` generates the ten
fixed Gregorian days — and Nouvel An Amazigh only from 2024, when the décret first took effect.

- ⚠ **Half the calendar cannot be generated.** Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram and Mawlid follow
  the Hijri calendar, turn on observation of the crescent, and are announced by decree. They are entered
  or they are absent, and absence is *reported* (`MissingReligious`) — a stage that spans an unrecorded
  Aïd is counted four days too long, which is exactly the kind of error nobody looks for.
- ⚠ **`Stage.DurationInDays` is nothing to convert — it is already in worked days.** Measured
  2026-08-13: 14×7, 22×7, 30×2, 42×3, 44×6, 66×2. Only two rows hold 30. Verified live on Med6: an axis
  of ten 22-worked-day columns meets every stated duration **exactly** (44 for the k=2 stages, 22 for
  the k=1 ones) while calendar spans vary 60–67 / 30–34 days. So the calendar generates where the unit
  is stated at the point of use, and elsewhere it reports: `RotationCyclePreview.DurationChecks` gives each
  stage's worked and calendar days against its stated number, as a range, never as a guard. Resolving
  which number is authoritative is 15.1's `Stage.Coefficient` / `DurationInDays` item below.
- **`GenerateAxisWindowsQuery` moved the axis layout server-side.** It was `setUTCMonth` in the page,
  which is correct for calendar months and wrong the moment a duration means worked days — no browser
  has the holiday table. Months and weeks stay calendar-exact (a monthly axis must land on the 1st);
  `WorkingDays` is the only unit under which two columns hold the same amount of stage.
- **`ClearRotationGroupsCommand`** un-partitions a promotion. Needed because `BuildLabels` lets the
  *existing* partition count win over the requested one — so a promotion mistakenly cut into two stays
  two-way for every later assign, whatever is asked for. Refused while any cell is published
  (`CannotClearPublished`); otherwise it removes no row and breaks no FK, since nothing points at a
  label — it only reports the planned cells that now describe no partition.
- **UI**: *Formation → Jours fériés* (CRUD + coverage + "générer les fêtes nationales"), the working-day
  unit and per-column counts on *Bloc de rotation*, and the strategy / redécouper / supprimer controls
  on *Groupes* — `Contiguous` and re-cutting previously needed Scalar.

### ✅ Phase 15.09 — the balance is per column, and the crossover is authored

**Done 2026-08-18/24.** Two defects in `RotationArranger`, one of them printed in a document the
faculty was about to hand out.

**The service queue was built over the cohorts of the *call*** and each cell indexed by its global
position. Those coincide only when the caller scoped to a `ConcurrencyBlock` — and « auto-répartir ce
stage », every partition and every période at once, is a real button that does not. The crossover
leaves one partition free per column; partitions are contiguous in the ordering and each service owns
a contiguous run of the queue, so the free partition fell *inside* one service's run. Measured on 5MED
Psychiatrie 2025-2026 (60 groups, 9 partitions, 5 services): **all nine columns in one service, 69-85
students against a capacity of 20, two of five services unused all year.** Reproduced 9/9 from
`queue[(ci + phase·⌊n/T⌋) mod n]`.

- ⚠ **Nothing reported it.** 60 cells written, no failure, and the `GroupConflicts` it counted are the
  ones the crossover is made of — indistinguishable from a correct plan. The printed répartition is the
  only place it shows, which is where the user found it.
- **A column's shape cannot be improved on; which services carry the remainder can.** Seven cohorts
  over five services is 2,2,1,1,1 whichever way they fall. Only the leftover tie-break can move, and it
  was stable — with 148 services on one imported capacity the same two carried the pair in every column
  of the year. `BuildServiceQueue` now breaks ties by the column's phase.
- **The step is at least 1.** `⌊m/cycleLength⌋` is 0 whenever a column set is smaller than the cycle,
  which froze a `PerPeriod` run into one service — `SingleService` by accident.

**The unscoped arrange is a fill, not a plan, and its correctness was borrowed.** Psychiatrie was
arrangeable in one click only because six other stages already held every group in 8 of its 9 columns:
480 of 540 candidate cells refused, 60 free, one per roster — no freedom over columns at all. Pressed
*first*, the same button writes the whole promotion into one stage for the year, and every stage
arranged afterwards gets nothing. `StageErrors.StageWouldFillEveryColumn` refuses it, narrowed twice:
only when the call names **neither** a partition **nor** a window (naming either is authored
targeting), and only when another stage of the promotion declares the same windows (a stage that *is*
the whole axis starves nobody). Med6 — six stages, ten columns, zero cells — is the live case.

**Reopening the block no longer shows an empty form.** `GetRotationCycleQuery`
(`GET levels/{id}/rotation-cycle`) reads the blocks **from the axis on disk**: stages whose slots carry
the identical window list *are* a block, so a date corrected afterwards on one stage's own grid shows
through instead of being papered over. The axis cannot state `kₛ` — every stage of a block carries a
slot on every column, which is what makes the crossover possible — so it is recovered in order (the
apply's audit entry → the widest run a cohort holds → nothing) and `RotationPeriodsSource` says which,
for the same reason `OutcomeSource` and `CnpnSource` do.

⚠ **Carried: every cell arranged before this lands still has the old shape**, and it looks correct in
the grid. `SMOKE-TEST.md` §22a has the two queries that find them; nothing is published, so
re-arranging is free.

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

⚠ **The 3ᵉ année of 2026-2027 is the first cohort 1650.25 actually binds** (`CnpnLevelEffectivity`,
level = 3ᵉ année, from = 2026-2027). That year has **no registrations yet**, which is the whole reason
the entry can wait — and also the reason it must not be left much longer:
`RegistrationCnpnStamper` reads the rule **once, at the creation of a registration**, so every stamp
handed out before the requirement sets exist is a registration pointing at a text that requires
nothing. Nothing is wrong at that moment; `CohortProvisioner` simply stands aside where no set is
recorded, so a whole promotion would plan as if it owed no stage at all.

**Order of operations, and it is not the obvious one:**

1. Record 1650.25's requirement sets — the stage list per level. **Awaiting the list from the faculty.**
2. *Then* open 2026-2027 for registrations.

Doing it the other way round is recoverable but not cheap: the stamps themselves stay right (the text
is the same row), but every plan built in between was built against an empty requirement set, and
`CohortProvisioner`'s refusals are counted rather than raised, so nothing on screen says so.



- **1650.25 has zero recorded requirements.** Nothing historical maps to it; the stage lists must be
  entered from `cnpn/CNPN Diplôme de Docteur en Médecine.pdf`. Six-year students have no CNPN content
  until then.
- **`PHARM-LEGACY` is a placeholder** created so Pharmacie's 13 existing curricula had a text to
  belong to. Replace its code, label and reference with the real Pharmacie arrêté.
- **"Médecine de famille" does not exist as a `Stage`.** The new CNPN requires it in S11–S12.
- **~2,200 students carry `CnpnAssignmentIsInferred`** — entry deduced from their current level
  because the legacy import never carried it. The single assumption is that the 1,013 at level 2 did
  not repeat an unrecorded first year. Confirm or correct in bulk.

---

### ✅ Phase 15.10 — a block you can take back, and the queries nobody had translated

**Done 2026-08-26.** Three things, in the order they were found.

**Removing a rotation block** — `DeleteRotationCycleCommand`, `DELETE levels/{id}/rotation-cycle?stageIds=…`.
The page restored a block and could replace it; it could not *undo* one, so a block entered by mistake
could only be written over. Scoped to the stages of the block (a promotion holds several — the new
CNPN's 3ᵉ année is two semesters), refused while anything on it is published, reporting `SlotsRemoved`
and `PlannedCellsRemoved`. The apply and the preview now report the cascaded cells too: they were
being destroyed silently.

⚠ **`RotationCycleContext` was reading the wrong table.** It counted published cells through
`ServicePeriod.CohortSlotAssignmentId` — the FK that names only the *first* cell of a run — so under
`SingleService` the trailing columns of a published run read as free, and that is the guard the apply
and the delete both stand on. `GetRotationCycleQuery` had asked `ServicePeriodSlotCoverage` correctly
since 15.09: the read was right and the write guard was not. Latent (every 6ᵉ année stage is
`PerPeriod`, 0 grid-linked périodes) and now the fifth caller of `PublishedCells`.

⚠ **`CohortProvisioner` had a query PostgreSQL cannot compile** — a collection subquery in a
projection whose element was `r.CnpnVersionId ?? r.Student.CnpnVersionId` followed by `Distinct()`,
written in 15.x when the CNPN moved onto the registration. It killed the macro plan on the real base
with the whole suite green. Rewritten flat and top-level; `SqlTranslationTests` +
`TestHarness.NewNpgsqlContext()` now catch that class **without a database**, since translation
happens at query-compile time. See `NOTES.md` (2026-08-26) — Testcontainers is still owed for
everything that needs rows.

**The final-year gate is asked once per batch** (`FinalYearGuard.EnsureMayEnterManyAsync`); the
single-student call delegates to it, so the two paths cannot drift. `CreateManyRegistrationsCommand`
was spending four queries per student inside its loop — ~2 800 to enrol a promotion of 700.

**The 6ᵉ année is planned end to end** on real data: 10 columns, 10 partitions, 1 000 cells, 0 rosters
double-booked, every service used in every column. `PLANNING.md` §9 has the numbers.

---

## 🔲 Phase 16 — Re-importing the Access base, cleanly

**Raised 2026-08-18 by the user, measured the same day against the live base.** The import of
2026-08-07 got the *rows* right — all 104,924 migrated and verified — but it manufactured identifiers
it did not need to, and the placeholders are now visible to every user of the app. Re-run the import
with the corrections below rather than patching the data in place, so the importer and the base agree.

### 16.1 — `LEGACY-nnnnn` is not an identifier, it is a prefix on the Appogée

Measured on `TodoDatabase`, 2026-08-18:

| | count |
|---|---|
| students | 10,204 |
| `CNE LIKE 'LEGACY-%'` | **4,695** |
| …of those, carrying a usable `Appogee` | **4,695** (all of them) |
| …of those, carrying no `Appogee` either | 0 |
| Appogée colliding with another student's real CNE | **0** |
| duplicate Appogée among the 4,695 | **0** |

The placeholder is literally `"LEGACY-" + Appogee` — `LEGACY-10001373` / `10001373`. So it carries **no
information whatsoever**: every one of those students already had an identifier, and the import
invented a second one that looks like a CNE and is not.

What it costs today:
- 46% of the student body cannot be found by their real identifier in a CNE search.
- It is the reason `StudentIdentifierRules.ValidCne` had to be loosened to a bare format check — the
  old `^[A-Z]\d{6,12}$` rejected 5,646 students, and these 4,695 were the bulk of them. Removing the
  placeholders removes most of that pressure, though the format check stays right for other reasons
  (faculty codes, internal spaces — see CLAUDE.md).
- A déliberation or évaluation canvas prints `LEGACY-14000022` in the CNE column, and scolarité has no
  way to know that is not what is on the student's card.

**The fix, and the thing not to do.** Leave `CNE` **null** where the source has none. Do *not* write
`CNE = Appogee`: that asserts the appogée is the national code, which is exactly the false claim the
prefix was invented to avoid making. Null is the honest value and the schema already allows it
(`Users.CNE` is nullable); uniqueness is the constraint that protects anything here, and it is enforced
separately. The consequence to handle deliberately:
- ⚠ **Every search, canvas and import that matches on CNE must fall back to Appogée**, and must say
  which one it matched. The déliberation planner already indexes both (`byCne` / `byAppogee`) and
  reports the ambiguous case, so that path is ready; audit the others before flipping the data.
- The reference tab of the déliberation canvas and the student list should show Appogée where CNE is
  null, rather than an empty cell.

### 16.2 — Open: does Access hold the Appogée *in* the CNE column?

The user's reading, not yet verified: some records of `Medecine.mdb` may carry an appogée number in
the CNE field, so a "real" CNE in PGSH today may in fact be an appogée. Not the same defect as 16.1 —
those rows have no `LEGACY-` marker and look perfectly normal.

To check before re-importing, against the `.mdb` (gitignored, real PII):
- the shape distribution of the source CNE column — a modern CNE is a letter plus digits, an appogée is
  digits only (and the eight samples above are all 8 digits starting with the intake year: `13…`,
  `14…`);
- how many source rows have CNE and Appogée equal, or CNE matching the appogée grammar;
- whether any source row has a CNE that is another row's Appogée.

Only then decide whether the importer should move such a value into `Appogee` and null the `CNE`.
⚠ **Do not guess this in bulk from the shape alone** — a digits-only CNE is not proof, and blanking a
real identifier is worse than keeping an odd-looking one.

### 16.3 — ⚠ The live base is no longer a clean copy of the source

**Raised 2026-08-24.** The development base has been written to by every smoke-test pass since the
import: CNPN stamps and effectivity rules, partitions cut and re-cut, verdicts recorded, rosters split
per promotion, service quotas, holidays, the 5MED répartition arranged and re-arranged, and the 51
`StageAllowedServices` authored for the 6ᵉ année. None of it is in `Medecine.mdb`, and some of it is
*deliberately* not (the roster split, the CNPN work) — so « re-import and start again » would throw
away real decisions alongside the test residue.

**So the re-import cannot be a restore.** Decide per category, before running anything:

| category | source of truth after the re-import |
|---|---|
| students, registrations, périodes, évaluations | the Access base — re-import wins |
| roster identity per promotion, `Registration.LevelId` | the importer, corrected — see 16.3 below |
| CNPN texts, stamps, effectivity rules, targeting | **PGSH** — nothing in Access expresses them |
| partitions, `StageSlot`s, cells, allowed services, holidays | **PGSH** — authored here, no source |
| year outcomes (`OutcomeSource`) | **PGSH** for anything `Declared`; Access for the rest |

⚠ **The two halves have to be re-linked, and the join is `Student`.** A re-import that renumbers
student ids detaches every CNPN stamp and every waiver from the person it was granted to. Pin the
identity (CNE, else Appogée — see 16.1) and verify the join **before** dropping anything.

Take a `pg_dump -Fc` first, and keep it: it is the only copy of the authored half.

### 16.4 — Re-import hygiene

- The importer must be **re-runnable against a restored dump**, not against the current base: the app
  has written to it since (CNPN stamps, partitions, verdicts, périodes). Decide up front whether 16.1
  is a re-import or a targeted data fix, and if the latter, write it as a migration so it is recorded.
- Take a dump first (`pg_dump -Fc`) — see the rollback section of [`SMOKE-TEST.md`](SMOKE-TEST.md).
- Re-check the two defects the last import left and that were repaired by hand afterwards, so the new
  run does not recreate them: rosters keyed without their promotion (`SplitAcademicGroupsPerLevel`) and
  `Registration.LevelId` left null on 1,003 rows. Both are in the importer now; confirm with a query
  after the run rather than assuming.
