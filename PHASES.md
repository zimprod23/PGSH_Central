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

## 🔲 Phase 12 — Production Readiness

**Status: Not started**

- Tighten CORS policy (replace `AllowAllForDev` with explicit origins)
- Implement health checks (`/health` endpoint, Npgsql + Keycloak probes)
- Enable Redis distributed cache (`builder.AddRedisDistributedCache("cache")` already stubbed)
- Environment-specific `appsettings.Production.json`
- Aspire deployment manifest for container orchestration
- Structured log shipping (Seq or equivalent)
- CI/CD pipeline
