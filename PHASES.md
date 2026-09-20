# PHASES.md — PGSH Development Roadmap

**Project:** Plateforme de Gestion des Stages Hospitaliers
**Stack:** .NET 9 · ASP.NET Core Minimal API · EF Core 9 · PostgreSQL · Keycloak · React 19 · .NET Aspire

---

## ⏭ Next up — ASAP (raised 2026-09-03, session 40)

Two items the user asked for explicitly, both ahead of everything still open in Phases 12 and 13.

| | what | why now |
|---|---|---|
| ~~**Phase 17**~~ ✅ | « Suspension d'examens » scoped to a **promotion**, declared as a window | **Built 2026-09-06.** A promotion's window joins its own working-day calendar and the axis laid afterwards steps over it, in jours ouvrables — no date is pushed onto anything. **§17.1 (moving P7 while P3 runs) built 2026-09-13** — `PublishedPeriodShifter`. **§17.2 (17/09/2026)**: the pause report names that remedy and counts how many crossed columns it would accept (`SlotsMovable`), and names the off-grid case neither remedy reaches. **§17.3 (17/09/2026)**: the calendar answers its two questions separately — a closure can be *crossed* without lengthening what crosses it (`ICalendarClosure.CountsAsWorkingDay`). **§17.4 (18/09/2026)**: the lifecycle answers *its* two questions separately too — `Movable` (« déplacer le début ») and `Extendable` (« repousser la fin »), plus `InternshipAssignment.ExtendTo`, which is what makes a started rotation repairable at all. ⚠ The promotion-wide **cascade** that consumes them is still not built. |
| **Phase 18** | Scheduled backups and a **named safe point** before every bulk act | the app is live on the real base — 10 203 students, 43 605 registrations, 105 626 périodes — and the only undo today is a `pg_dump` somebody remembered to take |

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

## ✅ Phase 11.4 — Test infrastructure (integration + functional)

**Status: unit suite done (`PGSH.Tests`); functional done (`Integration/ApiFactory`); Testcontainers
**built 13/09/2026** (`PGSH.Tests/Postgres/`) — the three tiers now cover translation, the pipeline,
and the schema.**

The suite runs over `UseInMemoryDatabase`, which ignores FK constraints, unique indexes,
`OnDelete` behaviour and SQL translatability — so a whole class of defect is invisible.

⚠ **Translation no longer needs a database, and the macro-plan path is swept** (2026-08-26).
`SqlTranslationTests` compiles each named query against the Npgsql provider through
`ToQueryString()`; twelve cases cover `CohortProvisioner`, `StudentAffectationService`,
`RotationArranger`, `GroupScheduleConflictGuard`, `ServiceOccupancyCalculator` and
`SchedulePublisher`, plus the shape that broke the plan, asserted still-refused. All of them compile:
the sweep found no second defect. It narrows what Testcontainers is still owed for — **rows, FKs,
unique indexes and `OnDelete`** — rather than replacing it.

Both additions are in:

1. ✅ **Integration — Testcontainers over real Postgres** (13/09/2026). One `postgres:17-alpine` per
   run, one database per test cloned from a template. `[PostgresFact]` skips *with a sentence* when
   Docker is absent rather than passing. First cases: the two **filtered** unique indexes no other
   provider here can express (`IX_AcademicYear_IsCurrent`, `IX_Student_CNE`) and `Cohort.Stage` as
   `RESTRICT`.
   - ⚠ **It does not run the migration chain, and it cannot.** Three CNPN *data* migrations open with
     `RAISE EXCEPTION` against an empty base by design (`docs/operations.md` §1), so the schema is
     built with `EnsureCreated` from the model. Migration drift therefore remains uncovered — the
     one thing this phase was expected to close and does not.
   - ⚠ **Moving the handler tests off InMemory wholesale is *not* done and is not obviously right**:
     ~1 990 tests through a container would cost minutes per run. The rule is per-question — a case
     goes to `Postgres/` when the answer depends on the schema, and stays in-memory otherwise.
2. ✅ **Functional — `WebApplicationFactory` + header-driven auth** (`Integration/ApiFactory`,
   `TestAuthHandler`). `PGSH.API` is referenced from the test project; see the build note in
   `CLAUDE.md` about `-p:BaseOutputPath`.

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
students the year went badly for; everyone the file does not name is *Admis*. Read [docs/year-closing.md → "The
canvas is a list of exceptions"](docs/year-closing.md) for the rules; the ones that decide the design:
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
- ⚠ **`Stage.Coefficient` / `Stage.DurationInDays` are a second source of truth — and since
  2026-09-01 they no longer agree.** The catalogue carries a weight and duration, and so does every
  `CurriculumStage` for the same stage. They agreed only because the reconstruction seeded one from
  the other; **1650.25 is the first text to reweight a stage, and it has now landed.** Measured on
  the live base the day the migrations applied: MED3 Chirurgie and Médecine read **coefficient 3** in
  the catalogue and **1** in 1650.25's requirement set, and **30 j.o.** in the catalogue against
  **66** in 2174.18's — which is exactly right, since a 4ᵉ/5ᵉ/6ᵉ année student revalidating a 3ᵉ année
  credit is still governed by the older text. Both numbers are correct *of their own text*. This
  bullet has stopped being a prediction.
  - ✅ **The annotation half is done** (`StageCatalogueFigure` + `StageSummaryResponse.TextFigures`).
    The Stages page no longer renders the catalogue number unqualified: where a text states a
    different figure the cell is marked and the tooltip names each text and what it says. Silent
    where they agree, and silent where no text mentions the stage — a marker that fires whatever the
    data says is noise, and noise is dismissed. The columns are now headed « (catalogue) ».
    - ✅ **…and the marker now names *which* text disagrees** (2026-09-07). Reported as « j'ai aligné
      la durée sur le CNPN, dans le même CNPN, et l'avertissement reste » — and it was right on both
      counts: the durations of MED3 do agree with 1650.25, but the **coefficient** does not (3 vs 1)
      and **2174.18** still states 66 j.o. A marker that says only « a text disagrees » leaves the
      reader unable to tell a rule they have yet to satisfy from one that is not theirs to satisfy,
      so they redo the edit. The tooltip opens on the diverging codes and marks each line `≠`/`=`.
    - ⚠ **And it survived the edit that resolved it.** `saveCurriculum` did not invalidate the stage
      list, which is the row carrying `TextFigures`. Both directions are now wired — a saved text
      invalidates the catalogue, a catalogue edit invalidates the recorded sets.
  - 🔲 **What is left is the substantive half** — deciding which number is *authoritative*, i.e.
    dropping the catalogue columns in favour of `CurriculumStage`. It cannot be done before
    `Stage.LevelId` becomes advisory: a stage belonging to two levels has no single catalogue row
    that could carry a figure for either. The three items in this section land together.
  - ⚠ The page is also **not year- or CNPN-scoped at all** — it is the timeless catalogue, so
    switching the navbar year changes nothing there. Unchanged by the annotation.

> **Cleanup 2026-09-01 — the CNPN area was made to follow the project's own rules.** `CnpnVersion`
> is an aggregate root (`init` + `Correct` / `DeclareEffectivity` / `WithdrawEffectivity`, two new
> domain events); the span rule that lived in two handlers now lives once; `EntryYearDeduction`
> replaces the walk-back that was written twice; `CnpnAssignment.ResolveAsync` (no callers, a
> parameter its body never read) is deleted; `RegistrationCnpnStamper` returns a report instead of a
> `Result` that could not fail; the targeting selector is a predicate rather than a list of ids; the
> area's queries are named and pinned by `SqlTranslationTests`; `ExecutionAuthorizer` moved to
> `Abstractions/Authorization/`. No migration, no API change, no schema change. 1 278 tests green.

### 🔲 Phase 15.2 — data entry (blocks scolarité, not code)

⚠ **The 3ᵉ année of 2026-2027 is the first cohort 1650.25 actually binds** (`CnpnLevelEffectivity`,
level = 3ᵉ année, from = 2026-2027). That year has **no registrations yet**, which is the whole reason
the entry can wait — and also the reason it must not be left much longer:
`RegistrationCnpnStamper` reads the rule **once, at the creation of a registration**, so every stamp
handed out before the requirement sets exist is a registration pointing at a text that requires
nothing. Nothing is wrong at that moment; `CohortProvisioner` simply stands aside where no set is
recorded, so a whole promotion would plan as if it owed no stage at all.

**Order of operations, and it is not the obvious one:**

1. Record 1650.25's requirement sets — the stage list per level. **3ᵉ année received 2026-09-01**
   and written as migration `Cnpn1650Med3Stages` (six stages moved down from the 4ᵉ année as *new
   rows*, coefficient 1, 30 j.o. = six weeks, `SingleService`). ⚠ Not yet applied. MED4/5/6 of the
   new text are not needed until their promotions arrive; MED1/MED2 hold two immersion stages of
   15 days, pending which level each sits at.
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

## ✅ Phase 15.11 — inscription: the third act of the year

**Raised 2026-08-30 by the user**, about the déliberation and the réinscription: « *ça concerne
seulement les étudiants qu'on a déjà dans la base — pour ceux qui n'y sont pas encore ça ne marchera
pas* ». Correct, and structurally so. Both acts start from a registration the student already holds —
the déliberation reads the closing year's registrations, the réinscription reads their verdicts — so
neither can see anybody who does not hold one. Nor did any bulk path create a `Student` at all:
`PGSH.Application/Students/CreateMany/` was an **empty directory**, and
`CreateManyRegistrationsCommand` takes `List<Guid> StudentIds`.

Five populations fell in the hole, and three of them are already in the live base:

| | why the other two acts miss them |
|---|---|
| new 1MED / 1PHARM intake | no `Student` row at all |
| transfer in mid-cursus (1-2MED elsewhere → 3MED here) | no `Student` row, and study behind them PGSH never saw |
| returners after a withdrawal | in the base, no registration in the closing year — **2 of the 12 « Retrait » students did exactly this** (Retrait 2023-24 → 5ème année 2025-26) |
| réorientation Médecine ↔ Pharmacie | same student, different programme |
| étudiants sous convention | `Student.AgreementType` exists and **nothing read it** |

### What was built

`Students/Registrations/Inscription/` — the same shape as the déliberation, because it is the same
kind of act: `GET inscription/template` → `POST inscription/preview` → `POST inscription`, all-or-
nothing, the dry run *being* the plan. `IInscriptionSheetParser` + `ClosedXmlInscriptionSheetParser`.
Four writing actions partitioning on two questions (known to PGSH? same programme?), plus
`AlreadyRegistered` as a **skip** so the file survives being re-sent with the late arrivals appended.

`PriorEnrolment` — one row per entry registration recording the équivalence. See [`docs/year-closing.md`](docs/year-closing.md) for why it
had to exist *before* « owed » widens to the CNPN requirement set, not after.

### Two defects found on the way, both latent before this

- **`RegistrationCnpnStamper.Fallback` was programme-blind.** A `CnpnVersion` belongs to exactly one
  `AcademicProgram`, so a réorientation done through the ordinary registration form already carried
  the old programme's text onto the new registration — and `TotalYears` read from it answers « est-ce
  sa dernière année ? » from the wrong arrêté. Now refused, falling through to the entry deduction,
  which resolves from the level's own programme.
- **`Appogee` is NOT NULL UNIQUE**, not optional. `IX_Student_Appogee` carries a
  « WHERE Appogee IS NOT NULL » filter that reads as though absence were allowed; the column is
  required, so the filter can never be false and `""` is a value that the second student without an
  Apogée would collide on. Both identifiers are manufactured when absent, visibly prefixed.

### Three more defects found while auditing the above

- **The e-mail an unknown row carried was treated as identifying.** A newcomer whose address cell was
  mistyped to an existing student's silently gave *that* student a registration under the newcomer's
  name. CNE and Apogée identify; CIN and e-mail corroborate.
- **In-file duplication was keyed on one identifier.** `IX_Registration_Student_Year` is unique, so
  one person on two lines — written once with his CNE and once with his Apogée — was a 500 at
  `SaveChanges`.
- **A manufactured CNE could be one the edit form can never save.** `SANS-CNE-` costs 9 of the 20
  characters `StudentIdentifierRules.CnePattern` allows; a long Apogée would have created a read-only
  student, the third instance of the validator-vs-existing-rows failure.

### Built after the audit

- **`InscribeStudentCommand`** (`POST inscription/student`) — the single-row way in the « exceptions,
  not exhaustive lists » rule requires. Same planner, same writer, no preview, and the refusal carries
  the row's own sentence rather than a count.
- **`InscriptionApplier`** — the writes extracted, so the file path and the form path cannot drift on
  the half that creates identities.
- **`StudentIdentifierRules.EmailLocalPart` / `EmailCandidate`** — one address rule for the two
  generators, which had silently disagreed (letters vs letters-and-digits).

### The screen

Built the same session: a third card on `YearClosurePage`, beside déliberation and réinscription —
the three acts of the year on one screen — plus a one-student modal. `PGSH.Frontend/PHASES.md` §3.5
has the detail, including the `FinalYearBlocked` rows the réinscription table had been dropping.

### Run against the live base, 2026-08-30

`SMOKE-TEST.md` §28, after a `pg_dump -Fc`. **Passes a, b, c, d, e, h, i and g-bis**; f and g were cut
short when the Keycloak session expired. Two students were created and verified in the store —
`students` 10 204 → 10 206 — and the rows prove four rules no test could reach: the provisional
**Apogée** (`SANS-APOGEE-…`, since both identifiers are `NOT NULL UNIQUE`), the suffixed second address
for the homonym, `AcademicProgram` read from the level rather than a column, and
`CnpnSource = Effectivity`. Re-uploading the identical file gave `0 à créer` · `2 déjà inscrit(s)`.

The CNE-length guard added after the audit fired on the real stack, refusing a 17-character Apogée
before it could create a student the edit form would never save again.

⚠ **`PriorEnrolments` is still 0 rows.** The équivalence — the whole reason the table exists, and what
a future widening of « ce qu'il doit » will rest on — has never been written outside a test. That is
step 28 f/g, and it is item 0 in `HANDOFF.md`. ⚠ Two test students (`SMOKETEST01`/`SMOKETEST02`) are
in the base; §28 has the two-statement removal.

---

## ✅ Phase 15.12 — Excel exports: the roll, and the post-validation stage record

**Built 2026-08-31 (session 32), backend complete, no UI yet.** Two .xlsx downloads. Both are reads
over the schema as it stands — no migration, no new table, no column.

### `GET students/export` — the roll

One sheet, **one row per registration** of the resolved year. Nom · Prénom · CNE · Apogée · CIN ·
Sexe · Date de naissance · E-mail · Programme · Niveau · Année universitaire · Groupe · N° groupe ·
Partition · Statut · Source de la décision · CNPN · Origine CNPN · Convention.
Filters: `academicYearId` (omitted ⇒ current), `levelId`, `program`, `academicGroupId`, `searchTerm`.

- **`Programme` and `Niveau` are columns, and `levelId` still cuts the per-promotion file.** Asked as
  a choice, it was a false one: the columns make a row self-describing when files are merged or
  reopened later, and the filter gives the per-promotion document with them intact.
- It is an export of **registrations**, not of students — `Groupe`, `Niveau` and `Statut` are facts
  about a year and 2 635 students in this base have sat in more than one.

### `GET stages/assignments/export` — the post-validation stage record

Three sheets: **Stages** (one row per attempt, with note and verdict), **Périodes** (one row per
période, joined by `Réf. stage`), **Synthèse** (verdict counts and the class average per stage).
Filters: `academicYearId`, `levelId`, `stageId`, `academicGroupId`, `onlyEvaluated`.

The multi-période rule (`StagePeriodFolder`, pure, ten cases): **a stay is a maximal run of périodes
in the same service with no worked day between them.** One stay prints as one span; several print
joined, with the services in the same order. `Nb périodes` / `Nb services` / `Découpage` carry the
multi-période fact as columns, never as the shape of a string. Full table in [`docs/exports.md`](docs/exports.md) and
`NOTES.md`.

Scoped by the **registration's** level so a rattrapage lands on its student's own promotion, with
both levels printed. Year read from `Registration.AcademicYearId`, never from the périodes' dates.
Unmarked attempts are in the document by default.

### What the document says about its own blanks (added 2026-08-31 after the first user report)

`ExportNotes` prints, above every sheet's header, the columns that carry no value in **any** row —
and, when the roster columns are among them, which of the two causes it is (« aucun groupe n'existe
encore » vs « N groupe(s) existent mais aucune inscription n'y est rattachée »). ⚠ A column blank on
every row is indistinguishable from a column the export forgot; that is what was reported, against a
file that was correct in every cell.

### What is left

- **The frontend.** Two routes, no buttons — `HANDOFF.md` item 17. The filters must come from the
  page's own state, or the file covers a different population from the list above it.
- **The pre-validation export** — `HANDOFF.md` item 18. Same population, no note/verdict columns,
  showing where everyone *is going*. `onlyEvaluated` is already the switch and `ExportWorkbook`
  already the shape: a column set and a caption, not a second pipeline.
- **Phase 10 (Reporting & Analytics) is partly answered by this**, and the « Synthèse » sheet is the
  first per-stage aggregate the system produces. It is not a replacement for the phase.

---

## ✅ Phase 15.14 — what the export left out

Two reports against a file that was correct in every cell.

- **A folded `SingleService` run said « une période » and never named its créneaux.**
  `CoveredSlotFolder` + `SlotCoverageQuery`: `Nb créneaux`, `Créneaux`, `Détail des créneaux` on the
  Périodes sheet, and the count beside `Nb périodes` on the Stages sheet. Rows stay one per période.
- **No chef in the document.** `ServiceChefDirectory` / `ServiceChefProvider` extracted from
  `GetLevelRepartitionQueryHandler`, asked **as of each période's own start**, with
  `Origine du chef` distinguishing a dated record from the undated legacy note (140 of 148 services).

⚠ Still open, and it is a *data* task rather than a code one: **link the real professors in
Personnel**. The export needs no change for it — a tenure or a sitting chef already outranks the
note, and the rows upgrade themselves the day someone is linked.

⚠ **…and until that happens the documents read the note *alone*** (2026-09-03).
`ServiceChefPolicy.InForce` = `SourceNoteOnly`, honoured by the export **and** the répartition,
because the base's 2 `ServiceChefAssignment` rows are **test links** — resolving them prints a test
account beside real students. Consequences, all deliberate: a service named only by an affectation
prints **no chef**, `ExportNotes.ChefSourceNote` says so under the caption of the two sheets that
name one, and « Origine du chef » reads « Note (import) » throughout. The authority order is
untouched and still tested (`ServiceChefDirectoryTests`); **linking the professors and setting the
constant to `Authority` is the whole of the remaining work**, and it is one line.

## ✅ Phase 15.13 — the three slow acts, and the correctness bug inside each

Reported as performance on 2026-08-31: the planning grid slow to open **and to close**, « Publier »
answering with dozens of errors that would not stop, « Générer le plan » long and silent with a risk
of damaged data if the page went away. All three were real. None was only about speed.

### What was built

- **`GetStageScheduleQuery` is paged** (`PageNumber`, `PageSize`, and a server-side `RotationGroup`
  filter) and carries a **`StageScheduleSummary`**: the selection's counts, the saturation report
  deduplicated per (créneau, service), `OccupiedSlotIds`, and `PartitionUsage` read across the whole
  stage. `ScheduleGridModal` renders one page, pages it, and states the whole selection beside it.
- **`CohortResponse.PeriodNumbers`** — which columns a cohorte stands in, read by a second flat query
  keyed on the page's ids. `AssignmentsPage` folded that out of the grid's cells, which stopped being
  possible the moment the grid was paged.
- **`SchedulePublisher.EnsureIntakeAsync` aggregates** — one refusal
  (`Schedule.PublishRefusedByIntake`) naming the count, the unforceable admissibility half and the
  heaviest three, instead of stopping at the first cell. A single breach keeps its own sentence.
- **« Publier toutes » is one call** on the stage page, with the same over-capacity confirmation the
  grid modal has. It looped before, which is where the toast storm came from.
- **`IApplicationDbContext.ExecuteAtomicallyAsync`** — the macro plan writes its whole matrix in one
  transaction, through `Database.CreateExecutionStrategy()` because Aspire enables retry-on-failure.
- **`StudentAffectationService` reads its candidates once per call**, keyed on (roster, niveau)
  together, instead of once per cohorte.
- The rotation-cycle page says what a run is writing, and warns before the tab is closed.

### What is deliberately left

- **« Dépublier toutes » still loops.** Each per-cohorte refusal names what *that* cohorte would lose;
  an aggregate has to be designed before it can replace them. `HANDOFF.md` item 19.
- **Atomicity is untestable here.** `UseInMemoryDatabase` has no transactions; the harness now says so
  instead of throwing. It joins FKs, unique indexes and `OnDelete` on the Testcontainers list.
- **No browser has seen any of it.** `SMOKE-TEST.md` §31.

## ✅ Phase 15.15 — the réinscription roll: the canvas the faculty actually sends

**Raised 2026-09-01 by the user.** The three acts of the year were built against canvases PGSH
generates. The faculty does not use them: for 2026-2027 it sent **`Réinscriptions 26-27 VF.xlsx`**,
6 862 rows of `Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027`. Those two étapes carry the
verdict with them, so one upload does what the déliberation and the réinscription do between them.

### What it is, measured against the source before anything was written

| | |
|---|---|
| rows | 6 862 |
| distinct `Code` values | 6 862 (no duplicate) |
| `Code` = Access `NO_ORDRE` = `Students.Appogee` | **6 813 match a student exactly** |
| rows whose from-étape **disagrees** with the registration on record | **0** of the 6 810 checkable |
| master rows (`MMBTM1 → MMBTM2`), out of scope | 23 |
| students PGSH does not hold | 26 |
| students with no 2025-2026 registration (returners) | 3 |
| 2025-2026 registrations the file never mentions | 1 216 (999 in 7ᵉ MED, 211 in 6ᵉ PHARM) |

The zero in row four is what makes a level disagreement safe to treat as a **refusal** rather than a
skip: the strictness costs nothing on the real file, and a verdict written onto the wrong
registration cannot be walked back.

### The two rules the file forced

- ⚠ **A level that has not moved is not always a redoublement.** 804 rows are final-year students
  re-registering in the same year — 659 in 7ᵉ année Médecine, 145 in 6ᵉ année Pharmacie — because the
  thesis year runs until the thesis is defended and PGSH holds no record of a defence. Recording
  `Failed` there would be wrong twice: it is not a failure, and `RegistrationStatus.AnnulsItsStages`
  would **wipe the year's stage record for 804 students**. Nothing is written for them, nor for a
  réorientation, nor where the closing year holds no registration — so `WillRecordOutcome` is
  deliberately smaller than `WillRegister`, and the screen says why.
- ⚠ **Silence inverts.** The déliberation canvas is a list of exceptions, so a student it does not
  name is admis. This is the roll of who *is* coming back, so a student it does not name is **not** —
  and PGSH cannot tell a graduate from an exclusion. The 1 216 are left untouched and counted, which
  is exactly the population the déliberation should then be run over with the defence roll.

### What was built

`Students/Registrations/ReinscriptionSheet/` — contracts, `ReinscriptionSheetPlanner` (shared by
preview and apply, so the dry run *is* the plan), the preview query, the apply command,
`ClosedXmlReinscriptionSheetParser`, `POST reinscription/sheet[/preview]`, and a section on
`YearClosurePage` placed **first**, outside the 1/2/3 numbering, because it is not a fourth step — it
is 1 and 2.

Two things it needed that did not exist:

- **`FacultyLevelCodes`** (`Application/Stages/Levels/`) — the faculty's code table, promoted out of
  `LegacyImport.LevelMapper` so the importer and the roll cannot disagree about what `MDME3` means.
  `MDME3` and `MPHAR3` are new in this file and appear in no legacy row. It also lists the codes PGSH
  knowingly does **not** manage, which is the whole reason it is a table: an importer has to tell « a
  programme we do not cover » (skip, count) from « a code nobody told us about » (refuse the file).
- **`FinalYearTest`** (`Application/Stages/Progression/`) — « peut-être sa dernière année ? », lifted
  out of `DeliberationPlanner`'s private method so both acts ask it once. Two copies would disagree
  about 804 students in this file alone.

### Verified

23 handler tests (every guard, each refusal asserting the **store is untouched**, each paired with a
control row that would have applied cleanly), 8 parser tests, 5 `SqlTranslationTests` cases, and the
central rule proved to bite by breaking it and watching two tests fail. The real file was parsed
through the real parser: 6 862 rows, 0 missing codes, 6 862 distinct, 0 scientific-notation codes,
every level code resolved or explicitly out of scope.

---

## ✅ Phase 15.16 — the roll is applied whole, and PGSH records its disagreement

**Raised 2026-09-02 by the user, on the report 15.15 produced from the real file.** The rollover was
correct and still unusable: it *refused* the rows PGSH's own record disagreed with, and the faculty's
document is the more authoritative of the two.

> « the excel should create registration, even tho some conditions are not fulfilled — why, because
> in most cases they already validated / revalidated everything but we did not add the evaluations
> yet, so we can just flag the students so we can later come back to them. »

### What the refusal actually cost, measured

| | |
|---|---|
| 7ᵉ MED the roll re-registers into the 7ᵉ | 651 |
| …refused by the final-year gate, *before* session 37's « entrer » fix | **182** — a quarter of the promotion |
| …refused by it **today**, i.e. held by the roll — measured live 2026-09-02 | **60** |
| 2025-2026 registrations the roll never names | 1 267 |
| …in a final year, recorded « Diplômé » from the absence | 1 217 |
| …undecidable (below a final year, or no CNPN) | 50 |

⚠ **The two numbers belong to two code versions and must not be conflated.** 182 is what the gate
refused before session 37 corrected « entrer » to mean « commencer » rather than « être inscrit en ».
With that fix it reaches only genuine entrants to a final year, and the live preview on 2026-09-02
prints **60**. 182 is the motivation; 60 is the count.

Neither set is students who are behind: in most of them the stage was **served and the évaluation
is simply not keyed in**. That is a fact about our data entry, and refusing a registration over it
refuses the student the very mechanism — re-registration — by which the debt gets cleared.

### The shape: create it, freeze it, and give somebody a list

`RegistrationHold` (`Domain/Registrations/`), `Registrations/Holds/` for the worklist and the
release. A held registration takes part in **no** roster cut, gets **no** cohort affectation and is
published **no** période; it keeps its status, its verdict and everything already published under it.

- `OutstandingPriorStages` — the 60. Created and frozen: he may not begin his final year's stages
  before the earlier ones are settled, which is what the hold says and what a skip could not.
- `AbsentFromReinscriptionRoll` — **all 1 267**, the 1 217 inferred graduations included.
  ⚠ The user asked for this explicitly, and the reasoning survives scrutiny better than the original
  proposal to hold only the 50: **the graduation is our inference, read off a blank cell, never the
  faculty's statement.** A partial roll would end the cursus of people still enrolled with nothing on
  the row saying a human had looked. It costs a real graduate nothing, and it catches what an absence
  most often is — a réinscription that has not arrived — because the flag is still standing the day
  somebody registers him by hand.

**What still refuses the whole file** is unchanged and the line is principled: a duplicated code, an
unknown level code, a level contradicting the registration on record, a level going backwards, a
« Retrait ». Those say the *file* is mistaken, not that our data is behind, and the write they would
produce is a verdict on somebody's year.

**The manual paths still refuse too** (`CreateRegistrationCommand`, `CreateManyRegistrations`,
`InscriptionPlanner`), with `FinalYearEntryWaiver` as the deliberate override. The roll is the
faculty's own document and outranks a hand-typed form; and per-student ceremony is exactly what does
not scale to 182 at once.

### The report as a document

`POST reinscription/sheet/export` — Synthèse · Lignes · Absents, **uncapped**, written from the plan
rather than from the capped on-screen report. It re-runs the planner, writes nothing, and is offered
before the confirmation and on a roll the apply would refuse: « donne-moi la liste des erreurs » is
the request, and a refusal naming only the first offending line cannot answer it.

### 15.16b — the students the file names and PGSH has never seen

**Raised 2026-09-02, same session, after the roll was applied.** 26 of the 6 862 lines name people
PGSH does not hold. They were skipped on the rule that *creating an identity is the inscription's
act* — sound, and still wrong in practice: the only trace was a downloaded spreadsheet, so nobody
acted on them, and the user asked to see them in the app.

**The conflict that shaped the design.** He wanted them flagged **and** partitioned with everyone
else. A signalement, as built, freezes — so flagging them the existing way would have excluded them
from the very planning he wanted them in. The fix is to make **blocking a property of the reason**:

| reason | freezes | why |
|---|---|---|
| `OutstandingPriorStages` | yes | he may not start his final year's stages before clearing the earlier ones |
| `AbsentFromReinscriptionRoll` | yes | nobody has explained the absence |
| **`IncompleteStudentFile`** | **no** | his dossier is *thin*, not *wrong* |

`RegistrationHoldPolicy` grows a third form: `Plannable` (no **blocking** hold — what planning obeys),
`OnHold`, and `Flagged` (any hold — what the worklist counts). `BlocksPlanning` is sent to the client
rather than re-derived there, and `ReleaseHoldReport` carries `StillBlocked` beside `StillHeld`.

**What is invented, and what deliberately is not.** Only the e-mail, because `Users.Email` is NOT
NULL UNIQUE and a login: allocated in the *planner* against the addresses in the **store**, printed on
the row so the operator can read and communicate it. **No CNE** — the row has an Apogée and
`Student.CNE` is optional. `BacYear` is left **empty**, not guessed; that emptiness is what the flag
names.

⚠ Two defects caught by tests rather than by the compiler: adding the **student** left the
registration untracked (the graph is only whole from the registration, which references the student
and owns the hold), and `ReleaseHold` on an **unsaved** hold matched an arbitrary one, since every
store-generated key is `Guid.Empty` before the save — now refused outright.

### Open, parked deliberately

**When a 1650.25 student starts revalidating.** Under 2174.18 the 6ᵉ and 7ᵉ are both stage years with
no year exam, the 7ᵉ final; under 1650.25 only the 6ᵉ, and it is final. PGSH asks
`level.Year == TotalYears` per student (`FinalYearTest`), which reproduces the old text's behaviour
on the new one — the holding pattern the user asked for until the faculty states the rule. Nothing
hard-codes 7, so closing it is a change to one test. **Not urgent: the first 1650.25 promotion is in
its 3ᵉ année.** ⚠ Do not invent a rule meanwhile — a revalidation window opened under the wrong text
is indistinguishable from one somebody authored.

### Still not modelled: the *examens cliniques*

The user described them and they are real: they open as soon as a student's stages are all done, he
can fail them, and he is re-registered to sit them again. PGSH therefore cannot today tell « still
finishing stages » from « stages done, waiting on the clinical exams » — both read as re-registered.
Deliberately left out: the user said the logic is complex and did not describe it, and inventing it
would put a state on screen nobody can act on.

---

## ✅ Phase 16 — Re-importing the Access base, cleanly

> **16.1 and 16.2 are closed (2026-09-01).** `Student.CNE` is optional, the importer manufactures
> nothing, and the open question below is answered with a measurement rather than a guess. What the
> re-import *itself* needs, and the two silent traps it hides, are in **16.5**.

**Raised 2026-08-18 by the user, measured the same day against the live base.** The import of
2026-08-07 got the *rows* right — all 104,924 migrated and verified — but it manufactured identifiers
it did not need to, and the placeholders are now visible to every user of the app. Re-run the import
with the corrections below rather than patching the data in place, so the importer and the base agree.

### ✅ 16.1 — `LEGACY-nnnnn` is not an identifier, it is a prefix on the Appogée

> **Done 2026-09-01.** `Student.CNE` is nullable, `IX_Student_CNE` is filtered on `IS NOT NULL`,
> migration `StudentCneOptional` clears the 4 695 placeholders (guarded on `^LEGACY-[0-9]+$`), and
> `LegacyIdentityMapper` carries the source value across or leaves it absent. `InscriptionPlanner`
> stopped manufacturing `SANS-CNE-…` for the same reason. Every response type carrying a CNE is
> `string?`, every search predicate reads `(x.CNE ?? "")`, and the two uniqueness checks are guarded
> on the request value — `null == null` is true in memory and false in SQL, so an unguarded predicate
> reports a phantom conflict in the test suite and none in production.
>
> ⚠ **One defect found on the way, of the class this file already names twice.**
> `UpdateStudentCommandValidator` required `int.TryParse` on the Apogée, which no `SANS-APOGEE-…`
> value satisfies — so every student the inscription import created without an Apogée was read-only
> the day somebody opened his file, the refusal naming a field nobody was editing. Third instance
> after the CNE regex (5 646 students) and `Objectives.NotEmpty()` (the whole stage catalogue).

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

### ✅ 16.2 — Answered: **no**. Access does not hold the Appogée in the CNE column

> **Measured 2026-09-01 against `Medecine.mdb`, 10 203 rows, 5 508 carrying a usable CNE:**
>
> | | count |
> |---|---|
> | CNE identical to the row's own `NO_ORDRE` | **0** |
> | CNE equal to *another* row's `NO_ORDRE` | **0** |
> | CNE of exactly eight digits (the `NO_ORDRE` shape) | **1** |
>
> Shape distribution of the 5 508: 4 561 letter + digits, 835 digits-only of other lengths, 104
> alphanumeric, 4 with punctuation, 3 with an internal space.
>
> So the 835 digits-only codes are **not** appogées — not one of them matches any `NO_ORDRE`. There is
> nothing to move into `Appogee` and nothing to blank, and the importer carries the CNE across
> verbatim. The original reading below is preserved because the check it asked for is the reason the
> answer can be stated rather than assumed.

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

### 16.5 — ⚠ The order, and the two traps that are silent

Measured 2026-09-01 while building the rebuild. A naive
`drop → dotnet ef database update → import` fails, and two of the three problems leave no trace.

**1 · The chain does not run in that order.** `Cnpn1650Med3Stages`, `Cnpn1650ImmersionStages` and
`Cnpn1650Med3CatalogueAlignment` open with `RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine »…'` —
they need the `Levels` and `Stages` the *import* creates. This one at least fails loudly:

```
1. dotnet ef database update 20260830143914_PriorEnrolment
2. PGSH.LegacyImport --source Medecine.mdb --connection <cs> --apply
3. PGSH.LegacyImport --seed-curricula --connection <cs> --apply
4. dotnet ef database update
5. PGSH.LegacyImport --stamp-cnpn --connection <cs> --apply
```

**2 · Step 5 did not exist, and its absence is silent** — now `CnpnHistoryAttributor`. The student
attribution was one `UPDATE` inside `CnpnVersioning` and the registration backfill another inside
`RegistrationCnpnAndLevelEffectivity`; both were written to run *over* data already present. In the
order above they run before the import, stamp nobody, and are then recorded as applied. The base then
holds 10 200 students and 49 500 registrations with a null text — which **every reader tolerates
gracefully**, so nothing complains while the déliberation stops knowing whose year might be his last
and `CohortProvisioner` plans against requirement sets nobody is bound by.

**3 · The authored half must be dumped on *natural* keys.** 16.3 already says the re-import cannot be
a restore; what it does not say is that an id-keyed dump is worse than none, because the import
regenerates every surrogate key and the rows land on the wrong rows. Preserved for the 2026-09-01
rebuild: 24 `Holidays`, 146 `StageAllowedServices`, 3 `CnpnLevelEffectivities`, 2
`ServiceChefAssignment`, and the 2026-2027 `AcademicYear` — which the Access base does not contain at
all, and which one of the effectivity rules takes effect from, so it is restored first.

### 16.4 — Re-import hygiene

- The importer must be **re-runnable against a restored dump**, not against the current base: the app
  has written to it since (CNPN stamps, partitions, verdicts, périodes). Decide up front whether 16.1
  is a re-import or a targeted data fix, and if the latter, write it as a migration so it is recorded.
- Take a dump first (`pg_dump -Fc`) — see the rollback section of [`SMOKE-TEST.md`](SMOKE-TEST.md).
- Re-check the two defects the last import left and that were repaired by hand afterwards, so the new
  run does not recreate them: rosters keyed without their promotion (`SplitAcademicGroupsPerLevel`) and
  `Registration.LevelId` left null on 1,003 rows. Both are in the importer now; confirm with a query
  after the run rather than assuming.

---

## ✅ Phase 17 — Une pause est un fait de promotion, pas de stage

**Status: BUILT 2026-09-06 (session 49).** Raised by the user 2026-09-03 (session 40), after asking
whether a période can be moved mid-rotation. 17.1 — moving P7 while P3 runs — **remains open**; see
below.

### What was built, and the decision it rests on

`PromotionPause` (`Domain/Calendar/`), an aggregate keyed on **(AcademicYearId, LevelId)** carrying
`StartDate`/`EndDate` (inclusive), `PauseKind`, `Reason`, `IsConfirmed`, `RecordedOn`.

⚠ **It is a scoped calendar, and declaring one moves no date.** That is the answer the phase demanded
be *written down* rather than inherited. `Holiday` and `PromotionPause` now both implement
`ICalendarClosure` — "a stretch of days on which the people it covers are not in a service" — and
`WorkingDayCalendar` is built from closures rather than from holidays. `WorkingDayProvider` grew the
split the phase called the substantive work:

| caller | calendar | why |
|---|---|---|
| `GenerateAxisWindowsQuery` (+ `LevelId`, `AcademicYearId`) | promotion | the columns must step over the window; this is where the compensation happens |
| `PreviewRotationCycleQuery` | promotion | `DurationChecks` says what each stage actually gets |
| `GetStageAssignmentsExportQueryHandler` | promotion when the file has one — `request.LevelId ?? stage.LevelId` | a file scoped to a stage is one promotion, since a stage belongs to a level |
| `GetRevalidationContextQuery` | the **registration's** promotion, not the stage's | a 6ᵉ année re-taking a 3ᵉ année stage sits the sixth year's exams |
| `GetHolidayCoverageQuery`, an export left open on both level and stage | faculty | no single promotion applies, and quietly picking one would be worse than counting none |

**The compensation is therefore in *jours ouvrables*, and it happens when the axis is laid.** Declare
the window in September; the axis generated afterwards puts fifteen worked days in a column that ends
a week later on the wall calendar, and the grid and the périodes published from it are laid against
the same days from the start. Gap 3 — the grid drifting from the périodes — is impossible by
construction on that path.

**Declared after a grid is laid, it moves nothing**, deliberately: the créneaux keep their dates and
are now short by what the window takes. `PreviewPromotionPauseQuery` counts exactly that — per stage
and per créneau, worked days before and after — plus the rotations crossing it split by lifecycle
state. Silently rewriting the dates of a published promotion is the one thing this must not do.

⚠ **And whether that shortfall can be repaired is a fact about the promotion, found by running §50.5
on the live base rather than by reasoning.** Re-laying the axis is a real act *only while nothing has
been published from it*: `ApplyRotationCycleCommand` refuses on `PublishedCells > 0` for the whole
block, and the 3ᵉ MED holds **1 000** — « Appliquer l'axe » is disabled and the page says « ce bloc ne
peut plus être redéfini ». The preview carries `PublishedCellsInGrid` and its warning branches on it,
because **a report that prescribes a refused button is worse than one that prescribes nothing**.

#### ✅ 17.2 — the report caught up with its own remedy (17/09/2026)

Raised by the user while reading the real screen — 3ᵉ MED, 10/03/2027 → 30/04/2027, 37 jours ouvrables,
16 créneaux, 1 000 cellules publiées. Three defects, all in `PromotionPauseImpactReader.Warnings`.

- ⚠ **The published branch named no remedy, and by then one existed.** It ended « déplacer une colonne
  déjà publiée n'est pas encore possible » — written the day it was true, and left standing when §17.1
  shipped the move four sessions later. **Naming no remedy where one exists reads as « les jours sont
  perdus »**: the same defect as prescribing a refused button, arrived at from the other side. It now
  names the move.
- ⚠ **And it now says how much of it is worth starting** — `SlotsMovable`, how many of the crossed
  columns the move would accept, via `PromotionPauseQueries.UnmovableSlotsQuery`. On that screen the
  answer is **16 of 16**: a published rotation is `IsStarted = false` until the administration starts
  it, which is exactly why « Rotations en cours » read 0 beside 933 students. The three sentences —
  all / some / none — differ because the acts they call for differ; a bare number left the reader to
  guess which case they were in.
- ⚠ **A third case had no branch at all**: rotations crossing the window while **no créneau** does —
  périodes written *hors grille* by the canevas des affectations, a délocalisation or a legacy import.
  Neither remedy reaches them (one starts from the axis, the other from the cells). On a promotion with
  rotations under way it fell into « reposez l'axe » — a gesture that succeeds and changes nothing for
  them — and on one at rest into **no warning whatsoever**. Now named, with the remedy that does apply:
  re-send the canevas with the windows shifted.

- ⚠ **A fifth case, found on the real screen 17/09/2026 and fixed the same day**: a column the window
  **empties**. `SlotsEmptied` / `CellsInEmptiedSlots`. December 2026 over the 3ᵉ MED — 23 worked days
  against columns of 15 — leaves one column of every one of the 8 stages with **no worked day at all**,
  and `Warnings()` had never been given `MinWorkingDaysAfter`: the only trace was the left end of a
  « 0 – 10 » range in a table cell. Emptied is not shortened — a shift catches up a short column, while
  an empty one is a rotation nobody serves — so it gets its own sentence, **added** to the remedy rather
  than substituted for it.

**The rule behind `SlotsMovable` was extracted rather than copied.** `IsStarted || IsComplete ||
Evaluation != null || Attendance.Any()` was written out in `InternshipAssignment.Reschedule` and again
in `PublishedPeriodShifter.PlanAsync`; a third copy in the report is how a screen comes to promise a
move the aggregate then refuses. It is now `ServicePeriodLifecycle.Movable`, and the store-side read
**composes** it (`ExpressionComposition.Through` + a new `.Not()`) instead of restating it — `Invoke`
is what EF refuses. ⚠ It is deliberately *not* derived from the four lifecycle states: it reads
`Attendance`, which bears on none of them, and a `Planned` rotation carrying présences must not move.

⚠ **What it still does not do: cascade.** A move shifts one column and leaves the ones after it where
they are, so repairing those 16 columns is 16 acts and the run-order guard will refuse the ones that
would overlap. The warning says so in words. The promotion-wide cascade — split the column the window
falls inside, push the rest of each partition's itinerary, check no other promotion is standing where
they land — is **not built**, and it is the natural next phase.

**Re-applying moves nothing** — the property the phase asked for, and here it is trivially true
rather than carefully arranged: nothing is added to what is stored, so the same window twice is the
same dates (`PromotionPauseTests.Declaring_the_same_window_twice_gives_the_same_days`).

**Revoking is prospective, and the same reasoning makes it free.** Nothing was pushed, so nothing is
walked back: the days go back into the calendar, créneaux keep the dates they were given, and périodes
already served keep what happened. The result says whether the window `HadBegun` and how many créneaux
were laid across it. ⚠ **No domain event on the revocation** — the aggregate root is removed, and EF
detaches a deleted entity before `ApplicationDbContext` collects events, so one raised there would be
dropped without a trace. `PROMOTION_PAUSE_REVOKED` in the register is what records it.

**The four acts:** preview (writes nothing, same reader as the act, `ExcludingPauseId` so a correction
is measured against a calendar that does not still hold the old dates), declare
(`PromotionPauseDeclaredDomainEvent`, `PROMOTION_PAUSE_DECLARED`), correct (union of the span it leaves
and the span it reaches, counted **before** the write, gated on `PromotionPause.WouldMove`), revoke.
Plus `GET calendar/promotion-pauses`, paginated and year-scoped.

**Guards:** `Level.IsPromotion`; two windows of one promotion may not overlap
(`PromotionPauseCalendarGuard`, the `AcademicYearCalendarGuard` division — the aggregate decides what
it can alone, the guard decides about the *other* rows); the window falls inside the year it names; a
ceiling of `MaxSpanDays = 120` because beyond a term the right row is a faculty `Holiday`.

**`MissingReligious` counts faculty closures only** — a pause named « Aïd al-Fitr » is a promotion
saying it is out that week, not the decree naming the date, and counting it would report the calendar
complete on the strength of one promotion's window.

**~~The stage-scoped pause stays.~~ ❌ Reversed on 18/09/2026 — it was retired instead.** See §17.2
below: the two mechanisms behaved as opposites, the cascade had to pick a side, and the side it picked
is the declarative one. `StagePauseRunner`, `PauseStagePeriodsCommand`, `ResumeStagePeriodsCommand`,
`InternshipAssignment.PausePeriod` / `ResumePeriod`, the two routes and the two buttons are gone.

**Côté écran:** `PromotionPausesPanel` on the calendar page (declare / preview / correct / revoke, the
impact report bounded to créneaux and stages with cohortes, rotations and students *counted*), and
`RotationCyclePage` now sends `levelId` to the axis generator — without which the whole mechanism is
invisible. `GeneratedAxisColumn.Pauses` is reported apart from `Holidays`: a holiday is everyone's.

**46 tests** (1 695 green): 15 pure-domain, 19 handler-level, 12 through the real HTTP pipeline, and
2 `SqlTranslationTests` cases — the créneau reached through `Stage.LevelId` and the période reached
through `InternshipAssignment.Registration` are joins the store had never been asked for. Both new
guards were broken and restored to prove they bite.

#### ✅ 17.3 — a day that counts and does not bound (17/09/2026)

The faculty's rule, given 10/09/2026: **the only planning constraint is that a période neither begins
nor ends on a rest day or a closure.** A closure may therefore be *crossed*, and crossing it need not
lengthen the window that crosses it — which the calendar could not express, because
`WorkingDayCalendar.IsWorkingDay` answered two questions at once. Marking a férié worked would also have
let a stage end on it.

- **Two predicates, and no `IsWorkingDay`.** `CountsTowardDuration` (is somebody expected in a service)
  and `CanBoundAWindow` (may a période begin or end here). Removing the old one **named its own
  offenders**: six test call sites, each of which turned out to be asking one question or the other.
  ⚠ They are **nested**, never independent — every bounding day counts, not every counted day bounds —
  and a sweep over seven months pins it.
- **The flag is `ICalendarClosure.CountsAsWorkingDay`, on the interface.** That interface's own sentence
  is that its two implementations « differ in scope and in nothing else »; a flag on `Holiday` alone
  would have made it quietly false. `PromotionPause` answers `false` **unconditionally and without a
  column** — a promotion sitting an exam is not in a service. `ProposedClosure` computes it from the
  scope rather than taking it: that record exists so a preview and the act it previews cannot report two
  numbers, and a free field there would have been the one able to make them disagree.
- ⚠ **`Lay` may now return a window holding more than was asked, and says so.** When the Nᵗʰ counted day
  falls on a worked closure, `End` advances to the next day that can bound and the worked days crossed
  on the way are **counted**. The item's note said to extend « sans le compter »; taken literally that is
  the contradiction its own next clause warns about, since the day the window ends on is itself worked.
  `Count(Start, End)` therefore still equals `WorkingDays`, and the divergence travels in
  `WorkingDayWindow.RunsLongerThanAsked`.
- **Two defects found while writing, both outside the calendar.** `UpdateHolidayResult.SlotsSpanning` was
  gated on `DatesMoved` alone, so the one change that gives days *back* would have been the only silent
  one (`CountingChanged` is named beside it); and `UpdateHolidayCommand.CountsAsWorkingDay` is `bool?`
  with null meaning **unchanged**, because a full-replace PUT from a client that has never heard of the
  field would otherwise undo a flag somebody set on purpose.
- **Additive migration, `DEFAULT false`** — the old arithmetic line for line, so it lands on a base
  carrying a published promotion without moving a date. ⚠ **No screen yet** (separate repository), so
  nothing can be flagged from the application: item **0cf**.

### The gap analysis this replaced — kept because it is why the shape is what it is

> **The sentence that settles the scope:** « a pause and a matter of exams … is a matter of whole
> promotion because some promos does not have exams while others have ». Two promotions rotate
> through the same services on the same morning; one of them is sitting an exam and the other is not.
> The unit of the act is therefore **(année académique, niveau)** — never the stage, and never the
> faculty.

#### What existed, read from the code 2026-09-03 — ⚠ **retired 18/09/2026, kept as the record of why**

`StagePauseRunner` (`Application/Stages/Planning/`) with `InternshipAssignment.PausePeriod` /
`ResumePeriod`. It ran, and it answered a different question — which is what kept it alive through two
reviews. The table below is the reading that eventually condemned it: every row is a decision taken at
**pause time**, and writing dates at pause time is the single cause of all four gaps.

| | today |
|---|---|
| scope | **one stage**, one academic year (both mandatory), optionally narrowed to cohortes, a partition label, or period numbers |
| what it touches | every `ServicePeriod` that is `Underway`; opens a `PeriodPause` row |
| when | **now** — pause and resume both stamp `DateOnly.FromDateTime(DateTime.UtcNow)` |
| compensation | on **resume**: `days = resume − start`, added to that période's `EndDate`, then every later période of the same assignment that is neither complete nor interrupted is pushed forward by the same amount |

Four gaps, and each is a design fault rather than a missing option:

1. ⚠ **Pausing a promotion's exam week is one call per stage, and nothing records that they were one
   event.** The calls can disagree — a stage forgotten, a cohorte filtered out, one that failed — and
   there is no row to correct or revoke afterwards. The promotion is not even expressible: the scope
   is a *stage*, which covers one promotion only because `Stage.LevelId` says so, and §15.1 already
   plans for a stage to span two levels.
2. ⚠ **The shift is in calendar days.** `WorkingDayCalendar` is not consulted on this path at all, so
   a pause spanning a weekend costs two days nobody was going to serve — in a project where *jours
   ouvrables* is already the unit the catalogue durations are expressed in (25 of 27 stages, measured
   2026-08-13).
3. ⚠ **Only the `ServicePeriod`s move; the `StageSlot`s and cells do not.** After a resume the
   published périodes and the grid they were published from disagree, silently, with nothing on
   either side saying so. Same class of drift as `UpdateStageSlotCommandHandler` rewriting a window
   without touching what was published from it — see 17.1.
4. ⚠ **Nothing can be declared in advance, and a forgotten resume is silent.** An exam week is on the
   calendar in September; the current shape needs somebody present on the first morning and on the
   last. A pause never resumed leaves rotations frozen with no end date, no compensation, and nothing
   on any screen that reads as wrong.

#### The shape that was built

**A `PromotionPause` is a declared window, not an event that has to happen twice.**

- Aggregate keyed on `(AcademicYearId, LevelId)` carrying `StartDate` / `EndDate` (inclusive, as
  `Holiday` is), `PauseKind`, `Reason`, `IsConfirmed`.
  - **`IsConfirmed` is `Holiday.IsConfirmed`'s bargain**: a provisional window still blocks its days
    — you plan on the best estimate — but every window laid over one is flagged, so the répartition
    can be reprinted when the dates are settled instead of being quietly a day out.
- ⚠ **Model it as a *scoped calendar*, not as a second date-pushing mechanism.** A `Holiday` is
  already "days nobody serves", already flows through `WorkingDayCalendar`, and is already read by
  the axis laying, `RotationCyclePreview.DurationChecks`, `StagePeriodFolder`'s gap test and the
  exports. An exam week is the same *kind* of fact with a narrower scope. So the work is
  `WorkingDayProvider.ForPromotionAsync(yearId, levelId, ct)` — faculty holidays ∪ that promotion's
  pauses — and every existing reader then compensates for free, in worked days, with the grid and the
  périodes staying in agreement because they are laid from the same calendar.
  - Callers holding no promotion (the Holidays screen, anything faculty-wide) keep the current
    calendar. **That split is the substantive work of this phase; the CRUD around it is not.**
- ⚠ **Re-applying must move nothing.** `ResumePeriod` *accumulates* — it adds days every time it
  runs. A declared window must be **derived from**, never added to: the same window applied twice is
  the same dates. That property is what makes the act correctable and revocable at all.
- **Four acts, and the last two are the reason for the phase:**
  - **Preview** — which stages, cohortes, périodes and créneaux the window crosses, and the worked
    days each loses. Writes nothing.
  - **Declare** — with a domain event. This is the widest act in the area (it moves the dates of
    every rotation of a promotion) and, like `CnpnVersion.DeclareEffectivity`, it must be observable.
  - **Correct** — the shape `UpdateHolidayCommand` already has: report over the **union** of the span
    it left and the span it arrived at, counted **before** the write, gated on the dates actually
    moving so that ticking « confirmée » on a span already right reports nothing.
  - **Revoke** — prospective while the window is still ahead. ⚠ **Decide explicitly what revoking a
    window already begun does, and write the answer down**: either the compensation already applied
    is walked back (and périodes already served then disagree with what happened) or it is kept and
    the revocation is only prospective. The second is almost certainly right — it is the rule
    `DeleteCnpnEffectivityCommand` and `ResumePeriod` already follow — but it must be *stated*, not
    inherited by accident.
- **Guards:**
  - `Level.IsPromotion` — « Retrait » sits no exams (`Levels.NotAPromotion`).
  - **Two pauses of one promotion may not overlap** — same rule and same reason as
    `AcademicYearCalendarGuard`: the days in the overlap would be counted twice against every
    duration.
  - The window falls inside the academic year it names.
  - ⚠ **Closed and interrupted périodes never move.** The retired `ResumePeriod` got this one right
    (`!p.IsComplete && !p.IsInterrupted`) and the rule outlived it: it is now
    `ServicePeriodLifecycle.Movable`, read by `InternshipAssignment.Reschedule` and by the pause
    preview, which is stricter — it also refuses a rotation carrying **journées de présence**, a state
    `ResumePeriod` walked straight over. A closed rotation is what actually happened, and pushing it
    forward rewrites the past to make room for the present.
- **~~The stage-scoped pause stays.~~ ❌ Reversed 18/09/2026, and the reversal is worth reading rather
  than skipping** — the argument above is not wrong, it is answering a question nobody had asked.
  - **What the argument said:** « a service closing for a week, a stage suspended for one cohorte, is
    genuinely per stage ». True as a *domain* statement. What made it decide wrongly is that it
    compared the promotion pause to an idealised stage pause rather than to the one in the repository.
  - **What was actually there**, measured 18/09/2026: an act that lengthened a stage in **calendar**
    days (a week-end inside the window counted as two lost days and pushed the rotation two days
    further); that moved the student's `ServicePeriod` dates and **not** the créneaux or the cellules,
    from which `ServiceOccupancyCalculator` reads occupancy — so the grid showed one window and the
    dossier another, with nothing saying which was true; that took its date from
    `DateTime.UtcNow` at both ends, so someone had to be present on the first morning and again on the
    last; that pushed later périodes **without** the `Movable` guard, over journées de présence already
    recorded; that raised no domain event; that wrote **nothing** to the register, not being
    `IAuditableCommand`; and whose `SaveChanges` was behind `if (affected > 0)`, so a run with no effect
    left no trace of having been attempted. Replayed, it moved everything twice.
  - **And it had never been used.** The live base held **0** paused périodes, **0** `PeriodPause` rows
    and **0** `PromotionPauses` on 18/09/2026. The « different question » was hypothetical; the defects
    were not.
  - ⚠ **Say the loss plainly: suspending one cohorte's rotation is now something PGSH cannot do.**
    That is a withdrawal, not a substitution, and it should be recorded as one. When the faculty asks
    for it, it gets built on the shape that survived — a scoped **declaration** that writes no date,
    plus `InternshipAssignment.Reschedule` for the columns it cuts — and not by restoring an act whose
    every defect followed from writing dates at pause time.

### ⏭ 17.3 — Rattraper une fenêtre sur une promotion publiée (spécifié 18/09/2026, **pas encore écrit**)

**La question, telle que l'utilisateur l'a posée :** « une pause tombe au milieu de P3 et finit au
milieu de P4 ; on repousse la fin de P3 et la suite suit. Sur une promotion non publiée c'est facile
— pourquoi serait-ce difficile sur une publiée ? Ce ne sont que des dates. »

**Il a raison, et la première spécification de cet item visait la mauvaise moitié du problème.**

#### ⚠ Ce qui a renversé la spécification

`ServicePeriodLifecycle.Movable` vaut
`!IsStarted && !IsComplete && Evaluation == null && !Attendance.Any()` : **une rotation commencée ne
se déplace pas du tout.** L'ancienne fiche disait « déplacer les colonnes traversées, une par une » —
or le cas qui compte vraiment n'est pas la semaine d'examens oubliée (celle-là se prévient en
déclarant avant de poser l'axe) mais la **fermeture imprévue en cours d'année** : grève, épidémie,
deuil national, un service qui ferme. Par définition elle arrive quand tout est publié **et
commencé**, donc l'acte spécifié n'aurait rien pu déplacer et aurait rapporté zéro.

⚠ **Le remède est de *prolonger* la colonne en cours, puis de pousser les suivantes** — c'est
exactement l'arithmétique de la pause retirée en 73. Elle avait la **bonne forme pour ce cas-là** et
tout le reste de faux (jours calendaires, grille laissée derrière, `UtcNow`, aucun événement, aucun
registre, accumulation au rejeu). Ce qu'il faut en garder est la forme, pas le code.

⚠ **Donc `Movable` confond deux questions** et devra les séparer :

| Sur une rotation commencée | |
|---|---|
| Déplacer son **début** | **Non** — cela réécrit ce qui a eu lieu |
| Repousser sa **fin** | **Oui** — rien de passé n'est touché, l'étudiant sert plus longtemps |
| Prolonger une rotation close ou notée | Non |
| Pousser une colonne **suivante**, non commencée | `Movable` tel quel |

#### La décision structurante : aucune table d'historique

L'axe est une **fonction pure** : date d'ancrage + durées des stages + **calendrier de la promotion**,
lequel contient déjà les fenêtres déclarées.

- Déclarer une fenêtre → recalculer → les colonnes se replacent.
- **Révoquer → recalculer → les dates d'origine reviennent toutes seules.**
- Deux, trois fenêtres coexistent sans interagir : il n'y a pas de deltas à composer.

⚠ **C'est pourquoi il ne faut rien stocker des anciennes dates.** L'utilisateur a demandé une
traçabilité pour pouvoir défaire ; la réponse est que recalculer *est* le défaire. Stocker les
anciennes fenêtres obligerait chaque correction à porter son propre annulateur, et deux fenêtres
successives deviendraient une histoire à rejouer dans l'ordre. C'est déjà le principe que
`PromotionPause` énonce sur lui-même (« derived from, never added to ») ; il s'agit de l'étendre à la
grille au lieu de s'arrêter au calendrier.

#### Les six autres décisions

1. **Modifier, jamais recréer.** `InternshipAssignment.Reschedule` existe, garde dans l'agrégat, et
   transporte les deux fenêtres.
2. **Un seul événement pour l'acte.** `ServicePeriodRescheduledDomainEvent` n'a **aucun
   consommateur** (vérifié le 18/09/2026) ; en lever un par rotation coûterait les minutes que
   `NOTES.md` décrit sur la déliberation, et l'historique par ligne est redondant dès lors que les
   dates sont dérivées. L'événement d'acte plus `IAuditTrail.RecordOutcome` portent les comptes.
3. **Le même acte sert une promotion non publiée**, où la moitié « périodes » est simplement sans
   effet. ⚠ Mais il ne **ré-arrange pas les services** : ne bouger que des dates est précisément ce
   qui le rend sûr sur du publié, là où reposer l'axe est refusé.
4. **Un seul refus** : un séjour portant une **note** ou des **journées de présence**. Les compter,
   les nommer, déplacer le reste.
5. **Le dépassement de capacité qui en résulte est un rapport, pas un blocage** — règle tranchée le
   12/09/2026 et rien ici ne la rouvre.
6. **Une seule migration : un marqueur « déplacée à la main » sur `StageSlot`.** Il n'en a aucun là
   où `CohortSlotAssignment.Source` en a un, donc un recalcul écraserait en silence la correction
   qu'un humain a faite par §61 — la faute exacte que `Source` avait été créé pour empêcher.

#### Coût, et pourquoi il est tenable

Le recalcul lui-même est trivial (une trentaine de colonnes). Le prix est dans les séjours — 4 625
pour la seule 4ᵉ MED. Donc : une requête étroite pour savoir ce qui bouge et ce qui est refusé,
l'aperçu tiré de la **même** fonction (donc gratuit), puis le chargement et la mutation dans une seule
transaction. Ne pas lever d'événement par ligne est la plus grosse économie.

### ✅ 17.4 — « déplacer le début » et « repousser la fin » sont deux questions (18/09/2026)

La pièce de domaine sans laquelle le rattrapage d'une promotion **publiée** ne peut rien faire du cas
qui compte. `ServicePeriodLifecycle` portait une seule règle de mobilité, `Movable`, qui refuse toute
rotation commencée — à juste titre : quelque chose a eu lieu à cette date de début. Mais une fenêtre
imprévue (grève, épidémie, deuil, session d'examens déclarée tard) tombe précisément sur une
promotion **en cours**, donc sous cette seule règle le rapport comptait zéro colonne déplaçable et
concluait « les jours sont perdus ».

**Ce qui a été livré :**

- **`ServicePeriodLifecycle.Extendable`** — `!IsInterrupted && !IsComplete && Evaluation == null`.
  Le démarrage n'entre pas, c'est tout le propos ; les présences non plus.
- **`InternshipAssignment.ExtendTo(periodId, newEnd)`** — un second acte, pas un drapeau sur
  `Reschedule` : deux questions, deux gardes, et un drapeau aurait fait choisir l'invariant par
  l'appelant.
- **`StageErrors.PeriodCannotBeExtended` / `PeriodExtensionGoesBackwards`** — deux phrases distinctes,
  parce qu'une phrase partagée dirait « cette rotation a commencé » à propos d'un allongement : vrai,
  et sans rapport avec le refus.
- **`Movable` gagne `!IsInterrupted`** — narrowing assumé, documenté à sa source.

⚠ **Les présences interdisent un déplacement et pas un allongement**, et cette asymétrie *est* la
raison d'être de la scission : une journée pointée vit entre le début et l'ancienne fin, et une
fenêtre qui ne fait que croître la contient toujours. Le refus qui manque donc à `Extendable` — ramener
la fin en arrière — vit dans le **nom de l'acte**, pas dans un état qu'un appelant pourrait mal lire.

⚠ **Les deux règles sont emboîtées, `Movable` ⊂ `Extendable`**, exactement comme
`CountsTowardDuration` et `CanBoundAWindow` depuis §17.3 et pour la même raison : deux prédicats
indépendants finissent par répondre des choses incompatibles sur une même ligne, et l'acte de
rattrapage choisit alors le mauvais. C'est pour en faire un **théorème** plutôt qu'une coïncidence que
`IsInterrupted` a rejoint `Movable` — une rotation coupée par un transfert a ses deux bouts pour faits.
En pratique une interruption implique un démarrage, donc rien de ce que le cycle de vie produit ne
change ; ce que cela ferme est une ligne que le magasin peut porter. Vérifié sur les **32**
combinaisons, pas sur un échantillon.

⚠ **`ExtendTo` écrit une date *absolue*, jamais un delta.** C'est la propriété qui rendra le recalcul
d'axe rejouable : déclarer, corriger puis révoquer une fenêtre relance l'acte autant de fois qu'il le
faut sans que la rotation s'allonge à chaque passage. `ExtendBy(jours)` aurait été exactement
l'accumulation pour laquelle la pause par étape a été **retirée** plutôt que réparée (§17.2).
L'événement est le même que celui du déplacement — `ServicePeriodRescheduledDomainEvent`, début
inchangé des deux côtés — parce qu'un allongement *est* un changement de fenêtre, et que deux types
pour un fait obligeraient chaque futur consommateur à s'abonner deux fois.

⚠ **Rien n'en dépend encore**, et c'est volontaire : c'est la clé de voûte du recalcul promotion-wide
(item 0ce), posée et éprouvée seule pour que la suite n'ait pas à la redécider. Aucun écran, aucun
changement de comportement pour un appelant existant.
→ `PGSH.Tests/Domain/PeriodExtensionTests.cs`, `ServicePeriodLifecycleTests`

**Et la seconde pièce, `StageSlot.Source`** — la seule migration que l'item 0ce annonçait. Pendant de
`CellSource` d'un cran plus haut : celui-là empêche l'arrangeur de réécrire une *cellule* choisie par
un humain, celui-ci empêche un recalcul d'axe de réécrire les *dates* choisies par un humain.
`StartDate`/`EndDate` en `private set`, deux portes — `MoveTo` (marque dans le même geste) et
`RelayTo` (l'axe écrit, et refuse une colonne déplacée à la main). ⚠ Une colonne déplacée à la main
**ancre** : un axe est ordonné, donc la cascade reprend après elle et un chevauchement est un refus
nommé, là où une cellule épinglée se contente d'être laissée tranquille. Migration `StageSlotSource`,
**purement additive, `DEFAULT 'Laid'`** — elle atterrit sur la base vivante sans reclasser une ligne.
→ `PGSH.Tests/Domain/SlotSourceTests.cs`, `PublishedColumnMoveTests`

### 🚧 17.5 — l'arithmétique du rattrapage, posée pure (18/09/2026)

Le cœur de l'item 0ce : **reposer les colonnes d'un axe sur le calendrier de sa promotion**, à partir
de la première que la fenêtre ampute. `AxisRelayPlanner` — pur, ni base ni horloge, comme
`RotationCyclePlanner` et pour la même raison : les cas pénibles s'éprouvent directement.

**Le modèle, vérifié dans le code plutôt que supposé** : un axe est `T` colonnes de `n` jours
ouvrables posées bout à bout (`WorkingDayCalendar.LaySeries`), et **chaque stage du bloc porte une
colonne par numéro** (`RotationCyclePlanner` : `tilings.SelectMany(columns)`), donc « P3 » est *une*
date et non une date par stage. Reposer l'axe est refaire cette pose sur le calendrier courant.

⚠ **La première colonne recalculée garde son début.** Toute la différence avec « reposer depuis le
départ » : la fenêtre tombe au milieu d'une colonne déjà commencée et les étudiants y sont entrés à
cette date. On la **prolonge**, puis les suivantes s'enchaînent — ce que `Extendable` autorise là où
`Movable` refuse (§17.4).

⚠ **Une colonne déplacée à la main ancre.** La cascade reprend après elle ; si la précédente vient
mordre dessus, c'est `RelayOverlapsAnchoredColumn` — nommer les deux colonnes et la date, plutôt que
pousser (effacer la décision) ou enjamber (casser l'ordre).

⚠ **Idempotent, et le test le dit** : reposer deux fois donne le même axe, et *révoquer la fenêtre
puis reposer rend exactement les dates d'origine* — sans table d'historique et sans rien à défaire.
C'est la propriété entière pour laquelle la pause par étape a été retirée plutôt que réparée.

⚠ **`WorkingDaysRecovered` est le chiffre qui justifie l'acte**, distinct de `ColumnsMoved` qui n'en
dit que l'ampleur ; et `AxisEndsOn` dit jusqu'où l'année s'allonge, qui est une décision et non un
détail d'arithmétique.

**Et le lecteur côté magasin, `AxisRelayReader`** — il lit les créneaux, **dérive** la longueur d'une
colonne, passe l'arithmétique au planificateur, puis traduit le déplacement des colonnes en ce qu'il
faut faire à chaque rotation publiée (`Move` / `Extend` / `Blocked`).

⚠ **La longueur d'une colonne est dérivée, jamais demandée — et pas depuis les dates courantes**, qui
sont précisément ce que la fenêtre a abîmé : une colonne amputée de 5 jours se reposerait à 17 et la
perte deviendrait définitive. Elle se mesure sur le calendrier **courant** de la promotion, par le
**mode** : une colonne posée en enjambant une fenêtre déjà déclarée tient toujours ses `n` jours
(définition de `Lay`), seules celles qu'une fenêtre *postérieure* traverse en tiennent moins. La
moyenne serait tirée vers le bas par ce que l'acte vient réparer ; le maximum casserait sur une
colonne rallongée à la main. `ColumnsAgreeingOnLength` dit à quel point on peut s'y fier, et une
majorité discordante avertit plutôt que de deviner.

⚠ **Une rotation bloquée n'arrête pas l'acte** : une note est un fait, et le reste de la promotion a
besoin d'être poussé. Elles se comptent (`PeriodsBlocked`), sinon rattrapé et non-rattrapé se lisent
pareil.

**Vérifié contre la base vivante (19/09/2026).** L'axe réel de la 4ᵉ MED — six colonnes de 22 jours
ouvrables pour 30 à 35 jours calendaires — et la fenêtre 05→09/10 déclarée par la faculté : les six
colonnes recalculées tombent exactement sur les dates calculées **à la main avant d'écrire le
planificateur**, férié par férié, et `WorkingDaysRecovered = 5`. ⚠ La première version du test n'a
porté qu'un des six fériés de l'étendue et a échoué pour cela — un calendrier incomplet ment dans le
sens rassurant.

**L'acte est livré** — `PreviewAxisRelayQuery` + `ApplyAxisRelayCommand`, deux routes sous
`levels/{levelId}/axis-relay`, tout par les agrégats, une transaction, deux comptes confirmés.
Vérifié contre la base vivante le 19/09/2026 : 30 créneaux, 4 625 rotations (925 allongées, 3 700
déplacées, 0 bloquée), les six colonnes exactement sur les dates prédites à la main.

**Et il écrit dans les deux sens depuis le 20/09/2026.** ⚠ Ce n'est **pas** une annulation, et le
dire ainsi induit en erreur : il n'y a ni historique, ni état précédent stocké, rien à rejouer.
L'axe est un calcul, et l'acte écrase le stocké par ce que ce calcul rend. Supprimer une fenêtre
déclarée fait rendre au même calcul des dates antérieures — on les réécrit, voilà tout. La détection cherchait seulement les colonnes trop
*courtes*, donc révoquer une fenêtre après un rattrapage laissait l'axe étiré sans retour possible.
`FirstDivergentColumn` regarde les deux sens ; `WorkingDaysChanged` est **signé** ; et surtout
`InternshipAssignment.ShortenTo` existe, parce que revenir en arrière veut dire raccourcir et
qu'`ExtendTo` refuse cela par construction. ⚠ **Les deux directions n'ont pas la même garde** :
allonger ne peut rien orpheliner, raccourcir le peut, et c'est pourquoi ce sont deux actes.

**Le rapport inter-promotions est livré** — `AxisRelayCrossingReader`, porté par l'aperçu. Il dit
combien de services porteront plus de monde qu'aujourd'hui à leur heure de pointe, et **nomme** les
promotions qui partageront ce pic. ⚠ Un rapport, pas une garde. ⚠ Et il ne refait pas l'arithmétique
d'occupation : `OccupancyTimeline` reçoit les dates proposées à la place des dates stockées, ce qui
évite la troisième copie et le piège du 03/09 (le pic, jamais la somme).

**L'écran est livré** (`AxisRelayPage`, dépôt `PGSH_Frontend`), et le panneau des suspensions y
pointe avec la promotion déjà choisie. ⚠ **Pourquoi un acte séparé plutôt qu'un effet de la
déclaration** : déclarer écrirait alors ~4 600 lignes et révoquer devrait les défaire, ce qui
détruit la propriété même de `PromotionPause` ; une fenêtre déclarée avant la pose de l'axe n'appelle
aucun recalcul ; deux fenêtres doivent donner un seul recalcul ; et l'acte déplace la fin de l'année
universitaire, qui est une décision.

**§17.5 est close.** Reste, hors de cette phase : `0cl` (la génération des présences ignore le
calendrier) et le pilotage humain de la fenêtre de confirmation.
→ `PGSH.Tests/Application/AxisRelayPlannerTests.cs`, `AxisRelayCommandTests`, `SqlTranslationTests`

### ✅ 17.1 — moving P7 while P3 runs (built 13/09/2026)

« Ten periods, we are in P3, can we change P7 — and therefore P8-P10 — without republishing
everything? » **Yes**, since `PublishedPeriodShifter`: the column and the périodes published from it
move as **one** operation, under one transaction, with the count confirmed.

How the three obstacles were answered:

- `UpdateStageSlotCommandHandler` gained a published-guard on 13/09/2026 and then, the same day, the
  act the guard was standing in for. It no longer refuses a published column: it re-derives every
  affected période's window and writes both halves together. Leaving it at the refusal was expensive
  — on a promotion published in its entirety the only remedy was « dépubliez d'abord », i.e. destroy
  a year's plan to shift one week.
- `SetCohortSlotAssignmentCommandHandler` asking about the **cohorte** rather than the cell turned out
  to be **right**, not a gap — publication is once per cohorte, so narrowing it would have let an edit
  *appear* to work and produce nothing. Its twin `ClearCohortSlotAssignment` was moved to match
  (13/09/2026); the asymmetry was the actual defect. → `docs/planning-rotation.md`.
- `UnpublishCohortScheduleCommand` still has **no period scope**, and that is now a standalone hazard
  rather than a blocker: nothing in the reschedule goes through it any more. It remains the only path
  that destroys marks, and it does so at whole-cohorte granularity. → HANDOFF « A2 (reste) ».

⚠ **`SingleService` was the complication, and it is what shaped the design.** A période spans a whole
run, so a window is **re-derived** from the cells it covers — min/max, the same rule
`CohortStayFolder` uses when publishing — and never offset by a delta. Moving the *middle* column of a
run therefore changes nothing, which is correct and is not what an offset would have written. The act
reports `PeriodsCovered` and `PeriodsShifted` separately for exactly this reason; 5ᵉ/6ᵉ année are
`SingleService` in 51 923 of 51 924 imported placements, so the smaller number is the common case.

⚠ **What it refuses**: a période that has begun, carries an evaluation, or carries **attendance** —
counted and named, because a présence is invisible until the day somebody needs it — and any move
that would leave a run's columns out of order.

⚠ **Not verified against the live base.** The 3ᵉ MED holds 7 464 published périodes; moving one of its
columns is the user's click, not a verification to help oneself to. `SMOKE-TEST.md` §61.

#### Definition of done — met, and rehearsed on the live base 06/09/2026

✅ **Driven end to end in a browser on the real faculty base** the day it was built: preview (5 j.
ouvrables, 6 créneaux, 933 étudiants — the 3ᵉ MED's exact roll, which is what verifies the scoping
through the registration), declaration, the A/B on the axis (C4 janv 18 → **janv 25**, every column
still 30 j. ouvrables), and revocation putting the axis back exactly. ⚠ **One thing was NOT run and
deliberately so:** re-laying the axis for real, which would replace the 3ᵉ MED's 804 published cells.

⚠ **The screen found a defect the tests could not**: the « aucun jour férié » caption was measured on
the *window* rather than on the academic year, so it fired on nearly every window — noise, by this
project's own rule. Corrected, with a test that bites.

- The pure calendar half tested exhaustively, the way `RotationTiling`, `PeriodAxis` and
  `OccupancyTimeline` are — no store, no clock, so the boundary cases are exact rather than
  approximately seeded.
- `SqlTranslationTests` for every new named query (`internal static IQueryable<T> …Query(...)`).
- `PGSH.Tests/Integration/` for each guard, each refusal paired with the request that must still
  succeed, and the store asserted untouched after every refusal.
- ⚠ **Rehearsed against a restored `pg_dump -Fc` of the live base before it is ever run on the live
  base** — this act moves the dates of a whole promotion's rotations. See Phase 18.

---

## 🟡 Phase 18 — Backup, and a rollback to the latest safe point

**Status: 18.1 BUILT 2026-09-03 (scheduled dumps · named safe points · manifest · restore plan · the
banner on every bulk act). 18.2 — the rehearsed restore and the per-act undo — remains.** Raised by
the user 2026-09-03: « the app is up and working so the data is super important and we dont want to
mess it up ».

> **This does not wait for Phase 12.** Production readiness is about deploying; this is about the base
> that exists *now*. Since the 2026-09-01 rebuild it holds the real faculty — **10 203 students,
> 43 605 registrations, 105 626 périodes, 87 092 évaluations** — plus the 2026-2027 réinscription
> applied through the UI on 2026-09-02.

### Why the current state is not enough

- The base lives in the named Docker volume `pgsh-postgres-data` with `ContainerLifetime.Persistent`
  (`PGSH.AppHost/Program.cs`). ⚠ **A volume is persistence, not a backup.** It survives a restart; it
  does not survive a bad write, a bad migration, or a bulk apply run against the wrong year.
- The only undo on record is an **ad-hoc `pg_dump -Fc` somebody remembered to take**. It has worked —
  `pgsh-avant-reimport-20260901-223756.dump`, `pgsh-avant-reinscription-20260902-140434.dump`,
  `pgsh-avant-axe-3med.dump` — precisely *because* a human typed it each time. That is a procedure,
  not a mechanism, and procedures are skipped on the day they are needed.
- ⚠ **The dangerous acts here are not delete buttons, they are bulk applies.** Each is carefully
  built to *name* what it costs. Naming is not undoing:

  | act | what one click writes |
  |---|---|
  | `ApplyDeliberationCommand` | a verdict on every registration of a promotion, all-or-nothing — **and the file is not stored**, so a half-corrected promotion cannot be reconstructed |
  | the réinscription roll | measured 02/09/2026: 6 813 inscriptions, 7 232 décisions, 1 217 graduations déduites, 1 327 signalements |
  | `ApplyRotationCycleCommand` | replaces an axis wholesale; the cells cascade with the slots |
  | `UnpublishCohortScheduleCommand(Force: true)` | cascades away `ServiceEvaluation`, `AttendanceRecord`, `PeriodPause`, `Delocalization` |
  | `DeleteAllCohortsCommand`, `DeleteAcademicYearCommand` | cohortes, and — through `CASCADE` — the year's rosters |

### What to build

**1 · Scheduled dumps with a retention window.** `pg_dump -Fc` on a timer (**daily** since 2026-09-04 — see the note under §18.1 — every point kept a day, then one a day
kept a month), written **outside** the container's own volume.
- ⚠ **`pg_dump` must not be piped.** Already paid for once — `SMOKE-TEST.md` records a dump corrupted
  by piping it out of the container. Write with `-f` inside the container, then `docker cp`.
- ⚠ **A backup nobody has restored is a hypothesis.** Every dump gets `pg_restore -l` at minimum, and
  a full restore into a scratch database is rehearsed on a schedule — not on the day it is needed.

**2 · A *named safe point* before every bulk act.** One command — `pgsh-snapshot <label>` — taking a
dump plus a manifest: label, timestamp, **git sha**, **the last applied migration**, and the row
counts of the tables that matter. « Roll back to the latest safe point » then becomes a command
instead of an archaeology exercise through `/tmp`.
- ⚠ **The manifest is the point, not the dump.** A dump taken before a migration and restored under
  code that expects the new schema gives a base the running app cannot read. The restore tool reads
  the manifest, says which `dotnet ef database update <name>` goes with it, and **refuses loudly** on
  a mismatch rather than leaving the operator to notice.
- Surface it where the risk is: the confirmation dialogs that already state a count — déliberation,
  réinscription roll, rotation-cycle apply, forced unpublish — should also state whether a safe point
  exists and how old it is.

**3 · Assert the restore, in SQL.** The 2026-09-01 rebuild established the shape: the restore script
`RAISE EXCEPTION`s on a row-count mismatch so `psql` exits non-zero and the operation stops. That
assertion is exactly what a silent fan-out needs — it is what would have caught 146
`StageAllowedServices` becoming 178 when joined on a service name that is not unique (§16.5).

**4 · Keycloak is a second volume, and nothing covers it.**
`builder.AddKeycloak(...).WithDataVolume()`. ⚠ Restoring the database without the matching realm
leaves `SyncUserMiddleware` matching a Keycloak `sub` against `User` rows that no longer exist — and
its fallback is **e-mail**, which is how somebody lands in another person's account. Either dump both
together, or establish and write down that the realm is stable and independent of the base.

**5 · Application-level undo, where a restore is too big a hammer.** A dump rolls back *everything*,
including work other people did in the meantime. The acts that most need undoing are per-promotion,
and two of them have no reversal at all: `ApplyDeliberationCommand` and the réinscription roll.
⚠ **`IAuditableCommand` records the criteria, the author and the date — it does not record the
previous values**, so the audit trail can say what was asked for and not what it replaced. Decide per
act between a reversal command and « the snapshot *is* the undo, and the UI says so ». The
déliberation's file is explicitly not stored, which is the strongest argument for the snapshot.
- Related, already in the queue as item 0e: the acts that *create* a cut leave no audit entry while
  the destructive one does — the wrong way round, and it cost a real « where did these 66 rosters
  come from » on live data on 02/09/2026.

**6 · The authored half is still the irreplaceable part.** §16.5 already names it — 24 `Holidays`,
146 `StageAllowedServices`, 3 `CnpnLevelEffectivities`, 2 `ServiceChefAssignment`, the 2026-2027
`AcademicYear` — none of which exists in `Medecine.mdb`. A dump covers all of it; the natural-key
export stays the tool for a **re-import**, never for a rollback.

### ✅ 18.1 — what was built, 2026-09-03

`PGSH.Domain/Backups/` (pure) · `PGSH.Application/Backups/` (port + handlers) ·
`PGSH.Infrastructure/Backups/` (the `docker exec pg_dump` adapter and the timer) ·
`PGSH.API/Endpoints/Backups/` · `BackupsPage` + `SafePointBanner` on the frontend.
**28 tests, suite 1 435 green**, `tsc` and `npm run lint` clean.

- **A point is a dump plus a manifest**, and the manifest is the substantive half: the last applied
  migration, the git sha, and the row counts of twelve tables. ⚠ **A dump taken before a migration
  and restored under code expecting the new schema gives a base the running app cannot read**, and
  nothing about the file says so.
- ⚠ **The register is the directory, not a table.** A registry kept *in* the base would be rolled
  back by the very restore it describes — every point taken after the restored one vanishing from
  the list while its file sits on disk. It is also why 18.1 needs **no migration**, which on a live
  base is the difference between shipping today and scheduling a window.
- ⚠ **`SafePointState` has five values and two of them are the point**: `Unavailable` (the runner
  cannot be reached) is not `None` (there is nothing to restore). They call for opposite acts — fix
  Docker, versus take a point — and one « aucune sauvegarde » covering both is the same defect as an
  omitted year read as « toutes les années ». `SchemaChanged` outranks `Stale` for a similar reason:
  an hour-old dump under the wrong migration is a restore that *refuses*, a three-day-old one under
  the right migration is a restore that *works and costs three days*.
- **The banner is the feature; the page is where it is administered.** « Créer un point maintenant »
  sits inside the déliberation's, the réinscription roll's and the rotation-cycle apply's own
  confirmations, so the dump becomes a side effect of the act instead of a procedure somebody has to
  remember. ⚠ It **does not block**: with no usable undo the act asks for a ticked box, exactly like
  `ConfirmedDefaultCount`. Blocking outright would mean that the day Docker is down, nobody can close
  a year.
- ⚠ **Nothing restores from the API.** A process cannot replace the database it is serving from — a
  restore drops and recreates objects the API holds open. `GET backups/{id}/restore-plan` returns
  what the rollback would **discard** and what it would **bring back**, as numbers read from the
  manifest's census against the base as it stands, plus the exact command to run with the stack
  stopped. A schema mismatch does not fail that read: the refusal has to be able to name the
  `dotnet ef database update` that makes the point usable.
- **Roles split where the risk does**: *taking* a point is `Roles.Administrative` — scolarité is who
  applies the bulk acts, so a gate it could not pass would put the button out of reach of the only
  person who needs it — while *deleting* one is `SuperUser`, and the **newest** point is refused
  outright to anybody, since it is the one every confirmation dialog is reading.
- **Retention prunes `Scheduled` points only.** A point somebody took by hand, or that a dialog took
  before a déliberation, is the only record of a state that has no other undo.
- ⚠ **`Backups:KeycloakRealmCovered` is `false` and the page says so out loud.** The realm is a
  second volume; restoring the base without it leaves `SyncUserMiddleware` matching a `sub` against
  `User` rows that are gone, and its fallback is the e-mail address.

### ✅ 18.1b — la cadence passe à **quotidienne** (2026-09-04, à la demande de l'utilisateur)

`Backups:Schedule:IntervalMinutes` : **60 → 1440**. Un `pg_dump` horaire de toute la base de la
faculté coûte plus de disque et d'I/O que la fenêtre de reprise ne vaut, et les actes qui ont
réellement besoin d'un retour en arrière — une déliberation, un rouleau de réinscription,
l'application d'un axe — prennent **leur propre point depuis la boîte de dialogue** plutôt que de
s'en remettre au minuteur. C'est cette dernière propriété qui rend la cadence horaire superflue.

⚠ **Deux constantes sont couplées, et changer l'une seule est un défaut silencieux.**
`SafePointEvaluator.DefaultFreshFor` valait **24 h**, documenté « matched to the scheduled hourly
dump : anything longer and the timer has missed a run ». La fraîcheur ne dit pas « le dump est
récent », elle dit **« le minuteur n'a pas sauté un tour »** — donc laissée à 24 h sous une cadence
quotidienne, elle aurait affiché `Stale` sur **toutes** les heures précédant chaque dump, sur un
système parfaitement sain. Une alerte qui se déclenche quoi que dise la donnée est du bruit, le
bruit se fait ignorer, et la vraie alerte part avec. Portée à **48 h** = un intervalle plein plus le
tour qui le referme.

- `A_point_one_whole_scheduled_interval_old_is_still_fresh` épingle le couplage : un point vieux
  d'un intervalle complet doit rester `Fresh`, et `DefaultFreshFor` doit dépasser un intervalle.
  L'intervalle y est **redit** plutôt que référencé — `BackupOptions` est dans Infrastructure et
  l'évaluateur est une règle de domaine pure — ce qui fait du test un *contrôle* du couplage et non
  une tautologie : changer l'option sans changer le test fait tomber la suite.
- `KeepHourlyForHours` → **`KeepAllForHours`**. Le palier reste correct sous n'importe quelle
  cadence (« tout point plus jeune que ceci est gardé, ensuite un par jour ») ; son ancien nom
  affirmait une cadence horaire qui n'existe plus. Même règle que partout ici : un nom qui décrit
  autre chose que ce que fait le code est une dérive, pas un détail.
- La rétention elle-même est inchangée : 24 h de tout, puis un par jour pendant 30 jours — ce qui,
  sous une cadence quotidienne, revient à 30 points. Et elle ne purge que les points `Scheduled`.

### 🔲 18.2 — what remains

- **A restore somebody has actually run.** `VerifyBackupPointCommand` reads an archive's table of
  contents back (`pg_restore -l`) — enough to catch the truncation a piped dump produced here once —
  and `BackupVerification.Restored` is a value **nothing sets yet**. ⚠ A backup nobody has restored
  is a hypothesis. **This is the last one open**, and 17/09/2026 is the day to close it.
- ✅ **`pgsh-snapshot` / `pgsh-restore` as scripts (17/09/2026)** — `scripts/`, with
  `pgsh-common.ps1` holding the container discovery. ⚠ The discovery rules are *copied from*
  `PgDumpBackupArchive` deliberately: a script that found the container differently would one day
  find a different one than the application, and a dump of the wrong base filed under this one's name
  is the silent failure phase 18 exists to remove. Several matches is a refusal, and pgAdmin is
  excluded **by image**, since its name contains « postgres » too.
- ✅ **Assert the restore (17/09/2026).** ⚠ Not in SQL in the end, and the reason is worth keeping:
  `pg_restore`'s exit code is **not** the verdict in either direction — `--clean --if-exists` reports
  missing objects on an empty base and exits non-zero having restored perfectly, and it exits 0 on
  errors that left tables empty. So the script **recounts** the manifest's census and refuses on a
  mismatch, naming the tables that disagree. A key the manifest does not carry is reported as
  « le manifeste n'en dit rien », never as agreement — the `DatabaseCensus` rule that a missing key is
  `null` and never `0`.
- ✅ **Keycloak's volume (17/09/2026) — the second branch: *established in writing as independent*.**
  `keycloak/pgsh-realm.json`, imported by `.WithRealmImport("../keycloak")`. ⚠ Chosen over dumping the
  volume because a dump is a second thing to remember to take, and the day it is missing you are where
  17/09 left us: the base came back from a safe point and the realm had no copy anywhere. A written
  realm needs no backup — it rebuilds. ⚠ `KeycloakRealmCovered` **stays `false`**: the realm is still
  not in the `.dump`, and flipping the flag would send a reader looking for it there.
- **Application-level undo** for the two acts a full restore is too big a hammer for
  (`ApplyDeliberationCommand`, the réinscription roll). ⚠ `IAuditableCommand` records the criteria,
  the author and the date — **never the previous values** — so today the snapshot *is* the undo, and
  the UI now says so.
- **The constructive acts still leave no audit entry** (item 0e): `BACKUP_POINT_CREATED` /
  `_VERIFIED` / `_DELETED` are recorded, and « qui a découpé cette promotion ? » still is not.

### Definition of done

- A dump exists that nobody had to remember to take, and a restore somebody has actually run.
- `pgsh-snapshot` / `pgsh-restore` documented in `SMOKE-TEST.md` beside the existing Rollback
  section, which becomes a pointer to them rather than a per-session recipe.
- ⚠ **Until this ships the standing rule holds: `pg_dump -Fc` before every bulk act, no exceptions.**
  It is already written into the queue for closing 2025-2026 (item 6), and it is why the three named
  dumps above exist at all.

---

## ✅ Phase 19 — Une demande nominative se résout par un groupe

Trois demandes réelles, apportées par l'utilisateur : « Sbai fait **tous** ses stages à l'hôpital
militaire », « Aya et Rihab **ensemble**, stage A en S1 et stage B en S2 », et des fratries dans le
même service. La question posée était double : le système sait-il les exprimer, et vaut-il mieux
passer par un **transfert définitif** que par un groupe ?

### Ce que la base disait avant qu'on écrive une ligne (mesuré 2026-09-03)

| constat | chiffre |
|---|---|
| rosters 6ᵉ MED 2024-2025 **entièrement** au HMIMV | **5** — groupes 102, 116, 130, 144, 158 |
| leur taille | **6-7 étudiants**, contre une moyenne de promotion de 5,8 |
| services du HMIMV | **35** — le plus gros hôpital de la base, devant Ibn Sina (27) |
| stages 6ᵉ année couverts par le HMIMV | **6 / 6** |
| stages 5ᵉ année couverts | **6 / 7** — *Santé Publique* n'autorise qu'un service, ailleurs |
| stages du catalogue sans **aucun** service autorisé | **3** (les deux immersions, le stage hospitalier d'initiation) |

**La faculté résout déjà cela par le groupe**, et ses groupes « militaires » ne sont pas minuscules :
ce sont des rosters ordinaires. Ce qui manquait n'était pas le mécanisme, c'était le moyen de les
**retrouver**.

### La réponse de fond

- Un **transfert définitif** n'envoie pas un étudiant dans un *service*, il l'envoie dans un *roster* :
  « groupe ou transfert » n'est pas un choix, le transfert est *la façon* d'entrer dans un groupe.
- **`DelocalizeStudentCommand` n'est pas la voie** : elle signifie « servi entièrement hors faculté »,
  supprime la rotation interne et attend une fiche papier. Le HMIMV est *dans* le catalogue.
- ⚠ **Un roster de deux étudiants coûte quelque chose que rien ne signale.**
  `RotationArranger.BuildServiceQueue` pondère chaque service par le nombre de cohortes *de taille
  moyenne* qu'il peut tenir, et **une cohorte est atomique** — deux étudiants consomment donc une
  place de file dimensionnée pour sept. L'équilibre de la promotion est faux, discrètement.

**L'ordre retenu :** ① transférer vers un roster qui y va déjà · ② **un** roster partagé par
contrainte récurrente (les militaires d'une promotion dans *un* groupe de 7) · ③ épingler les cellules
d'un roster existant · ④ un roster dédié de 1-2 étudiants, en dernier.

### 19.1 — ✅ Livré : les deux lectures qui rendent ① atteignable

- **`GET groups/placements`** — `GetRosterPlacementsQuery`. Une page de rosters d'une promotion, avec
  pour chaque stage les services qu'il occupe et les créneaux qu'il y tient. Filtrable par
  `serviceId` **ou** `hospitalId` (jamais les deux : un service appartient déjà à un hôpital), par
  `stageId`, et par `match=Anywhere|Exclusively`.
- **`GET hospitals/{id}/stage-coverage`** — `GetHospitalStageCoverageQuery`. La faisabilité, posée
  **avant** la promesse : cet hôpital peut-il accueillir toute la rotation de cette promotion, et
  sinon quels stages exactement. Volontairement **non scopée par année** — `Stage`, `Service`,
  `Hospital` et la liste des services autorisés sont du catalogue invariant.
- Deux classificateurs **purs** dans le domaine, chacun parce qu'un blanc y veut dire deux choses :
  - `RosterHospitalPlacement` (`Unplaced` / `Elsewhere` / `Partial` / `Entire`) — ⚠ « toutes ses
    cellules sont au HMIMV » est **vrai à vide** d'un roster que personne n'a réparti. Sur cette base,
    qui tient **0 cellule**, cela veut dire *tous* les rosters de la faculté.
  - `StageHospitalCoverage` (`NoServicesAuthored` / `NotAtThisHospital` / `Covered`) — ⚠ une liste de
    services autorisés **vide n'est pas appliquée**, donc le stage est ouvert à tout : le blanc dit
    « personne n'a saisi la liste », pas « cet hôpital est exclu ».
- `RosterPlacementSummary.PlacedRosters` sépare « personne n'y va » de « rien n'est encore réparti ».
- **Aucune migration.** Le tout est en lecture.

**Vérifié :** 1 474 tests verts (+67). ⚠ La moitié positive d'`Exclusively` a été **retirée pour
preuve** : cinq tests tombent, dont celui de l'endpoint.

⚠ **Angle mort trouvé en chemin, et c'est l'inverse de l'habituel** : `SelectMany` sur une *skip
navigation* lève `NotImplementedException` sur le fournisseur **in-memory** alors que Npgsql la
traduit. Voir `CLAUDE.md` (« Testing »).

### 19.3 — ✅ Livré : l'ordre des services est choisi, pas hérité de l'import

`StageAllowedService.Rank` + `ServiceRotationOrder` (pur) + `PUT stages/{id}/allowed-services/order`,
et les flèches sur la carte « Services autorisés » de la fiche du stage.

**Le constat.** `RotationArranger` parcourait ses services en `OrderBy(Service.Id)` — l'ordre de
création au catalogue, donc l'ordre de l'import Access. Or cet ordre **décide quelle plage de numéros
de groupe tombe dans quel service** : `BuildServiceQueue` émet le bloc de chaque service d'un seul
tenant, et la première colonne prend la phase 0, donc `offset = 0` et la cohorte en position 0 prend
`queue[0]`. Les premiers groupes, le premier service, la première période — et personne n'avait
choisi cet ordre.

⚠ **La fiche du stage affichait en plus un *quatrième* ordre** (par hôpital puis par nom), donc rien
à l'écran ne disait quel service était le premier, dans le seul endroit où être le premier décide de
quelque chose.

**Pourquoi c'est la réponse à 19.1 ③.** Une demande nominative se réglait sinon en retouchant une
cellule sur la grille — et **la répartition annuelle le montre** : `GroupNumberRanges` refuse de
fusionner par-dessus le trou laissé, donc « 21-27 » devient « 21-23, 25-27 » face à un « 24 »
solitaire, sur une page de plages propres. Réordonner produit le même placement en plages entières.

**Ce qui a été construit.**

- `ServiceRotationOrder` — **pur**, comme `PeriodAxis` / `RotationTiling` / `StagePeriodFolder` :
  `Reorder` / `Append` / `Without` / `SortKeyOf`, et les rangs sont toujours **contigus depuis 1**.
- ⚠ **Une liste partielle est refusée, jamais complétée.** Trois causes nommées séparément
  (manquant / inconnu / doublon) parce qu'elles appellent des gestes différents ; la cause la plus
  probable d'une liste courte est une page ouverte avant qu'un autre n'autorise un service.
- ⚠ **`ServiceRankWriter` écrit en deux temps** — rangs négatifs d'abord. L'index
  `IX_StageAllowedServices_Stage_Rank` est unique et non différé, donc un échange 1↔2 en un seul
  `SaveChanges` laisse à EF l'ordre des deux `UPDATE` et l'un des deux viole la contrainte à
  mi-chemin. Même forme que la rétrogradation avant la promotion dans `SetCurrentAcademicYear`.
- ⚠ **Le rang 0 trie en **dernier**, jamais en premier** : la colonne vaut 0 par défaut, donc une
  ligne écrite par un script correctif passerait devant tous les services placés à la main.
- **La migration remplit depuis `ORDER BY "ServiceId"`**, c'est-à-dire exactement ce que le code
  faisait déjà : l'appliquer ne change aucun plan. Sans ce remplissage l'index unique échoue d'entrée
  — les 146 lignes autorisées porteraient toutes 0, plusieurs par stage.
- **Jointure à charge derrière la même skip navigation** (`UsingEntity<StageAllowedService>`), donc
  les dix lectures existantes de `Stage.AllowedServices` sont intactes.
- Journalisé : `STAGE_SERVICE_ORDER_SET`.

**Ce que le levier ne fait pas**, et c'est dit à l'écran comme ici : il déplace la **promotion
entière**, pas un groupe ; la granularité est le **bloc** (sa largeur vient de la capacité du service,
donc réordonner permute les blocs en bloc) ; deux demandes contradictoires sur un même stage peuvent
être insatisfaisables. Là, il reste 19.2.

---

### ✅ 19.2 — Le placement nominatif : l'épinglage, la réservation, et la liste

> Spécifié le 07/09/2026, à partir d'une demande réelle : *« les services de Kénitra appartiennent
> maintenant au GST, leurs professeurs sont chefs et leurs services sont dans la base ; nous avons
> fait circuler un formulaire, nous avons une liste de volontaires pour les stages A, B et C, et ces
> services leur sont réservés. »*

**Ce n'est pas une délocalisation, et le dossier l'a déjà tranché** —
[`docs/planning-rotation.md`](docs/planning-rotation.md), « Une demande nominative se résout par un
groupe ». Kénitra sous le GST est le cas HMIMV, que la faculté a déjà joué : en 2024-2025 la 6ᵉ MED
tenait **cinq rosters entièrement au militaire**, de 6-7 étudiants, soit la taille normale. Ce sont
des services de la base, avec chefs, avec évaluation dans l'application. `DelocalizeStudentCommand`
y mettrait ces étudiants **hors** liste de travail du chef, **hors** occupation et **hors**
évaluation — pour un hôpital qui est dans le catalogue.

L'acte est donc : **une liste → un ou plusieurs rosters → des cellules épinglées.** La grille les
dessine alors sans aucun cas particulier, et `GroupNumberRanges` imprime « 12-22 » parce que ce sont
des rosters entiers et non des trous percés dans ceux des autres.

#### Ce qui existe déjà, et qu'il ne faut pas réécrire

| Pièce | Ce qu'elle fait déjà |
|---|---|
| `DelocalizationTargetResolver` | « qui l'opérateur a-t-il désigné » : ids de roster ∪ ids d'inscription ∪ **lignes collées CNE/Apogée**, une ligne de rapport pour chaque saisie ne désignant personne, `NotFound` et `WrongYear` distingués |
| `StudentGroupRelocator` | déplace une inscription vers un roster **sans trace**, re-pointe affectations et adhésion, reconstruit les périodes — et **n'enregistre pas**, donc N d'entre eux tiennent dans une transaction |
| `SetCohortSlotAssignment` | épingle une cellule (cohorte × créneau → service), refusant déjà un service externe ou hors liste autorisée |
| `GetRosterPlacementsQuery` | « quel groupe est à cet hôpital ? » — la lecture qui rend ① de l'ordre de placement atteignable |
| `ConfirmedCount` (aperçu/appliquer) | la garde qui attrape l'étudiant arrivé entre l'aperçu et le clic |

#### ① Le marqueur d'épinglage — `CohortSlotAssignment.Source`

⚠ **`CohortSlotAssignment` ne dit pas qu'un humain a choisi la cellule.** Elle porte
`{CohortId, StageSlotId, ServiceId}` et rien d'autre, tandis que `RotationArranger` supprime et
réécrit **toute** cellule non publiée à sa portée (`staleIds`). Un placement épinglé à la main est
donc détruit au prochain « auto-répartir ce stage » — sans refus, sans compte, avec un
`Assigned = N` parfaitement normal.

À construire : `CellSource { Arranged, Pinned }` sur la cellule, traitée par l'arrangeur exactement
comme une cellule publiée, et **comptée dans le résultat** (`PinnedCellsKept`), comme
`SkippedAlreadyServed` et `AdHocPeriodsKept`. *Un acte destructeur dont personne ne voit le chiffre
est un acte que personne n'a accepté.*

C'est le **prérequis** : sans lui, tout le reste de 19.2 est défait en silence par le clic suivant.

#### ② La réservation — `StageAllowedService.PlacementMode`

« Ces services sont réservés à ces étudiants » ne peut aujourd'hui **pas être dit**, seulement
espéré. La demande mord en un seul endroit, le vivier de l'arrangeur
(`AllowedServices.Where(!IsExternal).Where(Admits(levelId)).Where(Capacity > 0)`).

À construire : `ServicePlacementMode { Rotation, Reserved }` sur la ligne d'autorisation, en varchar
comme tout enum de la base.

- `Rotation` — l'arrangeur peut y placer n'importe qui. **Le défaut**, donc la migration ne change
  aucun plan existant.
- `Reserved` — l'arrangeur ne le choisit **jamais** ; seule une cellule épinglée y met quelqu'un.
  L'exclusivité aux volontaires est alors un fait des cellules épinglées, ce qui est vrai et
  vérifiable.

**Pourquoi sur la jointure et pas ailleurs.** `StageAllowedService` est déjà l'endroit où se répond
« ce service peut-il accueillir ce stage », et il porte déjà `Rank`, qui est une entrée de
planification et non une préférence d'affichage. Et il est **invariant à l'année des deux côtés**,
donc il reste du bon côté de la frontière. ⚠ **Une FK `ReservedForGroupId` est à rejeter** :
`AcademicGroup` est constitué par l'année, et l'accrocher à une ligne de catalogue invariante est
exactement le défaut de frontière que `CLAUDE.md` décrit.

⚠ **Et il faut dire ce qui a été retiré.** Réserver baisse `totalCapacity`, qui alimente
« il manque N places ». Une promotion qui perd des places en silence est le défaut « dire ce que
signifie un blanc » : le message de saturation nomme combien de services ont été retenus comme
réservés.

⚠ **Ne pas contourner par un quota de niveau à 0.** Il sort bien le service du vivier
(`Where(s => s.Capacity > 0)`) tout en laissant passer l'épinglage — mais il écrit « ce service
n'admet aucun étudiant de ce niveau », ce qui est faux, et empoisonne le rapport de charge, la page
du service et la garde de publication pour tout le monde.

**C'est aussi là qu'atterrira le choix FIFO** (« ceux qui remplissent le formulaire en premier
choisissent »), en troisième valeur `Choice`. Écrire un mode plutôt qu'un booléen est ce qui fait de
la version suivante un ajout et non une réécriture.

#### ③ Le motif du roster — `AcademicGroup.Purpose`

**Rien n'enregistre pourquoi un roster existe.** La seule preuve que le groupe 102 est le groupe
militaire est le motif de ses cellules ; un an plus tard nul ne distingue cela d'une coïncidence, et
un re-découpage le dissout sans que rien ne dise ce qui est perdu. Un champ libre sur
`AcademicGroup` suffit — « Volontaires Kénitra (GST) — formulaire du 12/09 ». Pas une entité, pas un
moteur de règles.

#### ④ L'acte de masse — l'affectation nominative

`PreviewBulkRosterAssignmentQuery` / `ApplyBulkRosterAssignmentCommand`, tous deux exécutant **un
seul** `BulkRosterAssignmentPlanner` — un aperçu calculé par un autre code est l'aperçu de rien.
C'est la forme de la délocalisation de masse, parce que c'est la même question posée d'un autre
verbe.

- **Promouvoir le résolveur de cibles en `StudentSelectionResolver` partagé**
  (`Application/Students/Selection/`). Il répond « quels étudiants l'opérateur a-t-il désignés », ce
  qui n'a rien de propre à la délocalisation, et il a désormais un deuxième appelant et un troisième
  en vue. C'est le précédent `RosterScope` de la session 52 : deux actes posant la même question et y
  répondant séparément, c'est ainsi que l'un a reçu la bonne portée et l'autre non.
- **Le déplacement lui-même est `StudentGroupRelocator`** — « changement de groupe », sans trace,
  qui est exactement ce que la faculté veut dire par « on les met simplement dans ces groupes ».
  ⚠ Il refuse dès qu'il s'est passé quelque chose ; ce cas-là appartient à
  `TransferStudentCommand`, qui porte la rotation en cours et **garde** la trace précisément parce
  qu'il y a maintenant quelque chose à tracer.
- **États de ligne** : `WillJoin`, `AlreadyThere`, `Underway` (refusé → transfert), `NotFound`,
  `WrongYear`, et ⚠ **`WrongPromotion`** — un roster est clé (année, niveau, numéro), donc un 4ᵉ
  année sur une liste de 5ᵉ est refusé et nommé, jamais fondu dedans.
- Refus en tête, lignes plafonnées à 200, **tout compte mesuré avant le plafond**, `ConfirmedCount` à
  l'application, `STUDENTS_ASSIGNED_TO_ROSTER` au registre.

#### La procédure intérimaire — ce qui marchait avant, et pourquoi elle est gardée ici

⚠ **Superseded le 07/09/2026 par ce qui précède** ; gardée parce qu'elle décrit ce que la base
fait encore tant que l'AppHost n'a pas redémarré, et parce qu'elle explique la moitié que le
détour ne donnait pas. Elle était : créer les services de Kénitra, donner aux rosters volontaires
un **label de partition à eux** (« K »), y mettre les volontaires un par un, épingler les cellules,
puis relancer « Répartir » **en décochant K** — la suppression de l'arrangeur étant portée aux
cohortes visées (`targetCohortIds.Contains(a.CohortId)`), les cellules de K survivaient.

Ce qu'elle ne donnait pas, et qui est ② : les services de Kénitra restaient dans le vivier (ils
doivent être dans la liste autorisée pour que l'épinglage soit accepté), donc l'arrangeur y plaçait
d'autres rosters — sans s'en apercevoir, puisque `saturatedServices` est calculé **après**
`SaveChangesAsync`, en rapport, jamais en contrainte pendant le placement. Et elle ne tenait que
tant que personne n'oubliait de décocher K : un seul « Générer le plan » vise toutes les partitions
de sa matrice. C'est ① qui a transformé la discipline en garantie.

⚠ **Le quota de niveau à 0 n'a jamais été la solution de rechange** : il sort bien le service du
vivier (`Where(s => s.Capacity > 0)`) tout en laissant passer l'épinglage — et il écrit « ce service
n'admet aucun étudiant de ce niveau », ce qui est faux, et empoisonne le rapport de charge, la page
du service et la garde de publication pour tout le monde.

⚠ **La numérotation reste une contrainte d'impression, elle, et l'acte de masse ne la résout pas** :
`GroupNumberRanges` replie des numéros de **roster**, donc les rosters volontaires doivent être
numérotés d'un seul tenant pour imprimer « 48-60 » plutôt qu'une pluie de nombres isolés.

#### Livré le 07/09/2026 — les quatre pièces, dans cet ordre

**① `CohortSlotAssignment.Source` (`CellSource.Arranged | Pinned`).** L'arrangeur traite une cellule
épinglée **exactement** comme une cellule publiée : jamais supprimée, jamais réécrite, sa place dans
la colonne exclue de l'équilibrage — donc les cohortes restantes se répartissent sur ce qui reste
réellement. Les deux sont fondues dans un seul ensemble (`lockedCells`) parce que tout ce qui suit
pose la même question, « cette cellule est-elle à moi à placer ? », et la réponse est non dans les
deux cas ; ce qui diffère est ce qui est **rapporté**, d'où `RotationArrangeResult.PinnedCellsKept`,
repris par `MacroPlanResult`. *Un acte destructeur dont personne ne voit le chiffre est un acte que
personne n'a accepté.*

⚠ **Et `SetCohortSlotAssignment` épingle aussi quand elle écrase une cellule existante**, pas
seulement quand elle en crée une. Écraser le choix de l'arrangeur *est* la décision humaine ; laissée
`Arranged`, la correction qu'on vient de faire serait défaite par la répartition suivante — le même
défaut, atteint par l'autre bout.

**② `StageAllowedService.PlacementMode` (`Rotation | Reserved`).** `Reserved` sort le service du
vivier de `RotationArranger` : seule une cellule épinglée y met quelqu'un. Sur la ligne
d'autorisation, qui est déjà l'endroit où se répond « ce service peut-il accueillir ce stage » et qui
porte déjà `Rank`, et qui est **invariant à l'année des deux côtés**. Défaut `Rotation`, donc la
migration ne change aucun plan existant.

- ⚠ **La capacité retenue quitte `TotalCapacity` avec le service**, ce qui est voulu et ce qui rend
  `ReservedServices` obligatoire à côté : « il manque N places » est mesuré contre un plafond plus
  petit, et une promotion qui perd des places en silence est le défaut que ce nombre existe pour
  empêcher.
- ⚠ **Tout réserver refuse par son propre nom** (`Schedule.AllServicesReserved`), jamais comme
  `NoServicesAdmitLevel` : « aucun ne vous accueille » enverrait l'opérateur élargir des quotas qui
  n'ont jamais été l'obstacle.
- **`PUT /stages/{id}/allowed-services/{serviceId}/placement-mode`**, journalisé
  `STAGE_SERVICE_PLACEMENT_MODE_SET`. Il ne déplace rien de déjà écrit — comme l'ordre, c'est la
  répartition suivante qui le lit.

**③ `AcademicGroup.Purpose`** — texte libre, 300 caractères, porté par la création et la mise à jour,
renvoyé par les deux lectures (liste et détail) parce qu'un résumé qui alimente un formulaire doit
porter tout ce que ce formulaire réécrit. `AcademicGroup.NormalisePurpose` est partagé par les deux
chemins : stocké brut d'un côté et normalisé de l'autre, un groupe édité sans toucher au champ
reviendrait en portant «&#160;», ce qui se lit comme un motif que quelqu'un a écrit.

**④ L'acte de masse** — `PreviewBulkRosterAssignmentQuery` / `ApplyBulkRosterAssignmentCommand`, tous
deux exécutant **un seul** `BulkRosterAssignmentPlanner`. `POST /groups/assign/bulk/preview` et
`POST /groups/assign/bulk`, journalisé `STUDENTS_ASSIGNED_TO_ROSTER`.

- **Deux verbes, décidés par étudiant** : sans groupe → *rattaché* (`StudentAffectationService` +
  `LateArrivalScheduler`, ce que fait « affecter à un groupe ») ; déjà dans un groupe → *déplacé*
  (`StudentGroupRelocator`, « changement de groupe », sans trace). ⚠ Le choix est figé dans le plan,
  jamais relu au moment d'appliquer : relu, la réponse pourrait changer entre ce que l'opérateur a
  confirmé et ce qui s'exécute.
- **Neuf états de ligne** : `WillJoin`, `WillMove`, `AlreadyThere`, `Underway`, `TargetMissingStage`,
  `WrongPromotion`, `CursusEnded`, `NotFound`, `WrongYear`. ⚠ `AlreadyThere` n'est **ni** applicable
  **ni** un refus : renvoyer une liste corrigée est l'usage normal de l'acte, donc l'essentiel d'un
  second passage atterrit là, et le compter comme refus donnerait l'air d'un échec.
- **Les gardes sont celles des actes unitaires, lues de la même façon** : l'engagement est
  `AffectationToll.IsUnderway`, **restreint au groupe d'origine** exactement comme le relocator le
  restreint. ⚠ Mais lu **par lot** — deux requêtes plates groupées sur (inscription, groupe) — parce
  que cent volontaires font sinon cent allers-retours, sur l'acte dont la raison d'être est que cent
  de quoi que ce soit est trop.
- **`StudentSelectionResolver` promu** hors de la délocalisation, dans
  `Application/Students/Selection/`, avec `StudentTargets`, `UnresolvedTarget` et `TargetResolution`.
  Il répond « quels étudiants l'opérateur a-t-il désignés », ce qui n'a rien de propre à la
  délocalisation ; chaque acte projette `UnresolvedTarget` sur son propre vocabulaire de lignes. Même
  précédent que `RosterScope` en session 52.
- Refus en tête, 200 lignes au plus, **tout compte mesuré avant le plafond**, `ConfirmedCount` à
  l'application.

**Migration `NominativePlacement`** — trois colonnes, deux valeurs par défaut qui préservent le sens
de chaque ligne existante, aucune donnée touchée.

**Tests** : 28 neufs — `NominativePlacementTests` (7), `BulkRosterAssignmentTests` (14),
`NominativePlacementEndpointTests` (5), plus 2 cas de traduction SQL. **1 740 verts.** Morsure
vérifiée sur les six gardes : épinglage retiré → 5 cas tombent ; mode de placement ignoré → idem ;
`Underway`, `WrongPromotion`, `TargetMissingStage` et `ConfirmedCount` neutralisés → 4 cas tombent,
un par garde. ⚠ Les deux requêtes groupées de l'acte sont épinglées par `SqlTranslationTests` : elles
groupent sur une clé construite depuis une **navigation**, et celle des périodes y arrive par un
`SelectMany` puis deux navigations en remontant — la forme qu'un fournisseur a le droit de refuser.

#### Ce qui reste, et ce qu'il ne faut toujours pas construire

- **L'écran.** Le back est complet et n'a **rien été cliqué**.
- **Ce qu'il ne faut pas construire : un solveur de contraintes.** « même service que X », « frère de
  Y » comme contraintes que l'arrangeur devrait satisfaire transforme chaque répartition en problème
  de satisfaction, alors que la recherche exhaustive de `RotationTiling` est déjà la partie coûteuse.
  Ces demandes sont des exceptions nominatives et peu nombreuses — 32 sur 611 dans le cas le plus
  large observé. Elles restent lisibles et restent la décision de l'humain.
- **Le choix FIFO** (« ceux qui remplissent le formulaire en premier choisissent ») atterrit en
  troisième valeur `PlacementMode.Choice` et réutilise `StudentSelectionResolver` tel quel. Écrire un
  mode plutôt qu'un booléen est ce qui en fait un ajout et non une réécriture.

---

## ✅ Phase 20 — « Qui a fait ça, et quand ? »

Le 02/09/2026, un test de fumée a créé **66 rosters** sur la 7ᵉ MED 2026-2027. L'utilisateur a
ensuite demandé pourquoi cette promotion avait des groupes alors qu'il ne l'avait jamais découpée —
et **le journal n'a pas pu répondre** : il tenait deux entrées pour toute la session, dont aucune ne
parlait de 66 rosters.

### Ce que le diagnostic a trouvé, mesuré le 04/09/2026

**Deux problèmes distincts, et le second est le plus gros.**

**A · Cinq actes n'écrivaient rien.**

| acte | enregistré avant | après |
|---|---|---|
| `AutoArrangeGroups` (découpage en groupes) | ❌ | ✅ `GROUPS_AUTO_ARRANGED` |
| `AssignRotationGroups` (découpage en partitions) | ❌ | ✅ `PARTITIONS_ASSIGNED` |
| `CreateGroup` | ❌ | ✅ `GROUP_CREATED` |
| `EmptyGroup` | ❌ | ✅ `GROUP_EMPTIED` |
| `EmptyAllYearGroups` | ❌ | ✅ `YEAR_GROUPS_EMPTIED`, et `PROMOTION_GROUPS_EMPTIED` quand l'acte est restreint à une promotion (07/09/2026) |
| `ClearRotationGroups`, `DeleteGroup` | ✅ | ✅ |

⚠ **La lecture « le destructeur est tracé, le constructeur non » était fausse** — c'est ce qui avait
été avancé d'abord. `EmptyGroup` est destructeur et n'écrivait rien non plus : la couverture était
simplement lacunaire.

**B · Rien ne pouvait relire la table.** Trente-cinq commandes y écrivaient, il n'existait **ni
route ni écran**, et elle contenait **11 lignes** — visibles uniquement en interrogeant la base à la
main. C'est la moitié qui manquait le plus : corriger A sans B n'aurait toujours pas répondu à la
question posée.

### Livré

- `GET audit-log` — paginé, filtrable par type d'acte et par plage de dates, réservé à
  `Roles.Administrative`. Renvoie aussi les **types d'actes présents avec leur effectif**, comptés
  sur tout le journal (jamais sur la fenêtre courante : réduits au filtre actif, il n'y aurait plus
  de chemin de retour vers les autres).
- `AuditMetadataJson` — les critères d'un acte sérialisés plutôt qu'interpolés. Voir [`docs/audit-calendar.md`](docs/audit-calendar.md).
- **Page « Journal des actions »** (`/admin/journal`, Système). Date, acte, objet, auteur, critères ;
  les actes destructeurs teintés ; un code sans libellé français affiché **tel quel** plutôt que
  masqué.
- **Aucune migration** — la table et le pipeline existaient déjà.

**Vérifié :** 1 508 tests backend verts (+15), `tsc` et `npm run lint` propres.

### Ce que le pilotage a trouvé

⚠ **La page rendait `null` quand la lecture échouait** : filtres, puis un vide absolu, ce qui se lit
comme un écran cassé. `errorMiddleware` ne comble pas ce trou — il laisse volontairement passer les
404 sur une lecture, « ceci n'existe pas encore » étant un état que l'écran doit rendre lui-même
(`PGSH.Frontend/CLAUDE.md` §1e). Rencontré immédiatement, l'API tournant encore sans la route. Une
404 y a maintenant sa propre phrase — « le processus API est plus ancien que cette page,
redémarrez » — distincte d'une panne de service.

### 20.1 — ⚠ Le filtre de dates comparait dans le mauvais repère (04/09/2026)

Trouvé au deuxième pilotage, sur le journal réel : trois entrées s'affichent « 03/09/2026 » et un
filtre « du 3 au 3 » n'en rendait que **deux**.

`AuditLog.CreatedAt` est en UTC ; l'écran l'affiche dans le fuseau du **navigateur**. L'entrée
manquante est écrite `2026-09-02 22:16 UTC` et lue « 03/09 00:16 » à Casablanca. Les bornes étant
des `DateOnly` résolus à minuit **UTC**, la date lue et la date filtrée n'étaient pas la même —
et l'utilisateur ne voit que la première.

- **Les bornes sont désormais des instants UTC** (`From` inclus, `To` exclu) et **c'est le client qui
  définit la journée** : « au 3 inclus » devient « < 4 septembre 00:00 *locale* ». Le serveur ne
  suppose plus aucun fuseau — ce qu'il ne saurait pas faire correctement, le Maroc basculant à UTC+0
  pendant le ramadan.
- ⚠ **`AsUtc` traite les trois `DateTimeKind` séparément.** La liaison depuis la query string donne
  `Utc`, `Local` ou `Unspecified` selon la forme reçue ; un `SpecifyKind(Utc)` uniforme prendrait une
  heure locale pour de l'UTC, un `ToUniversalTime()` uniforme décalerait une valeur `Unspecified` du
  fuseau du serveur. Même famille que le défaut corrigé : une date juste, comparée dans le mauvais
  repère.
- **Épinglé par `A_late_evening_entry_belongs_to_the_local_day_the_reader_sees`**, qui envoie la
  fenêtre qu'un client à UTC+2 produit pour « la journée du 3 » et exige que l'entrée de 22:16 s'y
  trouve. ⚠ Aucun test ne pouvait attraper l'original : les fixtures et les assertions vivaient dans
  le même repère UTC, et c'est justement l'écart entre ce repère et celui du lecteur qui était le
  défaut.

### 20.2 — La passe clean code / DDD sur l'aire d'audit (04/09/2026)

Faite à la demande de l'utilisateur, qui a rappelé la règle permanente : clean code, clean
architecture et DDD sur **tout** le code, ancien comme neuf, correctifs compris.

- **`AuditLog` était un sac de propriétés** — `set` public sur chaque membre — donc n'importe quel
  code tenant l'instance pouvait réécrire l'auteur ou la date après coup. C'est exactement la forme
  que `CnpnVersion` avait avant la session 35, et c'est plus grave ici : **une entrée d'audit est
  immuable par nature**, c'est la seule propriété pour laquelle elle existe. Accesseurs `init`
  au-dessus de champs explicites, plus une fabrique `Record`.
- **L'horloge est sortie du domaine.** `CreatedAt = DateTime.UtcNow` dans l'initialiseur de propriété
  rendait l'instant d'un acte intestable et faisait dépendre le domaine de l'heure de la machine,
  contre la règle que suit tout le reste (`SafePointEvaluator`, `WorkingDayCalendar`). Le pipeline
  injecte l'`IDateTimeProvider` et passe la valeur.
- **Pas d'`Entity`, délibérément.** Une entrée n'a pas d'invariant sur des enfants et rien à faire
  observer ; lui donner une base qui lève des événements de domaine serait du cargo cult.
- **`AuditLogVocabularyTests`** balaie l'assemblage : chaque `IAuditableCommand` doit déclarer un
  acte non vide, en SCREAMING_SNAKE, et **unique**. Les 45 existants passent. ⚠ Vérifié que le
  contrôle mord — dupliquer un code fait tomber `No_two_commands_share_an_action_code`.
- ⚠ **Le contrôle est au test et non à l'exécution, et c'est un choix.** Une garde qui lèverait en
  enregistrant ferait tomber *l'acte* — une réinscription entière — pour une faute de frappe dans sa
  ligne de journal. Le coût serait payé par l'utilisateur au pire moment ; ici il est nul.

**Non fait, et volontairement :** transformer les 45 codes en énumération. Cela toucherait une
quarantaine de fichiers et les chaînes sont **persistées** — la migration ne serait pas gratuite. Le
balayage par réflexion apporte l'essentiel de la garantie pour un fichier.

### Ce qui reste

- ⚠ **Aucune purge.** Le journal grandit indéfiniment. Ce n'est pas urgent (11 lignes aujourd'hui,
  et les actes audités sont rares par nature) mais une trace qui grossit sans limite finit par être
  une table qu'on tronque en catastrophe, c'est-à-dire une trace perdue au pire moment.
- **Les entrées d'origine gardent leur JSON interpolé.** Elles ne portent que des entiers, donc
  elles sont correctes ; à basculer sur `AuditMetadataJson` si l'une d'elles se met à porter du
  texte saisi.
- **L'objet n'est pas cliquable.** « `AcademicYear` #22 » pourrait mener à l'année, « `AcademicGroup`
  #7 » au groupe. Utile, pas indispensable — et il faudrait décider quoi faire quand la cible a été
  supprimée, ce qui est précisément le cas d'un acte destructeur.
- 🔲 **Cinq actes destructeurs n'écrivent toujours rien** — `DeleteAllCohortsCommand`
  (« Réinitialiser les cohortes »), `DeleteCohortCommand`, `UnpublishCohortScheduleCommand`,
  `UnpublishStageScheduleCommand`, `StageSlotCommands`. Ce sont ceux qui suppriment affectations,
  périodes, **évaluations et présences** ; « Dépublier » avec `Force` détruit les notes. Le
  `AffectationToll` est déjà calculé sur place, donc chaque entrée peut porter ce qui a été détruit.
  `HANDOFF.md` A3.
  - ✅ **Les deux actes côté roster sont sortis de cette liste** :
    `EmptyAllYearGroupsCommand` (06→07/09/2026, `YEAR_GROUPS_EMPTIED` /
    `PROMOTION_GROUPS_EMPTIED`) et `DeleteAllGroupsCommand` (07/09/2026, `YEAR_GROUPS_DELETED` /
    `PROMOTION_GROUPS_DELETED`). Deux codes chacun, parce qu'un registre qui appelle les deux portées
    d'un acte par un seul nom ne peut pas dire laquelle a été jouée — et c'est cette question,
    « qu'ai-je réellement fait ? », qui avait fait croire à des données orphelines le 07/09/2026.

---

## ✅ Phase 21 — La grille de planning dit enfin ce qu'elle montre

Deux défauts d'une même famille sur le seul écran qui n'en parlait pas : **une absence qui ne
s'annonce pas**, et **un fait lu à la mauvaise granularité**. Aucun des deux n'écrit ; aucun n'a
demandé de migration. Livrés le 05/09/2026.

### ① Une grille vide avait trois causes derrière un seul blanc

Rapporté par l'utilisateur : « il y a des périodes mais elles n'apparaissent pas dans la grille de
planning ». Mesuré le 04/09/2026 : de 2017-2018 à 2025-2026 la base tient **105 626 périodes pour
0 créneau et 0 cellule** — l'import Access portait les rotations *servies*, la base source n'ayant
aucune grille à porter. Seule 2026-2027 en a une (137 créneaux, 2 873 cellules).

⚠ **Rien n'est abîmé** (0 période pointe vers une cellule disparue) : ce qui manquait était la
*phrase*. « Rien n'est planifié » et « cette année n'a jamais été planifiée ici » appellent des
gestes opposés, et lire le second comme le premier invite à poser un axe sur une année terminée.

`StageScheduleSummary` porte désormais `DeclaredSlotCount`, `ServedPeriodCount` et `EmptyGridNote`,
la phrase venant de `StageScheduleNotes` — pur, à côté de `ExportNotes` et pour la même raison.

- ⚠ **Trois causes, pas deux**, et une quatrième séparée à l'intérieur de la dernière : un axe posé
  sur une promotion **sans cohorte** appelle « découpez, provisionnez », pas « répartissez ». C'est
  la leçon du panneau de faisabilité du 04/09, où une promotion sans aucun groupe recevait le
  message du second geste.
- ⚠ **`ServedPeriodCount` vaut `null`, jamais 0, quand la question n'a pas été posée** — toute
  grille qui a un axe. Chaque compte n'est lu que s'il doit être imprimé, donc la requête ordinaire
  ne paie rien pour la note.
- ⚠ **La note parle du stage et de l'année, jamais de la sélection filtrée** : sous un filtre de
  partition, le vide est le fait du filtre.
- ⚠ **L'année est lue sur l'inscription** (`ServedPeriodsQuery`), jamais déduite des dates de la
  période — les deux règles divergent sur 7 030 des 105 626 périodes et l'inscription a raison à
  chaque fois. Épinglé par `SqlTranslationTests`.

### ② La publication était marquée par cohorte, jamais par cellule

`SlotCellResponse.IsPublished`, lu par **`PublishedCells`** et jamais par la FK — celle-ci ne nomme
que la **première** cellule d'un pli `SingleService`. Mesuré sur *Gynécologie Obstétrique*
2026-2027 : **363 cellules, 121 nommées par la FK, 363 couvertes** ; un marqueur bâti sur la FK
lirait 242 cellules publiées comme libres. Le drapeau de ligne reste inchangé et reste juste — une
période suffit à rendre vraie une affirmation strictement plus faible.

⚠ **C'est un marqueur, pas une garde nouvelle.** L'édition reste refusée **par ligne**, comme
`SetCohortSlotAssignmentCommandHandler` la refuse. Ce que le drapeau ajoute est le refus *en amont*
du retrait d'une cellule publiée, déjà refusé côté serveur sans que rien ne le dise avant le clic.

### Couverture

Sept tests (`StageScheduleGridTests`) plus un cas de traduction. La morsure est vérifiée dans les
deux sens et chaque cassure ne fait tomber que son propre test : le drapeau relu depuis la FK fait
tomber le cas de la cellule de queue, la note lue sur la sélection filtrée fait tomber le cas du
filtre.

### Piloté le 05/09/2026

Sur *Gynécologie Obstétrique* 2026-2027, page 1 : **75 cellules, 75 couvertes, 25 nommées par la
FK** — l'écran marque les 75. Un marqueur bâti sur la clé aurait laissé **50 cellules publiées passer
pour libres sur un seul écran**. Page 5 : 63/63 (4 × 75 + 63 = 363). Contrôle négatif sur *Pédiatrie*
4ᵉ MED, répartie et non publiée : 50 cellules, **0** marquée, 50 croix actives. Sur CHIRURGIE
2024-2025 la phrase nomme **627 périodes**, le compte exact en base.

⚠ **Trois des neuf lignes de `SMOKE-TEST.md` §45 sont impilotables faute d'état** : aucun couple
(stage, année) n'a des cohortes sans créneau *et* sans période ; aucune partition de 2026-2027 n'a de
cohortes sans cellules ; et le bouton « Grille de planning » n'est rendu que si le stage a des
cohortes cette année-là, ce qui met « un axe sur une promotion sans cohorte » hors d'atteinte depuis
cet écran (la fiche y répond déjà par « Aucune cohorte pour ce stage »). Ces trois branches ne
tiennent donc que par les tests.

### Ce qui reste
- **Le refus d'édition reste par ligne.** Le desserrer par cellule demanderait que
  `SetCohortSlotAssignmentCommandHandler` passe de « la cohorte tient une période liée » à
  `PublishedCells.IsCellPublishedAsync` — c'est-à-dire décider si l'on peut déplacer une cellule
  libre d'une cohorte partiellement publiée. Même question que le report à mi-parcours, `§17.1`.

---

## ✅ Phase 22 — Les deux derniers écrans qui classaient les sources eux-mêmes

Suite directe de la session 42 (`ServiceChefDirectory` partagé, `ServiceChefPolicy.InForce`) et de
sa dette explicite : la fiche du service avait été corrigée, **la liste des services et la page
service du portail étudiant non**. Livré le 05/09/2026, **sans migration**.

### Ce que les deux écrans montraient

Les deux lisaient `Service.ServiceChefId` — le *rattachement* — qui est **null sur les 148 services
de la base**. La liste affichait donc « — » sur chaque ligne et le portail « aucun chef de service
désigné », pendant que la fiche, la répartition annuelle et l'export nommaient quelqu'un pour **140**
d'entre eux. Un étudiant lisait « aucun chef » sur la page d'un service dont sa propre répartition
imprime le chef.

### Ce qui a été fait

- `ServiceSummaryResponse.ChefAttribution`, résolu par `ServiceChefProvider` **sur les ids de la
  page** — jamais sur la requête filtrée, qui grandit avec le catalogue pour des lignes que personne
  ne regarde.
- ⚠ **Le portail n'a demandé aucun changement serveur** : il appelle `/services/{id}`, la même route
  que la fiche admin, qui porte `chefAttribution` depuis la session 42. C'était une omission côté
  client — la forme la plus discrète du défaut : la bonne réponse était déjà dans la charge utile.
- `ServiceChefAttributionResponse` déplacé dans `Application/Hospitals/Chefs/`, avec
  `From(annuaire, serviceId, asOf)`. Trois écrans l'impriment ; et la fabrique tient les **deux**
  appels (`For` + `HasWithheldLinkedChef`) sur une seule date d'observation.

### Les distinctions que le code refuse de collapser

| état | liste | portail étudiant |
|---|---|---|
| un nom, venu d'une affectation | le nom | la carte complète (grade, PPR) |
| un nom, venu de la note d'import | le nom + pastille « note » | le nom + « D'après la fiche du service » |
| rattaché, rien d'imprimé | « rattaché, non nommé » | « Non communiqué » |
| personne | « — » | « Aucun chef de service désigné » |
| champ absent (API antérieure) | « ? » | « Information non disponible » |

⚠ **Les deux dernières lignes sont le point.** « Inconnu » n'est pas « personne », et « rattaché mais
non imprimé » non plus — les trois appellent des gestes différents. C'est la règle
d'`ExportNotes` et d'`OutsideYearCount`, appliquée à une cellule de tableau.

⚠ **La carte de l'étudiant n'invente ni « Dr. », ni initiales d'avatar, ni grade** pour un nom venu
de la note : il n'y a aucun `Employee` derrière, et habiller une note en fiche de personnel est une
affirmation que rien ne soutient.

### Couverture

Trois tests ajoutés à `ServiceChefAttributionTests` (1 562 → 1 565 verts). ⚠ **La morsure est
l'équivalence, pas la valeur** : `The_services_list_names_exactly_what_the_fiche_names` compare la
ligne de liste à la réponse de la fiche pour le même service, donc elle tombe le jour où l'un des
deux se remet à classer les sources. Vérifié en cassant — la liste remise à nommer la FK fait tomber
**3** tests.

### Ce qui reste

- **`ServiceChefPolicy.InForce` est toujours `SourceNoteOnly`.** Les deux seules affectations de la
  base sont des comptes de test ; le jour où de vrais chefs sont désignés dans Personnel, c'est
  **une ligne** à changer et les cinq écrans suivent. C'est tout l'intérêt d'avoir une constante
  partagée plutôt qu'un choix par écran.
- **Piloté le 05/09/2026** (`SMOKE-TEST.md` §46) : 14 lignes sur 15 nomment un chef, la fiche de
  « Cardiologie » nomme **le même** Pr.A.Benyass que sa ligne, et Pédiatrie1/2 affichent les noms de
  la note plutôt que le compte de test rattaché. Le repli « ? » a été vu sur le processus antérieur
  au champ, ce qui vérifie que l'écran ne retombe pas en silence sur l'ancienne lecture.
- **Le portail étudiant a été piloté sous une vraie session `Student`** : *Gynécologie Obs A*
  affiche « Pr.M.H.Alami » + « D'après la fiche du service » (la page disait « aucun chef de service
  désigné » avant), un service sans note ni rattachement dit toujours « Aucun chef de service
  désigné », et — le cas qui compte — **Pédiatrie1 nomme Pr.N.Elhafidi sans que « Youssef Alaoui »
  apparaisse nulle part** : le compte de test ne fuit pas vers l'écran de l'étudiant.
- ⚠ **« Non communiqué » reste impilotable** : il faudrait un service rattaché **et** sans note, et
  la base n'en a aucun. Cette branche ne tient que par le test.

---

## ✅ Phase 23 — « Changement de groupe » : corriger une répartition sans laisser de trace

**Le besoin, tel qu'il a été posé (06/09/2026).** Il existait deux façons de déplacer un étudiant et
aucune ne répondait à la question courante. Le **transfert** (temporaire ou définitif) *raconte* le
déplacement : il écrit une ligne `HistoryType.GroupTransfer` sur le dossier, ferme une
`CohortMembership` et en ouvre une autre, interrompt la rotation en cours. La **délocalisation** est
autre chose encore — le stage entier se fait hors faculté. Ce qui manquait est le cas le plus
banal : *la répartition s'est trompée de groupe*, et il faut que le dossier dise ce qui est vrai —
qu'il est dans ce groupe-là, et qu'il y a toujours été.

### La distinction, et c'est toute la phase

|  | transfert | changement de groupe |
|---|---|---|
| ce qu'il affirme | « il était là, il est maintenant ici » | « il a toujours été ici » |
| `CohortMembership` | l'ouverte est **close**, une seconde est ouverte | l'ouverte est **réécrite sur place** |
| événement de domaine | `StudentGroupTransferredDomainEvent` → ligne d'historique | **aucun** |
| périodes | la rotation en cours est coupée et conservée | reconstruites à l'identique de la cohorte d'arrivée |
| quand c'est légitime | toujours | tant que **rien** n'a eu lieu |
| trace | dossier + parcours + journal | **journal seulement** |

⚠ **« Sans historique » veut dire « rien sur le dossier », jamais « rien nulle part ».** Les deux
mots ne nomment pas la même chose : le *dossier* est le récit de l'étudiant et ne doit rien montrer ;
le *registre* est le journal des actes d'administration et doit tout montrer. L'acte est
**irréversible** — le groupe d'origine n'est plus écrit nulle part après coup — donc la commande est
`IAuditableCommand` (`STUDENT_GROUP_CHANGED`) et l'entrée du journal est le seul endroit où ce groupe
survit. *Un registre qu'un acte destructeur peut contourner n'est pas un registre.*

### Ce qui est réellement repointé

`Registration.AcademicGroupId` · chaque `InternshipAssignment.CurrentCohortId` vers la cohorte du
groupe d'arrivée **pour le même stage** · la ligne `CohortMembership` ouverte, réécrite en gardant sa
`StartDate` · les périodes, reconstruites depuis les cellules **publiées** de la cohorte d'arrivée,
avec leurs lignes `ServicePeriodSlotCoverage`.

- ⚠ **Les lignes de membership déjà closes ne sont pas touchées.** Elles enregistrent un transfert
  qui a *réellement* eu lieu, et cet acte n'a pas à l'effacer. Le dossier se lit alors comme si
  l'étudiant était passé directement de l'ancien groupe qu'il a vraiment quitté à celui où il est —
  ce qui est la vérité une fois la correction appliquée.
- ⚠ **`OriginalCohortId` est remis à null.** L'adresse de retour d'un prêt temporaire nommait la
  cohorte que cette affectation ne tient plus ; laissée en place, l'auto-retour renverrait l'étudiant
  vers un groupe dont le dossier ne dit plus qu'il en vient.

### Ce qui est refusé, et pourquoi il n'y a pas de `Force`

`GroupChangeErrors`. Une correction n'est vraie que tant que rien ne la contredit :

- **`RotationsUnderway`** — une période démarrée, une note, une journée de présence. Le refus nomme
  les quatre chiffres (les mêmes que ceux de la dépublication, lus par le même `AffectationToll`) et
  **désigne le transfert**. ⚠ Pas forçable, exactement comme
  `AcademicGroupErrors.RosterAffectationsUnderway` : l'acte qui détruit des notes est « Dépublier »,
  qui annonce son coût et demande deux fois ; un bouton côté groupe ne doit jamais devenir la porte
  de service.
- **`TargetIsUnassignedRoster`** — « Non réparti » ne porte aucune cohorte, donc les affectations
  resteraient dans celles du groupe de départ pendant que la fiche dit qu'il n'est nulle part.
- **`TargetRosterMissingStage`** — le groupe d'arrivée ne fait pas ce stage. Refusé plutôt que
  silencieusement perdu : c'est l'affectation qui enregistre qu'il doit le stage. Mesuré le
  06/09/2026, tous les rosters d'une promotion portent exactement les mêmes cohortes (7/7 en 5ᵉ MED,
  6/6 en 3ᵉ MED, 5/5 en 4ᵉ MED, 2/2 en 5ᵉ Pharmacie), donc ce refus ne mord pas en pratique — mais un
  roster d'un autre CNPN diffère légitimement.
- **`AlreadyAffectedInTargetCohort`** — il tient déjà une affectation là (une revalidation posée à la
  main). En déplacer une seconde donnerait **deux** affectations sur une (inscription, cohorte).
- **`NotInAGroup`** — il n'y a pas de groupe à corriger ; c'est « Affecter à un groupe » qu'il faut.
- Plus les deux gardes que toute écriture sur un pointeur de roster fait : année et promotion.

### L'échange, qui est deux changements et rien d'autre

`SwapStudentGroupsCommand` — A prend le groupe de B et B celui de A. Il existe parce que déplacer un
seul étudiant laisse un roster court et l'autre long. ⚠ **Les deux destinations sont lues avant que
l'un ou l'autre ne bouge** : prise au moment où on en a besoin, la seconde enverrait B dans le groupe
où A vient d'arriver — les deux dans un seul roster et l'autre vide. Un seul `SaveChanges` pour les
deux moitiés, donc un refus sur la seconde laisse la première exactement où elle était.

### Deux règles qui existaient en un seul exemplaire et devaient le rester

- **`CohortStayFolder`** (domaine, pur) — plier les cellules d'une cohorte en *séjours*. La règle
  était **privée dans `SchedulePublisher`** ; il a fallu la sortir dès qu'un second acte a dû
  produire les périodes que tient un membre d'une cohorte. ⚠ Écrite deux fois, elle aurait fini par
  diverger sur `SingleService` : l'étudiant déplacé aurait tenu *kₛ* périodes là où ses camarades en
  tiennent **une**, on lui aurait demandé *kₛ* notes, et sa moyenne aurait été calculée autrement que
  celle de toute sa promotion.
- **`AffectationTollReader.ForRegistrationInRosterAsync`** — une portée de plus sur le lecteur
  existant plutôt qu'un décompte maison, pour que deux refus ne décrivent pas les mêmes lignes
  différemment.

### ⚠ Un défaut latent corrigé au passage — `MidStageTransferRescheduler` n'écrivait aucune couverture

Les trois créations de période de cette classe posaient `CohortSlotAssignmentId` **sans** la ligne
`ServicePeriodSlotCoverage`. La FK répond « ça vient de la grille ? » ; seule la couverture répond
« *cette cellule-là* est-elle publiée ? », et c'est elle que lit `PublishedCells` — donc
`RotationArranger`, `DeleteStageSlot`, `ClearCohortSlotAssignment` et `ClearSlotAssignments`. Sans
elle, la cellule d'un étudiant transféré se lit **libre** : le prochain auto-arrangement la réécrit
sur un autre service pendant que sa période continue de nommer l'ancien, et supprimer la colonne est
autorisé sous ses pieds. `SchedulePublisher` et `LateArrivalScheduler` l'ont toujours écrite.

### Couverture

31 tests (1 565 → 1 596 verts) : 7 sur `CohortStayFolderTests` (le pli, les deux ruptures, l'ordre),
17 sur `StudentGroupChangeTests`, 7 dans `PGSH.Tests/Integration/GroupChangeEndpointTests.cs`, plus
un cas de traduction SQL pour les deux requêtes nommées de `CohortMemberScheduler` et une portée de
plus sur celles du toll.

- **Le contrôle de la garantie de silence est un test à part** :
  `A_transfer_of_the_same_student_does_raise_the_event_that_writes_history`. « Aucun événement » ne
  vaut rien si la fixture ne peut pas en produire un.
- **Vérifié en cassant, trois fois, une à la fois** : remplacer `ReassignToGroup` par
  `TransferToGroup` fait tomber **exactement** `The_move_leaves_no_trace_on_the_student_file` ; ne
  plus plier les runs `SingleService` fait tomber exactement
  `A_single_service_run_becomes_one_periode_covering_every_cell` ; matérialiser les cellules non
  publiées fait tomber exactement `An_unpublished_target_cohorte_gives_him_no_periode`.
- **Le test d'intégration qui compte** est
  `The_dossier_keeps_nothing_and_the_register_keeps_everything` : il assied les deux moitiés
  ensemble, parce que « aucune trace » serait sinon indiscernable de « acte non enregistré ». Aucun
  handler ne peut y répondre — la ligne du journal est posée par `AuditLogPipelineBehavior` avant le
  handler et validée par le `SaveChanges` de celui-ci.

### Piloté sur la base réelle le 06/09/2026

Point de sauvegarde pris d'abord. **Quatre actes exécutés puis annulés** par leurs inverses ; l'état
final a été comparé en SQL au relevé pris avant, et il est identique — mêmes cohortes, mêmes cellules,
couverture Gynécologie toujours à 3. Détail complet : `SMOKE-TEST.md` §47.

- **Le cas qui compte est tombé du premier coup** : le sujet portait un run `SingleService` couvrant
  **3 cellules**, et il est resté **une** période après le déplacement. C'est `CohortStayFolder` sur
  données réelles.
- **La trace** : `Histories` inchangé à 1, **7** memberships et non 14, toutes ouvertes, `StartDate`
  d'origine conservée, aucun motif — et sur toute l'année 2026-2027, **0 membership close, 0 motif**.
- **Le refus** sur 2025-2026 a nommé ses quatre chiffres et renvoyé au transfert ; les effectifs sont
  restés identiques et **le journal n'a pas bougé** — un acte refusé n'écrit rien.
- **L'échange** a laissé les deux effectifs à 7 et n'a écrit **qu'une** ligne de journal.

### ⚠ Deux défauts que seul l'écran pouvait trouver

- **La page depuis laquelle l'acte est lancé ne se rafraîchissait pas.** `getGroupById` fournit le tag
  `group-<id>`, différent de celui de la liste ; la mutation n'invalidait que le second. Le `POST`
  répondait 200, l'étudiant avait bougé en base, et la fiche continuait de le lister — ce qui se lit
  comme un bouton qui n'a rien fait. Invisible au type-check, au lint, aux 1 596 tests et à la
  relecture ; confirmé par le journal réseau (refetch de `/api/groups`, aucun de `/api/groups/4121`).
  ⚠ **Le tag du groupe de départ n'est connu que de l'appelant** — la requête ne nomme que la
  destination — donc il se passe en champ client-only. `PGSH.Frontend/CLAUDE.md` §1j.
- **Les deux codes d'acte n'avaient pas de libellé** dans `auditActions.ts` et se seraient lus en
  `SCREAMING_SNAKE`, sur les deux seuls actes de l'application dont le journal est l'*unique* trace.

### Ce qui reste

- **Aucune migration** : la phase ne change ni table ni colonne. Elle réécrit des lignes existantes.
- **Deux branches restent non pilotées parce que l'état n'existe pas dans cette base** :
  « le groupe d'arrivée ne fait pas ce stage » et « il tient déjà une affectation dans la cohorte
  d'arrivée ». Elles ne tiennent que par les tests.

---

## ✅ Phase 24 — Un service peut refuser d'être dépassé

**Le besoin, tel qu'il a été posé (06/09/2026).** *« Quand on publie une cohorte, on nous propose
d'autoriser le dépassement de capacité des services — mais certains chefs de service n'aiment pas ça.
J'aimerais une petite case sur le service pour ne pas l'autoriser (autorisé par défaut) ; à la
publication, on ne dépasserait alors que les services qui n'ont pas coché ce refus. »*

### Pourquoi c'était la bonne demande

La mesure était déjà au dossier et l'argument est celui qui avait servi un mois plus tôt : **233 des
353 cellules planifiées dépassent la capacité (66 %)**, donc « autoriser le dépassement d'effectif »
se coche par réflexe. C'est ce constat qui avait forcé, le 17/08, à sortir l'*admissibilité* du
champ de la case — *une règle qu'on n'applique que lorsque personne n'a besoin de la contourner n'est
pas appliquée*. La moitié « effectif », elle, **doit** rester franchissable : les 148 services portent
tous la `Capacity = 20` par défaut de l'import, que personne n'a saisie, et la 4ᵉ MED ne pourrait pas
publier une seule cellule sans la case.

Les deux faits ensemble ne laissent qu'une issue : la décision est **par service**, prise par la
personne que le nombre concerne. Pas un durcissement global, pas une seconde case globale.

| | qui décide | franchissable ? |
|---|---|---|
| admissibilité (`LevelNotAdmitted`) | les quotas du service | **non**, jamais |
| effectif sur un service permissif | l'administrateur qui publie | oui, c'est à ça que sert la case |
| effectif sur un service **ferme** | le chef du service | **non** — nouveau |

### Ce qui a été construit

- **`Service.AllowsOverCapacity`**, `true` par défaut (migration `ServiceOverCapacityPolicy`, une
  seule instruction : `ADD COLUMN ... NOT NULL DEFAULT TRUE`).
- **`SchedulePublisher.EnsureIntakeAsync` lit trois règles** au lieu de deux, et `allowOverCapacity`
  y devient une *demande* plutôt qu'une décision — la question « ce refus est-il franchissable ? » se
  pose **par cellule**, une publication traversant plusieurs services qui ne répondent pas pareil.
- **`Schedule.OverCapacityRefusedByService`**, son propre code d'erreur, qui distingue quota et
  plafond total dans la phrase (les remèdes diffèrent) et dit d'emblée que la case ne le lèvera pas.
- **`PublishRefusedByIntake` compte les deux moitiés infranchissables séparément** et cesse de
  proposer la case quand il ne reste rien de franchissable.
- **`SaturatedCellResponse.Forceable`** sur chaque saturation de la grille de planning, avec le tri
  « non forçable d'abord » — qui absorbe l'ancien « non admis d'abord ».
- **Quatre écrans** : la fiche du service (une ligne qui l'affirme dans les deux sens), le formulaire
  (un `Switch` avec sa conséquence écrite), la liste (un cadenas sur l'état rare seulement), la grille
  (compteur « non forçables », marque par ligne dans le rapport, et la description de la case qui
  **nomme** les services fermes concernés).
- **12 tests** (`ServiceOverCapacityPolicyTests`), dont le contrôle qui compte : un service ferme
  **dans** son effectif publie comme les autres. Suite complète : **1 608 verts**.

### ⚠ Le raccourci qu'il a fallu défaire, et c'est le cœur technique de la phase

Depuis le 17/08, la case cochée signifiait *ne construis même pas la table d'occupation* — l'optimisation
qui avait rendu la séparation de l'admissibilité gratuite. Sous ce raccourci, **un service ferme est
injoignable** : son nombre n'est jamais lu, donc son refus n'existe pas. La table est désormais
construite sur **exactement** les services fermes de l'appel (`ServiceIntakeLookup.FirmServicesAmong`),
si bien qu'une publication qui n'en touche aucun — le cas courant, et le seul aujourd'hui — ne mesure
toujours rien. Plusieurs tests publient avec `allowOverCapacity: true` pour cette seule raison, et
c'est ce qui les fait tomber quand on rétablit le raccourci.

### ⚠ Ce que le drapeau ne fait pas

- **Il lie la publication, pas la planification.** `RotationArranger` continue d'équilibrer sur
  `CapacityFor` et remplira un service ferme au-delà de son nombre. C'est le bon ordre : un plan est un
  brouillon, et refuser de le dessiner ne laisse nulle part où voir le problème. Il apparaît alors
  dans le rapport de saturation, marqué « non forçable ».
- **Il ne touche rien de publié.** Le refus porte sur les publications à venir ; le formulaire le dit.
- **Il n'est pas porté sur « Charge des services »**, volontairement : cette page et son document
  imprimable devraient s'accorder, et les quatre endroits d'où une publication se décide sont couverts.
  Nommé pour que ce soit une décision et non un oubli.

### Piloté sur la base réelle le 06/09/2026

Migration appliquée au redémarrage : **148 services sur 148 à `true`**. L'essai a porté sur
*Cardiologie B* (Maternité Souissi), le meilleur cas possible — pic de **118 étudiants** venant de
**trois promotions** contre une capacité de 20, et premier dans l'ordre de rotation de la Cardiologie
de 4ᵉ MED. Rendu ferme, puis remis comme il était.

- **Le refus** est arrivé mot pour mot, la case **cochée** : « … accueillerait 118 étudiant(s) pour
  une capacité de 20. Ce service n'autorise pas le dépassement d'effectif… » — et **0 période écrite**,
  sur la cohorte comme sur toute la promotion.
- **La grille l'annonçait avant le clic** : « 18 affectations saturées, **dont 6 non forçables** », les
  6 lignes marquées et **en tête** au-dessus de lignes au *même* dépassement (+98) qui, elles, sont
  forçables — donc l'ordre suit bien la forçabilité et non les chiffres. Le dialogue nommait le
  service.
- **Le contrôle** : drapeau remis à `true` → **mêmes 18 saturations, badge disparu**. Le marqueur suit
  le drapeau, pas les nombres.
- **Non piloté délibérément** : la publication qui *réussit*. Elle écrirait 4 625 périodes réelles sur
  une promotion dont la publication est un acte en attente (`HANDOFF.md` item `0d`) — c'est un clic de
  l'utilisateur, pas une vérification.

### ⚠ Deux défauts que seul le clic pouvait trouver

- **Le mien** : `onChange={(e) => setForm((p) => ({ …e.currentTarget.checked }))}` — React remet
  `currentTarget` à `null` une fois l'événement propagé, et l'updater fonctionnel s'exécute au rendu
  suivant. Basculer l'interrupteur faisait tomber la fenêtre dans l'ErrorBoundary. Invisible au
  type-check (la propriété est typée non-nullable), au lint et aux tests.
- **Le même motif ailleurs, préexistant, et il cassait une page** : cinq autres occurrences, dont
  « Date confirmée » de `HolidaysPage` — vérifiée en cliquant, elle faisait tomber « Ajouter un jour
  férié », c'est-à-dire que **saisir un férié était impossible**, sur la page où les fêtes lunaires ne
  peuvent qu'être saisies à la main. Les six corrigés ; règle en `PGSH.Frontend/CLAUDE.md` §1l.

### Ce qui reste

- **Aucun service n'est ferme dans la base** : le drapeau existe, il vaut `true` partout, et rien ne
  change tant qu'un chef n'a pas demandé le contraire. C'est ce qui rend la phase non destructive et
  ce qui la rend, pour l'instant, invisible.

---

## ✅ Phase 25 — Un stage fait hors faculté, pour toute une promotion

**Le besoin, tel qu'il a été posé (06/09/2026).** *« Vu la saturation, on veut un formulaire où l'on
demande aux étudiants d'accepter d'aller dans une autre région (KÉNITRA). Par la logique de
l'application c'est une délocalisation. Mais comme cela peut concerner beaucoup d'étudiants, j'ai
pensé créer un service appelé KENITRA, y affecter les étudiants sur chaque stage concerné, et quand
ils rentrent ils déposent le papier de validation à la scolarité qui la saisit à la main. »*

### Ce que la demande avait déjà juste, et la seule chose à en retrancher

La délocalisation existait, et elle **attendait exactement cette ligne de catalogue** : son handler
exige que le service externe existe et ne le contraint délibérément *pas* à la liste des services
autorisés du stage. Un service « KENITRA » n'est donc pas un détournement, c'est la forme prévue.

Ce qui a été écarté, c'est de passer par **la répartition** — mettre KENITRA dans les services
autorisés et laisser l'arrangeur y placer les cohortes. Trois raisons, toutes mesurées dans le code :

| en passant par la grille | conséquence |
|---|---|
| une rotation publiée attend un chef pour la démarrer, la clore et l'évaluer | KENITRA n'en a pas : les lignes restent `Planned` dans la liste de personne — le trou exact des 3 220 périodes d'un chef |
| une cellule pèse dans l'occupation, la saturation et la garde de publication | un plafond inventé pour un hôpital que la faculté ne gère pas fausse le rapport que l'opération est censée soulager |
| `IsDelocalized` est ce qui fait dire au dossier « fait hors faculté » | par la grille, le dossier dirait que l'application a supervisé un stage qu'elle n'a jamais vu |

### ⚠ Le défaut que la demande a fait apparaître : délocaliser ne libérait pas la place

`ServiceOccupancyCalculator.EntriesQuery` comptait la charge d'une cellule comme
`a.Cohort.Assignments.Count` — **tous les membres de la cohorte**, présents ou non. Or une
délocalisation retire les périodes de l'étudiant mais le **laisse dans sa cohorte** (c'est ce qui rend
l'annulation possible). Envoyer soixante étudiants à Kénitra soulageait donc la grille, l'équilibrage
de l'arrangeur et la garde de pré-publication de **rien du tout**, alors que le service quitté était
bel et bien soixante fois plus léger.

Le compte est désormais `Count(x => !x.ServicePeriods.Any(p => p.IsDelocalized))`, **écrit dans cinq
endroits qui ne doivent jamais diverger** — le calculateur, la page d'un service, le rapport de
charge, `RotationArranger.CohortsQuery` et l'hydratation d'occupation du générateur de planning.

### Ce qui a été construit

- **`Service.IsExternal`**, `false` par défaut (migration `ExternalServices`). Un service hors faculté
  ne peut pas être autorisé sur un stage, ne peut pas être posé dans une cellule à la main
  (⚠ 25 stages sur 27 n'autorisent aucun service, donc la liste blanche n'y garde rien), est retiré du
  vivier de l'arrangeur, et n'entre jamais dans l'occupation.
- **`Delocalize` ne refuse plus que sur une note.** Une période commencée est supprimée comme une
  période planifiée — un étudiant qui part en cours de rotation est le cas ordinaire, et nos dates sont
  une formalité que l'hôpital d'accueil ne suit pas. Une période **évaluée** est refusée :
  `Delocalizations.OverMark`.
- **Les dates deviennent facultatives** et retombent sur la fenêtre du stage pour cette promotion.
  ⚠ Elle **refuse plutôt que d'inventer** quand le stage n'a aucun créneau — les années importées en
  sont toutes là, et une paire de dates fabriquée ressemblerait à un fait enregistré.
- **Le verdict accepte les trois modes** — note /20, validé/non validé, validation par objectif — avec
  les mêmes règles que l'évaluation d'un chef, parce que c'est la même `ServiceEvaluation`.
- **L'acte de masse** : aperçu + application, sélection par rosters entiers, étudiants nommés et liste
  collée de CNE/Apogée, les trois réunies. Il **saute** ce qu'il ne peut pas faire, jamais en silence,
  et il est gardé par `ConfirmedCount` — pas par une case à cocher.
- **L'annulation**, qui n'existait pas : la période ad-hoc n'est pas une période publiée, donc
  `RemovePublishedPeriods` la laissait délibérément en place et rien ne pouvait revenir en arrière.
  Refusée une fois le verdict papier saisi.
- **La grille dit ce qu'elle montre** : `DelocalizedCount` à côté de `StudentCount`, faute de quoi un
  roster parti en masse affiche un effectif plein devant des cellules qui ne chargent rien.

### ⚠ Repris le 10/09/2026 : la fenêtre est celle de la **cohorte**, pas celle du stage

Livré ci-dessus, le calcul des dates omises était `min`/`max` sur **tous** les créneaux du stage. Sur
un axe croisé — une colonne par partition, un passage par partition — c'était autant de fois trop long
qu'il y a de partitions : mesuré sur Cardiologie 4ᵉ MED, **14/09/2026 → 25/03/2027** écrit dans douze
dossiers pour un stage servi en un mois, et une bande de calendrier étirée de trois mois avec.

- **La fenêtre est résolue par cohorte**, sur les créneaux derrière les cellules qu'elle occupe
  (`DelocalizationWindowResolver`), et **après** que les étudiants sont connus. L'acte de masse en
  résolvait **une** avant même de savoir qui était nommé : deux partitions dans un lot recevaient les
  mêmes dates, dont l'une au moins fausse par construction.
- **La provenance voyage avec les dates** — `DelocalizationWindowSource` : `Named` (saisies),
  `Cohort` (le passage du groupe), `StageAxis` (⚠ tout l'axe, faute de cellule). Une cohorte non
  encore répartie **retombe** sur l'axe, délibérément, parce que délocaliser un stage non planifié est
  un cas soutenu — mais l'écran le dit au lieu de faire passer quatre mois pour une mesure.
- **Le rapport n'annonce plus une paire de dates unique** : `StartDate`/`EndDate` ne sont remplies que
  si toutes les lignes applicables partagent une fenêtre, et `DistinctWindowCount` distingue les trois
  états. Les dates sont sur chaque ligne, avec une colonne « Période » et un badge « tout le stage ».
- **Le refus reste** : un stage sans aucun créneau répond `Delocalizations.NoWindow` plutôt que
  d'inventer des dates.

`SMOKE-TEST.md` §55, `docs/delocalization.md`, `NOTES.md` (session 57).

### Ce qui n'a **pas** été construit, et pourquoi

- **Aucun second import pour les validations.** Le canevas d'évaluation existant les atteint déjà :
  il refuse les périodes *non closes*, et une délocalisation naît close. Portée « stage entier »
  obligatoire — une délocalisation n'a pas de numéro de période, puisqu'elle ne suit aucune grille.
- **La validation par objectif reste unitaire** : `ImportEvaluationsCommand` refuse ce mode, faute
  d'une colonne par objectif dans la feuille.
- **Les cellules ne sont pas effacées** quand tout un roster part. C'est ce qui rend l'acte
  réversible : annuler puis republier restaure la rotation, ce que supprimer les cellules ne ferait
  pas.

### Côté écran

L'interrupteur « Service hors faculté » (formulaire, liste, fiche), `BulkDelocalizationModal` (aperçu
→ application, le bouton porte le nombre de l'aperçu), la note /20 et l'annulation dans la modale d'un
étudiant, et « dont N hors CHU » sur la ligne d'un roster. Voir `PGSH.Frontend/PHASES.md`.

### Ce qui reste

- **Aucun service externe n'existe dans la base.** Le drapeau existe, il vaut `false` partout, et rien
  ne change tant que la scolarité n'a pas créé la ligne « Stage hors CHU — Kénitra ».
- **Rien n'a été piloté au navigateur** — voir `SMOKE-TEST.md` §49 et `HANDOFF.md` item `0am`. Le
  type-check, le lint et le build sont propres, et aucun des trois n'aurait vu les deux défauts que le
  clic a trouvés la veille.

## 📋 Phase 26 — « Un stage acquis ne se ressert jamais » (planifié, rien d'écrit)

**Règle tranchée par l'utilisateur le 07/09/2026** : un stage validé est définitivement acquis ; ce
qui reste se poursuit à l'inscription suivante ; un stage échoué se règle par « Revalider ». Le
**dossier** l'applique déjà — `OutstandingStageFinder.Fold` : *« one validated attempt clears the
stage for good, whichever year earned it »*. La **planification**, elle, l'ignore.

⚠ **Rien n'est implémenté.** Cette phase est le plan, pas un livrable. `HANDOFF.md` A7.

### 26.1 — La répartition cesse de replacer un étudiant dans un stage acquis
`StudentAffectationService.AssignAsync` dédoublonne sur `(RegistrationId, CohortId)`. Une nouvelle
inscription et de nouvelles cohortes ne coïncident jamais avec les anciennes, donc l'affectation
automatique recrée **tous** les stages de la promotion, ceux déjà validés compris.

- **La même règle qu'ailleurs, pas une seconde.** « Acquis » = au moins une tentative `Validé` dans
  une année que `AnnulsItsStages` n'annule pas. Deux définitions de « acquis » à deux endroits, c'est
  le défaut que `ServicePeriodLifecycle` et `StageScoring` ont déjà coûté.
- ⚠ **Écarter en le disant.** « 3 étudiants non affectés : stage déjà acquis », par le
  `BulkResponse` par ligne dont dispose déjà le service. Une promotion sortie un étudiant plus courte
  ressemble exactement à une promotion de cette taille — c'est la raison d'être du même mécanisme dans
  la découpe des rosters.
- ⚠ **Requête plate, en une fois.** Ce chemin parcourt une promotion entière et le macro-plan le
  répète par bloc de concurrence ; une lecture par cohorte serait le retour des ~700 aller-retours.
- **Impact mesuré le 07/09/2026 : nul.** Les seuls redoublants des promotions planifiées de
  2026-2027 sont **27 étudiants** (12 en 3ᵉ MED, 10 en 4ᵉ, 5 en 5ᵉ) et **toutes** leurs tentatives
  antérieures sont `NonÉvalué`. C'est donc le moment le moins cher pour poser la règle : no-op
  aujourd'hui, correcte dès la première année portant des notes.

### 26.2 — « Revalider » devient « servir un stage dû » *(dépend de la phase 15.2)*
Retirer la précondition `NothingToRevalidate`, qui exige une tentative **échouée** : un stage jamais
tenté, ou servi et resté `NonÉvalué`, n'a aujourd'hui aucun chemin. Le garde-fou qui reste est le bon
— un stage validé sur n'importe quelle inscription ne se rouvre jamais. C'est l'item 13 de la file.

### 26.3 — « Ce qu'il doit » s'élargit à *exigé par le CNPN − validé* *(dépend de la phase 15.2)*
`OutstandingStageFinder` dit déjà que c'est l'endroit naturel pour élargir. ⚠ **Pas avant que les jeux
d'exigences de 1650.25 soient saisis** : aujourd'hui « dû » = « toutes les tentatives ont échoué »,
donc ce qui est dû est exactement ce qui est revalidable — cohérent. 26.3 sans 26.2 fabriquerait des
dettes que rien ne peut éteindre.

### ⚠ Ce que cette phase ne touche pas — la 7ᵉ année
`Septième Année Médecine` porte **0 stage au catalogue** et ses 1 347 inscrits de 2026-2027 portent
**0 affectation** : la dernière année est celle de la thèse et des examens cliniques, et ce qu'un 7ᵉ
année « doit encore » sont des stages de **6ᵉ**, reportés. Aucune répartition annuelle ne peut donc l'y
replacer, et ses **510** stages dus (**242** étudiants) sont **tous** revalidables aujourd'hui — chacun
porte une tentative échouée. **Pour la 7ᵉ année, la règle de l'utilisateur est déjà le comportement.**

### ⚠ Le vrai risque est ailleurs, et ce n'est pas du code — `HANDOFF.md` A8
**4 196 tentatives `NonÉvalué`** chez ces mêmes 7ᵉ année : servies, jamais notées. Ni acquises
(23 569 le sont), ni dues (510 le sont) — en limbes, sur aucune des deux listes. Le jour où la faculté
tranche que « non noté » vaut « non fait », elles deviennent des dettes d'un coup, sur des étudiants
qui se croyaient finis. Le remède est l'import d'évaluations et la liste de travail des chefs.

## 📋 Phase 27 — Le découpage et le calendrier, dits comme la faculté les dit (planifié, rien d'écrit)

**Quatre demandes de l'utilisateur, le 10/09/2026.** Elles ne partagent pas un mécanisme mais une
forme : chacune remplace une façon détournée de dire une chose par la façon dont la faculté la dit
déjà. ⚠ **Rien n'est implémenté.** `HANDOFF.md` **0ay**, **0az**, **0ba**, **0bb**.

### 27.1 — Un férié qu'on traverse, et la contrainte de planification dite en toutes lettres
**La règle, telle qu'elle a été donnée** : *la seule* contrainte de planification est qu'une période
ne **commence** ni ne **finisse** un week-end ou un jour férié. Un férié peut donc tomber **à
l'intérieur** d'une fenêtre sans l'allonger. Aujourd'hui PGSH ne sait pas le dire : `Holiday` est
chômé ou n'existe pas.

- **Le drapeau** : `Holiday.CountsAsWorkingDay`, `false` par défaut — aucun axe déjà posé ne bouge.
- ⚠ **Le cœur technique est une scission, pas une colonne.** `WorkingDayCalendar.IsWorkingDay` répond
  à **deux** questions à trois appels : `Count` (ce jour compte-t-il dans la durée), `NextWorkingDay`
  (une fenêtre peut-elle **commencer** là), `Lay` (les deux — il avance la durée *et* son dernier jour
  devient le `End`). Il en faut deux : « compte dans la durée » et « peut borner une fenêtre ».
  Week-end : ni l'un ni l'autre. Férié sans drapeau : ni l'un ni l'autre. Férié drapeau : compte, mais
  ne borne pas.
- ⚠ **`Lay` doit tenir sa promesse** — « `End` est toujours un jour bornable ». Le Nᵉ jour posé peut
  tomber sur un férié drapeau : la fenêtre s'étend alors au jour bornable suivant **sans le compter**,
  sinon le compte et la date de fin se contredisent.
- ⚠ **`WorkingDaysLost` tombe à 0** pour un férié drapeau, sinon l'écran de couverture réclame des
  jours que personne n'a perdus.
- ⚠ **La `PromotionPause` ne prend pas le drapeau** — une semaine d'examens ne compte jamais comme
  ouvrée. Si le drapeau monte sur `ICalendarClosure`, les pauses répondent `false` et l'interface le
  dit.
- **Ordre** : avant de reposer les axes de 2026-2027 (`HANDOFF.md` 0ap).

### ✅ 27.2 — Découper en *N* groupes, et pas seulement en groupes de *N* *(livré le 11/09/2026)*
« Répartir la 5ᵉ MED en 100 groupes » : on ne donne que 100. `AutoArrangeGroupsCommand` ne porte que
`GroupSize` ; la cible en **nombre** rejoint la même commande, les deux exclusives.

- ⚠ **« Également » veut dire plus grand reste.** 933 en 100 = **33 × 10 et 67 × 9**, jamais 99 × 10
  et 1 × 33 — ce que donne le `Skip`/`Take` actuel.
- ⚠ **Les paniers CNPN cassent la division.** La coupe est par (année, niveau, `CnpnVersionId`) et
  chaque texte prend des groupes entiers : « 100 » s'apporte entre paniers, et un panier de 7 ne rend
  pas 12 groupes. Le rapport nomme le nombre réellement créé **et** l'écart.
- ⚠ **« 100 » veut dire 100 de plus** tant que la numérotation continue : le dire, ou refuser sur une
  promotion déjà découpée.
- ✅ ⚠ **Et l'*ordre* des tailles compte autant que les tailles** *(corrigé le 11/09/2026, session
  62)*. Les grands rosters étaient rendus **en tête**, et `PartitionAllocator.Contiguous` — la
  convention de la faculté, choisie 8 fois sur 8 — donne à la partition A le premier bloc de numéros :
  tous les rosters surdimensionnés atterrissaient donc dans les premières colonnes. La 3ᵉ MED est
  sortie en **100, 100, 100, 93, 90 ×6** au lieu de **94, 94, 94, 93 ×7**, ce qui a consommé toute la
  marge de Santé Publique et de Simulation Médicale. Le correctif est un ordre, pas une taille.
  → [`docs/planning-rosters.md`](docs/planning-rosters.md) §②ter.

### 27.3 — Le canevas de découpage
Une ligne par étudiant, une colonne « Groupe », on remplit, on renvoie — et les groupes manquants sont
créés. **Les deux moitiés du patron existent** (`Get*TemplateQuery` pour la descente,
`ApplyReinscriptionSheetCommand` pour la remontée ; ClosedXML est déjà là).

- ⚠ **Ce n'est pas `ApplyBulkRosterAssignmentCommand`**, qui vise **un** roster : la feuille les nomme
  tous. Ce qui se réutilise est son vocabulaire (`BulkRosterAssignmentRowStatus`) et ses deux verbes
  décidés par étudiant.
- ⚠ **Aperçu et `ConfirmedCount` obligatoires**, plus l'annulation à côté — la feuille tombe sur des
  lignes que personne n'a tapées une par une.
- ⚠ **Apparier sur `Appogee` *et* CNE** : le CNE est facultatif.
- ⚠ **Un tableur peut faire naître des rosters** : « groupes à créer » se compte à part et voyage dans
  la confirmation.

### 27.4 — Un modèle de planification par niveau *(basse priorité, dit par l'utilisateur)*
« Groupes 1-2 en P1 au service S1, en P2 à S2… » : le circuit nommé une fois, appliqué à une
promotion ; et la réciproque, enregistrer comme modèle une répartition générée. **Un modèle est la
grille sans ses dates et sans ses rosters** — (partition, `PeriodNumber`, `ServiceId`) par stage —
parce que ce qui est fixe est le nombre de colonnes, pas les dates ni les étudiants.

- ⚠ **C'est l'axe qui rend les colonnes comparables** : même *T* = Σ*k*ₛ et même jeu de stages, sinon
  un contrôle de compatibilité qui **nomme l'écart** plutôt qu'une écriture partielle.
- ⚠ **Une cellule venue d'un modèle est `CellSource.Pinned`** — une décision humaine. `Arranged` la
  ferait réécrire par la répartition suivante sous un `Assigned = N` parfaitement normal.
- ⚠ **La `PartitionStrategy` voyage avec le modèle**, ou « partition A » ne désigne pas les mêmes
  groupes d'une année à l'autre.
- **Pourquoi basse priorité** : c'est un confort par-dessus un chemin qui marche (poser l'axe,
  répartir, épingler), pas un manque.


## ✅ Phase 28 — Le registre couvre enfin la planification, et deux actes qui mentaient

**Livré le 10/09/2026** (`HANDOFF.md` A3). La campagne de répartition de 2026-2027 est une boucle
d'actes destructeurs rejouée par promotion — découper, poser l'axe, répartir, publier, regarder,
dépublier, réinitialiser, recommencer — et **cinq des actes de cette boucle n'écrivaient rien**.

### 28.1 — Dix codes d'actes de plus
Côté cohorte : `STAGE_COHORTS_RESET`, `COHORT_DELETED`, `COHORT_SCHEDULE_UNPUBLISHED`,
`STAGE_SCHEDULE_UNPUBLISHED`. Côté grille : `STAGE_SLOT_CREATED`, `STAGE_SLOT_UPDATED`,
`STAGE_SLOT_DELETED`, `COHORT_SLOT_PINNED`, `COHORT_SLOT_CLEARED`, `STAGE_SLOT_CELLS_CLEARED`.

- ⚠ **Les actes constructeurs sont audités aussi**, pour la raison qui a fait auditer
  `AutoArrangeGroupsCommand` : « qui a posé cet axe » est la même question que « qui l'a supprimé ».
- ⚠ **`forced` voyage dans `COHORT_SCHEDULE_UNPUBLISHED`** : forcé, cet acte détruit des notes de
  chef et des journées de présence, et elles ne survivent nulle part ailleurs.

### 28.2 — ⚠ Deux actes portaient `IAuditableCommand` depuis la phase 20 et n'écrivaient rien
`DeleteAllGroupsCommand` et `EmptyAllYearGroupsCommand` n'écrivent que par `ExecuteDelete` /
`ExecuteUpdate`, qui contournent le change tracker, et **n'appelaient jamais `SaveChanges`** — la
ligne mise en attente par `AuditLogPipelineBehavior` mourait avec la portée de la requête. La phase 20
les comptait faits. Corrigé par un `SaveChangesAsync` explicite, et couvert.

- ⚠ **Le trou de couverture est la vraie leçon** : le fournisseur *in-memory* **refuse**
  `ExecuteDelete`, donc le chemin de succès de ces actes n'était atteignable par **aucun** test du
  dépôt — les tests de handler existants n'assertaient que leurs refus parce que c'est tout ce qui
  pouvait s'exécuter. `TestHarness.NewSqliteContext` l'ouvre (SQLite est relationnel) ;
  `ExecuteDeleteAuditTests` est le fichier. Cela reste **loin** d'un vrai PostgreSQL : voir 18.x.

### 28.3 — `IAuditTrail` : une entrée dit ce qu'elle a emporté
Une commande ne connaît que ce qui a été *demandé*. Le behavior **ouvre** l'entrée, le handler y
dépose son constat avant son `SaveChanges`, et la piste **remplace** l'entité en attente au lieu de
la modifier — `AuditLog` reste immuable. C'est aussi ce qui fait entrer dans l'entrée l'**année
réellement atteinte**, qu'une commande omettant l'année ne peut pas nommer.

- **Un acte sans effet s'enregistre avec son zéro** ; un acte **refusé** continue de n'écrire rien.
- ⚠ **Incompatible avec `ExecuteAtomicallyAsync`**, dont la reprise re-stage l'entrée telle
  qu'ouverte : deux lignes pour un acte. Écrit dans `IAuditTrail`.

### 28.4 — ⚠ « Publier » n'était pas audité du tout, alors que « Dépublier » l'était (11/09/2026)
Le registre tenait le *défaire* sans le *faire*, sur l'acte qui crée les `ServicePeriod` — tout ce que
les chefs notent et tout ce que les présences visent. `COHORT_SCHEDULE_PUBLISHED` et
`STAGE_SCHEDULE_PUBLISHED` (portée visée comprise, l'acte étant scopable comme son inverse).

- ⚠ **`allowOverCapacity` voyage avec l'entrée**, exactement comme `forced` en 28.1 : passer outre un
  service ayant déclaré refuser d'être dépassé est un geste posé **contre** ce refus.
- ⚠ **Le `SaveChanges` du handler est inconditionnel**, là où le publisher n'écrit que s'il a des
  périodes à poser — c'est la forme *conditionnelle* du défaut de 28.2, et sa troisième occurrence.
  « Publier » rejoué sur un stage déjà publié n'aurait sinon rien laissé.
- `PublishCohortAsync` répond un **nombre** de périodes et non un `Result` nu : 28.3 exige qu'une
  entrée dise *combien*. `PublishScheduleAuditTests`.
- ⚠ **Trouvé en comparant deux commandes, pas par un balayage.** `IAuditableCommand` est déclaratif,
  donc **rien ne signale un acte qui ne le déclare pas** — c'est la limite structurelle du sweep de
  `0bd`, qui ne peut interroger que les actes déjà marqués.

### Ce qui reste
- **L'atomicité de ces trois actes.** `DeleteAllGroups`, `EmptyAllYearGroups` et `DeleteAllCohorts`
  enchaînent jusqu'à six `ExecuteDelete` **hors transaction** : une annulation à mi-parcours laisse
  une destruction à moitié faite. `ExecuteAtomicallyAsync` est l'outil, et il faudra d'abord régler
  son interaction avec la piste (28.3). `HANDOFF.md` **0bc**.
- **Le reste des actes auditables n'a pas été balayé de bout en bout** : seuls ceux qui ne
  sauvegardaient pas l'ont été. Un acte dont le `SaveChanges` est *conditionnel* écrirait de la même
  façon dans le cas non couvert. `HANDOFF.md` **0bd**. ⚠ 28.4 en est la troisième occurrence.
- ⚠ **Et le balayage ne peut pas voir un acte qui ne se déclare pas.** Les deux publications l'ont
  montré : la question « cet acte devrait-il être audité ? » ne se pose qu'en lisant les actes
  symétriques deux à deux (publier/dépublier, créer/supprimer, appliquer/annuler).

---

## ✅ Phase 29 — La capacité se lit avant qu'il existe un plan

**Livré le 12/09/2026** (`HANDOFF.md` **0bi**, demandé le 11/09). La campagne de 2026-2027 découvrait
les manques de places **devant le bouton « Publier »**, c'est-à-dire après une journée de découpage,
d'axe et de répartition — et huit publications de la 3ᵉ MED ont été faites avec « autoriser le
dépassement » coché parce que le chiffre est arrivé trop tard pour être discuté.

### 29.1 — `GET /services/promotion-fit`
Une lecture par (année, promotion) qui met en regard, pour chaque stage, **ce qu'il faut** — la
tranche de la promotion qui s'y tient en même temps — et **ce qu'il y a** — la somme des places des
services autorisés. Écran : *Admin → Infrastructure → **Faisabilité des promotions***.

⚠ **Ce n'est pas un second `OccupancyReport`.** Celui-là lit les cellules, donc il affiche **zéro**
pour une promotion qu'on n'a pas encore découpée. Cette lecture n'a besoin d'aucun plan : effectif,
durées, capacités, rien d'autre. C'est toute sa raison d'être.

### 29.2 — `PromotionAxis`, et la calibration
`Domain/Stages/PromotionAxis.cs` — pur, sans magasin : `kₛ = durée_s / pgcd(durées)`, `T = Σkₛ`, et la
tranche simultanée `⌈N·kₛ/T⌉`. Même identité que `Lₛ = P·kₛ/T` de `RotationCyclePlanner`, lue par
étudiant plutôt que par partition, **donc le nombre de partitions se simplifie** : découper plus fin
ne soulage aucun stage.

⚠ **La calibration est le premier test du fichier.** Sur la 3ᵉ MED (933 étudiants, 2×30 j + 6×15 j →
10 colonnes de 15 j) la formule retrouve exactement les deux nombres mesurés à la main sur la base
vivante les 11-12/09 : Dermatologie **−14**, Santé Publique **+6**. Une formule qui ne retrouverait
rien serait un second avis sur lequel personne ne pourrait agir.

### 29.3 — Quatre « impossible », nommés séparément
« aucun service autorisé », « aucun n'admet cette promotion », « tous réservés », « durée manquante » —
quatre actes différents, et l'arrangeur lui-même refuse par trois erreurs distinctes. « Impossible »
l'emporte sur « en dépassement » quelle que soit la profondeur : l'un est un trou de catalogue, l'autre
une décision que la faculté prend régulièrement.

⚠ **Le pool de services est celui de `RotationArranger`, clause pour clause** — externe dehors,
non-admis dehors, `Reserved` dehors, capacité par `Service.CapacityFor`. Compter des places que
l'arrangeur n'utilisera pas serait pire que de ne rien afficher.

### Ce qui reste
- **La page prévient, elle ne place pas mieux.** `BuildServiceQueue` pondère par la capacité seule et
  ne lit **jamais** l'occupation vivante, donc répartir la 4ᵉ MED l'étalera sur les services où la 3ᵉ
  est déjà assise. Ce qui change est le *moment* où la faculté décide, pas la décision.
- **`0bg` et `0bh` restent entiers** : Pédiatrie qui demande 351 places là où il en existe 120 est une
  décision de la faculté, pas un écran.
- **Rien n'est encore vu au navigateur** : `SMOKE-TEST.md` **§62**, après redémarrage de l'AppHost.

---

## ✅ Phase 30 — Les actes destructeurs atterrissent entiers, ou pas du tout

**Livré le 12/09/2026** (`HANDOFF.md` **0bc**, ouvert par la phase 28). Les trois actes de masse de la
campagne n'écrivent que par `ExecuteDelete` / `ExecuteUpdate` : six instructions séparées pour
« Supprimer les groupes », cinq pour « Réinitialiser les cohortes », une plus le journal pour
« Vider les groupes ». Chacune était définitive dès qu'elle passait, et **rien ne les liait**.

### 30.1 — Ce qu'une interruption laissait
Coupée après la troisième suppression, « Supprimer les groupes » laissait les groupes, leurs cohortes
et toute la grille debout, avec plus personne dedans : un plan complet pour zéro étudiant, que rien à
l'écran ne distingue d'un plan voulu — et **aucune ligne au registre**, la sienne étant la septième
instruction. ⚠ ASP.NET annule la requête dès que la connexion tombe, et un onglet fermé *est* une
connexion tombée : sur une promotion de 925 étudiants l'acte dure des secondes visibles.

### 30.2 — Pourquoi cela n'était pas une ligne de code
`ExecuteAtomicallyAsync` vide le change tracker à chaque tentative et remettait une **photographie**
des entités mises en attente à l'entrée. Or `IAuditTrail.RecordOutcome` **remplace** l'entrée en
attente (l'`AuditLog` est immuable), donc la reprise remettait celle d'avant le constat pendant que la
piste tenait la remplaçante : deux lignes pour un acte. D'où l'ancienne règle « choisir entre
l'enveloppe et le constat », qui laissait sans transaction précisément les actes qui en avaient le
plus besoin.

### 30.3 — La responsabilité va où est la connaissance
L'entrée appartient à la piste : c'est elle qui la remet, en tête de chaque tentative, dans sa version
courante. `IAuditTrail.RunAtomicallyAsync` est désormais le seul chemin d'un acte **audité** ;
`IApplicationDbContext.ExecuteAtomicallyAsync` reste celui d'un acte sans journal et ne remet plus
rien de lui-même. Les quatre appelants qui l'utilisaient déjà — `AutoArrangeGroups`,
`GenerateMacroPlan`, `CurrentYearDesignation`, `ServiceRankWriter` — passent par la piste : **un seul
mécanisme**.

⚠ **Aucun état mutable n'a été ajouté au `DbContext`**, qui est *pooled* : un champ que rien ne
réinitialise se serait promené d'une requête à la suivante.

### Ce qui reste
- **L'atomicité ne protège pas d'une destruction complète qu'on regrette** : `pg_dump -Fc` avant tout
  acte de masse, comme avant.
- **La reprise réelle n'est pas testable ici** — elle ne se déclenche que sur une panne transitoire de
  la base. `AtomicUnitOfWorkTests` reproduit le vide et le rejeu à la main, ce qui vérifie la
  propriété exacte dont le mécanisme dépend.
- **`0bd` reste ouvert** : le balayage « un acte auditable atteint-il un `SaveChanges` ? » n'a couvert
  que les handlers qui n'en appelaient jamais.

## ✅ Phase 31 — Trouver une personne, et tirer une composition au sort

**Livré le 12/09/2026**, sur deux demandes de la faculté (`HANDOFF.md` **0bk** pour la vérification à
l'écran, **0bl** pour la moitié qui reste). Les deux défauts sont de la même famille : une propriété
que personne n'a choisie, installée par la façon dont une requête était écrite.

### 31.1 — Un terme de recherche est une conjonction de mots
Les sept recherches d'étudiant comparaient le terme **entier** à chaque colonne, donc « Mohamed
Alami » — le nom complet, la première chose que l'on tape — ne rendait **personne** : aucune colonne
ne porte le prénom et le nom à la fois. `SearchTerms.Split` découpe (espaces, virgules,
points-virgules ; ⚠ ni le tiret ni l'apostrophe, qui appartiennent aux noms), cinq mots au plus, et
chaque mot doit se retrouver sur la **même** personne.

⚠ **La règle élargit strictement l'ancienne** : une colonne qui contient la chaîne contient chacun de
ses mots. C'est ce qui rendait le changement sûr, et chaque cas nouveau est appairé à ce témoin dans
`FullNameSearchTests` ; `StudentSearchTests`, antérieur et intact, tient les cas d'avant.

### 31.2 — Et les colonnes sont les mêmes sur tous les écrans
Six sur la liste, cinq sur un roster, quatre sur la liste de travail d'un chef, trois sur les occupants
d'un service : un étudiant trouvé par son Apogée depuis la liste ne l'était **pas** depuis le service
où il se tient, ce qui se lit comme une absence du service. `StudentSearch.WhereStudentMatches` est
désormais le seul chemin — nom, prénom, CNE, Apogée, CIN, e-mail — sur les sept écrans et sur
l'export, et `EmployeeSearch` fait la même chose pour les professeurs.

⚠ **Le risque technique était la composition d'expressions** : une règle écrite sur `Student`,
appliquée à des inscriptions, des périodes et des cellules. La façon naïve est un `Invoke`, que **EF
refuse** — sept écrans en 500 d'un coup avec la suite verte. `ExpressionComposition.Through` substitue
le chemin au paramètre ; `SqlTranslationTests` compile les quatre formes employées.

### 31.3 — La composition des groupes est tirée au sort
`AutoArrangeGroupsCommandHandler` lisait ses candidats par nom de famille et les déposait dans cet
ordre : les rosters se formaient par tranches de l'alphabet, et un roster décide une année entière —
la partition, les créneaux, les services, les chefs. `RosterDraw` tire (Fisher–Yates) et **ne touche
ni le nombre de rosters ni leur taille** : cela reste `RosterCut`, dont l'ordre des tailles est ce qui
égalise les colonnes depuis la phase 27.

⚠ **Un mélange muet aurait été un recul par rapport au tri qu'il remplace** : le tri était explicable.
Le numéro du tirage part au registre (`GROUPS_AUTO_ARRANGED` → `drawSeed`, avant le premier
`SaveChanges`), et les candidats sont lus dans un ordre **total** — nom, puis identifiant — sans quoi
ce numéro ne désignerait rien.

### 31.4 — Une panne n'est pas un défaut (13/09/2026)
Ajouté après l'incident du 13/09 : WSL s'est mis à jour de lui-même, la distribution Docker s'est
arrêtée et PostgreSQL avec elle. `GlobalExceptionHandler` rangeait toute exception non-`DomainException`
dans `_ => 500, « Server failure »`, donc **chaque écran** a répondu comme l'aurait fait un bug.

- Une base injoignable répond **503** avec une phrase : la demande n'a rien enregistré, et ce qu'il
  faut regarder est le serveur de base de données. Le client fait du 503 la **seule** exception au
  masquage des ≥ 500.
- ⚠ **Le tri est étroit** : seule une `DbException` de la chaîne peut déclarer la panne, et seulement
  si elle est transitoire ou porte un échec réseau. Un serveur qui a répondu « je refuse » reste un
  500 — l'erreur inverse rendrait un vrai défaut invisible.
- La sonde Docker des sauvegardes cesse de porter le délai du `pg_dump` (10 s contre 600 s), et un
  délai dépassé est un fait porté (`ProcessRunner.Execution.TimedOut`), pas une chaîne à relire.

### Ce qui reste
- **Une vraie coupure n'est pas reproductible ici** : que PostgreSQL débranché lève bien l'exception
  qu'on classe demanderait Testcontainers, toujours pas construit. La classification, elle, est
  couverte des deux côtés.
- **L'accent ne se replie que du côté du terme** : « Zoubaïr » retrouve `ZOUBAIR`, l'inverse non.
  Il faut `unaccent` côté PostgreSQL — extension, colonne générée, migration — et d'abord **une
  mesure** de combien de noms portent un accent dans la base. Item `0bl`.
- **`PartitionAllocator` n'a pas bougé**, délibérément : la place d'un *groupe* dans le tableau est
  choisie (`Contiguous`), et la tirer au sort rendrait la répartition imprimée illisible.
- **Rien n'a été mesuré sur la base vivante** cette session (lecture de production refusée par
  l'outillage) : les deux défauts sont lisibles dans le code et couverts par des tests, et aucune
  affirmation chiffrée n'a été écrite sans mesure.

---

## ✅ Phase 32 — Téléverser les affectations d'une promotion

Demandée par l'utilisateur le 13/09/2026, en regard du canevas de découpage (file d'attente, item 0ba) :
l'un dit **qui est dans quel groupe**, celui-ci dit **où va chacun et quand**. « C'est un peu dangereux,
il faut un rapport d'erreurs généreux » — ce sont ses mots, et c'est la forme que l'acte a prise.

**Livré**

- `GET affectations/sheet/template` — le canevas **pré-rempli** : une ligne par (étudiant, stage du
  niveau), les périodes déjà servies remplies, le reste en blanc. Passe par `ExportWorkbook` /
  `IExportWorkbookWriter`, comme les autres documents.
- `POST affectations/sheet/preview` — l'aperçu. N'écrit rien, et **c'est le même planificateur** que
  l'application : un aperçu calculé d'un côté et une écriture faite de l'autre sont deux règles que rien
  n'empêche de diverger, et ici la seconde détruirait des périodes.
- `POST affectations/sheet` — l'application. **Tout ou nothing**, dans une transaction, sous
  `IAuditTrail.RunAtomicallyAsync`. Crée les cohortes manquantes, les affectations, leurs périodes, et
  les délocalisations que le fichier déclare.
- `InternshipAssignment.DeclareRotation` — l'agrégat qui remplace une rotation entière par celle qu'un
  humain a déclarée, refuse sur une note, et recalcule note et statut derrière lui.
  `AffectationImportedDomainEvent` → `HistoryType.AffectationImported`.
- 38 tests neufs : 23 de handler, 9 par le vrai pipeline HTTP (dont l'aller-retour complet
  téléchargement → édition dans le classeur → téléversement), 6 cas de traduction SQL. Morsure vérifiée
  sur les trois gardes qui comptent (la note, les deux nombres).

**Les quatre décisions, prises par l'utilisateur le 13/09/2026**

| Question | Décision |
|---|---|
| Une affectation qui existe déjà | **Remplacer**, mais jamais par-dessus une note (`AlreadyMarked`). |
| Écrire aussi la grille ? | **Non** — périodes seules, hors grille. |
| Portée d'un fichier | **Une promotion, tous ses stages** — (année, niveau). |
| Comment se déclare une délocalisation | Un **motif** + un service `IsExternal` ; les deux, ou refus. |

**Ce qui n'est pas fait, et pourquoi**

- **Pas d'annulation en masse.** Le fichier corrigé remplace ce qu'il décrit — cela couvre la faute de
  frappe — mais rien ne retire une affectation qui n'aurait jamais dû exister. Il faudrait marquer le
  lot, donc une migration. Même forme que l'item 0aw pour la délocalisation. → file d'attente.
- **Pas d'écriture de la grille.** Écrire les `StageSlot` et les `CohortSlotAssignment` ferait du
  canevas un arrangeur : il faudrait réconcilier les fenêtres par (stage, année, période) sous
  `SlotOverlapGuard`, qui est niveau-**et**-année. Décidé hors périmètre.
- **Rien n'a été cliqué.** `SMOKE-TEST.md` §56.

Règles complètes : [`docs/affectation-sheet.md`](docs/affectation-sheet.md).

---

## ✅ Phase 33 — Défaire un téléversement d'affectations

Le manque que la phase 32 avait nommé (file d'attente, item 0bp) : renvoyer le fichier corrigé couvrait
la faute de frappe, mais rien ne retirait une affectation qui n'aurait jamais dû exister, et le point de
sauvegarde était le seul retour en arrière.

**Ce qui a été construit**

- `AffectationImport` — un **agrégat**, pas une colonne marqueur sur `ServicePeriod`. La question posée
  trois mois plus tard n'est pas « quelles lignes viennent d'un tableur », c'est « défais ce que j'ai
  fait jeudi » : cela a une identité, une portée, un auteur, un moment, un état — et cela doit savoir ce
  que l'acte a **détruit**, pas seulement ce qu'il a écrit.
- `InternshipAssignment.RestoreRotation`, l'inverse exact de `DeclareRotation` : elle remet les
  drapeaux de cycle de vie **et la cellule de grille**, sans quoi le plan et l'exécution resteraient
  désaccordés sans que rien ne le dise.
- Trois routes — lister, aperçu, annuler — avec un `confirmedCount` qui porte la moitié destructrice
  (les affectations supprimées), et le même marché tout-ou-rien que l'aller.
- Migration `AffectationImportJournal` : trois tables, purement additive.

**Le défaut trouvé en chemin, et il décide de tout le reste**

`AttendanceRecord` cascade depuis `ServicePeriod`, et `DeclareRotation` ne gardait que la note : réécrire
une rotation commencée supprimait **en silence** les journées de présence. Nouveau refus
`AlreadyAttended`, du planificateur jusqu'à l'agrégat.

⚠ Ce n'est pas une garde de plus à côté des autres : c'est la **prémisse** de l'annulation. Comme
l'import ne détruit jamais qu'un service, une fenêtre, quelques drapeaux, une cellule et un motif, tout
ce qu'il détruit tient dans le registre — donc l'annulation est **totale**. Une annulation qui remet en
silence moins qu'elle n'a enlevé est pire que pas d'annulation, parce que quelqu'un s'y fie.

**Tests** : 18 neufs (1 946 verts). ⚠ Les gardes de l'agrégat sont testées **directement** : le
planificateur refuse les mêmes cas, donc un test passant par le handler reste vert que l'agrégat garde
quoi que ce soit ou non — vérifié en cassant la garde du domaine, tous les tests de handler sont restés
verts. La garde de l'agrégat est celle qu'on ne peut pas contourner ; elle a son propre test.

⚠ **Rien n'a été cliqué et la migration n'est pas appliquée** — `SMOKE-TEST.md` §58.
