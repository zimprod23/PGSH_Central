# NOTES.md — Project Familiarization Notes

This file captures accumulated context, domain knowledge, and non-obvious observations built up while working through the PGSH codebase. It is a living document — update it as understanding deepens.

---

## Domain Understanding

### What PGSH actually is

PGSH is the internship management platform for a Moroccan medical faculty (likely FMPR — Faculté de Médecine et de Pharmacie de Rabat, inferred from an old Keycloak realm name `fmpr` found during cleanup). Medical and pharmacy students must complete clinical rotations in hospitals as part of their degree. PGSH manages the full administrative cycle: enrolling students each year, assigning them to groups, planning their rotation schedule through hospital services, tracking attendance, and recording evaluations.

### How the academic year works

Each `AcademicYear` (e.g., "2025-2026") has a set of `AcademicGroups` (e.g., "Group 1 - Rabat Zone", ~20 students each). Students are enrolled via `Registration`, which ties a student to a year, a level, and a group. The group assignment can happen automatically via the `AutoArrangeGroups` operation, which uses `GeographicZone` for geographic clustering.

### How rotations work

A `Stage` defines a rotation type tied to a `Level` (e.g., "Cardiology Rotation, Year 4 Medicine"). It has a duration, coefficient, and objectives (scored criteria). A `Cohort` is a specific instance of a Stage for a particular AcademicGroup in a given year. `CohortRotationTemplates` are the *plan* — the ordered list of services and planned dates the cohort will rotate through. When the rotation actually runs, `ServicePeriods` are created (the *execution*), optionally linked back to the template that spawned them.

### The student journey through the system

```
Enrollment:  Student → Registration (year + level + group)
Planning:    AcademicGroup → Cohort (group + stage)
             Cohort → CohortRotationTemplate (planned service sequence)
Assignment:  Registration → InternshipAssignment (student in cohort)
Execution:   InternshipAssignment → ServicePeriod (at a specific service)
             ServicePeriod → AttendanceRecord (daily presence)
             ServicePeriod → ServiceEvaluation → ObjectiveScores (graded)
Result:      InternshipAssignment.FinalScore (cached aggregate of scores)
```

### Key terminology

| Term | Meaning |
|---|---|
| Stage | A type of hospital rotation (e.g., "Chirurgie S6") |
| Cohort | A group doing a specific stage together in a given year |
| Template | `CohortRotationTemplate` — the rotation plan (planned dates + services) |
| Period | `ServicePeriod` — actual execution of one rotation slot |
| Assignment | `InternshipAssignment` — one student enrolled in one cohort |
| Level | Academic year of study (Year 1–6 in Medicine/Pharmacy) |
| CNE | Code National de l'Étudiant — unique national student ID |
| Appogee | University software student number (second unique ID) |
| PPR | Employee government registration number |
| PV | Procès-Verbal — official document (used for employee signature dates) |
| Grade | Academic rank: `MC` (Maître de Conférences), `PES` (Professeur Enseignement Supérieur), `PH` (Praticien Hospitalier), Nurse, Administrator |
| ServiceChef | Head of a hospital department; evaluates students in that service |
| GeographicZone | Field on AcademicGroup used by the auto-arrange clustering algorithm |

---

## Business Rules Discovered

These are rules enforced in domain or application code — not always obvious from the schema alone.

- **One registration per year**: A student cannot have two `Registration` records for the same `AcademicYearId`. Enforced in `Student.AddRegistration` and `CreateRegistrationCommandHandler`.
- **Validated registrations are locked**: `Student.RemoveRegistration` blocks deletion if `Status == RegistrationStatus.Validated`. Updates are allowed.
- **ServiceChef must be staff first**: `Service.AssignChef` throws if the employee isn't already in `Staff`, and checks `Position == Position.ServiceChef`.
- **Unique student identifiers**: CNE is hard-unique (index, never null). Appogee is unique with a nullable filter (some students may not have one yet).
- **Group uniqueness is year-scoped**: `(AcademicYearId, GroupNumber)` and `(AcademicYearId, Label)` are both unique — same group numbers can appear in different years.
- **Level uniqueness**: `(Year, AcademicProgram)` is unique — you can't have two "Year 4 Medicine" levels.
- **Attendance uniqueness**: `(ServicePeriodId, Date)` is unique — one attendance record per student per day per rotation.
- **FinalScore is a cache**: `InternshipAssignment.FinalScore` is described as "stored, not authoritative" — it must be recomputed from `ObjectiveScore.Score × StageObjective.Weight`. Currently never written. (Phase 7 item.)
- **Ad-hoc vs. planned periods**: `ServicePeriod.CohortRotationTemplateId` is NULL for ad-hoc rotations (created outside the template plan), non-null for template-driven ones.

---

## Current State of Each Layer

### Domain — Clean
All orphan models removed. Entities correctly model the domain with proper navigation properties. The main gap is that most entities outside the User/Registration/Student cluster are plain POCOs (no domain methods, no business logic encapsulated). Domain events are only raised on Registration operations.

### Infrastructure — Clean
EF configurations are complete and well-structured. All relationships are explicitly configured. The 10 new indexes are in place. `PermissionProvider` is a stub. The Aspire connection name `"TodoDatabase"` is a legacy artifact from scaffolding — it has no functional meaning and refers to the main PostgreSQL database.

### Application — Cleaned, partially built
- **Hospital/Center/Service**: Full CRUD — complete.
- **Academic (Level, AcademicYear, AcademicGroup)**: Read/create/update complete. The `AutoArrangeGroupsCommandHandler` is functional. `GenerateScheduleCommandHandler` in `AcademicGroups/Manage/Schedule/` is complex and needs review — this is the next major piece of work.
- **Student/Registration**: Full CRUD complete. Bulk registration works. History read works.
- **Stage/Cohort**: Create/update/delete complete. `GetCohortByStageId` handler exists but the response was an empty class — needs implementation.
- **InternshipAssignment/ServicePeriod/Evaluation/Attendance**: **No endpoints exist**. Entities and EF config are done; the entire execution and evaluation layer is unbuilt at the Application/API level.
- **Users**: GetById and GetByEmail work. `UserRegisteredDomainEventHandler` is an empty placeholder.

### API — Clean
All endpoints follow the `IEndpoint` pattern. All dead files removed. All routes consistent (no leading slashes, no `/api/` prefix in Created URLs). Enum fields in request types use proper enum types — no `int` casts anywhere. `GlobalExceptionHandler` catches all `DomainException` subclasses generically. `CustomResults.Problem` correctly maps all error types to HTTP status codes.

### SharedKernel — Clean
All types are correct and concise. `BulkResponse<TId, TResponse>` is well-designed for partial-success scenarios (used by bulk registration).

---

## Non-Obvious Technical Observations

### TPH means StudentId == UserId
The `User` table uses Table-Per-Hierarchy (TPH) with a `UserType` discriminator. A student's `Guid Id` is the same as their `User.Id`. There is no separate `Students.Id` column — it's the inherited PK. Querying `dbContext.Students` hits the `Users` table with `WHERE UserType = 'Student'`.

### UserContext.SyncAsync links Keycloak to DB on first login
When a user logs in for the first time, `SyncUserMiddleware` calls `UserContext.SyncAsync`. This:
1. Tries to find the user by `IdentityProviderId` (Keycloak `sub` claim)
2. Falls back to email if no match
3. Calls `user.LinkIdentity(keycloakId)` to store the link
4. Throws `UserProfileNotFoundException` → 403 if neither matches

This means: **a local `User` record must exist before anyone can log in**. The seeder in `MigrationService` creates initial users. In production this needs an admin flow to create profiles.

### CohortMembership tracks transfer history, not current cohort
`InternshipAssignment.CurrentCohortId` is the FK for the *current* cohort. `CohortMembership` is a history table — it records every cohort a student has ever been in, with start/end dates and transfer reason. A null `EndDate` means they're currently in that cohort. This enables tracking cohort transfers mid-rotation.

### GenerateScheduleCommandHandler is the complex core operation
Located at `Application/AcademicGroups/Manage/Schedule/GenerateScheduleCommandHandler.cs`. This is the operation that converts `CohortRotationTemplate` plans into actual `InternshipAssignment` + `ServicePeriod` records for students. It is the most complex piece of business logic in the codebase and has not been reviewed/cleaned yet. It will be the focus of the next cleanup session.

### GetStudentsQueryHandler filters CNE, Appogee, CIN as exact match
These are not partial-text filters — they use `==` equality. The `SearchTerm` filter does a `Contains` on FirstName, LastName, Email. This is intentional: CNE/Appogee/CIN are precise identifiers.

### The `int? AcademicProgram` bug in GetLevelsQuery is now fixed
Previously the query accepted `int?` and the handler cast it: `(int)l.AcademicProgram == request.AcademicProgram`. This was type-unsafe and broke JSON deserialization from string enums. Now correctly typed as `AcademicProgram?`.

### Endpoint binding pattern (POST / PUT / GET)

Three patterns coexist intentionally — all are correct:
- **POST**: bind the Command directly from the request body. No wrapper needed when there is no route parameter to merge. Enum fields deserialize as strings thanks to the global JsonStringEnumConverter.
- **PUT**: inner sealed Request record merges the route id with body fields, then constructs the Command. Enum fields use proper enum types, not int.
- **GET list**: [AsParameters] binds the Query record directly from query string parameters.

The previous codebase used int for all enum fields in Request records, forcing manual casts. All eliminated.

---

## Patterns to Follow When Adding New Features

### Adding a new GetMany handler
```csharp
// 1. Build filtered IQueryable with AsNoTracking()
var query = dbContext.Entities.AsNoTracking().AsQueryable();
if (request.Filter.HasValue) query = query.Where(...);

// 2. Use the extension — no manual count/skip/take
var response = await query
    .OrderBy(e => e.Name)
    .ToPaginatedResponseAsync(request.PageNumber, request.PageSize,
        e => new SummaryResponse(...), cancellationToken);

return Result.Success(response);
```

### Adding a new Create handler
```csharp
// Existence check (FK validation)
bool parentExists = await dbContext.Parents.AnyAsync(p => p.Id == request.ParentId, ct);
if (!parentExists) return Result.Failure<int>(Error.NotFound("Parent.NotFound", "..."));

// Uniqueness check
bool duplicate = await dbContext.Entities.AnyAsync(e => e.Name == request.Name, ct);
if (duplicate) return Result.Failure<int>(Error.Conflict("Entity.Duplicate", "..."));

// Map, add, save
var entity = new Entity { ... };
dbContext.Entities.Add(entity);
await dbContext.SaveChangesAsync(ct);
return Result.Success(entity.Id);
```

### Adding a new Update handler
```csharp
var entity = await dbContext.Entities.FirstOrDefaultAsync(e => e.Id == request.Id, ct);
if (entity is null) return Result.Failure(Error.NotFound("Entity.NotFound", "..."));

// Uniqueness: exclude self
bool nameExists = await dbContext.Entities.AnyAsync(
    e => e.Name == request.Name && e.Id != request.Id, ct);
if (nameExists) return Result.Failure(Error.Conflict("Entity.Duplicate", "..."));

// Mutate, save
entity.Name = request.Name;
await dbContext.SaveChangesAsync(ct);
return Result.Success();
```

---

## Open Questions / Things to Verify

- **`GenerateScheduleCommandHandler`**: Does it correctly handle the case where students are transferred between cohorts mid-year? The `CohortMembership` model supports it but the handler logic needs review.
- **`IsCurrent` flag on `AcademicYear`**: Is this maintained automatically or manually? Currently no logic enforces that only one year has `IsCurrent = true`. Should there be a check in `CreateAcademicYear` or `UpdateAcademicYear`?
- **`TodoDatabase` connection name**: This is the Aspire-assigned connection name for the main PostgreSQL database. It's a legacy artifact from project scaffolding. Consider renaming it in `AppHost/Program.cs` and `appsettings` to something like `"pgsh-db"` for clarity.
- **`Employee.WorkPlace`**: The enum has values `Hospital` and `Fmpr`. Is `Fmpr` still the correct name for the faculty workplace, or should it be renamed to match the actual institution name?
- **Domain events on Hospital/Center/Service**: Currently no domain events are raised on creation or modification of these entities. If downstream notifications (e.g., service capacity changes affecting rotation scheduling) are needed, events should be added.
- **`Student.Ranking`**: What is this field for? National ranking for program entry? It's nullable and has no business logic around it.
