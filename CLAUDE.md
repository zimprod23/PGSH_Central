# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**PGSH** — *Plateforme de Gestion des Stages Hospitaliers* — manages hospital internships for medical and pharmacy students (Medecine, Pharmacie, Master, Doctorat programs). It covers the full lifecycle: hospital structure, academic groups, student registrations, rotation planning, execution, attendance, and evaluation.

See [`SCHEMA.md`](SCHEMA.md) for the full database schema, [`PHASES.md`](PHASES.md) for the development roadmap, [`NOTES.md`](NOTES.md) for accumulated domain knowledge and codebase context, [`PLANNING.md`](PLANNING.md) for the crossover arithmetic and the end-to-end procedure for producing a répartition annuelle, and [`SMOKE-TEST.md`](SMOKE-TEST.md) for the manual verification pass over the most recent sessions.

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

### A block of stages runs on one axis, and the crossover is solved, not formula'd
`Stages/RotationCycle/` turns "these stages run concurrently, stage *s* for *kₛ* periods" into the matrix
`GenerateMacroPlanCommand` already consumes. It generates what used to be ticked by hand; nothing
downstream of the matrix changed.

**The arithmetic.** A partition needs `T = Σkₛ` columns to visit every stage. If `Lₛ` partitions sit in
stage *s* at once then, counting partition-columns two ways, `Lₛ·T = P·kₛ`, so:

```
T  = Σ kₛ            columns  (the shared axis, entered once)
Lₛ = P · kₛ / T      partitions concurrently in stage s — must be a whole number
```

- ⚠ **`T` is `Σkₛ`, never `partitions × k`.** Partitions do not lengthen the timeline, they subdivide who
  is where. Three stages at k=1 with six partitions is **3** columns with 2 partitions per stage — not 6.
- ⚠ **`Lₛ` integral pins `P` to a multiple of `T / gcd(kₛ)`.** Refusals name that multiple
  (`PartitionCountIncompatible`), because "wrong number" without "here is what works" is useless.
- ⚠ **A period is a *column of the axis*, and every stage carries a slot per column** — a partition takes
  a run of `kₛ` consecutive ones. Modelling a 2-period stage as one 2-column slot is still wrong, because
  the *other* stages need those columns for the crossover. Whether the group changes service between them
  is a separate question, answered by `Stage.RotationMode` — see below.
- ⚠ **Some duration mixes are impossible, not unsupported.** Stages of 2 and 1 give `T = 3`, and a
  two-column run must cover column 2 wherever it starts — so every partition is there and the other stage
  stands empty. No `P` fixes it. `RotationTiling`'s search is exhaustive, so `NoFeasibleArrangement` is a
  proof, not a timeout.
- **The arrangement is an exact cover** (`RotationTiling`), backtracking across partitions *and* columns.
  With equal `kₛ` it reproduces the cyclic Latin square the closed form used to give; unequal `kₛ` break
  that form outright, because stage boundaries of different lengths no longer line up.
- **`RotationCyclePlanner` is pure** (no DB, no clock), which is what lets the invariants be tested
  exhaustively — every partition visits every stage once, for exactly `kₛ` periods, tiling the year, with
  every stage at exactly `Lₛ` in every column.
- ⚠ **`Lₛ > 1` means those partitions are arranged in *one* call, never one each.**
  `GenerateMacroPlanCommandHandler` groups the matrix into `ConcurrencyBlock`s — same stage, same
  window — and hands all their labels to `RotationArranger` together, because the service queue is
  balanced over the cohorts of a single call. One call each balances every partition against the full
  service list in ignorance of the others, and the leftovers *stack*: `BuildServiceQueue`'s stable
  ordering gives the remainder to the same leading services every time, and every partition of a
  column carries the same rotation offset. Measured on Med5 (Gynécologie `k=3`, `L=3`, five services,
  twenty groups): three calls of 7/7/6 gave **6/5/3/3/3**, one call of 20 gives **4/4/4/4/4** — which
  is what `MED05.png` prints.
- **Authoring the axis and running the plan are two acts.** The apply writes the `StageSlot`s and
  *returns* the matrix; the caller hands it to the macro plan. Same separation as déliberation /
  réinscription, and it keeps cohort provisioning, arranging and publishing on their existing path.
- **The axis is replaced wholesale, never merged** — half-old, half-new columns are the exact
  misalignment the feature removes — and is **refused outright while any cell is published**.
- The new CNPN's 3rd year is **two blocks** (three stages per semester), not one block of six. The 6th
  year is **one** block of six with mixed durations: `k = [2,2,2,2,1,1]`, `T = 10`, `P = 10`,
  `L = [2,2,2,2,1,1]` — which is exactly the ten monthly columns of `Med6.png`. Blocks of one level
  coexist; replacement is scoped to the stages named in the command.
- **A column is stated in months, weeks or *jours ouvrables*** — `GenerateAxisWindowsQuery`, and it is
  a **server** call. Laying the axis in the browser with `setUTCMonth` was right for calendar months and
  silently wrong the moment a duration means worked days: no client has the holiday table. Months and
  weeks stay calendar-exact (a monthly axis must land on the 1st); only `WorkingDays` lays each column
  to a fixed count of worked days, and it is the **only** unit under which two columns are the same
  amount of stage — février and mars are not.

### Several périodes is not several services — `Stage.RotationMode` says which
A stage occupying `kₛ` columns can spend them moving S1 → S2 → … with an evaluation each, or standing
in **one** service for the whole run with **one** evaluation. `StageRotationMode` (`PerPeriod` default
/ `SingleService`) is the switch, and `Stage.LevelId` means a stage belongs to one promotion, so
per-stage is already per-promotion.

- ⚠ **Neither mode is the normal one.** Measured on the imported Access history 2026-08-14: 5ᵉ année is
  `SingleService` in **30,614 of 30,614** stage placements and 6ᵉ in 21,309 of 21,310, while 3ᵉ genuinely
  rotates (5,385 placements over two services, 409 over three). 5MED Gynécologie is one period of ~70
  calendar days against a catalogue of 44 j.o. — three columns, one service. The per-période rotation was
  never the general case; it is 3ᵉ and 4ᵉ année.
- **The axis is untouched.** `T = Σkₛ` belongs to the block, the group really does occupy all `kₛ`
  columns, and the cells still exist one per column — `PeriodAxis`, `GroupScheduleConflictGuard` and the
  printed répartition are unaffected. Only two things move:
  - `RotationArranger` freezes the rotation offset across the call (`runOffset`) instead of advancing it
    per column, so every cell of the run takes the same service. The phase still comes from the run's
    *first* column, so two partitions doing the stage in different windows still land differently.
  - `SchedulePublisher` collapses the run into **one** `ServicePeriod` spanning it. `StageScoring` and
    `RecomputeFinalScore` need no special case: the mean of one mark is that mark, and "every period
    validated" is that one validated.
- **A run is derived from the cells, not from the caller's window** (`SchedulePublisher.BuildStays`):
  maximal consecutive period numbers *with the same service*, per cohort. That is what makes publishing
  one concurrency block and publishing the whole stage produce the same stays. Breaking on a service
  change matters too — a cell edited by hand to another service is two stays, not one period whose
  service is a lie for half its span.
- ⚠ **A single-service stage must be arranged run by run** (`SingleServiceRunNotScoped`). Unscoped,
  "auto-arrange this stage" makes every column one run and hands a cohort one service for the whole year
  — written silently, looking exactly like a correct plan. The macro plan always scopes (a
  `ConcurrencyBlock` *is* a run), so the guard only bites the bare auto-arrange path. Non-contiguous
  windows are refused too (`SingleServiceRunNotContiguous`): a single stay cannot have a hole.
- ⚠ **The mode is frozen once the stage is published** (`RotationModeLockedByPublication`) — the periods
  on disk were shaped by it.

#### `ServicePeriodSlotCoverage` — because one period can cover several cells
`ServicePeriod.CohortSlotAssignmentId` names the **first** cell of a run. It still answers "did this come
from the grid?", which is all ~25 call sites ask. It cannot answer "is *this cell* published?" — under
`SingleService` the trailing cells of a published run would read as free, and the arranger would rewrite
them or `DeleteStageSlot` would drop a column out from under a running stage.

- One coverage row **per covered cell under both modes**, so the guards read one table, not two.
- The four callers go through `PublishedCells` (`PublishedAmongAsync`, `IsCellPublishedAsync`,
  `SlotHasPublishedCellAsync`) rather than reading the FK: `RotationArranger`, `DeleteStageSlot`,
  `ClearCohortSlotAssignment`, `ClearSlotAssignments`.
- The migration back-fills one row per existing grid-linked period — correct because nothing can have
  been published in `SingleService` mode before the mode existed.

### Undoing a publication: `unpublish → clear cells → delete slot`
The chain is right and each link is guarded, but ⚠ **deleting a `ServicePeriod` is not bookkeeping**:
`ServiceEvaluation`, `AttendanceRecord`, `PeriodPause` and `Delocalization` all **cascade** from it.

- `UnpublishCohortScheduleCommand` refuses once anything has started (`ScheduleUnderway`) and the refusal
  **names what would be lost** — periods started, marks entered, attendance days. `Force: true` is the
  caller having read that sentence; the UI shows it and asks a second time. Bulk unpublish never forces.
- Removal goes through the aggregate (`InternshipAssignment.RemovePublishedPeriods`), which recomputes
  the note and the status from what is left. Deleting the rows underneath it is what left assignments
  reading *Validated, 14.5* with nothing behind them. `RecomputeStatusFromPeriods` deliberately does
  **not** preserve terminal states the way `SyncStatusAfterReschedule` does — a verdict pronounced over
  evaluations that no longer exist is exactly what has to be walked back.
- ⚠ **Ad-hoc periods are never touched.** A period with no cell behind it is imported history, a
  délocalisation or a revalidation; none came from a répartition and none can be recreated by publishing
  one. Reported as `AdHocPeriodsKept`.

### Publishing never lands on top of a stage already served
⚠ **An assignment that already holds any `ServicePeriod` is skipped**, and the count is reported
(`SkippedAlreadyServed`). Measured 2026-08-14: every one of the 706 5MED assignments of 2025-2026 carries
an imported period per stage, while `IsPublishedAsync` only counts *grid-linked* ones — so publishing the
new répartition would have given each student a second set for the same stage, averaged into the note and
waited on by the lifecycle. Publication materialises a plan; it never re-materialises a past.

Filtered per **assignment**, not per cohort: a cohort routinely mixes students with the stage behind them
(repeaters, délocalisés) and students without, and the latter still need their schedule.

### Jours ouvrables — the calendar is entered, and half of it cannot be computed
`WorkingDayCalendar` in `Domain/Calendar/` is the single answer to "how long is this really": calendar
days minus the weekly rest days (`WorkingWeek.Moroccan` = Sat + Sun) minus every declared `Holiday`.
Pure and immutable, built once by `WorkingDayProvider`, which loads the **whole** table (~15 rows a
year — a date range would need an unknowable forward margin anyway).

- ⚠ **`Stage.DurationInDays` is already in worked days for 25 of 27 stages — measured 2026-08-13.**
  The distribution is 14×7, 22×7, 30×2, 42×3, 44×6, 66×2: 22 is a month of worked days, 44 two, 66
  three, 14 about three weeks. Only the two 30s (pharmacie officine, stage hospitalier d'initiation)
  are ambiguous — 30 worked days is six weeks, so they are most likely calendar days left over from the
  import. **Consequence: author the axis in `WorkingDays` and the catalogue durations are met exactly**
  (Med6 at 22 j.o./column gives CHIRURGIE k=2 → 44, its stated figure, while its calendar span swings
  60–67 days). Nothing is converted regardless: the calendar generates where the unit is stated **at the
  point of use**, and everywhere else it *reports*:
  `RotationCyclePreview.DurationChecks` gives each stage's worked and calendar days against its stated
  number, as a **range** (partitions take different runs of the axis) and never as a guard.
- ⚠ **National dates are law; religious dates are observation.** `MoroccanPublicHolidays.FixedFor`
  generates the ten fixed Gregorian days (and Nouvel An Amazigh only from 2024, when the décret first
  took effect). Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram and Mawlid follow the Hijri calendar, turn on
  observation of the crescent, and are announced by decree — **they cannot be generated, only entered**.
  Their absence is reported (`MissingReligious`) rather than left to surface as a stage that ran long.
- ⚠ **An empty calendar is not a neutral one.** With no holidays recorded, « jours ouvrables » quietly
  means "minus weekends" — narrower than it says, and the normal state of a fresh base. Hence
  `CalendarIsEmpty` on both the axis and the preview: a silent best effort here is a wrong end date.
- **`Holiday.IsConfirmed` is load-bearing.** A provisional lunar date still blocks its days — you plan
  on the best estimate — but every window laid over one is flagged, so the répartition can be reprinted
  when the decree lands instead of being quietly a day out. Same shape as `OutcomeSource`.
- **A holiday spans days** (`StartDate`…`EndDate`, inclusive) because the ones that matter are
  multi-day: Aïd is two, vacances are two weeks. `WorkingDaysLost` is counted against the
  *weekend-only* calendar — measured against a calendar that already contains the holiday, every
  holiday costs zero — so a férié falling on a Sunday correctly reads 0.
- **A window opens and closes on a worked day.** Asked to start on a Saturday it starts Monday, and it
  never swallows a trailing weekend, so consecutive columns cannot overlap the rest day between them.
- ⚠ **A per-service working week is deliberately not modelled.** Many services run Saturday mornings
  and a garde runs every day. This calendar answers a *planning* question about a promotion; attendance
  is recorded per day against `AttendanceRecord` and is never derived from it.
- Deleting a holiday breaks no link, but any `StageSlot` laid over it keeps dates that no longer
  reproduce from the count that produced them — `SlotsSpanning` says how many, so the confirmation can
  name the number.
- ⚠ **Moving a date is the same event as deleting it, and it is the path that actually happens** —
  the September estimate corrected the day the décret names Aïd. So `UpdateHolidayCommand` reports
  `SlotsSpanning` too, over the union of the span it **left** and the span it **arrived at**: the
  first was laid around a holiday no longer there, the second has just gained one it never counted.
  Counted once where they overlap (the usual one-day correction), and counted **before** the write.
  - `DatesMoved` gates it. Ticking « Date confirmée » on a span already right moves no day count, and
    reporting slots there teaches the user to dismiss the one report that matters.

### ⚠ Nothing declares that two stages share a period — the axis is derived
`StageSlot` is keyed `(StageId, AcademicYearId, PeriodNumber)`, so Médecine P1 and Chirurgie P1 are
independent rows with independent dates. No constraint ties them, and neither guard notices a drift:
`SlotOverlapGuard` is per-stage (which is what makes the crossover authorable), and
`GroupScheduleConflictGuard` only fires on a group actually double-booked, which a crossover never is.

- ⚠ **A small drift is more dangerous than a large one.** Where one window strictly contains another,
  `PeriodAxis` treats the outer as a composite and drops it — absorbing the mistake without trace. A
  partial overlap at least shows up as an extra column with hatched holes.
- `PeriodAxisDiagnostics` reports period numbers whose stages disagree, surfaced on the répartition
  response. It **cannot** be an error: Med6 legitimately has Chirurgie's P1 at two months and ANES REA's
  at one. Telling those apart is the human's job; showing them is ours.
- Using `RotationCycle` avoids the class entirely — the block's stages get one set of windows written
  once, so they cannot drift.
- **The axis is built from the windows the level *declares*, not from the ones something sits in** —
  `declaredSlots ∪ cells`, so a period authored but not yet arranged still gets its column and its
  hatched holes. Built from the cells alone it vanished, and an empty table was the only thing an
  admin saw after applying a rotation cycle: indistinguishable from an apply that failed.
  - ⚠ **An empty répartition has two causes and they call for opposite acts** — no periods (author an
    axis) or periods nobody is in (arrange). `RepartitionSummary.DeclaredSlotCount` is what separates
    them; `RowCount` alone collapses them. Same shape of mistake as widening on an omitted year: one
    state standing in for two.
  - The cells' own windows are unioned in rather than assumed to be a subset — a cell is tied to the
    level through its *cohort*, so a slot reached via another stage would otherwise take its column,
    and its cells, out of the table entirely.

### A roster belongs to one promotion, and its number counts within that promotion
`AcademicGroup` is keyed `(AcademicYearId, LevelId, GroupNumber)` — `IX_AcademicGroup_Year_Level_Number`,
`NULLS NOT DISTINCT`. The faculty numbers its groups per promotion and runs them at the same time: the
3rd year 1-80, the 5th year 1-60, the 6th year 1-100. A number without its promotion identifies nothing.

- ⚠ **This was the largest data defect in the base, and it emptied a répartition.** `LegacyImportPlanner`
  keyed rosters on `(ANNEE_UNIV, GROUPE_STG)` alone, folding all three numberings into one set of rows:
  measured 2026-08-13, **80 of the 100 numbered rosters of 2025-2026 carried registrations from four or
  five promotions at once**, and `LevelId` was null on all 1,003 rows. `GroupScheduleConflictGuard`
  forbids a roster from being in two services at once — correctly, on the premise that a roster is one
  set of students — so the 3rd year's April–July placements *were* the 5th year's, seven of the 5th
  year's nine columns were refused, and the printed document came out with two.
  `SplitAcademicGroupsPerLevel` splits them; the importer now keys on the promotion too.
- **« Non réparti » (`GroupNumber = 0`) is the one roster with no promotion**, by definition: it holds
  every promotion's unassigned registrations — 4,725 of them in 2025-2026 — and carries no cohorts.
  `NULLS NOT DISTINCT` is what keeps a year to one of them.
- ⚠ **Reach a promotion's rosters by `LevelId`, never by "has a registration at that level".** That
  fallback existed for legacy rows without a level; it also reaches the bucket, so cutting one level
  handed a partition label to 4,725 people. Planning paths (`AssignRotationGroups`,
  `ClearRotationGroups`, `PreviewRotationCycleQuery`) match on `LevelId` alone. `GetAcademicGroupsQuery`
  deliberately keeps the wider reach — it is the screen scolarité assigns *from*, and hiding the bucket
  behind a level filter hides the students it exists to surface.
- **Numbering restarts at 1 per promotion** (`AutoArrangeGroups`, `CreateGroup`). It used to continue
  from the year's highest number, which is why a 5th year would have printed as groups 81-140.
- **The label is keyed the same way** — `IX_AcademicGroup_Year_Level_Label`, `NULLS NOT DISTINCT`
  (`GroupLabelPerPromotion`). Held to (year, label), « Groupe 1 » — the obvious name for the 4th
  year's first roster — was already taken by the 3rd year's, so a promotion could not be *named* the
  way it is numbered and printed. A label distinguishes two rosters of one promotion and nothing more.
- ⚠ **An index makes rosters distinguishable; it cannot stop them being mixed.** Two writes point a
  row at a roster by plain FK and neither is guarded by anything downstream — every later check is
  keyed on the roster the row *claims*. Both are now refused (`AcademicGroupErrors`,
  `StageErrors.CohortPromotionMismatch`):
  - `TransferStudentCommand` — a target roster in another **year** or another **promotion**. Otherwise
    the student is affected to that roster's cohorts, i.e. stages he does not owe, and counted against
    the other promotion's service quota. This is the write that could recreate by hand what
    `SplitAcademicGroupsPerLevel` had to repair across 1,003 rows.
  - `CreateCohortCommand` — a roster paired with a stage of another promotion. `CohortProvisioner`
    always checked this on the bulk path; the hand-built path had no equivalent.
- ⚠ **« Non réparti » must never acquire a partition label or a cohorte.** Either turns the bucket
  into a roster and moves every promotion in it as one body. `AssignRotationGroups` can no longer
  reach it, but `CreateGroup`/`UpdateGroup` write `RotationGroup` directly and are refused
  (`UnassignedRosterCannotBePartitioned`), which is what lets `CohortProvisioner` match on `LevelId`
  alone — its old "or has a registration at that level" fallback matched the bucket for *every* level
  in a plan. The 12 bucket cohorts in the base are legacy-import history and are left alone; the guard
  is on creation.
- ⚠ **Only `AssignRotationGroupsCommand` cuts a promotion.** `RotationArranger` used to fall back to
  `services.Count` — the *stage's* service count — whenever no count was given and no group carried a
  label. That is not a statement about how a promotion divides, and it is sticky: Santé Publique has
  one service, so arranging it first cut the whole promotion one-way and every later stage inherited
  it, because `BuildLabels` lets an existing cut win over any requested count. The arranger now fills
  gaps in an existing cut, cuts only on an explicit count, and reports `PromotionNotPartitioned` when
  a partition is targeted on a promotion nobody has divided — rather than writing 0 cells, which
  reads as "nothing to do".
- ⚠ **…and the cut it fills gaps in is the *promotion's*, never the stage's cohorts**
  (`PromotionPartitioning`). `BuildLabels` takes "the existing partition count" from the labels it is
  shown, and a stage routinely reaches only part of its promotion — `CohortProvisioner` skips what a
  text does not require, and cohorts are provisioned stage by stage. Shown one stage's cohorts, a
  promotion cut into ten read as cut into two, and the gap-fill wrote those two onto real rosters
  permanently: measured on Med6 (2026-08-13), **A = 42, B = 42, C–J = 2 each** on a promotion re-cut
  into ten clean partitions the session before. The balance is wrong for the same reason — "fill the
  smallest partition" measured over a subset is not the promotion's smallest. The mirror case is worse
  because it is silent: a stage whose own cohorts carry no label made `alreadyCut` false, so a
  legitimate partition target was refused as *not partitioned*.
  - An arrange labels only the rosters **it is actually placing**. The count and the balance come from
    the whole promotion; the write does not, because partitioning a roster this arrange never touches
    is `AssignRotationGroupsCommand`'s act, with its own guards and its own audit entry.
- ⚠ **`LevelId` is required on both the cut and the clear** — it is the guard, not a filter. Optional,
  a year-wide call cut *every* promotion of the year in one act (each with its own partition count,
  resolved by `BuildLabels` into a single one for all of them) and reached « Non réparti », the roster
  that belongs to no promotion. The type says so now, so the compiler refuses the year-wide call and
  `?levelId=` is a required query parameter.
- **A count is not read off a page** — `GetPromotionPartitioningQuery` (`GET groups/partitioning`).
  The Plan macro tab derived its partitions, their sizes and « N groupes sans partition » from
  `GET /groups` at `pageSize: 200`; a promotion adds ~100 rosters a year, so past 200 every number on
  that tab reads low — including the one whose whole job is to say a gap-fill is owed. Raising the
  page size moves the cliff. The aggregate is computed where the rows are.

### « Retrait » is a status wearing a level's clothes — `Level.IsPromotion`
The Access base used `CODE_N = 'MED00'` to mark a **withdrawal** rather than a year of study, and
`LegacyImport.LevelMapper` deliberately kept it as a `Level` with `Year = 0` so the registration — and
the rotations already served that year — survived the import instead of being dropped.

**The data is coherent and is not to be "repaired".** All 12 registrations read `Status = Withdrawn`,
the parcours run 1ère → 2ème → 3ème → **Retrait**, 8 of the 12 carry real périodes, and two of those
students came back (Retrait 2023-24 → 5ème année 2025-26). The real year they withdrew from is
**unrecoverable**: MED00 *replaced* it in the source.

- ⚠ **What it costs is that a marker is offered wherever a promotion is.** It has no stage, no cohorte
  and nobody to rotate, but it is a `Level`, so it appeared in every picker beside « Troisième Année ».
  One of its rosters ended up carrying partition **E** — not a deliberate cut but
  `SplitAcademicGroupsPerLevel` copying the folded roster's label onto each shard.
  `CnpnTargetPlanner` had already had to special-case year 0 by hand (« année ≤ 2 » must not sweep up
  the withdrawn); `Level.IsPromotion` exists so the third such exception is not written by hand too.
- **Refused:** `AssignRotationGroupsCommand` and `AutoArrangeGroupsCommand` (`Levels.NotAPromotion`).
- ⚠ **`ClearRotationGroupsCommand` is deliberately *not* refused.** A label already on a marker's
  roster is exactly what has to come off, and refusing the undo because the state should not exist
  leaves no way to reach it but SQL. Same shape as the bucket cohorts: **the guard is on creation**.
- **Reads split on intent, not on the row.** `GetLevelsQuery.PromotionsOnly` is *off* by default: the
  student dossier, the parcours and the level catalogue all have to name a withdrawn registration's
  level. It is the screens asking « which promotion am I planning? » that pass `true`
  (`getPromotionLevels` on the frontend). A browse filter over *existing* rosters keeps the full list.

### A partition's shape is a choice, and it shows up in the published table
`PartitionAllocator` cuts a promotion into rotation partitions (`AcademicGroup.RotationGroup`). Two
strategies, both producing equal-sized partitions, and the arranger cannot tell them apart:

| `PartitionStrategy` | 8 groups, 2 partitions | printed cell |
|---|---|---|
| `Interleaved` (default) | A = 1,3,5,7 · B = 2,4,6,8 | `1, 3, 5, 7, 9…` |
| `Contiguous` | A = 1-4 · B = 5-8 | `1-40` |

- ⚠ **A partition is a fact about a *cell*, never about a row of the répartition.** `RotationGroup`
  lives on `RepartitionCell`. It sat on `RepartitionRow` — meaning "the partition its first period
  belongs to" — which is not a property of the row at all: over the year the row visits every
  partition, because that is exactly what the crossover is. The failure mode is the dangerous kind,
  plausible and self-consistent: with two partitions every Médecine row opens on A and every
  Chirurgie row on B, so the document printed **one colour per stage** under a legend reading
  « Partition A / Partition B ». A cell whose cohorts disagree carries `null`, not the first label
  found.
- ⚠ **…and the published répartition does not print it at all.** A partition is scolarité's internal
  division for building the rotation; the reader of that page is a student looking for his own group,
  to whom "Partition G" explains nothing he can act on. The document colours by **stage** — which is
  what he navigates by, is true of a whole row, and is already written in the first column, so the
  five-tint palette may safely cycle. `RepartitionCell.rotationGroup` is still sent (it is a real
  fact, and it is what explains *why* a cell holds the numbers it does) and is still shown where it
  is actionable: `ScheduleGridModal`, `AssignmentsPage`.
  - The partition palette could **not** cycle, which is how the collision was found: it wrapped at 6
    while the 5th year has 9 partitions and the 6th has 10, so A and G printed identically under a
    legend giving each its own swatch.

- ⚠ **The stripe was never designed — it falls out of balancing.** "Fill the smallest partition,
  walking groups in number order" alternates on every group, so each partition steps by the partition
  count. The step is `partitionCount`, not always 2, and `RotationArranger` defaults it to
  **`partitionCount ?? services.Count`** when nobody says otherwise.
- **Contiguity in the cell comes from contiguity in the partition.** A cell's service is
  `serviceQueue[(ci + offset) % n]` where `ci` is the cohort's index *within the partition*, and the
  queue repeats each service in a run — so consecutive `ci` share a service. Interleaved partitions
  make those consecutive indices non-consecutive group numbers, and `GroupNumberRanges` correctly
  refuses to merge across a hole. Nothing is broken; there is simply nothing to collapse.
  - ⚠ **…and it is what the printed cell costs.** A stage with few services and a whole promotion to
    place puts every group of a partition in one cell: Santé Publique in the 5th year is *one*
    service taking 6-7 groups per période. Cut contiguously that prints « 21-27 » — five characters,
    and exactly what `MED05.png` prints. Cut interleaved it cannot collapse at all and prints
    « 3, 12, 21, 30, 39, 48, 57 » — twenty-five. Both are correct plans; only the second needs three
    lines of a column. Worth saying when an admin asks why the document got hard to read.
- ⚠ **`Contiguous` interacts with the CNPN split.** `AutoArrangeGroupsCommandHandler` buckets by
  (year, level, `CnpnVersionId`) and numbers sequentially per bucket, so each text's groups occupy a
  contiguous run — a contiguous partition can therefore land entirely inside one text where an
  interleaved one mixes them. Since `CohortProvisioner` skips stages a text does not require, that
  changes which rows exist in each partition's half of the matrix.
- **A gap-fill never re-cuts.** `AssignUnlabelled` only fills `null` labels, and `BuildLabels` lets the
  *existing* partition count win over the requested one — so a re-run cannot reshuffle a plan already
  built on the current partitioning.
  - ⚠ **It needs its own control, or the only reachable path is the destructive one.** The UI showed
    the assign form *only while no label existed*, so a promotion that later grew 20 unlabelled groups
    could be repaired only by « Redécouper » — a full re-cut — with « Supprimer les partitions » next
    to it. That is how a level got cleared by a stray click. A safe act hidden behind a destructive
    one is a defect in the same way an unguarded destructive act is. Changing the cut is `Reassign: true` (`ReassignAll`), which is
  **refused outright while any cell of the promotion is published**: students have been sent there.
  Merely-planned cells are counted (`PlannedCellsAffected`) so the caller knows an arrange is owed.
- ⚠ **A wrong count can only be undone by clearing** — `ClearRotationGroupsCommand`. That
  `BuildLabels` rule means a promotion mistakenly cut into two stays two-way for every later assign
  whatever count is asked for, so "unset the partitions" is a distinct act, not a flag on assign. It
  is refused while published (`CannotClearPublished`) for the same reason a re-cut is: the printed
  répartition names the partition students were sent as, and a label nobody holds cannot reproduce it.
- **Clearing destroys nothing else, and that is provable rather than hopeful.** Nothing points at a
  label — cohorts hang off groups, cells off cohorts and slots, periods off cells — so the command
  removes no row and breaks no FK. What it costs is that the planned cells no longer describe any
  partition, which is `PlannedCellsAffected` (an arrange is owed), not data loss.

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

⚠ **« L'année en cours » is a singleton the database enforces** — `IX_AcademicYear_IsCurrent`, unique
with filter `"IsCurrent"`. `AcademicYearResolver` takes the *first* row flagged current and every
handler that omits a year gets it, so two rows flagged at once means two screens quietly disagreeing
about which promotion they show, with nothing on either to say so. `CreateAcademicYear` demotes the
others, but that is one write path guarding an invariant of the table.

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
  - ⚠ **This is the wrong shape for this faculty and should be split.** Admissibility ("this service
    does not take 1st-years") is not negotiable; a capacity target here is, because the base is
    structurally over-subscribed — measured 2026-08-14, **233 of 353 planned cells are over capacity
    (66%), worst 85 against 20**. One flag governing both means the hard rule gets switched off
    every time. Also note all 148 services carry the imported default `Capacity = 20` and **not one
    quota is authored**, so every capacity verdict today is measured against a number nobody wrote.

### A service's load is not readable one period at a time
`Services/Occupancy/` answers "what does this service actually hold, and when" — the question
`RotationArranger` could only answer as a bare count of saturated services and `SchedulePublisher`
only as a refusal, one service at a time.

- ⚠ **The timeline is segmented at every window boundary, never one row per `StageSlot`.** Nothing
  ties two stages' periods together — a slot is keyed (stage, year, number) — so Chirurgie P1 and
  ANES REA P1 have independent dates and legitimately different lengths. Per-slot rows show each
  slot's own cohorts, while the students standing there on a given morning are the union of every
  window covering that day: **the peak lives in the overlap and a per-slot list never shows it**.
  `OccupancyTimeline` is pure (like `PeriodAxis` and `RotationTiling`) so the boundary arithmetic is
  tested exhaustively; boundaries are `start` and `end + 1`, or back-to-back windows merge.
- **It measures the load exactly as the guard does** — `Cohort.Assignments.Count`, cells not
  `ServicePeriod`s (a plan is worth inspecting before it is published), date overlap and no year
  predicate, and the same `HasLevelRestrictions` branch. A page that explained a refusal with a
  number that never produced it would be worse than no page.
- The year bound is the year's **dates**, not `AcademicYearId`: two academic years never overlap on
  the calendar, so nothing is lost, and a slot stamped with the wrong year but dated inside this one
  is exactly the drift worth surfacing.
- `GetServiceStagesQuery` is the reverse of `Stage.AllowedServices`, and flags the contradiction
  neither side can see alone: the stage lists the service, the service's quotas exclude the stage's
  promotion, so auto-arrange silently drops it.
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
