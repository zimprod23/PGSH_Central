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

Entities: `Stage`, `StageObjective`, `Cohort`, `CohortRotationTemplate`

- `Stage`: ties a curriculum unit to a `Level` with `DurationInDays` and `Coefficient`
- `StageObjective`: weighted evaluation criteria per Stage (`Weight`, `IsMandatory`)
- `Cohort`: groups an `AcademicGroup` with a `Stage` for a rotation cycle; `Label` required
- `CohortRotationTemplate`: the rotation *plan* — ordered sequence of Service assignments with planned dates (`SequenceOrder`, `PlannedStart`, `PlannedEnd`)
- Full CRUD endpoints for Stages, Levels, Cohorts

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

## 🔲 Phase 7 — Scheduling Automation

**Status: Not started**

- Generate `ServicePeriod` records from `CohortRotationTemplate` plans in bulk (the "publish rotation" operation)
- Conflict detection: service capacity vs. concurrent student count, date overlaps between templates
- `InternshipAssignment.FinalScore` computation: aggregate `ObjectiveScore.Score × StageObjective.Weight` across all ServiceEvaluations, persist back to assignment
- `InternshipStatus` lifecycle transitions with guard rules (e.g., cannot Validate without a completed Evaluation)
- Batch attendance generation for a ServicePeriod (pre-create records for each working day)

---

## 🔲 Phase 8 — Permission System

**Status: Stub exists — not implemented**

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

## 🔲 Phase 11 — Frontend

**Status: Scaffolded — Vite + React 19 + Mantine UI + Redux + Keycloak**

- Keycloak login flow and token refresh
- Student dashboard: registrations, current stage, attendance, evaluations
- Coordinator screens: group management, rotation planning, assignment overview
- Hospital admin: service capacity, attendance validation, evaluation submission
- Auto-arrange groups UI with preview before commit

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
- **`HydrateOccupancyAsync` loads all ServicePeriods**: For large academic years the occupancy loader pulls the full `ServicePeriods` table. Add a filter: only load periods overlapping the requested date range.
- **`UserContext.SyncAsync` memory cache is process-local**: In a multi-instance deployment each instance maintains a separate cache. Migrate the sync cache key to the Redis distributed cache already provisioned in `AppHost`.
