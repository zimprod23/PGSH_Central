# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**PGSH** — *Plateforme de Gestion des Stages Hospitaliers* — manages hospital internships for medical and pharmacy students (Medecine, Pharmacie, Master, Doctorat programs). It covers the full lifecycle: hospital structure, academic groups, student registrations, rotation planning, execution, attendance, and evaluation.

See [`SCHEMA.md`](SCHEMA.md) for the full database schema, [`PHASES.md`](PHASES.md) for the development roadmap, [`NOTES.md`](NOTES.md) for accumulated domain knowledge and codebase context, and [`SMOKE-TEST.md`](SMOKE-TEST.md) for the manual verification pass over the most recent sessions.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build PGSH.sln

# Run the full stack (API + PostgreSQL + Keycloak + Redis + frontend via Aspire)
dotnet run --project PGSH.AppHost

# Frontend only
cd PGSH.Frontend
npm install
npm run dev       # Vite dev server, port 5173
npm run build
npm run lint

# Add a new EF Core migration (run from repo root)
dotnet ef migrations add MigrationName --project PGSH.Infrastructure --startup-project PGSH.API

# Apply migrations manually (MigrationService also runs them on Aspire startup)
dotnet ef database update --project PGSH.Infrastructure --startup-project PGSH.API
```

## Solution Structure

```
PGSH.sln
├── PGSH.AppHost/          # .NET Aspire orchestration (PostgreSQL, Keycloak, Redis, API, frontend)
├── PGSH.MigrationService/ # EF Core migration worker + Bogus data seeder; runs at Aspire startup
├── PGSH.ServiceDefaults/  # Shared Aspire config: telemetry, resilience, health checks
├── PGSH.API/              # ASP.NET Core 9 minimal API (Endpoints/, Extensions/, Middleware/)
├── PGSH.Application/      # CQRS commands & queries via MediatR
├── PGSH.Domain/           # Domain entities, value objects, enums
├── PGSH.Infrastructure/   # EF Core DbContext, Keycloak auth, authorization, migrations
├── PGSH.SharedKernel/     # Base types: Entity, Result<T>, Error, DomainEvent
└── PGSH.Frontend/         # React 19 + TypeScript + Vite + Mantine UI + Redux + Keycloak
```

## Architecture

**Clean Architecture** — Domain → SharedKernel ← Application ← Infrastructure ← API.

### CQRS / MediatR
All business logic lives in `PGSH.Application/` as commands (`*Command`) and queries (`*Query`). API endpoints dispatch them through MediatR `ISender`. Pipeline behaviors in registration order: `RequestLoggingPipelineBehavior` → `ValidationPipelineBehavior` (FluentValidation, runs validators in parallel).

### Minimal Endpoints
Every endpoint implements `IEndpoint` (defined in `PGSH.API/Endpoints/IEndpoint.cs`). `EndpointExtensions` auto-discovers all implementations via reflection. To add an endpoint, create a class implementing `IEndpoint` in `PGSH.API/Endpoints/<domain>/`.

### Result Pattern
All handlers return `Result<T>`. Endpoints map failures to HTTP problem responses via `CustomResults.Problem(result)`. Never throw exceptions for expected business failures — use `Result.Failure(Error.NotFound(...))` etc.

⚠ **`Result<T>` cannot carry a null success value.** Its implicit operator is
`value is not null ? Success(value) : Failure(Error.NullValue)`, so returning `null` from a method
declared `Result<T?>` silently produces a *failure*, not an empty success. Never model an optional
outcome that way — resolve the value only when it is actually wanted, and keep `Result<T>` non-nullable.

### Domain Events
Entities inheriting `Entity` raise events via `entity.Raise(new SomeEvent(...))`. `ApplicationDbContext.SaveChangesAsync` publishes them **after** the transaction commits (eventual consistency). Event handlers live in `PGSH.Application/<domain>/`.

### Database
- **PostgreSQL** via EF Core 9 + Npgsql. Column and table names are **PascalCase** (snake_case naming is not enabled).
- `ApplicationDbContext` is in `PGSH.Infrastructure/Database/`.
- Entity configurations (`IEntityTypeConfiguration<T>`) are organized by domain folder: `PGSH.Infrastructure/Users/`, `PGSH.Infrastructure/Hospitals/`, `PGSH.Infrastructure/Stages/`, `PGSH.Infrastructure/Registrations/`.
- The Aspire connection name is `"TodoDatabase"` (legacy name from project scaffolding — unrelated to functionality). Standalone dev reads from `appsettings.Development.json`.

### Authentication & Authorization
- **Keycloak** (realm `pgsh`, port 8082) issues JWT tokens validated by `Aspire.Keycloak.Authentication`.
- `KeycloakRoleTransformer` maps `realm_access.roles` from the JWT to standard `ClaimTypes.Role`.
- `SyncUserMiddleware` calls `UserContext.SyncAsync` on every authenticated request — links Keycloak `sub` to the local `User` record by `IdentityProviderId`, falls back to email matching. The local `User` record must exist before a login works.
- `HasPermission` attribute on endpoints uses `PermissionAuthorizationHandler`. **Current state:** role-based check only — granular per-user permissions via `PermissionProvider` are a Phase 8 stub.

### Enum Serialization
`JsonStringEnumConverter` is registered globally in `AddPresentation()`. All enums serialize/deserialize as strings in JSON (e.g., `"Pending"` not `0`).

### API Documentation
Scalar UI at `/scalar/v1`, Swagger UI at `/swagger`. Both are configured with Keycloak OAuth2 PKCE for authenticated requests in development.

## Testing — mandatory, not optional

`PGSH.Tests` (xUnit + FluentAssertions + NSubstitute + EF InMemory) is part of the definition of done.

- **Every new feature and every bug fix ships with tests, in the same change.** Treat "implement X" as
  implicitly including "and cover it". Run them green before handing back.
- Cover the happy path **plus each guard**: every `Result.Failure` a handler can return is a test case.
- Shared seeding lives in `PGSH.Tests/TestHarness.cs` (`SeedCatalog`, `SeedService`, `SeedChef`, `SeedCohort`,
  `SeedRegistration`, `SeedAssignment`, `SeedPeriod`, `SeedSlot`, `SeedSlotAssignment`, `SeedObjective`) —
  extend it rather than re-rolling in-memory boilerplate per file.
- **Drive the real lifecycle in setup.** Seeding a period as pre-closed leaves the assignment `Planned`, so it
  never reaches `Evaluated`; go through `Start()` → `CompletePeriod()` → `SubmitEvaluation()`.
- **Never encode a known bug as expected behaviour.** If a test would cement an unresolved asymmetry, leave the
  case uncovered with a comment saying why, and raise it.
- ⚠ **Known blind spot:** `UseInMemoryDatabase` ignores FK constraints, unique indexes, `OnDelete` behaviour and
  SQL translatability — constraint and query-translation defects are invisible to this suite, and authorization
  cannot be tested at all without the HTTP pipeline. Testcontainers + `WebApplicationFactory` are the agreed
  next step and are **not yet built**.

## Application Layer Conventions

### Shared helpers — always use these, never inline
- **Pagination** — `QueryableExtensions.ToPaginatedResponseAsync(pageNumber, pageSize, selector, ct)` in `Application/Extensions/`. Apply after filtering and `OrderBy`. Never manually write `CountAsync + Skip + Take + ToListAsync + new PaginatedResponse`.
  - ⚠ **Paginate by what the response *contains*, not by the handler's return type.** A single-object
    response hides unbounded collections from any `List<T>` grep: `GetGroupByIdQuery` returned one
    `GroupDetailResponse` carrying 4,725 students — one per registration in the "Non réparti" group —
    each with two correlated sub-queries, and that is what crashed the browser.
  - ⚠ **Anything scoped per academic year must filter on it server-side.** Cohorts exist per
    (stage, group) and groups per year, so an unscoped stage query returns every year it ever ran
    (681 rows for "Chirurgie"). Never fetch all years and filter in the client.
- **Academic year** — `AcademicYearResolver` in `Application/AcademicYears/`. Any handler whose result
  depends on the year resolves it through this, and takes `int? AcademicYearId` on its command/query.
  - ⚠ **An omitted year means "the current one", never "all of them".** Widening on absence is the
    defect, not the fallback: it is what made the évaluation-import canvas list 3,553 students across
    six promotions where 688 were wanted, and what let *publish / auto-arrange / start / close / pause
    / resume* reach into past years whenever they were scoped by partition label instead of by
    explicit cohort ids. A read that genuinely spans years says so some other way.
  - ⚠ **Year-stamped tables:** `AcademicGroup`, `Curriculum` and `StageSlot`. A `StageSlot` is keyed
    `(StageId, AcademicYearId, PeriodNumber)` — the same P1 exists once per promotion with its own
    dates — and `SlotOverlapGuard`'s no-two-periods-at-once rule is level-**and-year**-wide, since two
    promotions never share a student.
  - ⚠ **Do not denormalise `AcademicYearId` onto `Cohort`** to shorten
    `a.Cohort.AcademicGroup.AcademicYearId`. Measured 2026-08-08 on the worst stage (CHIRURGIE, 563
    cohorts over 6 years): the whole two-hop join is **49 of 910 shared buffers — ~5%**; the other 861
    are the nested loop into `InternshipAssignments`, which denormalising `Cohort` does not touch. The
    join was never the cost — the *missing predicate* was (3,553 rows fetched where 688 were wanted).
    Drift is not the objection (a composite FK to `AcademicGroup(Id, AcademicYearId)` would make the
    copy non-driftable); the objection is that it optimises the cheap half.

### The CNPN is a cohort's text, not a year's
`Curriculum` is keyed **(CnpnVersionId, LevelId)** — never on the academic year. Arrêté 1650.25
(BO 7422, 17 July 2025) took Médecine from 7 years to 6 from 2024-2025, while art. 2 leaves everyone
registered *before* that year under arrêté 2174.18 in its pre-2175.22 form. From 2026-2027 one
(level, year) therefore holds students of two texts, so the year cannot identify a requirement set.
- **Assignment is by first registration and sticky** — `CnpnAssignment` in `Application/Stages/Cnpn/`.
  Never by the level a student currently sits in: those agree only for students who never repeated,
  and 2,635 have. `Student.CnpnVersionId` is written solely by `Student.AssignCnpnVersion`, which
  refuses to move a confirmed stamp without `overrideExisting`.
- **Who a text binds is authored, not inferred** — `Cnpn/Targeting/`. A rule
  (`programme + année ≤ N + as-of year`) is previewed, reviewed, then frozen onto
  `Student.CnpnVersionId`. Preview and apply share `CnpnTargetPlanner`, so the dry run *is* the plan —
  the same guarantee the evaluation import makes, for the same reason.
  - ⚠ **The rule is never stored as live state.** Re-evaluated next September, "année ≤ 2" selects a
    different set of people, and the whole point of the stamp is that a student's text does not move
    under them. What survives is the membership plus the command's audit entry (`IAuditableCommand`
    records the criteria, author and date) — that is why there is no `CnpnTargetRule` entity.
  - ⚠ **A selector covers only students who already exist.** Future intakes are the version's
    `AppliesToEntrantsFromAcademicYearId`. A text needs *both* halves or next year's first-years land
    under nothing.
  - ⚠ **Bulk never moves a confirmed stamp.** Doing it wholesale is exactly how the per-student guard
    gets defeated, so a conflict is reported (`ConfirmedOnAnotherText`) and left alone. Upgrading an
    *inferred* stamp is not a move and is allowed — that is how scolarité confirms the ~2,200
    deduced assignments.
  - ⚠ **Where the rule and the arrêté disagree, the system reports and the faculty decides.**
    `EntryPredatesText` is the repeater sitting in an early level; `IncludeEntryContradictions` is
    the faculty saying yes, and it is never assumed.
- ⚠ **Entry is often unrecorded.** The legacy import only carried students once they had stages, so
  ~2,200 enrolled students have no registration before 2025-2026. `CnpnAssignment` then deduces entry
  from their level (you cannot be in year 3 without two prior years) and sets
  `CnpnAssignmentIsInferred` — surfaced for scolarité, never presented as fact.
- A version with a null `AppliesToEntrantsFromAcademicYearId` is recorded for citation and never
  selected (arrêté 2175.22 is exactly this).
- **Groups are homogeneous by CNPN.** `AutoArrangeGroupsCommandHandler` splits by
  (year, level, `CnpnVersionId`) — a group rotates through a stage set together, so two students
  owing different sets cannot share one. Unstamped students form their own bucket, labelled
  "CNPN à confirmer", rather than being folded into a text they may not follow.
- **`CohortProvisioner` will not give a group a cohort for a stage its text does not require** of
  its level. Where no requirement set is recorded for a (text, level) the check stands aside — an
  enforcing check would block all planning for six-year students, since 1650.25's requirements are
  not entered yet. Refusals are counted, not dropped (`NotRequiredByCnpn`).
- ⚠ **`Stage.LevelId` is the next thing to break.** A stage belongs to exactly one level, but the
  new text moves stages between years ("les stages du 7e glissent vers le 6e"), so one `Stage` row
  needs two levels. `CurriculumStage` already expresses `(version, level) → stage`; `Stage.LevelId`
  should become advisory. Deferred deliberately — it is the same problem as the semester gap below.
- **Recording a text** — `Cnpn/Manage/`: create, correct, delete, and « X reprend Y » (clone every
  level of one text from another in one act, skipping levels the target already has or that fall
  outside its span). Two guards worth knowing: a code is unique *per programme*, and **two texts of
  one programme cannot claim the same intake year** — version selection resolves "the latest intake
  at or before entry" and a tie has no defensible winner. `TotalYears` cannot be shortened below a
  level that already carries requirements. `AcademicProgram` is not editable: curricula and student
  stamps hang off the row.
- ⚠ **Deleting a text is gated on students, not on curricula.** `Users → CnpnVersions` is `NO ACTION`
  (a raw FK violation, i.e. a 500) and `Curriculums → CnpnVersions` is `CASCADE` (silent destruction
  of authored requirement sets). So `DeleteCnpnVersionCommand` refuses outright while any student is
  stamped — including an *inferred* stamp — and otherwise reports how many requirement sets the
  cascade took, so the confirmation can name the number. The justification for allowing the cascade
  at all is the gate: **a text nobody follows has nobody who could owe anything**, so removing its
  requirements strands no obligation. Deletion is for the mistyped row; a superseded arrêté stays,
  because the students who followed it stay.
  - The UI disables the control when `studentCount > 0` rather than letting it fail, and warns that
    removing the only text governing an intake sends new registrations to the previous one.
- ⚠ **`Stage.Coefficient` / `Stage.DurationInDays` duplicate `CurriculumStage`'s.** The catalogue
  carries its own weight *and* every text carries one for the same stage. They agree today only
  because the history reconstruction seeded one from the other; the first text that reweights a stage
  makes them disagree, and the **Stages page shows the catalogue value** — a number no CNPN
  necessarily states. The Stages page is also not year- or CNPN-scoped at all: it is the timeless
  catalogue, so switching the navbar year changes nothing there. Resolve with Phase 15.1.
- ⚠ **Still year-based, and shouldn't be:** the new CNPN organises 12 *semesters* with typed
  placements (immersion / nursing / part-time clinical / full-time / family medicine) and credits
  (10 per semester S5–S8, 20 for S9–S10, 30 for S11–S12). PGSH models year-levels and a free
  coefficient. Recording 1650.25's requirements is an approximation until that gap is closed.

### PGSH cannot know who passed the year — the faculty declares it
There is no exam, no TP, no note de module and no jury in this system; it covers stages. So
`Registration.Status` is not computed, it is **stated**: a canvas per promotion (`(year, level)`),
filled from the PV de déliberation and uploaded — `Application/Students/Registrations/Deliberation/`.
`RecordYearOutcome` is the only writer, and it records **how** the verdict was learned.

| Décision | `RegistrationStatus` | next year |
|---|---|---|
| Admis | `Validated` | niveau + 1 |
| Redoublant | `Failed` | même niveau |
| Exclu | `Excluded` | — |
| Diplômé | `Graduated` | — |
| Abandon | `Withdrawn` | — |

- ⚠ **`OutcomeSource` (`Declared` | `Inferred`) is load-bearing, not bookkeeping.** An inferred verdict
  may never overwrite a declared one — a guess that can silently replace a fact makes the whole column
  unreadable. It is also what lets Phase 14.3c back-fill the six imported years safely.
  `null` means nobody has pronounced yet, which is every legacy year.
- ⚠ **`Excluded` is not `Failed` and `Graduated` is not `Validated`.** One ends the cursus, the other
  repeats or advances. Collapsing either pair breaks the réinscription, which is the only consumer that
  has to tell them apart.
- **A contradiction against our own stage record is reported, never enforced.** An *Admis* with an
  unvalidated stage is flagged and the import proceeds: the jury rules on the whole year, we see only
  stages, and with 0 authored periods an unmarked stage is the norm. Same choice as `EntryPredatesText`.
- **Déliberation is all-or-nothing; réinscription is idempotent.** Not an inconsistency — the
  deliberation file is *not stored*, so a half-closed promotion cannot be reconstructed, while a
  rollover can simply be re-run once the odd verdicts are fixed. Keep both properties.
- **They are two acts, months apart** (July / September), and not every admis comes back. Never fuse
  them.

### The year is constitutive, not an attribute — know which side a table is on
- **Year-constituted** — `AcademicGroup`, `Cohort`, `Registration`, `Curriculum`, `StageSlot`. Remove
  the year and the row is meaningless. `AcademicGroup.AcademicYearId` being non-nullable is the schema
  already saying so.
- **Year-invariant catalog** — `Stage`, `Level`, `Service`, `Hospital`, `Center`, and the
  `Student`/`Employee` identities. "Chirurgie" and "Service de Cardiologie" outlive every promotion.

Every bug in this class lives exactly on that boundary: a year-invariant key (`stageId`, `serviceId`)
used to reach year-constituted rows. When you write `.Where(x => x.StageId == id)` against cohorts,
assignments or slots, the year predicate is not optional.

A global EF query filter was considered and rejected: of ~101 handlers touching year-constituted
tables, ~15 are *deliberately* cross-year — student parcours, level dossier, curriculum comparison,
revalidation's cross-level retake — and those are the load-bearing reads, not edge cases.
`IgnoreQueryFilters()` is also all-or-nothing, so the escape hatch would disable unrelated filters.
  - ⚠ **To show a count, ask for `pageSize: 1` and read `TotalCount`** — never fetch the rows.
- **Localization mapping** — `LocalizationMapper.FromCoordinates(x, y, z)` in `Application/Hospitals/`. Use for any Center, Hospital, or Service handler that maps GPS coordinates.
- **Per-period mark / verdict** — `StageScoring.PeriodMark` / `IsPeriodValidated` in `Domain/Stages/`. The single source of truth shared by the domain roll-up and every read handler (student record, fiche). Never recompute a mark inline.
- **Execution scoping** — `ExecutionAuthorizer` in `Application/Employees/MyServices/`. Every handler acting on a period/evaluation/attendance goes through it: `EnsureCanActOnPeriodAsync`, `EnsureCanActOnEvaluationAsync`, `EnsureCanRecordAttendanceAsync` (write), `EnsureCanReadAttendanceAsync` (read — wider, includes the owning student).
- **Period overlap** — `SlotOverlapGuard` in `Application/Stages/Slots/`. Any handler creating or moving a `StageSlot` must call it; the rule is level-wide, not per-stage.
- **Service capacity** — two numbers, never one. `ServiceOccupancyCalculator` says how many students are
  *there*; `ServiceIntakeCalculator` says how many are *allowed*. Every capacity decision compares the two.

### A service states its capacity one way, and which way depends on whether it is restricted
`Service.CapacityFor(levelId)` is the single answer; nothing outside the domain should reason about
the two fields separately.

| service | limit in force | load counted against it |
|---|---|---|
| no `ServiceLevelCapacity` rows | `Service.Capacity` | every promotion at once |
| rows, one for this level | that row's quota | **this promotion alone** |
| rows, none for this level | 0 — not admitted | — |

`ServiceLevelCapacity` is keyed `(ServiceId, LevelId)`, and a `Level` is already (programme × année),
so one key expresses "10 first-year Médecine, 15 third-year, no pharmaciens" — the last by omission.

- ⚠ **No rows means the service admits everyone.** That is not "unconfigured", it is a service nobody
  has restricted, and it is what keeps the 148 imported services plannable without a data-entry pass.
  Restriction is an act: the *first* row closes the service to every level without one. Any UI showing
  this must say so — an empty table reads as "nothing set yet" when it means "open".
- ⚠ **Quotas replace `Service.Capacity`, they do not sit under it.** On a restricted service that
  number is **dead data**: a service of 20 granting 10 and 15 will hold 25 and nothing objects. Chosen
  deliberately — the quotas *are* the statement of what the service accepts, and a second ceiling
  silently contradicting them was judged worse than the arithmetic. Consequences: no "quota exceeds
  capacity" validation (it contradicts nothing), and the service form must say the total is ignored
  once a quota exists, or admins keep tuning a number with no effect.
- **The load must be counted the way the limit is written** — per promotion against a quota, across
  all promotions against a total. Mixing them is the bug this table exists to prevent.
- **Only a restricted service can breach a quota.** On an unrestricted one the guard reports the plain
  `CapacityExceeded` — naming a quota nobody authored sends the user hunting for a rule that is not there.
- **Every capacity refusal is waived by `AllowOverCapacity`** (the publish checkbox), including
  `LevelNotAdmitted`: the whole check sits inside `if (!allowOverCapacity)`.
- `RotationArranger` drops services that refuse the stage's level *before* building the rotation, and
  weights by `CapacityFor(levelId)`: weighting by `Capacity` hands a service of 40 that accepts 5
  first-years the largest share of the first-year rotation. All refusing → `NoServicesAdmitLevel`,
  because "no services" and "no services *for you*" are different screens.
- **`AddAllowedServiceCommand` refuses a service whose quotas exclude the stage's level**, naming the
  promotions it does take. Caught when the list is authored, not weeks later when auto-arrange skips
  it silently. The Stage page's picker passes `admitsLevelId` so the option never appears.
- The level of a cell is `Cohort.Stage.LevelId` (non-nullable), not `AcademicGroup.LevelId` (nullable).
  ⚠ This inherits the `Stage.LevelId` problem noted under the CNPN: when one stage spans two levels,
  quotas will need the same rework.

### A service's chef is usually only a string in its description
The Access base named the professor as free text and nothing else — no email, no PPR — so the import
could not create an `Employee` without inventing an identity. Measured 2026-08-09: **140 of 148
services carry `Responsable (source) : Pr.A.Settaf` in `Description`, and 0 have `ServiceChefId` set.**
`ServiceChefSourceNote` in `Domain/Hospitals/` owns the format (the importer writes it, the
répartition reads it) so the two ends cannot drift.

Resolution order is authority order: the tenure open on the planning start date → the sitting chef →
the note. ⚠ Only the first is **dated**, which is what lets a répartition reprinted years later name
the chef it was published under. The note is undated and says who the legacy base last recorded, so
it is flagged (`ChefIsFromSourceNote`) rather than blended in. Linking real chefs is what upgrades
those rows; until then, printing the note beats printing nothing on 95% of the document.

### A summary response feeds the edit form, so it must carry every field that form writes back
`HospitalSummaryResponse` omitted `Description`, and the admin form dutifully sent `''` — so
**editing any hospital erased its description**. The same shape was about to eat the coordinates.
When adding a column that an admin form edits, add it to the *summary* too, or make the form load the
detail (`ServiceFormModal` does the latter, since quotas do not belong in a list row).

### Identifiers of external provenance get a format check, not a shape check
`StudentIdentifierRules.ValidCne` in `Application/Students/`, used by both the create and update
validators — a rule enforced on one path only is a student who can be created and then never saved.
The old `^[A-Z]\d{6,12}$` described the modern CNE correctly and **rejected 5,646 of the 10,204
students in the base**, so more than half of them could not be edited at all, whatever field was being
corrected: 4,695 `LEGACY-nnnnn` placeholders the Access import manufactured, 835 digits-only codes,
plus faculty codes like `22FMPR1444` and codes with an internal space (`R 13089613`). PGSH is not the
authority on the grammar of a national code; **uniqueness is the constraint that protects anything
here**, and it is enforced separately.

### Search handlers — one shape
Always `request.SearchTerm.Trim().ToLower()` and compare against `Field.ToLower().Contains(term)` for **every**
field in the predicate. A single field left un-lowered is a silent bug (`Appogee` was case-sensitive for months,
so `"ap2200a"` never found `AP2200A`). On the frontend, pair every server-querying search with
`useDebouncedValue(…, 350)`, an `isFetching` indicator, and `skip` below 2 characters.

### Store-generated keys — never pre-set them on children of a tracked parent
Assigning `Id = Guid.NewGuid()` to an entity added to an **already-tracked** aggregate makes EF classify it
`Modified` instead of `Added` → `UPDATE … WHERE Id = <new guid>` → 0 rows → `DbUpdateConcurrencyException`.
Let the store generate the key (see the comments at `InternshipAssignment.cs` `Delocalize` / `TransferToCohort`).
`dbContext.Add(root)` on a brand-new graph is safe — `Add` marks the whole graph `Added` regardless of key values.

### Shared response types — use these, never duplicate
- `UserResponse` → `Application/Users/UserResponse.cs`
- `LevelResponse` → `Application/Stages/Levels/LevelResponse.cs`

### Handler patterns
- **Existence checks**: use `AnyAsync` when you only need to verify a FK exists. Use `FirstOrDefaultAsync` when you need to modify the entity.
- **Uniqueness checks**: always exclude the current entity on updates (`c.Id != request.Id`).
- **GetMany**: always start with `AsNoTracking()`, apply filters, then `OrderBy(...).ToPaginatedResponseAsync(...)`.
- **Validators**: use `IsInEnum()` for enum parameters, not string length checks.

### Endpoint patterns
- **POST (no route id)** — bind the `Command` directly, no inner `Request` record needed:
  ```csharp
  app.MapPost("entities", async (CreateEntityCommand command, ISender sender, CancellationToken ct) => ...)
  ```
- **PUT (route id + body)** — use an inner `Request` record to merge route param with body, then construct the command:
  ```csharp
  public sealed record Request(string Name, EntityType Type, ...);
  app.MapPut("entities/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
  {
      var command = new UpdateEntityCommand(id, request.Name, request.Type, ...);
      ...
  })
  ```
- **GET list** — use `[AsParameters]` to bind the Query directly from the query string:
  ```csharp
  app.MapGet("entities", async ([AsParameters] GetEntitiesQuery query, ISender sender, CancellationToken ct) => ...)
  ```
- **Enum fields in requests** — always use the actual enum type (not `int`). `JsonStringEnumConverter` is globally registered so `"Medical"` deserializes correctly. Never cast `(ServiceType)request.ServiceType`.
- **Routes** — no leading slash. Correct: `"hospitals/{id:int}"`. Wrong: `"/hospitals/{id:int}"`.
- **Error mapping** — always use `result.Match(Results.Ok/Created/NoContent, CustomResults.Problem)`. Never return `Results.Ok` unconditionally on a command that can fail.
- **DomainException subclasses** — `GlobalExceptionHandler` catches all `DomainException` subclasses automatically via the base class. Add new exception types by inheriting `DomainException` — no handler changes needed.

## Key Design Conventions

- **NuGet versions** are centralized in `Directory.Packages.props` — never add `Version=` in `.csproj` files.
- **Implicit usings** and **nullable reference types** are enabled project-wide — don't add `using System;` etc.
- Domain entities go in `PGSH.Domain/`, grouped by domain folder. Value objects and enums alongside their entity.
- `int` PKs for reference/catalog data (Level, Stage, Cohort, Hospital, Center, Service). `Guid` PKs for operational/transactional data (Registration, InternshipAssignment, ServicePeriod, User, etc.).
- Enum-backed status fields stored as `varchar` via `.HasConversion<string>()` in EF configuration.
- CORS is open (`AllowAllForDev`) in development. Lock it down before production (see Phase 12).
- `PermissionProvider.GetForUserIdAsync` is a stub returning empty — do not rely on it until Phase 8.
- No step-comments (`// 1. Fetch`, `// 2. Validate`) — code should be self-documenting. Only add comments when the WHY is non-obvious.
