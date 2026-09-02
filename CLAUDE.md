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
  SQL translatability — constraint and query-translation defects remain invisible. **Testcontainers is still
  not built**; do not read a green suite as proof that a query runs on PostgreSQL.
  - ⚠ **It bit for real on 2026-08-26.** `CohortProvisioner` projected
    `g.Registrations.Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId).Distinct().ToList()`
    *inside* a `Select(g => new { … })`. The subquery's element is a computed value carrying no key,
    so Npgsql cannot correlate it — « Unable to translate a collection subquery in a projection… » —
    and the macro plan died on the first real request with the whole suite green. Reach for a **flat,
    top-level query** keyed on the parent id and fold in memory.
  - **Half of that hole closes without a database** — `SqlTranslationTests` +
    `TestHarness.NewNpgsqlContext()`. Translation happens when a query is *compiled*, before any
    connection opens, so a context on the Npgsql provider pointing at nothing answers
    "does this become SQL?" via `ToQueryString()`. It proves nothing about the *rows*; it does stop a
    500. Add a case whenever a query takes a shape a provider might refuse (collection subquery in a
    projection, `Distinct`/`GroupBy` over a computed element, a client-side call in a predicate).
    - **The whole macro-plan path is swept** (2026-08-26): `CohortProvisioner` →
      `StudentAffectationService` → `RotationArranger` (+ `GroupScheduleConflictGuard`,
      `ServiceOccupancyCalculator`) → `SchedulePublisher`. All twelve compile; the sweep found no
      second defect. ⚠ **`SchedulePublisher` had never executed against PostgreSQL at all** — the
      Med6 rehearsal ran `publish: false` and the base holds 0 grid-linked périodes, so the first real
      publication would have been its first run. Every query on that class is named, the per-cohort
      publish included: it shares nothing with the stage-wide one but the class, so sweeping only the
      path the macro plan takes would have left the human's own button uncovered.
    - **The CNPN area is swept too** (2026-09-01): the stamper's four reads, the effectivity
      planner's scope and detail queries, the targeting selector, and the two read screens'
      correlated `Count` projections. It had **no case at all** before, on the strength of its
      queries looking flat — which is what was believed about `CohortProvisioner`. It is also the
      least forgiving place for the mistake: the stamper runs inside the réinscription, which
      creates a whole promotion's registrations in one act.
    - **A query is testable here only if it is *named*.** Each one is an
      `internal static IQueryable<T> …Query(IApplicationDbContext, …)` beside its caller, which the
      handler then executes — the shape `CohortProvisioner.GroupTextsQuery` established. A query
      buried in a private async method cannot be compiled without running it.
    - ⚠ **A projection is not a predicate.** A client-side call in the final `Select` does **not**
      fail here — EF evaluates the top-level projection on the client by design and `ToQueryString()`
      returns SQL for it. The same call in a `Where` throws. This file catches what the provider
      *refuses*; a projection that quietly client-evaluates is a performance question and belongs to
      the half that needs a real database.

### `PGSH.Tests/Integration/` — the half of an endpoint that is not the handler
`ApiFactory` (`WebApplicationFactory<Program>`) hosts the **real** `Program.cs` in-process, so a test
reaches a route the way a browser does: routing, the required-ness of a query parameter, model
binding, authentication, `SyncUserMiddleware`, the exception handler and the `Result.Failure` →
problem-details mapping. A handler test sees none of that.

- ⚠ **A guard ordered *after* the write returns the same `Result.Failure` and passes the handler
  test.** Only the store tells the two apart, so a refusal test asserts the refusal **and** that
  nothing was written. That is the case this suite exists for.
- ⚠ **Write the control too.** A route that 400s on everything — a typo in the path, a binding failure
  — satisfies every refusal assertion and proves nothing. Pair each refusal with the request that must
  still succeed.
- Authentication is header-driven (`TestAuthHandler`: `X-Test-User`, `X-Test-Roles`), and **sending no
  header leaves the request anonymous** — a handler that always authenticates cannot tell "allowed"
  from "not checked". Roles are emitted as Keycloak's `realm_access` JSON so `KeycloakRoleTransformer`
  is exercised rather than bypassed.
- `ResetAsync()` per test, via `IAsyncLifetime`. The host and its store are shared across a class, and
  a test that writes leaves its rows behind: rows one test wrote made three unrelated tests fail when
  a guard was removed to check the suite bites, which hides which assertion actually broke.
- ⚠ **The tests now depend on `PGSH.API`, so `dotnet test` fails with MSB3021/MSB3027 while the Aspire
  stack is running** — the API holds its own `bin`. Build somewhere else instead:
  `dotnet test PGSH.Tests/PGSH.Tests.csproj -p:BaseOutputPath=<tmp>/`. Do **not** also set
  `BaseIntermediateOutputPath` — one shared `obj` across projects gives MSB4006 (circular dependency).
- **Prove a new guard test bites**: break the guard, confirm the test fails, restore it. A pipeline
  test has many ways to pass for the wrong reason.

## Application Layer Conventions

⚠ **`AsNoTracking()` on a reusable subquery reaches its host.** Tracking is a property of the whole
compiled query, so composing a marked selector into another query makes *that* query no-tracking
too. `CnpnTargetPlanner.MatchedStudentIdsQuery` is composed into the read that loads the students
the apply then mutates: marked, the apply stamped detached objects and `SaveChanges` wrote nothing —
preview right, apply reporting success, not one student moved. **A shared query states no tracking
behaviour; each caller states its own.**

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

#### What a student owes is a fact about a *registration*, not about the student
`Registration.CnpnVersionId` (+ `CnpnSource`) is the governing text for **one student, at one level,
in one year** — resolved once, when the registration is created, and never recomputed.
`Student.CnpnVersionId` remains "the text he is on now"; it is the default a new registration is
stamped from, not the answer to what any given year required.

- ⚠ **The case that forces it.** A 4ᵉ année student still owing two stages from his 3ᵉ année owes them
  under the 3ᵉ année of *his* 3ᵉ année. Reshaping that level for the promotions behind him must not
  reach back and change his debt — and with one stamp per student it did, because requirements were
  always resolved from where he stands today.
- **Read order everywhere: `r.CnpnVersionId ?? r.Student.CnpnVersionId`.** Null is not "owes nothing",
  it is "never resolved" — the six imported years were backfilled from the student's stamp
  (`Backfilled`, deliberately not `StudentStamp`: nobody was asked at the time), and ~2,200 enrolled
  students carry no stamp at all. Moved onto the registration: `CohortProvisioner`,
  `AutoArrangeGroupsCommandHandler`, `DeliberationPlanner`, `RecordRegistrationOutcomeCommand`,
  `GetStudentRegistrationsQuery`.
- ⚠ **A pronounced year freezes its text** (`Registration.CnpnFrozenByOutcome`). The verdict was
  recorded against a requirement set; moving that set afterwards leaves nobody able to say what the
  jury ruled on. There is no override — re-opening the year is the act that makes the change
  legitimate. Note the guard lives in **two** places (aggregate and `CnpnEffectivityPlanner`), and the
  planner's fires first: a test that only goes through the planner proves nothing about the aggregate.
- `DeleteCnpnVersionCommand` gates on registrations as well as students (`CannotDeleteWithRegistrations`).
  Not redundant: a text can govern a closed year of a student who has since moved on, so the student
  count reaches zero while registrations still name it — and the FK is `Restrict`, i.e. a 500.

#### « À partir de la 3ᵉ année de 2026-2027 » — `CnpnLevelEffectivity`
One authored row: **this text governs this level from this year onward, whoever is sitting in it.**
`Cnpn/Effectivity/`. The intake year on the text governs the promotion *arriving*; these govern the
promotions already in the building.

- ⚠ **No entry-based rule can express it.** After the 7→6 reduction was contested the cut actually
  applied was « la 3ᵉ année de 2026-2027 et en dessous » — two students with the *same entry year*
  land on different texts, one repeating the named level and one a year ahead of it.
- **Read once per registration, then frozen** (`RegistrationCnpnStamper`). That is what makes both
  halves true at once: the repeater re-registering gets a *new* registration, so the rule sees him
  automatically; the student who moved on is judged by the stamp on his *old* one.
- ⚠ **This is not the live-state rule `CnpnTargeting` avoids.** That objection is about re-evaluating
  an existing student's stamp — « année ≤ 2 » selects different people every September. Evaluating
  once, at creation, preserves the guarantee exactly while removing the need for somebody to remember
  to run a bulk command each year (which is what leaked on repeaters and on returners).
- **« et en dessous » is one row per level, never a stored comparison.** A comparison would have to be
  re-evaluated to be read, and a level added or renumbered later would silently change which
  promotions a published text binds. Rows say which levels were meant, forever.
- ⚠ **A rule is the one path allowed to move a *confirmed* student stamp.** It fires on one
  registration, at creation, from a rule authored for that exact (level, year) — not over a population
  re-selected each September, which is what the bulk guard exists to stop.
- **Resolution order** (`RegistrationCnpnStamper`): effectivity rule → the student's stamp → the text
  on his most recent earlier registration → `CnpnAssignment` from his intake. A registration being
  created is its own entry evidence, so a genuine new entrant needs no prior save.
- **Deleting a rule is prospective.** Nothing already stamped moves; the count is returned so the
  confirmation can name it. `ApplyCnpnEffectivityCommand` exists only for the order that actually goes
  wrong — the réinscription ran in September, the faculty settled the cut in October — and echoes back
  `ConfirmedMoveCount`, like the déliberation's `ConfirmedDefaultCount` and for the same reason.
- Uniqueness: `(CnpnVersionId, LevelId)` and `(LevelId, FromAcademicYearId)`. The second is the
  substantive one — two texts starting to govern one level in one year has no defensible winner.

#### The text is an aggregate, and what it may decide alone is the whole question
`CnpnVersion` was a property bag until 2026-09-01: no `Entity` base, public setters on every member,
and every invariant of the text living in whichever handler happened to need it. In the *same*
namespace `Curriculum : Entity` had `AddStage` / `RemoveStage` / `CopyFrom` and raised events, and
`Registration.StampCnpnVersion` / `Student.AssignCnpnVersion` were already model aggregates — the
text was the odd one out, and it had already cost the usual price.

- ⚠ **One rule was written twice, in two directions.** « Un texte ne peut pas régir un niveau
  au-delà de sa durée » lived in `CreateCnpnEffectivityCommandHandler` as `level.Year >
  version.TotalYears` **and** in `UpdateCnpnVersionCommandHandler` as `deepestEffective >
  TotalYears`, with nothing tying them. Both now come from `CnpnVersion`.
- **It carries `init` accessors over explicit backing fields**, exactly as `AcademicYear` does and
  for the same reason: the seeder, the migration and the tests still build one with an object
  initialiser, while nothing changes one *afterwards* except `Correct` / `DeclareEffectivity` /
  `WithdrawEffectivity`. `AcademicProgram` has no mutator at all — the type now says what the
  comment used to.
  - The compiler found the two places that were reaching in (`FinalYearGateTests` assigning
    `TotalYears` on a seeded text), which is the point of the change.
- **`DeclareEffectivity` / `WithdrawEffectivity` raise domain events.** Stamping a registration and
  moving a student both raised one; *the act that decides the text of every registration created at
  that level from that year on* was silent. It is the widest act in the area and the only one
  nothing could observe.
- ⚠ **Three rules stay with the handler, and the line is principled**: a code unique within a
  programme, an intake year claimed by one text, and a (level, year) a **rival** text already takes
  effect for. Those are about the *other* texts and no aggregate can see them — the same division
  `AcademicYear` makes, where « does it end before it starts » is the year's and non-overlap is the
  handler's.

⚠ **…and one rule deliberately does *not* read the aggregate's own children** — `CnpnSpanFloor`.
`Correct` is handed the deepest level year already carrying requirements and the deepest one already
governed, read from the store by the handler, rather than counting `Curricula` and
`LevelEffectivities` itself.

- **An un-Included collection is indistinguishable from an empty one.** Counted on the aggregate, a
  caller that forgot an `Include` gets « rien enregistré », the shortening goes through, and every
  requirement set below the new span is stranded — with no unique index to catch it.
- ⚠ **And this suite cannot see that mistake.** Measured 2026-09-01: deleting the `Include` from
  `UpdateCnpnVersionCommandHandler` left **all 23 `CnpnVersionManagementTests` green**, because the
  in-memory provider fixes navigations up from the change tracker. On PostgreSQL the collection
  would simply be empty. Same family as the translation blind spot: the store is asked for the fact,
  the aggregate decides what to do about it.
- `DeclareEffectivity` *does* read `LevelEffectivities` for « déjà déclaré », and that is a
  different bargain on purpose: `IX_CnpnLevelEffectivity_Version_Level` is unique, so a missed
  `Include` degrades to a constraint violation rather than to silent loss — the shape
  `Curriculum.AddStage` already had.

#### Entry is deduced once, by `EntryYearDeduction`
« On ne peut pas être en 3ᵉ année sans avoir passé deux ans » — walk back `level - 1` academic years
from the earliest registration on record. It is the **single assumption the whole backfill rests
on** (~2,200 students the legacy import caught mid-cursus), and it was written **twice**:
`CnpnAssignment.DeduceEntryYearId` and `RegistrationCnpnStamper.WalkBack`, each with its own private
`YearRef`. Pure, no store and no clock, like `PeriodAxis` / `RotationTiling` / `StagePeriodFolder`,
so the clamping cases are exact rather than approximately seeded.

- `CnpnAssignment` now answers only the half that needs the store — which text governs an intake.
  Its `ResolveAsync` is gone: it had **no production callers** (seven tests kept it alive) and its
  `asOfAcademicYearId` parameter was never read in the body while the doc comment described what it
  supposedly anchored.

#### `RegistrationCnpnStamper` returns a report, not a `Result`
It has no failure path and never had one: every refusal it can meet is a fact about *one*
registration — no text could be resolved, or the year is already pronounced — and stopping the batch
on one of them would refuse the other six hundred. They are counted into `Unresolved` /
`FrozenByOutcome` instead. The five callers were each testing `stamp.IsFailure` on a `Result` that
could not fail.

- **Assignment is by first registration and sticky** — `CnpnAssignment` in `Application/Stages/Cnpn/`.
  Never by the level a student currently sits in: those agree only for students who never repeated,
  and 2,635 have. `Student.CnpnVersionId` is written solely by `Student.AssignCnpnVersion`, which
  refuses to move a confirmed stamp without `overrideExisting` — or by an effectivity rule, above.
- **Who a text binds is authored, not inferred** — `Cnpn/Targeting/`. A rule
  (`programme + année ≤ N + as-of year`) is previewed, reviewed, then frozen onto
  `Student.CnpnVersionId`. Preview and apply share `CnpnTargetPlanner`, so the dry run *is* the plan —
  the same guarantee the evaluation import makes, for the same reason.
  - ⚠ **The rule is never stored as live state.** Re-evaluated next September, "année ≤ 2" selects a
    different set of people, and the whole point of the stamp is that a student's text does not move
    under them. What survives is the membership plus the command's audit entry (`IAuditableCommand`
    records the criteria, author and date) — that is why there is no `CnpnTargetRule` entity.
    ⚠ Do not read this as an argument against `CnpnLevelEffectivity`: what must never be re-evaluated
    is an *existing* stamp. A rule read once, at the creation of a registration, and frozen onto it
    moves nobody's text under them — which is why the two mechanisms coexist rather than compete.
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
- ⚠ **`Stage.Coefficient` / `Stage.DurationInDays` duplicate `CurriculumStage`'s — and as of
  2026-09-01 they disagree on real rows.** The catalogue carries its own weight *and* every text
  carries one for the same stage. They agreed only because the history reconstruction seeded one from
  the other, and **1650.25 is the first text to reweight a stage**: MED3 Chirurgie and Médecine read
  coefficient **3** in the catalogue and **1** in 1650.25, **30 j.o.** in the catalogue and **66** in
  2174.18's set. Neither number is wrong *of its text* — a 5ᵉ année student revalidating a 3ᵉ année
  credit is still under 2174.18, which is why the alignment migration recorded 66 there before
  overwriting the catalogue. What was wrong is that the **Stages page showed the catalogue value**
  unqualified — a number no CNPN necessarily states, with nothing on screen saying a text disagreed.
  - **Closed for the display half** (`StageCatalogueFigure`, 2026-09-01): the row now carries
    `TextFigures` — every text's own coefficient and duration — and the cell marks the figure and
    names each text only **when one disagrees**. Silent when they agree and silent when no text
    mentions the stage: a marker that fires whatever the data says is noise, and noise is dismissed,
    which puts the real one out of sight. Same rule as `ExportNotes`.
  - ⚠ **Read by a second flat query keyed on the page's stage ids**
    (`GetStagesQueryHandler.TextFiguresQuery`), never as a collection inside the row projection —
    that element carries no key and is the shape Npgsql refuses. Pinned by `SqlTranslationTests`.
  - The *substantive* half — which of the two numbers is authoritative — is still 15.1's.
  - ⚠ **…and no number is *applied* anywhere.** Measured 2026-09-01: nothing on the revalidation,
    dossier, progression or export path reads `DurationInDays` at all — neither the catalogue's nor
    the text's — **except in the revalidation dialog, closed 2026-09-01**. So the 92 6ᵉ année
    students who owe MED3 Chirurgie under 2174.18 owe **66 j.o.**, that figure is recorded and now
    visible, and no screen proposes it. The one such window on record ran **65 j.o.**, matching 66
    and not the catalogue's 30 — so the catalogue is the wrong default precisely where it would be
    reached for. When a duration is eventually proposed it must resolve through the registration's
    own text (`r.CnpnVersionId ?? r.Student.CnpnVersionId`), like every other CNPN read.
  - **`GetRevalidationContextQuery` is that read**, and it is the one place a duration is now
    resolved from the governing text. It lays the proposed window with `WorkingDayCalendar.Lay`
    and returns **null when the text states nothing** — absence is not zero, and a proposal
    invented from the catalogue would be indistinguishable from one somebody authored.
  - ⚠ **Proposed, never imposed.** The command still writes the dates it is given; a retake
    shortened by agreement stays possible. Closing the gap meant removing the *silence*, not
    adding a guard.
  - **`RevalidationPlanner` is shared by the preview and the command**, so `CanOpen` is decided by
    the rules that would refuse the act — the same guarantee `CnpnTargetPlanner` and the évaluation
    import make. A dialog offering an act the command then refuses is worse than no dialog. The Stages page is also not year- or CNPN-scoped at all: it is the timeless
  catalogue, so switching the navbar year changes nothing there. Resolve with Phase 15.1.
⚠ **Open, and deliberately parked: when a 1650.25 student starts revalidating.** Under 2174.18 the
6ᵉ *and* 7ᵉ années are stage years with no year exam, the 7ᵉ being final; under 1650.25 there is only
the 6ᵉ, and it is final. PGSH treats « dernière année » as `level.Year == TotalYears` per student
(`FinalYearTest`), which reproduces the old text's behaviour on the new one — the deliberate holding
pattern until the faculty states the rule. **Nothing hard-codes 7 anywhere**, so closing this is a
change to one test, not a hunt. Not urgent: the first 1650.25 promotion is in its 3ᵉ année, so the
question does not bite for two years. ⚠ Do not "helpfully" invent a rule here in the meantime — a
revalidation window opened under the wrong text is indistinguishable from one somebody authored.

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
  - ⚠ **Cells cascade with the slots** (`CohortSlotAssignments → StageSlots` is `CASCADE`), so both the
    preview and the apply state the count (`PlannedCells` / `PlannedCellsRemoved`). Nothing is lost that
    an arrange cannot rebuild from the returned matrix, but a destructive act nobody is shown a number
    for is one nobody agreed to — the same rule as `RostersRemoved` and `PlannedCellsAffected`.
- **Removing a block is its own act** — `DeleteRotationCycleCommand`
  (`DELETE levels/{id}/rotation-cycle?stageIds=…`). Replacing an axis is not undoing one: a block
  entered by mistake could only be written over, never taken back, short of deleting each stage's slots
  by hand from its own grid. Same shape as `ClearRotationGroupsCommand` beside « Redécouper ».
  - ⚠ **Scoped to the stages named, never to the level.** One promotion legitimately holds several
    blocks — the new CNPN's 3ᵉ année is two semesters — so a removal keyed on the level would take the
    other semester with it.
  - Refused while anything on it is published (`CannotDeletePublished`; unpublish first, that path is
    guarded and says what it costs), and `NoBlockToDelete` rather than a cheerful success on a
    promotion that never had one.
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

### A service holds who is standing in it, so the balance is per **column**
`RotationArranger` builds the capacity-weighted service queue over the cohorts of **one period**, and
indexes it by their position *within that period*. Not over the cohorts of the call: the two coincide
only when the caller scoped to a `ConcurrencyBlock`, and « auto-répartir ce stage » — every partition,
every période, one call — is a real button that does not.

- ⚠ **The crossover leaves one partition per column, and the whole partition landed in one service.**
  Every other cell of the column is refused (the group is already placed in another stage over that
  window), so a column holds `n/P` cohorts while the queue was built for `n`. Partitions are
  contiguous in the ordering and each service owns a contiguous run of the queue, so the partition
  fell *inside* one service's run. Measured on 5MED Psychiatrie 2025-2026 (60 groups, 9 partitions,
  5 services, 2026-08-18): **all nine columns went to a single service — 69 to 85 students against a
  capacity of 20 — and two of the five services were never used all year.** Reproduced exactly, 9/9,
  from `queue[(ci + phase·⌊n/T⌋) mod n]`.
- ⚠ **Nothing reported it.** 60 cells written, no failure, and the `GroupConflicts` it counted are the
  ones the crossover is made of — indistinguishable from a correct plan. The printed répartition is
  the only place it shows, which is where the user found it.
- **A column's shape cannot be improved on; which services carry the remainder can.** Seven groups
  over five services is 2,2,1,1,1 whichever way they fall — the column indexes every queue position
  exactly once, so rotating the queue cannot change the multiset. Only the *leftover* tie-break can,
  and it was stable: with equal capacities (all 148 imported services carry the same default) the
  same two leading services carried the pair in every column of the year. `BuildServiceQueue` now
  breaks ties by the column's phase.
- **The step is at least 1.** `⌊m/cycleLength⌋` is 0 whenever a column set is smaller than the cycle,
  which froze a `PerPeriod` run into one service — `SingleService` by accident.
- **`SingleService` decides once for the run**, over everyone the run touches and from its first
  phase, so a group still stands in one service for the whole run and two partitions doing the stage
  in different windows still land differently.
- ⚠ **A published cell is excluded from its column's balance rather than counted against it.** The
  free cohorts spread over every service, including one an already-published cohort is sitting in.
  Harmless today (0 grid-linked periods in the whole base) and worth closing when publication is real.

### Which stage a partition is in is authored; which service it lands in is computed
Two decisions, two owners, and confusing them is where this area goes wrong.

| decides | owner | evidence |
|---|---|---|
| which rosters travel together | `AssignRotationGroupsCommand` | 9 partitions of 6-7 rosters |
| which partition is in which stage over which columns — **the crossover** | rotation block (`RotationCycle`) / macro matrix | one stage itinerary per partition |
| which service each cohort gets inside one (stage, column) | `RotationArranger` | 7 distinct service paths inside one partition |

Measured on 5MED 2025-2026: every partition has **one** stage itinerary and **6-7** service paths.
A roster's year is its partition's; its services are not, and must not be — a partition sharing a
service is the whole-partition-in-one-service defect above.

- ⚠ **`RotationArranger` cannot invent the crossover.** It has no notion of `kₛ`, so an unscoped
  arrange writes a cell for every (cohort × column) it is not refused. On a stage nothing has crossed
  into yet that is the whole promotion in one stage all year, and every stage arranged afterwards
  gets nothing — refused now as `StageWouldFillEveryColumn`, the `PerPeriod` counterpart of
  `SingleServiceRunNotScoped`. Two conditions narrow it and both matter: it fires only on the call
  that names *neither* a partition *nor* a window (naming either is authored targeting — « A →
  Médecine P1-P2 » is the faculty's own layout), and only when another stage of the promotion
  declares the same windows (a stage that *is* the whole axis starves nobody).
- **The unscoped arrange is a fill, not a plan, and its correctness is borrowed.** Psychiatrie was
  arrangeable in one click only because the other six stages already held every group in 8 of its 9
  columns: 480 of the 540 candidate cells refused, 60 free, one per roster — no freedom over columns
  at all, only over services. Pressed first, the same button decides the year.
- ⚠ **The caller's stage order is the first partition's year.** `RotationTiling.Enumerate` walks the
  stages as given, so `schedules[0]` is that order laid end to end and partition A takes the
  lowest-index schedule that fits. Entering Gynéco(3), Neuro, ORL… puts A in Gynéco P1-3, Neuro P4,
  ORL P5 — confirmed against the applied 5MED block. Preferred, not guaranteed: where no complete
  arrangement contains it, a later schedule wins over failing.
- **A block is read back from the axis, not from the request** — `GetRotationCycleQuery`
  (`GET levels/{id}/rotation-cycle`). Stages whose slots carry the identical window list *are* a
  block, so a date corrected afterwards on one stage's own grid shows through instead of being
  papered over, and a stage that drifted correctly falls out of the block.
  - ⚠ **The axis cannot state `kₛ`** — every stage of a block carries a slot on *every* column, which
    is exactly what makes the crossover possible. Recovered in order: the apply's audit entry → the
    widest run a cohort actually holds → nothing. `RotationPeriodsSource` says which, for the same
    reason `OutcomeSource` and `CnpnSource` do: a duration deduced from an empty grid is not one
    somebody entered.
  - The audit metadata is **not** filtered by year: it carries the *request's* `AcademicYearId`,
    which is null whenever the caller left it to the resolver. The stage set is matched against the
    slots on disk instead — a stronger check, since an apply for another year cannot match a block
    that is not there.

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
- The five callers go through `PublishedCells` (`PublishedAmongAsync`, `IsCellPublishedAsync`,
  `SlotHasPublishedCellAsync`) rather than reading the FK: `RotationArranger`, `DeleteStageSlot`,
  `ClearCohortSlotAssignment`, `ClearSlotAssignments`, `RotationCycleContext`.
  - ⚠ **`RotationCycleContext` was the one that drifted**, and it is the worst place for it: it is the
    guard the rotation-cycle *apply* and *delete* stand on, so a run published under `SingleService`
    would have had its trailing columns deleted out from under it while the lead cell alone read as
    locked. `GetRotationCycleQuery` had it right from the start — the read was correct and the write
    guard was not, which is the dangerous way round. Latent only because every 6ᵉ année stage is
    `PerPeriod` and the base holds 0 grid-linked periods.
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

### Taking a plan apart: what each act is allowed to take with it
Four buttons undo planning, and they used to disagree about what "undo" means — one of them silently,
one of them not at all. `AffectationToll` / `AffectationTollReader` (`Stages/Planning/`) is the single
answer to "what is this act about to destroy", read the same way by all of them so two refusals cannot
describe the same rows differently.

⚠ **An affectation does not hang off the roster pointer.** `InternshipAssignment` is
(inscription × **cohorte**) and `ServicePeriod` hangs off the affectation, so clearing
`Registration.AcademicGroupId` leaves every one of them exactly where it was.

- **« Vider le groupe » cleared the pointer and nothing else.** The result was not an empty roster, it
  was a roster that *reads* empty: the affectations stayed in its cohortes, their périodes stayed on
  the chefs' worklists and in `ServiceOccupancyCalculator`'s counts, and the printed répartition still
  named them — against a page showing 0 étudiants, with nothing on either side saying so.
- ⚠ **…and putting the students back does not undo it.** A re-découpage sends them to *other* rosters,
  and `StudentAffectationService` dedupes on **(inscription, cohorte)** — the new cohortes are not the
  old ones, so each student comes back with a **second** affectation for the same stage. Double in the
  dossier, double against the service quota, two rows for one rotation.
- **The rule now:** nothing planned → empties silently · affectations merely planned → refused, and the
  refusal names the count (`RosterHasAffectations`); `DropAffectations: true` is the caller having read
  that sentence · anything underway → refused outright (`RosterAffectationsUnderway`).
- ⚠ **That last one is deliberately not forceable.** The act that destroys marks and attendance is
  « Dépublier », which names its cost and asks twice. A roster-side button must never become the way
  round it — same reason `AllowOverCapacity` had to stop waiving admissibility.
- **`EmptyAllYearGroupsCommand` has no `DropAffectations` at all.** A roster's affectations are a
  handful of rows an admin can be shown a number for; a year's are the whole faculty's planning, and
  destroying them is not what anybody means by « retirer les étudiants des groupes ». It refuses while
  any exist and points at the per-stage reset, where the cost is announced stage by stage.

⚠ **`DeleteCohortCommand` had no guard whatsoever**, while its bulk twin `DeleteAllCohortsCommand`
refused as soon as one affectation left `Planned`. So the *safe* act was the one touching a hundred
cohortes and the unguarded one was the button beside each line: deleting a cohorte mid-stage removed
every `ServicePeriod` — « plan-generated **and** ad-hoc » — and `ServiceEvaluation`,
`AttendanceRecord`, `PeriodPause` and `Delocalization` all cascade from those. A chef's marks and a
term of attendance, gone on one click, answered with a 204 and no number. Both now share the guard and
both return what they removed.

- ⚠ **`DeleteAllCohortsCommand`'s year was optional and unresolved**, i.e. null meant "every year this
  stage ever ran" — on the one command in this area that deletes rows, against a stage that keeps 563
  cohortes across six years. It resolves through `AcademicYearResolver` like everything else.
- **`Engaged` is read from the affectation's status as well as from the périodes.** A terminal verdict
  (`Validated`, `Rejected`) can stand over périodes since removed, and that is still not something a
  structural act may delete sideways.
- **Two flat queries, not one.** The period counts fold an aggregate over a collection navigation;
  nesting that inside a second aggregate over the affectations is the shape Npgsql refuses — the
  family that killed the macro plan. `SqlTranslationTests` pins both.

**The rotation block was already right, and for the right reason.** `RotationCycleContext` counts
*published cells* through `PublishedCells` (the coverage table, not the FK), so apply and delete are
both refused while anything on the axis is published — and a started rotation is published by
construction. Nothing was added there. ⚠ The one thing it cannot see is a cohorte served only by
**ad-hoc** périodes (imported history, délocalisations, revalidations): those hang off no cell, so they
neither block the removal nor are destroyed by it — removing slots cascades cells, never périodes.

**Order, when a promotion has to be taken apart:** dépublier (names what it costs) → réinitialiser les
cohortes du stage → vider les groupes → supprimer les groupes / supprimer le bloc de rotation. Each
step refuses until the one before it is done, and each says why in a sentence naming numbers.

### The planning grid is a matrix, and a matrix is not exempt from pagination
`GetStageScheduleQuery` returns **a page of cohorte rows plus a `StageScheduleSummary`** — never every
row. Measured 2026-08-31 on the live base: the current year's biggest stage carries **105 cohortes over
ten columns**, i.e. a thousand cells in one object and a thousand cell components mounted at once.

- ⚠ **The half that proves where the cost was: closing was slow too.** Closing does no server work at
  all, so the seconds were the browser mounting and unmounting the grid, not the query — the SQL
  behind it measures ~40 ms. Paging the rows is therefore the fix on both ends; the Mantine `Modal`
  also drops its exit transition, which kept the whole tree alive while it played.
- **The partition filter moved to the server with the paging** (`RotationGroup`). Filtering the rows
  the client happens to hold answers « aucune cohorte » for anyone sitting on page 3, and nothing
  distinguishes that from a partition nobody has cut — the same reason the chef worklist's search had
  to move.
- ⚠ **Everything the screen *states* had to move with it.** A bounded list can only describe itself,
  and the numbers beside it drive acts on the whole selection: « Publier tout (N) » fires one
  stage-wide call, so an N counted from 25 visible rows promises 25 and publishes 90. `Summary`
  carries `TotalCohorts`, `PublishedCohorts`, `ConfiguredUnpublishedCohorts`, the saturation report
  and the two derived facts below — all measured over the selection, in the store.
  - **`Partitions` is deliberately *not* narrowed by the filter.** They are the chips the user filters
    *with*; narrowed by the active filter there is no way back to the others.
  - **`Saturations` is deduplicated per (créneau, service)**, which is what makes it bounded by
    columns × services rather than by cohortes — a dozen cohortes in one over-filled service is one
    problem, not twelve. `SaturatedCellCount` stays exact when the list is capped, and the UI says
    which of the two it is printing.
  - **`OccupiedSlotIds`** (the selection) is what « nouveaux créneaux uniquement » reads: off the page
    it would call a column empty because *this page's* cohortes are not in it, and then rewrite a
    rotation already arranged.
  - **`PartitionUsage`** (the **whole** stage, unfiltered) is what warns that partition B already
    holds the columns A is about to be arranged into. That question is about the rows the filter has
    just removed, so it can only be answered server-side.
- **A cohorte carries the columns it stands in** — `CohortResponse.PeriodNumbers`. « Démarrer /
  clôturer sur P4-P6 » used to fold that out of the grid's cells, which worked only while the grid
  shipped everything; past page 1 every cohorte would have read as running in no period at all and
  been dropped from the list silently. Read by a **second flat query** keyed on the page's ids, not by
  a collection subquery in the row projection — the element would be a computed `int` with no key,
  which is the shape Npgsql refuses.

### A bulk act is one command, or it is a storm of refusals
« Publier tout » on the stage page looped: one `PublishCohortSchedule` per cohorte, sequentially, each
rebuilding the service occupancy from scratch — and since `errorMiddleware` toasts every rejected
mutation, an over-capacity plan produced **one red toast per cohorte**, arriving one at a time as the
loop ground on. It now sends `PublishStageSchedule` once, which the grid modal already did.

- ⚠ **…and one call had to stop refusing on the first cell.** `SchedulePublisher.EnsureIntakeAsync`
  now collects **every** breach and returns one refusal (`Schedule.PublishRefusedByIntake`) naming the
  count, how many of them are the unforceable admissibility half, and the heaviest three. Refusing on
  the first meant fixing a stage-wide plan one service at a time, with a full re-publish between each.
- **A single breach keeps its own sentence** (`Schedule.CapacityExceeded` /
  `LevelCapacityExceeded` / `LevelNotAdmitted`). The aggregate exists for a promotion; wrapping one
  cell in « 1 affectation dépasse… » says strictly less than the message it replaced.
- The guard still runs **before** the write, and the tests assert the store is untouched after a
  refusal — a handler test alone cannot tell a pre-check from a post-check.
- ⚠ **« Dépublier toutes » is still a client-side loop**, and has the same shape. It was left alone
  deliberately: each per-cohorte refusal *names what that cohorte would lose* (périodes démarrées,
  notes, jours de présence), and an aggregate would have to be designed before it can replace them.

### A multi-step write is one transaction, or a closed tab is a half-built plan
`IApplicationDbContext.ExecuteAtomicallyAsync` — used by `GenerateMacroPlanCommandHandler`, which
writes cohortes, then affectations and cells **stage by stage**, committing after each.

- ⚠ **The failure is the browser's, not the database's.** Closing the tab or losing the connection
  cancels the request; EF abandons the remaining steps and everything already committed stays. What is
  left is a plan built for the first three stages and nothing for the rest — not obviously broken,
  simply wrong, and indistinguishable from a plan somebody meant that way.
- **A `Result` failure rolls back too.** A refusal returned halfway through leaves exactly the same
  partial state as a dropped connection.
- ⚠ **Through `Database.CreateExecutionStrategy()`, never straight to `BeginTransaction`.** Aspire's
  `AddNpgsqlDbContext` enables retry-on-failure, and a retrying strategy refuses a user-initiated
  transaction outright — which would turn every wrapped handler into a 500. `ChangeTracker.Clear()`
  opens each attempt, or a retry inserts the failed attempt's entities twice.
- ⚠ **Domain events still publish from each inner `SaveChangesAsync`, i.e. before the outer commit.**
  Nothing on the macro-plan path raises one (the entities are built with object initialisers), but a
  handler wrapped here must raise no event whose handler assumes the write is durable.
- ⚠ **The in-memory provider has no transactions**, so `TestHarness` and `ApiFactory` ignore
  `InMemoryEventId.TransactionIgnoredWarning` and the call becomes a no-op. That is the honest reading:
  **this suite cannot prove atomicity**, exactly as it cannot prove a FK or an `OnDelete`. It proves
  the steps inside the unit of work.

### Read every cohorte's candidates once, not once per cohorte
`StudentAffectationService.AssignAsync` issued **one `Registrations` query per cohorte**. The macro
plan walks that loop once per concurrency block, so a single « Générer le plan » on a promotion of 105
rosters over seven stages was ~700 round trips. It is now one read for the call, keyed on
**(roster, niveau) together**.

- ⚠ **The pair is the guard.** Keyed on either half alone, a student registered in this roster at
  another level is affected to a stage he does not owe — the same trap as filtering by level and year
  with two independent `Any`s. Measured server-side on the live base: 92 ms of loop against 4 ms
  batched for one stage, before EF and the network are counted.
- **A long write still needs to say it is long.** With the round trips gone the run is seconds rather
  than tens of them, and the rotation-cycle page states what is being written and that interrupting
  costs the run but damages nothing — which is only truthful because of the transaction above.

### Publishing never lands on top of a stage already served
⚠ **An assignment that already holds any `ServicePeriod` is skipped**, and the count is reported
(`SkippedAlreadyServed`). Measured 2026-08-14: every one of the 706 5MED assignments of 2025-2026 carries
an imported period per stage, while `IsPublishedAsync` only counts *grid-linked* ones — so publishing the
new répartition would have given each student a second set for the same stage, averaged into the note and
waited on by the lifecycle. Publication materialises a plan; it never re-materialises a past.

Filtered per **assignment**, not per cohort: a cohort routinely mixes students with the stage behind them
(repeaters, délocalisés) and students without, and the latter still need their schedule.

### What a chef sees is a slice of his services, and the axis is the period's lifecycle
`Employees/MyServices/`, sliced by `ServicePeriodState` — the domain's own four-way split (above),
not a vocabulary invented for this screen. The four **partition** the periods of a service: every row
falls in exactly one, the counts add up to the whole, and one page of one slice is what the endpoint
returns.

- ⚠ **A published rotation is `IsStarted = false`, and the worklist only ever showed started rows.**
  So "the schedule is published" and "there is no schedule" looked identical from the one screen that
  has to tell them apart: 4MED Pédiatrie 2026-2027 published 898 périodes into six services and its
  chef was shown nothing at all. `Planned` makes them visible; it does not make them actionable
  (`ServicePeriodResponse.State` is what the UI refuses to draw a button on). **Starting is still an
  administrative act** — « Démarrer les affectations » — exactly as closing is.
- ⚠ **The list must be bounded, and the year is the wrong axis to bound it on.** Measured 2026-08-29,
  one chef's two services held **3 220 periods back to 2019**, returned unpaginated and mounted at
  once, which is what took the browser down. Year scoping was tried twice as the fix and blanked live
  worklists both times — an `AcademicYear` record drifts out of step with the dates rotations really
  run on. `IsStarted` / `IsComplete` / "has an evaluation" are facts about the rotation itself, so
  bounding on them can hide nothing that is live. The same 3 220 split 300 / 0 / 683 / 2 237, and only
  the last is an archive.
- **The year narrows on top of that, defaults to the current one, and is never silent**
  (2026-08-30). The state is what makes the list finite; the year is a second axis the chef drives,
  because « quelle année ? » is a real question on every slice — next year's plan, this year's
  evaluations, last year's marks — and not only on the archive.
  - ⚠ **What makes it safe is `OutsideYearCount`, not restraint.** Both previous incidents were
    silent: rows the filter removed left nothing behind to say they had existed, so an empty screen
    and an empty service were indistinguishable. The response now says how many further periods
    *of the slice being shown* the year is holding back, the UI states it, and `AllYears` is one
    click away. A filter that announces what it removed cannot reproduce that failure; a filter kept
    away from live work only postpones it.
  - ⚠ **The year is *read*, never inferred from dates** — `p.InternshipAssignment.Registration.
    AcademicYearId`. The schema already states it and states it **totally**:
    `ServicePeriods.InternshipAssignmentId`, `InternshipAssignments.RegistrationId` and
    `Registrations.AcademicYearId` are all `NOT NULL`, the last behind a `RESTRICT` FK. So the years
    **partition** the périodes by construction — no row outside every year, none in two — which is
    the property a date comparison has to approximate and gets wrong.
  - **Measured 2026-08-30, the two rules disagree on 7 030 of 105 626 périodes (6.7%), and the
    registration is right every time.** 5 043 are 2019-2020 stages that ran into 2020-2021 because
    the year was postponed; 1 841 are 2024-2025 stages finishing after 31 août; 145 the same for
    2023-2024. A date rule cannot tell *a year that ran late* from *the next year's work* — it has
    no fact to tell them apart with, and the registration is that fact.
  - ⚠ **This is what the reported defect actually was.** 41 6ᵉ année Pédiatrie périodes, registered
    2025-2026 and run 08 jul → 08 sep 2026, appeared under 2026-2027 « à évaluer » for a promotion
    with **no** partitioning, no planning and nothing published that year — because a date predicate
    saw them finish eight days into the new one. Their registration always said 2025-2026. Reading it
    does not *fix* the case so much as make it unrepresentable.
  - **Every path that creates a `ServicePeriod` hangs it off the right year already**, which is why
    the read needs no fallback: `SchedulePublisher` and `LateArrivalScheduler` use the registration
    being planned, `RevalidateStageCommand` opens the retake on *the registration the student holds
    now*, and `Delocalize` / `TransferToCohort` keep the assignment they were given. The old worry —
    "a retake carries an old year" — is not a thing the code can produce.
  - ⚠ **Anything unresolvable widens, never empties**: no row flagged `IsCurrent`, or a year id that
    no longer exists, spans every year. That is why the window is resolved here rather than through
    `AcademicYearResolver`, whose contract is to *fail* when no year can be named — right for a
    handler that writes, wrong for the one read that has to survive it. Showing too much is visible
    and recoverable in a click; showing nothing is neither.
  - `AllYears` is the explicit widening the "omitted year means the current one" rule demands, and it
    wins over an explicit `AcademicYearId`: the two together can only come from a caller that has
    just changed its mind.
- **One predicate, `ScopedQuery`, answers both the page and the four counts**, so a badge and its list
  cannot disagree — and the state half of it is `ServicePeriodLifecycle`'s, not this handler's. It is
  `internal static` and named for the usual reason: a query buried in a private async method cannot be
  handed to `ToQueryString()`, and the in-memory provider translates nothing.
- ⚠ **The counts travel with every page, including an empty one.** A bounded list has a failure mode
  the unbounded one did not — landing on an empty slice reads exactly like "this chef has no work" —
  so the client opens on the first slice that has something in it and says where the rest is.
- ⚠ **The search had to move to the server with the pagination.** Filtering the rows the client
  happens to hold answers « aucun étudiant » for anyone sitting on page 3, and nothing distinguishes
  that from a real absence. It narrows the counts too, which turns the badges into an answer to
  "where is this student?" across the four slices.
- ⚠ **`ToPaginatedResponseAsync` clamps a page size of 0 *upward* to 1.** So `?pageSize=0` would
  answer a 50-student window with one student and nothing anywhere saying so; the query resolves a
  non-positive value as "unstated" (`EffectivePageSize`). `[AsParameters]` *does* honour a declared
  default on .NET 9 — measured, and pinned by `ChefWorklistEndpointTests` — but the fallback is the
  handler's own so it does not depend on that, nor on the order the enum is written in.
- **A `SingleService` stage already gives one evaluation for the whole run.** `SchedulePublisher`
  collapses the `kₛ` cells into one `ServicePeriod` spanning them, so the chef sees one row per
  student and marks it once. Verified on the live base 2026-08-29: 4MED Pédiatrie is 898 périodes for
  898 assignments across three windows, and all five 4MED stages are `SingleService`. Nothing extra is
  needed for that promotion; a `PerPeriod` stage genuinely does want one evaluation per column.
- ⚠ **`EmployeeDashboardPage` counted the same way and was worse**: it fetched every *closed* period
  of each chef service — 2 920 rows — to render one number, on the landing page. It reads
  `counts.awaitingEvaluation` now, and — since the worklist defaults to the current year — that badge
  and the list it leads to name the same set. The rule from `PGSH.Frontend/CLAUDE.md` applies
  unchanged: to show a count, ask the server for it.

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

#### The canvas is a list of exceptions, and silence is the verdict
A PV names the students the year went badly for; it does not restate 641 admissions. So
`DeliberationScope.DefaultUnlistedToAdmis` inverts what the file is — everyone not named is **Admis** —
and the scope is the **academic year**, with `LevelId` narrowing it to one promotion rather than
defining it.

- ⚠ **Year-wide is safe here, and it is not the widening-on-absence defect.** The *year* is still
  resolved to exactly one; a student holds at most one registration per year (unique index), so a CNE
  is as unambiguous across every level of one year as within one promotion. Matching across **years**
  is what makes a row ambiguous, and that remains impossible.
- ⚠ **The default promotes; it never graduates** — and "is this his last year?" is asked per
  *student*, from his own `CnpnVersion.TotalYears`, never per level. From 2026-2027 one 6ᵉ année
  Médecine holds both students whose text ends there (1650.25) and students who go on to a 7ᵉ
  (2174.18), so the level alone cannot answer it. Anyone who **may** be in his last year is counted
  (`FinalYearUndecided`) and left untouched.
  - **Why, measured 2026-08-18 on the real base:** *855 of the 1 657* students in 7ᵉ année Médecine had
    been in the 7ᵉ année before — 132 of them four times — and 74 of 356 in 6ᵉ Pharmacie. The final
    year is the **thesis year**: students sit in it until they defend, PGSH holds no record of a
    defence, and so "still there" and "finished" are *both* ordinary. Reading silence as diplômé
    graduated **~930 people who were simply still enrolled**. An exceptions file only works where the
    exception is rare; in a final year that is reversed, so the rule inverts there.
  - The faculty names its graduates instead — the **defence roll** is the document it actually holds,
    and a row reading `Diplômé` still records one.
  - ⚠ **Naming a « Diplômé » is stricter than being left undecided, and the two tests differ on
    purpose.** A row is refused (`NotAFinalYear`) unless `level.Year == TotalYears`, while
    `MayBeAFinalYear` stands aside on `>=`. So a registration sitting *above* its text's span can
    neither be promoted by silence nor graduated by name — measured 2026-08-29, the base holds **6**
    of them (5 in 7ᵉ année Médecine stamped `PHARM-LEGACY`, 1 in « Interne CHU »). Any generated PV
    must emit « Diplômé » on `==` only: one such row refuses the whole file.
  - ⚠ This is also why an absent CNPN stamp needs no special case any more. It used to *block* the
    file; now nobody in a possible final year is decided for either way, so the unstamped student
    falls into the same bucket and `DefaultIssues` is gone entirely.
- ⚠ **The default never overwrites a verdict already recorded** — not even an inferred one. Otherwise
  re-uploading last week's exceptions file, after twelve verdicts were corrected by hand, silently
  flips all twelve back to admis. It is also what makes the import safely re-runnable, the way the
  réinscription is. Changing a recorded verdict is explicit: name the student, or use the single-row
  path below.
- ⚠ **`Level.IsPromotion` is checked here too.** « Retrait » has no year to clear, and a year-wide
  default would otherwise promote the withdrawn.
- ⚠ **A boolean confirmation would not do.** The whole risk is the student nobody named, and a
  registration created *between the preview and the apply* adds one. `ApplyDeliberationCommand.
  ConfirmedDefaultCount` carries the number the operator was shown and refuses on a mismatch
  (`DefaultsNotConfirmed`). All-or-nothing still holds: a mistyped CNE means the student it was meant
  for is admitted by silence, so one bad row refuses the file.
- **Reports are bounded.** A year-wide file is whatever was uploaded and the reply is a single object —
  the exact shape that hides an unbounded collection. `Rows` is capped (`MaxReportedRows`) with
  `RowsTruncated`; the counts stay exact, and `ByLevel` is one entry per level. The réinscription orders
  its rows **attention-first** for the same reason: the cap must never hide a row somebody has to act on.

#### One student at a time — because a promotion's file cannot be the only way in
`Registrations/Outcome/` — `RecordRegistrationOutcomeCommand`, `ReopenRegistrationYearCommand`
(`POST registrations/{id}/outcome[/reopen]`). A late jury, a PV corrected for one name, an abandon
notified in November. Under an exceptions file this path is *required*: re-uploading the promotion's
file is precisely what must not be needed to fix one row.

- ⚠ **`UpdateRegistrationCommand` used to write `Status` directly**, leaving `OutcomeSource` null — so
  the edit form showed « Admis » while the réinscription reported « aucune décision enregistrée » and
  refused to carry the student over. Neither was wrong about what it read. `Student.UpdateRegistration`
  now routes a year-outcome status through `RecordYearOutcome` and a return to `Active`/`Pending`
  through `ReopenYear`.
- ⚠ **Reopening does not undo what the verdict caused.** The réinscription may already have created
  next year's registration, and that row can carry a group, cohorts and published périodes. It is
  **reported** (`LaterRegistrationExists`), never deleted — deleting it would take a student's
  rotations with it.
- The single-row path stands aside on an absent CNPN stamp exactly as the canvas does: one student at a
  time must not be stricter than five hundred at once.

#### Joining a roster is not transferring between two
`AcademicGroups/Join/` — `AssignStudentToGroupCommand` (`POST groups/assign-student`). The ordinary
September case: the déliberation is applied, the groups are cut, the schedule is published, and then
somebody registers.

- ⚠ **The transfer path silently did nothing for him.** Every step of `TransferStudentCommand` filters
  on assignments the newcomer does not have, so he landed on the roster with no cohorte and no période
   — a student the planning had never heard of, in a group that looked correct. Refused now
  (`AlreadyInAGroup` guards the mirror case), because the two acts differ in what they must carry.
- **Only windows that have not closed are materialised** (`LateArrivalScheduler`). A stage the roster
  finished in October gives him an `InternshipAssignment` — he owes it, and it shows unserved on his
  dossier — but no `ServicePeriod` claims he stood in a service on days he was not enrolled, and the
  count is reported (`StagesAlreadyOver`) so somebody decides between a délocalisation, a revalidation
  and next year. This is the opposite choice from `MaterializeAtTargetAsync`, which *does* materialise
  closed cells — rightly, since a transferred student really did serve them, with another group.
- A period is never back-dated before the day he joined, and a cell the roster has already **started**
  (read from its périodes, not from the calendar) gives him a started one, so he appears on the chef's
  screen the same day.

#### The fourth shape — the faculty's own roll, which is acts 1 and 2 at once
`Students/Registrations/ReinscriptionSheet/` — `POST reinscription/sheet[/preview]`. One spreadsheet,
one line per student: `Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027`. Those two étapes carry
the verdict with them, so a single upload records the decision on the year closing **and** creates the
registration for the year opening.

**It exists because it is what actually arrives.** `Reinscription/` *derives* next year from verdicts
already recorded (admis → niveau + 1); this one is handed the answer. Deriving a second time would
disagree with the file on 804 of its 6 862 lines.

- ⚠ **`Code` is the numéro Apogée**, not the CNE — the file has no CNE column at all, which is one
  more reason `Student.CNE` may be absent. Measured against the live base 2026-09-01: **6 813 of the
  6 862 codes match a student exactly, none is duplicated, and all 6 810 rows whose student holds a
  2025-2026 registration agree with it about the level.** The strictness below costs nothing.
- ⚠ **Silence is not a verdict here, and that inverts the déliberation.** That canvas is a list of
  *exceptions*, so a student it does not name is admis. This is the roll of who **is** coming back, so
  a student it does not name is not — a graduate, an exclusion, an abandon — and PGSH cannot tell
  those apart. Nothing is written for them; `NotCovered` reports the number (1 216 for 2026-2027, of
  which 999 are 7ᵉ année Médecine and 211 are 6ᵉ année Pharmacie, i.e. the thesis years).
- ⚠ **A level that has not moved is not always a redoublement.** In a final year it is the thesis
  still being written, which is as ordinary as finishing. Recording `Failed` there would be wrong
  twice: it is not a failure, and `RegistrationStatus.AnnulsItsStages` would **wipe the year's stage
  record** for 804 students. `DeriveOutcome` writes no verdict on a final-year repeat, on a
  réorientation (comparing a 3ᵉ année Médecine with a 1ʳᵉ année Pharmacie compares nothing), or where
  the closing year holds no registration. `WillRecordOutcome` is therefore deliberately smaller than
  `WillRegister`, and the UI says why — an operator expecting one verdict per registration would read
  the gap as rows silently dropped.
- **`OutcomeSource` is `Declared`.** This is the faculty's own document stating where each student is
  registered next year, not PGSH reading an enrolment sequence. Getting it backwards makes the whole
  column unreadable — `Inferred` may never overwrite `Declared`.
- **All-or-nothing on the errors, idempotent on everything else.** The line is whether a row is
  *wrong* or merely *not actionable*: a duplicated code or a level contradicting the registration on
  record refuses the whole file, because the write it produces is a verdict on somebody's year and
  nothing puts that back. An unknown student, a master's programme, a student already rolled over are
  skipped and counted, so the file can be re-sent once the missing students are inscribed.
- ⚠ **No `ConfirmedRowCount`, and the absence is the point.** The déliberation confirms a number
  because it writes a verdict onto students *nobody named*; this file names every student it touches,
  so a registration created between preview and apply is simply not in it. A number here would be
  ceremony, not a guard.
- **The final-year gate is the same one** — `FinalYearGuard.EnsureMayEnterManyAsync`, called once per
  destination level rather than once per row (four round-trips each would be ~27 000 for this file).
  A blocked row is a skip, named with what he owes.
- **There is no template route**, deliberately: the other three canvases are documents PGSH hands out
  and gets back, and generating a rival version of the faculty's own only invites the two to drift.
  The parser accommodates it instead — headers matched without accents or casing, the two « Etape »
  columns found by prefix and taken **in sheet order** because their year suffix changes every
  September.
- ⚠ **`Code` arrives as an Excel *number*.** Read through `GetString()` it can come back as
  `2.4008386E7`; every row would then match no `Appogee`, read as an unknown student — which is a
  *skip* — so the file would apply cleanly and do nothing.

##### …and an absence decides exactly one thing
A registration of the closing year the file does not mention belongs to somebody who is not coming
back. **In the last year of his own text that is a defence**, so the year is recorded « Diplômé ».
Anywhere else it is not decidable — abandon, exclusion, or a réinscription that has not arrived — and
nothing is written. Measured on the 2026-2027 roll: **1 006 absent in 7ᵉ année Médecine and 212 in 6ᵉ
année Pharmacie**, against **47** absent below a final year.

- **`Inferred`, never `Declared`.** Nobody named these students on a document; PGSH read an absence.
  That also makes the correction free: a real defence roll is `Declared`, and `Declared` overwrites
  `Inferred` while the reverse is refused.
- ⚠ **`FinalYearTest.IsExactlyFinal` is stricter than the déliberation's own « Diplômé » check, in
  two ways.** It compares with `==` rather than `>=`, which keeps out the 6 registrations sitting
  *above* their text's span; and it refuses to answer without a text, where the déliberation stands
  aside and lets « Diplômé » through. The difference is **who spoke**: the faculty naming a student
  may override PGSH's ignorance, an absence may not.
- ⚠ **This is what brought `ConfirmedGraduationCount` back.** The act needed no confirmation number
  while every write landed on a student the file names; a graduation lands on one it does **not**, so
  a registration created between the preview and the apply would have its cursus ended by a
  confirmation nobody gave for it — exactly `ApplyDeliberationCommand.ConfirmedDefaultCount`'s case.

#### The faculty's level codes are a table, not a column — `FacultyLevelCodes`
`Application/Stages/Levels/`. `MED04`, `MDME3`, `MDPH06` → the `Level` they name, resolved to
`(Year, AcademicProgram)` because `IX_Level_Year_Program` is unique and a label is not.

- **The mapping is many-to-one and has been since 2025-2026.** The faculty is renaming its codes one
  promotion at a time as each cohort moves up, so `MED01` and `MDME1` are the *same* first year under
  two names. In 2026-2027 the third year is `MED03` for the students repeating it and `MDME3` for the
  ones arriving. A code column on `Level` could not hold both, and the rename is vocabulary, not
  structure. **`MDME3` and `MPHAR3` are new in the 2026-2027 roll and appear in no legacy row.**
- ⚠ **Codes PGSH knowingly does not manage are listed too** (`OutsideScope` — the `MMBTM` masters).
  That is the whole reason it is a table: an importer must tell « a programme we do not cover » from
  « a code nobody has told us about ». The first is skipped and counted, the second refuses the file,
  because a mistyped code silently dropped is a student who quietly does not get re-registered.
- `LegacyImport.LevelMapper` reads this rather than carrying its own copy. Two tables for one
  vocabulary is how a promotion gets imported at one level and re-registered at another.

#### « Peut-être sa dernière année ? » is one rule — `FinalYearTest`
`Application/Stages/Progression/`. Pure, and shared by the déliberation's default and the
réinscription roll's verdict derivation. **It is a question, not a guard**: `FinalYearGuard` decides
whether somebody may *enter* a final year, this only says whether a year *might be* one — and every
caller responds to a yes by writing **nothing**. Two copies would disagree about 804 students in the
2026-2027 roll alone.

#### The third act — inscription, for the people neither of the other two can see
`Students/Registrations/Inscription/` — `GET inscription/template`, `POST inscription/preview`,
`POST inscription`. The déliberation writes verdicts onto the closing year's registrations; the
réinscription reads those verdicts and creates the next year's. **Both begin from a registration the
student already holds**, which is precisely why neither can reach the September intake, a transfer
arriving from another faculty, a student coming back after an absence, or a réorientation. They hold
no registration to be read, and before this there was no bulk path that created a `Student` at all
(`Students/CreateMany/` was an empty directory; `CreateManyRegistrationsCommand` takes ids).

- **Four writing actions, and they partition on two questions** — does PGSH already hold this person,
  and is he entering the programme he was already in: `NewEntrant` (unknown, level 1) · `TransferIn`
  (unknown, above level 1) · `Returning` (known, no registration this year, same programme) ·
  `ProgrammeChange` (known, the level belongs to another programme).
  - ⚠ **« Sous convention » is not a fifth.** `Student.AgreementType` says how a place is funded, and
    an étudiant sous convention is any of the four — a first-year under an agreement is a
    `NewEntrant`, one arriving in 3ᵉ année a `TransferIn`. Made a kind it would overlap the others and
    the counts would stop adding up. It is a column any row may carry. Nor is « redoublant » one: he
    is carried by the réinscription from his own verdict.
- ⚠ **`LevelId` is required, and it is the guard.** The déliberation may omit it because the year
  makes an identifier unambiguous on its own; here nobody on the sheet holds a registration the level
  could be read from, so it has to be stated. Refused outright on a non-promotion
  (`NotAPromotion`) — « Retrait » has no stages and nobody to rotate.
- ⚠ **`AlreadyRegistered` is a skip, not an error** — the opposite of the déliberation, and for a
  stated reason: **this act creates identities**, so the file has to survive being re-sent with the
  late arrivals appended. The déliberation's file is not stored and cannot be re-sent; a rollover, and
  this, can. Everything else refuses the whole file.
- ⚠ **`ConfirmedStudentCount` is a number, never a checkbox**, and the stake is higher than the
  déliberation's `ConfirmedDefaultCount`. A student row is an *identity* — a CNE, a numéro Apogée, an
  address `SyncUserMiddleware` matches a Keycloak login against — and nothing puts a wrongly-created
  promotion back.

##### What a transfer owes is a fact nobody can reconstruct later — `PriorEnrolment`
One row per entry registration: institution, country, **last level year completed there**, the
équivalence reference, the date. Same shape as `FinalYearEntryWaiver` — a required reference and a
snapshot — because a decision that cannot say what it recognised is not a record.

- ⚠ **Required above the first year for a student PGSH has never seen** (`OriginRequired`), and it is
  not bookkeeping. Today « owed » is *every attempt came back NonValidé*, so a transfer with no
  attempt owes nothing and `FinalYearGuard` stands aside. That reading holds only while the definition
  is negative: **the day « owed » widens to the CNPN's requirement set** — the stated plan once
  1650.25's sets are entered — **a student transferred into 5ᵉ année owes every stage of the four
  years he did elsewhere.** `LastLevelYearCompleted` is the boundary that widening must not look
  below, and it cannot be reconstructed from anything else PGSH holds. It has to exist first.
- **No stages are invented.** Materialising validated `InternshipAssignment`s for the years done
  elsewhere would make the dossier look complete at the price of rows nobody served — which every
  count, mean, chef worklist and occupancy figure would then have to learn to exclude.
- All three of establishment, last year and reference are needed **together**; two of the three is
  refused rather than silently dropped.

##### Which identifiers name a student, and which only corroborate
`CNE` and `Appogee` **identify**; `CIN` and `Email` corroborate. All four are unique on `Students`,
but only the first two are what a row is understood to name.

- ⚠ **A row whose CNE is unknown while its e-mail belongs to somebody is a mistyped cell, not that
  person registering.** Matched on the e-mail it silently gives an existing student a registration
  under a newcomer's name; treated as a newcomer it violates the unique index at `SaveChanges` with
  nothing actionable in the message. It is neither — it is `IdentifierConflict`.
- ⚠ **In-file duplication is checked on *every* identifier the row carries, plus the student it
  resolved to** — not on the first one present. `IX_Registration_Student_Year` is unique, so one
  person on two lines is a raw constraint violation, i.e. a 500; and two lines for one new person,
  one written with his CNE and one with his Apogée, pass any check keyed on a single column.

##### Manufactured identifiers are never silent, and must survive the edit form
- ⚠ **`CNE` *and* `Appogee` are both NOT NULL UNIQUE.** `IX_Student_Appogee` carries a
  « WHERE Appogee IS NOT NULL » filter that reads as though absence were allowed — the column is
  required, so the filter can never be false, `""` is a *value*, and the second student without an
  Apogée collides with the first. A row must carry one of the two; whichever is missing is built from
  the other (`SANS-CNE-…` / `SANS-APOGEE-…`, prefixed so it is readable as provisional) and the row
  says so.
- ⚠ **A manufactured CNE is checked against `StudentIdentifierRules.IsValidCne` before it is
  written.** A validator describes what a *save* must satisfy, so a code the pattern rejects makes the
  student read-only the day somebody opens his file — the refusal naming a field nobody was editing.
  The prefix costs 9 of the 20 characters allowed, so a long Apogée really does overflow it. Refusing
  at creation is the cheap end of the same failure that made 5 646 students unsaveable once already.
- ⚠ **An e-mail is a login.** `Users.Email` is NOT NULL UNIQUE and an intake list routinely has no
  address column, so one is generated `prenom_nom@um5.ac.ma`. `SyncUserMiddleware` falls back to
  matching a Keycloak `sub` on e-mail, so a manufactured address that somebody already holds hands a
  student **another person's account**: the taken set is read from the store, not merely from the
  batch, and every generated address is reported per row and counted (`GeneratedEmails`). The lookup
  is built only when a row will read it.
- ⚠ **The generation rule lives in `StudentIdentifierRules`, once**, because there are two
  generators: `LegacyIdentityMapper` manufactured all 10 204 imported addresses and
  `InscriptionPlanner` manufactures every new one. They had already drifted — one kept letters only,
  the other letters *and digits*, so « Mohamed2 Alaoui » became `mohamed_alaoui` in the importer and
  `mohamed2_alaoui` here, for the same person. Two namespaces for one faculty, and the re-import
  Phase 16 plans would have renumbered people who already log in. **Letters only is what is on disk**,
  so it is what the rule states.

##### One student at a time — `InscribeStudentCommand`
`POST inscription/student`, a JSON body, no file. The transfer notified in November, the returner who
turns up in week three, the réorientation settled after the intake file was sent.

- **Every bulk import owes a single-row way in**, and it matters more here than for the déliberation:
  an inscription file names people who do not exist yet, so re-sending it to add one late arrival
  means re-stating a whole promotion to say one thing.
- **Every value arrives as text, exactly as a sheet cell would**, and is parsed by the same code — so
  the form and the file cannot disagree about what « 03/09/2006 » or « SM A » means, and a refusal
  reads identically on both. Typed fields here and strings there is two grammars for one column.
- **No preview and no confirmation.** `ConfirmedStudentCount` exists because a file has rows nobody
  read and can be edited between the simulation and the apply; here the request *is* the row.
- ⚠ **The refusal carries the row's own sentence** (`InscriptionErrors.RowRefused`, code
  `Inscription.<Action>`). « 1 ligne en erreur » is what a file needs and names nothing a form user
  can act on.
- **Both paths share `InscriptionPlanner` *and* `InscriptionApplier`.** Sharing only the planner
  would leave two copies of the writes, and it is the writes that create identities — same reason
  `FinalYearGuard.EnsureMayEnterManyAsync` is the implementation and the single-student call
  delegates to it.

##### A réorientation is the second path allowed to move a confirmed student stamp
A `CnpnVersion` belongs to exactly one `AcademicProgram`, so carrying a stamp across Médecine →
Pharmacie leaves `Student.CnpnVersionId` naming a text that governs a cursus the student has left —
and everything reading `TotalYears` from it (the final-year gate, the déliberation's « est-ce sa
dernière année ? ») then answers from the wrong arrêté.

- ⚠ **`RegistrationCnpnStamper.Fallback` was programme-blind**, so this was already wrong on any
  réorientation done through the ordinary registration form. It now refuses a carried stamp whose
  programme does not match the registration's level and falls through to `ResolveFromEntryAsync`,
  which resolves from the level's own programme.
- ⚠ **Unresolved is not « leave it as it was ».** Where PGSH holds no text of the new programme
  applying at or before his entry, `Student.ClearCnpnVersion()` removes the stamp. Null means « never
  resolved » — the same thing it means on the ~2 200 students nobody has stamped — and every reader
  falls back on it gracefully. Keeping the old one asserts something false.
- This does not contradict `CnpnTargeting`'s rule. What must never be re-evaluated is an *existing*
  stamp against a population re-selected each September; this fires once, on one named student, at the
  moment the faculty moves him between programmes.

### A guard that refuses loses the faculty's statement — `RegistrationHold`
`Registrations/Holds/` + `Domain/Registrations/RegistrationHold`. **The faculty's réinscription roll
is applied even where PGSH's own record says the student is not ready.** The registration is created
and *held*: it takes part in no roster cut, gets no cohort affectation and is published no période,
until somebody clears it by hand from « Signalements ».

- ⚠ **The case that forces it.** 182 of the 651 7ᵉ année Médecine the 2026-2027 roll re-registers
  read as owing an earlier stage (that measurement predates the « entrer » correction — see the
  count note below) — and in most of them the stage was served and only the évaluation
  is not keyed in. That is a fact about our data entry, not about the student. Refusing the row lost
  the faculty's statement; applying it silently lost ours. The hold keeps both, and turns a diff
  between a spreadsheet and a database into a worklist.
- ⚠ **Whether a signalement freezes is a property of the *reason*, not of the flag** —
  `RegistrationHoldReasonExtensions.Blocking`. A signalement means « quelqu'un doit regarder ceci »;
  blocking is a second, separate question.

  | reason | freezes | why |
  |---|---|---|
  | `OutstandingPriorStages` | **yes** | he may not start his final year's stages before clearing the earlier ones |
  | `AbsentFromReinscriptionRoll` | **yes** | nobody has explained the absence |
  | `IncompleteStudentFile` | **no** | his dossier is *thin*, not *wrong* |

  The first two say « nobody has established that this student may go on ». The third says « we are
  missing his paperwork », and nothing about a missing date de naissance says he may not rotate
  through a service. Collapsing them would either freeze people over a birth date or let an
  unexplained absence plan itself.
  - ⚠ **A list, not a method, because the policy has to translate it.** EF cannot call
    `BlocksPlanning()` inside a predicate; it translates `Contains` over a static array into an `IN`.
    So the array is the single statement and the method reads it.
  - ⚠ **`Plannable` is « no *blocking* hold », `Flagged` is « any hold ».** The worklist counts the
    second and planning obeys the first; a screen that conflated them would report 1 353 blocked
    students where only 1 327 are. `RegistrationHoldResponse.BlocksPlanning` is **sent**, never
    re-derived on the client — same split as `ServicePeriodResponse.State`.
  - ⚠ **`ReleaseHoldReport` carries `StillBlocked` beside `StillHeld`.** A student left carrying only
    « dossier à compléter » is on the worklist *and is planned*; telling the operator he is still
    frozen would be false.
- ⚠ **Every absentee is held, the 1 217 inferred graduations included.** The graduation is *our
  inference*, read off a blank cell, never the faculty's statement: a partial roll would end the
  cursus of people still enrolled with nothing on the row saying a human had looked. It costs a
  genuine graduate nothing — his year is closed, there is nothing to plan — and it catches what an
  absence most often really is, a réinscription that has not arrived, because the flag is still
  standing on the day somebody registers him by hand. The `Diplômé` verdict is still recorded
  (`Inferred`, self-correcting); the hold sits on top of it.
- **Holds need no confirmed count, unlike `WillGraduate`.** A hold is released in one click and the
  row keeps its history; a graduation ends a cursus and nothing puts that back. **Confirm what cannot
  be undone.**
- ⚠ **One predicate, `RegistrationHoldPolicy`, or five screens disagree about who is frozen.** Same
  rule as `ServicePeriodLifecycle` and `StageScoring`, and the expressions are the authority with the
  delegates compiled from them. In a **predicate** the collection aggregate is an `EXISTS` and
  translates; the same collection in a **projection** is the shape Npgsql refuses. Pinned by
  `SqlTranslationTests` — the two hottest planning reads compose it, so a translation failure would
  500 the first real « Générer le plan » with the whole suite green.
- **Excluded from**: `AutoArrangeGroupsCommandHandler` (the roster cut),
  `StudentAffectationService.EligibleRegistrationsQuery` (cohort affectation),
  `CohortProvisioner.GroupTextsQuery` (a held registration does not decide its roster's texts).
  ⚠ **The roster cut names them rather than dropping them** — a cut silently one student short looks
  exactly like a promotion that size, which is the failure the flag exists to remove.
- ⚠ **Released by hand, never by the condition lapsing**, and the note is required. A registration
  that quietly re-entered the répartition the day an évaluation was keyed in is precisely the silent
  behaviour being removed. The row survives its release so the file can say who cleared him and on
  what — the same snapshot bargain as `FinalYearEntryWaiver`. `StillHeld` is returned because two
  reasons can stand at once and « c'est réglé » is a different fact from « il en reste un ».
- ⚠ **No bulk release, deliberately.** It would undo in one click the only thing that made a 1 267-row
  inference safe to record.
- **Idempotent per reason**, because the roll is re-runnable: a second upload neither stacks flags nor
  rewrites evidence somebody is acting on.
- ⚠ **Two numbers, two code versions — do not conflate them.** **182** is what the gate refused *before* session 37 corrected « entrer » to mean « commencer » rather than « être inscrit en » ; it is the motivation, not the current count. With that fix the gate reaches only genuine entrants to a final year — the MED06 → MED07 population — and **measured live on 2026-09-02 the roll holds 60**. Both are real; 60 is what the preview prints today.
- ⚠ **The division with `FinalYearEntryWaiver` is principled, not accidental.** The *roll* holds; the
  *manual* paths (`CreateRegistrationCommand`, `CreateManyRegistrationsCommand`,
  `InscriptionPlanner`) still refuse, with the waiver as the deliberate override. The roll is the
  faculty's own document and outranks a hand-typed form, and ceremony per student is exactly what
  does not scale to 182 at once.

#### ⚠ « Couvert par le fichier » means *named*, not *written*
`Skip()` dropped the source registration id, so a row skipped as **« déjà inscrit »** stopped counting
as mentioned — and `ReadAbsence`, which reads « not mentioned » as « ne revient pas », then inferred a
soutenance from the silence of a student the file names on his own line.

- **Latent until the roll was run twice, and the second run is now the normal path** (it is how the
  newcomers get created). Measured on the live base 2026-09-02: the re-upload offered **8 077 gels and
  791 « Diplômé » déduits** where the first pass had found 1 267 and 1 217 — i.e. it would have ended
  the cursus of 791 students it had itself re-registered minutes earlier.
- **The rule:** any row that resolves to a closing-year registration marks it covered, whatever the
  row then does. A skip is still a mention.
- The apply is *designed* to be re-runnable, which is what makes this class of defect dangerous rather
  than merely wrong: the second run is expected, encouraged, and was destructive.

#### ⚠ …and idempotency that reads a navigation needs the `Include` *and* the index
`PlaceOnHold` is idempotent per reason by reading `Registration.Holds`. The roll's closing-year query
did not `Include` it, so the second upload raised a **second** absentee flag on all 1 267 of them —
2 534 rows where 1 267 were meant. **An un-Included collection is indistinguishable from an empty
one**, and this suite cannot see the mistake: the in-memory provider fixes navigations up from the
change tracker, so the idempotency test passed throughout.

- Fixed on both sides, deliberately: the `Include`, **and**
  `IX_RegistrationHold_Registration_Reason_Active` — unique on (registration, reason) among the
  unreleased rows. Same bargain as `IX_CnpnLevelEffectivity_Version_Level`: the next missed `Include`
  degrades to a constraint violation instead of silent duplication.
- `SchemaInvariantTests` asserts the index is *declared* on the Npgsql model. That is the half
  checkable without a database; whether PostgreSQL enforces it still needs Testcontainers.

#### The roll creates the students it names and PGSH has never seen
26 of the 6 862 lines of the 2026-2027 file. They used to be skipped, on the rule that **creating an
identity is the inscription's act, not the rollover's** — which is sound, and the skip was still
wrong in practice: the only trace was a downloaded spreadsheet, so nobody acted on them.

- **Created from what the file actually carries** — the Apogée and the name — and flagged
  `IncompleteStudentFile`, which is advisory, so they partition and plan with everyone else while
  somebody finishes the dossier.
- ⚠ **No CNE is manufactured.** The row carries an Apogée and `Student.CNE` is optional since the
  `LEGACY-` placeholders were cleared, so a `SANS-CNE-…` would read in every list exactly like a code
  somebody holds. `BacYear` is required by the schema and absent from the roll, so it is left **empty**
  rather than invented — that emptiness is precisely what the flag names.
- ⚠ **The e-mail is the one invented value, because `Users.Email` is NOT NULL UNIQUE** — and it is a
  login: `SyncUserMiddleware` falls back to matching a Keycloak `sub` on it, so an address colliding
  with a real one hands a student another person's account. Allocated **in the planner** against the
  addresses in the *store* (not merely the batch), so the dry run shows the exact address that will be
  written, and printed on the row's own message rather than only counted — « N adresses générées »
  says nothing about *which* address a given student was handed.
- ⚠ **`dbContext.Registrations.Add(registration)`, not `Students.Add(student)`.** `Add` marks the
  reachable graph, and the graph is only whole from the registration: it references the student and
  owns the hold, while `Student.Registrations` was never populated. Adding the student alone left the
  registration untracked and **nothing was written** — caught by a test, not by the compiler.
- ⚠ **An unsaved hold cannot be released by id.** The key is store-generated, so every hold added in
  one unit of work carries `Guid.Empty`, and `FirstOrDefault(h => h.Id == holdId)` would lift whichever
  sits first. `ReleaseHold` refuses an empty id outright.

#### The report is a screen and a document, and only one of them may be capped
`GetReinscriptionSheetExportQuery` — `POST reinscription/sheet/export`, three sheets
(Synthèse · Lignes · Absents).

- ⚠ **Written from `ReinscriptionSheetPlan.AllRows`/`AllAbsentees`, never `Report.Rows`.** The report
  is capped at `MaxReportedRows = 1000` and ordered attention-first so a browser survives it; the
  roll produces ~1 450 rows somebody must walk one at a time. Reading the capped list would stop at
  1 000 lines while looking exactly like a complete file.
- **It re-runs the planner rather than reading a stored report** — nothing is stored — which is the
  property, not a workaround: document and screen come from one plan, so a file printed for the
  archive cannot describe a different population from the one that was applied.
- ⚠ **It writes nothing, so it is offered before the confirmation and on a roll the apply would
  refuse.** « Donne-moi la liste des erreurs » is the request, and a refusal naming only the first
  offending line cannot answer it.
- **No `TooManyRows` cap.** The other two exports are scoped by a year the caller may omit; this one
  is bounded by the uploaded file plus one year's registrations. There is no axis to narrow, so a
  limit could only refuse a document the user has no other way to obtain.

### The last year does not begin until everything below it is validated
`Stages/Progression/` — `OutstandingStageFinder` (what a student still owes, cursus-wide) and
`FinalYearGuard` (whether that stops him). Enforced by `ReinscriptionPlanner`,
`CreateRegistrationCommand`, `CreateManyRegistrationsCommand` and `InscriptionPlanner` alike.

- **The rule is the faculty's**: a 7ᵉ année under arrêté 2174.18, a 6ᵉ under 1650.25, cannot be
  **entered** while a stage from an earlier year is unvalidated. Asked per **student** from his own
  `TotalYears` — from 2026-2027 one 6ᵉ année Médecine holds students of both texts, so the level alone
  cannot answer "is this his last year?".
- ⚠ **« Entrer » is the whole rule, and reading it as « être inscrit en » inverts it.** The final year
  is not a year one passes or fails — there is no déliberation for it. The student validates and
  revalidates his stages one at a time, never redoing one already validated, and sits the *examens
  cliniques* once they are all done; he is **re-registered each September until both are cleared**. So
  the re-registration *is* the mechanism by which he clears the debt, and refusing it because he still
  owes a stage refuses him the only way to stop owing it. `FinalYearGuard` therefore stands aside for a
  student who **already holds a registration at that level** — he is continuing, not beginning.
  - **Measured 2026-09-01 against the faculty's own roll**: of the 651 7ᵉ année Médecine it
    re-registers into the 7ᵉ, **182 were refused** — a quarter of the promotion, every one named by the
    faculty as coming back. With the rule corrected the gate refuses **60**, which is exactly the
    MED06 → MED07 population it was written for.
  - **A gap does not make it a beginning.** A student who sat in the final year, dropped out and comes
    back has the same stages to revalidate; the guard reads « has he ever been registered at this
    level », not « was he there last year ».
  - ⚠ **`Debt.LevelYear` is the *registration's* level, not the stage's** (`OutstandingStageFinder`
    projects `a.Registration.Level.Year`). A failed attempt recorded against the final-year
    registration is therefore not an *earlier* debt, and the gate rightly ignores it — which is how
    the first version of the test for this passed with the rule removed.
- ⚠ **The existing déliberation check cannot answer it.** `StudentsWithUnvalidatedStagesAsync` is
  scoped to `a.Registration.AcademicYearId == yearId` — stages of the year being deliberated. A 6ᵉ
  année student owing a 4ᵉ année stage is invisible to it. The debt has to be read across every
  registration, which is what `OutstandingStageFinder` does.
- **Owed = every attempt came back `NonValidé`** — the same test `DossierStageState.ToRevalidate`
  uses, deliberately, because two screens disagreeing about what a student owes is worse than either
  being slightly wrong.
  - ⚠ **…counting only the attempts a year still stands behind** (`RegistrationStatus.AnnulsItsStages`,
    settled 2026-09-01). A redoublant repeats the year from scratch, stages included, so an attempt
    served inside a `Failed` year establishes **nothing** — not an acquisition, and not a debt. The
    case: pass a stage, fail the year, repeat it, fail that stage. Without the filter he reads as
    having acquired it and enters his final year owing it, while the last thing he did was fail it.
    Only `Failed` annuls — `Withdrawn`/`Excluded` end the cursus instead — and `Active` annuls
    nothing, which is what keeps the imported cursus (all `Active`, no verdict ever recorded) intact.
  - ⚠ **`NonÉvalué` is not owed.** An unmarked stage is one nobody graded, not one he failed, and this
    base holds almost no marks — counting it would block the whole faculty on missing data.
  - ⚠ **Nor is a stage never attempted.** Reading "owes" from the CNPN's requirement set would be
    stricter and today wrong: 1650.25's requirements are not entered, so every six-year student would
    owe everything. Widen *there* when the sets are complete, not at each call site.
- ⚠ **`TryGetValue`, never `GetValueOrDefault`, on a `Dictionary<Guid, int>` of final years.** The
  default is `0`, not null, and a 0 read as "his text runs 0 years" makes *every* year his last —
  which blocked every student with no CNPN on record, i.e. the one case the guard must stand aside
  for. It fired hardest exactly where it should not have fired at all.
- ⚠ **…and the faculty's own roll no longer refuses at all — it registers and holds.** See
  `RegistrationHold` above. The gate still *decides*, and its sentence becomes the hold's evidence;
  what changed is what is done about the answer. The 60 the corrected rule refuses on the real roll
  are now 60 registrations created and frozen, which is the difference between a promotion that is
  complete on paper with 60 rows to review, and 60 students nobody re-registered.
- **Enforced on the manual paths too.** A guard the bulk rollover applies and the registration form
  does not is a guard anyone steps around by using the other button. The inscription import asks it of
  the students PGSH already holds — a returner really can be re-entering a final year owing a stage —
  and never of a newcomer, who has no cursus here to owe anything from.
- **Asked once for a batch** — `EnsureMayEnterManyAsync`, which is the implementation; the
  single-student call delegates to it, so the two paths cannot drift. Per student it is four queries —
  the level's year, his text, his whole cursus, his waiver — i.e. ~2,800 round-trips to enrol a
  promotion of 700 through `CreateManyRegistrationsCommand`. Narrowed twice so the batch stays cheap
  *and* the single call gets no dearer: the cursus is read only for the students this level is the last
  year of, the waivers only for those who then owe something, so a batch where nobody is in his final
  year is two queries whatever its size.
  - ⚠ **`Contains` is right on a list and wrong on a promotion.** `ForStudentsAsync` takes the ids the
    caller named — bounded by what somebody selected; `ForPromotionAsync` is scoped by the predicate
    that selects the promotion, because 8,077 registrations is a set nobody enumerated. Reach for the
    predicate whenever the set is *described* rather than *listed*.
  - `ReinscriptionPlanner` still carries its own copy of the decision, for exactly that reason: it is
    predicate-scoped. Folding it in means teaching the guard to take a predicate, not handing it ids.
- **The exception is a row, not a flag** — `FinalYearEntryWaiver`, keyed (student, year), with a
  required reason and a **snapshot of what was owed** at the moment it was granted. By the time it is
  read back the stage may have been revalidated or dropped by a new text, and a waiver that cannot say
  what it excused is not a record. Refused when nothing is owed (it would assert an exception that
  never happened) and **irrevocable once the registration it permitted exists** — removing it would
  leave a student in a final year with nothing on record saying who allowed it.
- `ReinscriptionReport` counts `FinalYearBlocked` **and** `FinalYearWaived`: an override nobody sees
  is an override nobody reviews.

### Revalidation is the escape valve, and it is deliberately loose
`Stages/Revalidation/` — `RevalidateStageCommand` re-opens a failed stage on the registration the
student holds **now**, as a fresh `InternshipAssignment`; the failed one stays as history.

- **Not constrained to the registration's own level** — a 6ᵉ année student redoing a 1ʳᵉ année stage is
  the case it was built for. The real constraint is that a failed attempt exists.
- **Served where he failed it**: the original service is reused unless overridden, as an **ad-hoc
  placement outside the published grid** (`CohortSlotAssignmentId` stays null, like a délocalisation).
  It is one student making good one stage, not a cell in his group's rotation.
- Placement is all-or-nothing and **optional**: create the assignment now, schedule it later.
  `CohortId` slots him into any cohort currently running the stage.
- Guards: scolarité only · a stage validated on *any* registration is never re-opened · **every** prior
  attempt must be settled `NonValidé` (one still pending blocks, so two attempts can never run at once).
- ⚠ **Gap:** it requires a prior *failed* attempt (`NothingToRevalidate`), so there is no way to hand a
  student a stage he never attempted — a legacy record never entered, a case somebody has to fix by
  hand. And no generic "assign this student to this cohort" command exists: every other creation path
  is bulk (`GenerateSchedule`, `StudentAffectationService`) or specific (`Delocalize`,
  `LateArrivalScheduler`). That is the flexibility hole to close next.

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

### Managing the year itself — set it, correct it, remove it
`AcademicYears/Manage/`. Three acts a year needed and did not have, and each is guarded by something
the year cannot see about itself.

- **`SetCurrentAcademicYearCommand`** (`POST academic-years/{id}/current`) is deliberately its own
  route, not a field on the update. A year is normally created months before it becomes current, so
  folding the two together left « créer une année » as the only way to move the flag — the flag moved
  as a side effect of something else.
- ⚠ **Demotion is saved as its own statement, before the promotion.** `IX_AcademicYear_IsCurrent` is
  unique and filtered and Postgres checks it at the end of each statement, so two flagged rows is a
  constraint violation, not a state EF can order its way out of. One `SaveChanges` emitting both
  updates leaves that order to EF, which has no reason to pick the safe one.
- ⚠ **…and it goes through the aggregate, not `ExecuteUpdateAsync`.** That helper is *unsupported by
  the in-memory provider*, so the demote `CreateAcademicYearCommandHandler` performed could never be
  reached by a test — the one part of the handler that can leave the base with no current year at all.
  The exposure is identical either way; only the coverage differs.
- **Deleting is refused while anything year-constituted exists, and the refusal names every count** —
  registrations, périodes, cohortes, dérogations, règles d'effectivité, CNPN whose intake year it is.
  Counted together rather than short-circuited: a user who clears the registrations only to be told
  about the périodes has been sent round the loop twice.
  - ⚠ **The schema makes an ungated delete destructive in two different ways, neither of which
    announces itself.** Measured 2026-08-24: five FKs are `RESTRICT` (a raw violation, i.e. a 500 with
    nothing actionable in it) while **`AcademicGroups.AcademicYearId` is `CASCADE`** — the rosters go
    silently. The chain stops there only because `Cohorts` and `Registrations` restrict on the roster.
    Same bargain as `DeleteCnpnVersionCommand`: what may cascade is exactly what has no meaning once
    the thing that constituted it is gone, and an empty roster of a year nobody registered in records
    nothing. `RostersRemoved` is reported so the confirmation can name it.
  - ⚠ **The current year is never deleted.** Every unscoped handler resolves through it, so removing it
    leaves no answer to « quelle année ? ». Designating another one first is the reversible act.
- ⚠ **Two years may not share a day** — `AcademicYearCalendarGuard`, shared by create and update
  because a rule enforced on one path only is a row that can be created and then never saved.
  `ServiceOccupancyCalculator` bounds a year by its **dates** rather than by `AcademicYearId`
  (deliberately — a slot stamped with the wrong year but dated inside this one should surface), and
  that is only safe while the two cannot disagree. Overlapping years count every slot in the overlap
  twice against a service's load, which is the number the publish guard refuses on. The rule was never
  enforced; the base happens to satisfy it.
- **Moving a year's span does not move the périodes laid on it**, so narrowing one can leave its own
  slots outside it. Reported (`SlotsOutsideSpan`), not refused — a year is routinely corrected while
  its axis is a draft — and counted **before** the write, or the slots that fell out become
  indistinguishable from slots that were always elsewhere. Same shape as `UpdateHolidayCommand`.
- **The entity carries `init` accessors over explicit backing fields**, not plain setters: an object
  initialiser — how the seeder, the importer and the tests build a year — still works, while nothing
  can change a year *afterwards* except `MakeCurrent` / `Relinquish` / `Rename` / `Reschedule`. The
  compiler found the one place that was doing it the other way (`LegacyImportPlanner` flipping
  `IsCurrent` on the last year), which is exactly the write the guard exists for.

⚠ **Filtering by level *and* year is one `Any`, never two.** Both predicates have to hold on the
**same** `Registration`, because a student past his second year satisfies each on a different row —
and 2 635 students in this base have repeated, so the false positive is the ordinary case, not an
edge one. `GetStudentsQueryHandler`'s promotion filter is the shape to copy:
`s.registrations.Any(r => r.LevelId == levelId && (yearId == null || r.AcademicYearId == yearId))`.
Measured on the live base 2026-08-29: « 5ᵉ année Médecine, 2026-2027 » is **833** students; asked as
two independent `Any`s it is **2 127** — 2.6×, all of them people who merely *passed through* that
level. The same trap applies to any (year-invariant key, year) pair reached through a collection.

⚠ **`RegistrationStatus` joins that same `Any`, and it is the stricter case.** A verdict is a fact
about *one* registration, so « Diplômé » ∧ « 2026-2027 » asked as two conditions returns every
student who ever graduated and happens to hold a 2026-2027 registration — which, in a thesis year
re-registered every September until the defence, is most of them. `GetStudentsQuery.Status` (and its
twin on `GetStudentsExportQuery`, so the file matches the list it is downloaded from) is what makes
the 1 217 diplômés a réinscription records reachable from a screen instead of only from a file.

A global EF query filter was considered and rejected: of ~101 handlers touching year-constituted
tables, ~15 are *deliberately* cross-year — student parcours, level dossier, curriculum comparison,
revalidation's cross-level retake — and those are the load-bearing reads, not edge cases.
`IgnoreQueryFilters()` is also all-or-nothing, so the escape hatch would disable unrelated filters.
  - ⚠ **To show a count, ask for `pageSize: 1` and read `TotalCount`** — never fetch the rows.
- **Localization mapping** — `LocalizationMapper.FromCoordinates(x, y, z)` in `Application/Hospitals/`. Use for any Center, Hospital, or Service handler that maps GPS coordinates.
- **Who leads a service** — `ServiceChefDirectory` / `ServiceChefProvider` in
  `Application/Hospitals/Chefs/`. Tenure open on the date → sitting chef → legacy note, asked
  **as of a date** the caller names. Never re-derive it: the répartition and the stage export both
  read this one, and `FromSourceNote` must travel with the name.
- **Per-period mark / verdict** — `StageScoring.PeriodMark` / `IsPeriodValidated` in `Domain/Stages/`. The single source of truth shared by the domain roll-up and every read handler (student record, fiche). Never recompute a mark inline.
- **Where a rotation stands** — `ServicePeriodLifecycle` / `ServicePeriodState` in `Domain/Stages/`
  (`Planned` → `Underway` → `AwaitingEvaluation` → `Settled`). Same rule as `StageScoring`, for the
  same reason: `IsStarted && !IsComplete && !IsInterrupted` had been written out in four separate
  files and its not-started twin in two, i.e. six chances to disagree about what « en cours » means
  with nothing to catch it. Never restate the triple — `Where(ServicePeriodLifecycle.Underway)` in a
  query, `IsUnderway(p)` in memory, `StateOf(...)` in a projection.
  - ⚠ **The expressions are the authority and the delegates are compiled from them.** Two hand-written
    copies, one for EF and one for memory, is the drift the class removes. EF needs an `Expression`
    (a method call in a `Where` is refused by the provider), so that is what is written.
  - ⚠ **`Settled` is defined as the *complement* of the other three, not as "closed and marked".**
    That is what makes the four a partition: a row complete-but-never-started is a state the
    lifecycle cannot produce and the store can hold, and under a positive definition it would belong
    to no state — invisible in every list and counted in none. `ServicePeriodLifecycleTests` walks
    all 16 flag combinations.
  - ⚠ **`AwaitingEvaluation` / `Settled` read the `Evaluation` navigation**; in memory it must be
    loaded. `Planned` / `Underway` touch flags only and are always safe.
  - **The state is sent to the client, never re-derived there.** The chef page had the same four-way
    split written again in TypeScript — one rule, two sides of a network boundary, nothing able to
    catch them disagreeing.
- **Execution scoping** — `ExecutionAuthorizer` in `Application/Employees/MyServices/`. Every handler acting on a period/evaluation/attendance goes through it: `EnsureCanActOnPeriodAsync`, `EnsureCanActOnEvaluationAsync`, `EnsureCanRecordAttendanceAsync` (write), `EnsureCanReadAttendanceAsync` (read — wider, includes the owning student).
- **Period overlap** — `SlotOverlapGuard` in `Application/Stages/Slots/`. Any handler creating or moving a `StageSlot` must call it; the rule is level-wide, not per-stage.
- ⚠ **A service's load over a window is the *peak inside it*, never the sum of what touches it.**
  `ServiceOccupancyLookup.LoadOn` summed every cell overlapping the asked-for window, so two cells
  that each touch the window *without touching each other* were added. Measured 2026-09-03: the
  planning grid showed **118** on Pédiatrie2 where the service never held more than **62** — the two
  4ᵉ année Pédiatrie columns are consecutive (one ends 06/10, the next starts 07/10). The grid, the
  arranger's balance and **the pre-publish guard** all read this class, so publication was refused on
  loads that never occur; the per-service page and the charge report were right throughout because
  they go through `OccupancyTimeline`. `LoadOn` now sweeps the same way. It cannot miss a real
  breach — a real one is an instant where the sum crosses the ceiling, and that instant is one of the
  evaluated candidates.
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
- **`AllowOverCapacity` waives a target, never an admissibility rule** — `SchedulePublisher.
  EnsureIntakeAsync` (renamed from `EnsureCapacityAsync`, which described half of what it does).
  Two rules of different kinds:
  - **Admissibility** (`LevelNotAdmitted`) — the service carries intake rules and none names this
    promotion. Checked **whatever the caller asks for**. Publishing anyway sends students to a
    service that does not take them, which no checkbox makes true.
  - **Occupancy** (`CapacityExceeded`, `LevelCapacityExceeded`) — over the number. Waivable, because
    the number is a target.
  - ⚠ **Why the split had to happen: the override is ticked as a matter of routine.** The base is
    structurally over-subscribed — measured 2026-08-14, **233 of 353 planned cells are over capacity
    (66%), worst 85 against 20** — so one flag governing both meant the hard rule was switched off
    every time it was reached. *A rule enforced only when nobody needs the override is not enforced.*
  - The refusal **says it cannot be forced**, because the checkbox is on screen promising otherwise;
    the checkbox's own description says so too, and is now labelled « dépassement d'**effectif** ».
  - The occupancy lookup is built **only when a number will be read** — with the override on,
    admissibility is answered by the intake rules alone, so splitting the flag did not make the
    common publish do more work than when it skipped everything.
  - ⚠ Still true: all 148 services carry the imported default `Capacity = 20` and **not one quota is
    authored**, so every *capacity* verdict today is measured against a number nobody wrote. That is
    an argument about the soft half only — it is exactly why the soft half stays waivable.

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

#### …and two findings exist only when every service is read at once
`Services/OccupancyReport/` — `GET services/occupancy-report`, and the «&nbsp;Charge des
services&nbsp;» page + printable document behind it. The per-service read answers « what does *this*
service hold »; nothing answered « which services are the problem », which is the question asked
before publishing a promotion and which opening 148 pages does not answer.

- ⚠ **A service that holds nobody all year is invisible from its own page** — there it looks like a
  service with nothing planned, which is exactly what it is. It is the *other half* of a saturation
  elsewhere: measured on 5MED Psychiatrie, all nine columns went to one service and **two of the five
  were never used**, and the printed répartition was the only place it showed.
- ⚠ **`OccupancyStageRow.ServicesUnused` is the number this report exists for.** A stage listing five
  services and placing everybody in two has an arrangement defect no single service page can produce,
  because the denominator is `Stage.AllowedServices` and the numerator is the cells.
- ⚠ **A filter narrows what is *listed* and what is *attributed*, never the load a saturation is
  measured on.** A service is shared and the ceiling that refuses a publish counts every promotion
  standing in it, so measuring « la 5ᵉ année seule » against the service total prints « ok » for a
  service that is over because of the 3ᵉ — and refuses the publish anyway. `Share` carries the
  filtered half; `PeakStudents` stays the whole load. Same class as reading an omitted year as « all
  of them »: one number quietly standing in for another.
- **A peak is simultaneous presence, never a sum over the year.** Built with the same pure
  `OccupancyTimeline`, so one cohort of 40 passing through a service in three windows is 40, not 120.
  Summed it would be a saturation that never happened and would look exactly like a real one.
- **A month's bar is the peak reached inside it, not its mean.** A month with one saturated week
  reads comfortable on an average, and the week is what somebody has to act on.
- ⚠ **`Saturation` is `null`, never 0, when there is no ceiling to divide by.** 0 sorts as « the
  least saturated », which is exactly wrong for a service admitting nobody — and those sort *first*,
  above even a service at 400 %, because theirs is the refusal publication cannot force.
- **Three flat reads, one for every service.** 148 services × a query each is the shape that made a
  single « Générer le plan » ~700 round trips. `PlacementsQuery` projects the cohort's assignment
  **count** — an aggregate over a navigation, which translates — where a projected collection of
  those assignments is the element with no key Npgsql refuses. Pinned by `SqlTranslationTests`.
- ⚠ **An empty report has two causes calling for opposite acts** — no créneau authored (author an
  axis) or créneaux nobody is in (arrange) — and « 0 étudiant » collapses them into a third reading
  the user arrives at first: that the report is broken. `Notes` separates them, exactly as
  `RepartitionSummary.DeclaredSlotCount` does. **This is the live base's state today** (0 slots, 0
  cells on every year), so it is the screen the user meets first.
- ⚠ **Never print a placement count as an effectif.** « Étudiants placés » was
  `Σ cells (cohort size)` — 11 148 against **933** real 3ᵉ année students, because a student counts
  once per créneau he occupies. Removed from the header (the **peak** is the measure of people) and
  the two table columns renamed « Placements » with a line saying what they count. A number that
  looks like a headcount and is not one is worse than no number.
- ⚠ **A printed document needs `print-color-adjust: exact`, declared on every element.** Browsers
  drop background graphics when printing: the SVG figures survive (`fill` is content) but the year
  strip draws each band as a `background` on an empty span, so the one figure showing *when* a
  service is full came out blank in the PDF and perfect on screen.
- ⚠ **…and `break-inside: avoid` belongs on figures only.** On the section holding the 148-row table
  it does not move the block, it **clips** it — the longest table was the likeliest to lose its tail.
  Tables break across pages, keep their rows whole, and repeat `thead`.
- **`Notes` follows `ExportNotes`' rule** — silent when the data has nothing to say. It names the
  uniform imported `Capacity = 20` only when every open service really does carry the same number,
  because a warning that fires whatever the data says is noise, and noise is dismissed, which puts
  the real one out of sight.

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

#### ⚠ …and a student may have no CNE at all (2026-09-01)
`Student.CNE` is **optional**. The Access base records a national code for 5 510 of its 10 203
students, and the import manufactured `LEGACY-{NO_ORDRE}` for the other 4 693 — a value that reads,
in every list, every export, every déliberation canvas and every évaluation-import match, exactly
like a code somebody holds. **46% of the roll carried one.**

- **The column was already nullable** (`Users` is TPH and an `Employee` has no CNE); the requirement
  lived in EF's model and in the validators. `IX_Student_CNE` is now `UNIQUE … WHERE "CNE" IS NOT
  NULL` — Postgres already treated NULLs as distinct, so the filter states the intent rather than
  changing the rule. `StudentCneOptional` also clears the placeholders, guarded on
  `^LEGACY-[0-9]+$`.
- ⚠ **`Appogee` is the identifier that is in practice always present** — it carries the legacy
  `NO_ORDRE` verbatim for all 10 203 imported rows, and it is what the faculty's own réinscription
  roll keys on. Every canvas that matches on a CNE already indexes both.
- ⚠ **`null == null` is true in memory and NULL — i.e. false — in SQL.** So a uniqueness check on an
  optional identifier is guarded on the *request* value being present (`CreateStudentCommandHandler`,
  `UpdateStudentCommandHandler`), or the in-memory suite reports « CNE déjà utilisé » against the
  next student without one while PostgreSQL passes silently. The filtered index says the same thing.
- **`InscriptionPlanner` no longer manufactures one either.** A row with an Apogée and no CNE is
  stored with none. The `SANS-APOGEE-` half stays: a row must carry *one* of the two, and a student
  with neither could not be found by any of the canvases afterwards.
- ⚠ **`UpdateStudentCommandValidator` required `int.TryParse` on the Apogée**, which no
  `SANS-APOGEE-…` value satisfies — so every student the inscription created that way was read-only
  the day somebody opened his file. Third instance of the same defect class; it is length and
  presence that the column actually requires.
- **Phase 16.2 is answered** (measured 2026-09-01 against `Medecine.mdb`): **no** source row carries
  a `NO_ORDRE` in its CNE column — 0 identical to its own, 0 matching another row's, and only 1 of
  the 5 508 usable codes has the eight-digit shape. The CNE column is carried across verbatim, and
  there is nothing to move into `Appogee`.

### ⚠ A validator describes what a *save* must satisfy, not what a good record looks like
Every rule on an update path is applied to rows that already exist. If the imported data does not
satisfy it, those rows become **read-only** — and the refusal names a field the user was not editing,
so it reads as a broken button rather than as a rule.

- **It has happened twice.** `StudentIdentifierRules` rejected 5,646 of 10,204 students (above). And
  `UpdateStageCommandValidator` required `Objectives.NotEmpty()` while the Access import carried no
  objectives at all — **0 of 27 stages** satisfied it, so the entire stage catalogue could not be
  saved. Reported as « switching the rotation mode gives an error »; the actual message was
  *« At least one stage objective is required »*.
- **Ask where the requirement is really true.** Objectives are needed only by
  `EvaluationMode.ValidateObjectives`, and that is already enforced by the evaluation validators and
  `EvaluationObjectiveResolver`. A stage-level `NotEmpty()` asserted it for every stage, in every
  mode, forever. Validate what *is* supplied — a blank label, a zero weight — never that something
  optional was supplied at all.
- ⚠ **Handler tests cannot see any of this.** The validator runs in `ValidationPipelineBehavior`, so
  a test that calls the handler directly passes a malformed command straight through:
  `StageRotationModePersistenceTests` passes `[]` for objectives and stayed green the whole time.
  **A validator rule is covered in `PGSH.Tests/Integration/` or it is not covered**
  (`StageEndpointTests`).
- ⚠ **And the message has to reach the screen.** `StagesPage` caught every failure with a bare
  `catch` and showed « Erreur lors de l'enregistrement », so the one sentence that explained the
  refusal was discarded at the last step. Validation failures carry their messages in `errors[]`
  (`detail` is only the generic « One or more validation errors occurred »); every other refusal is
  in `detail`. Read both — the pattern is `(err as { data?: … })?.data`, as on `GroupsPage`.

### An export is a document, and it leaves the system — `Exports/`
Two .xlsx downloads: the roll (`students/export`) and the post-validation stage record
(`stages/assignments/export`). `PGSH.Application/Exports/` holds the format-agnostic workbook model
(`ExportWorkbook` / `ExportSheet` / `ExportCell`), the French labels and the errors;
`ClosedXmlExportWorkbookWriter` in Infrastructure is the **only** code that knows what .xlsx is —
the same split as the three `ClosedXml*SheetParser`s, in the other direction.

- **One writer, or three faculties.** Three handlers each styling their own workbook is three
  documents that agree on nothing: the header band, the frozen pane, the auto-filter, the date and
  number formats are decided once. Add a sheet, not a styling pass.
- ⚠ **Cells are typed, and that is not cosmetic.** A date written as text cannot be sorted and a mark
  written as text cannot be averaged — which is the first thing anybody does to a post-validation
  file, and it fails silently. `ExportCell.Day/Numeric/Count/Text/Paragraph` carry the value in its
  own type. Identifiers stay `Text` on purpose: a CNE that looks like a number must not lose its
  leading zeros, and « 3-4 » must not become a date.
- **Every sheet carries a caption naming its scope** — promotion, année, row count. A file whose only
  statement of scope was its name cannot be audited three months later, and every export here is
  scoped by a year the caller was allowed to omit. Same reasoning behind `ExportFileName`.
- ⚠ **An export is the one read deliberately exempt from pagination**, so it is the one read that can
  pull the base into memory. Both are capped (`ExportErrors.TooManyRows`) and the refusal names the
  count *and* the axis that narrows it — « trop de lignes » alone sends the user back to the same
  button. Year-scoped, neither cap can be reached by the real data; they bite only on a caller that
  has found a way to widen past a year.
- ⚠ **Flat queries only.** The périodes of an attempt and the objective scores of an évaluation are
  collections; folded into a projection they are exactly the shape that killed the macro plan. Three
  top-level reads keyed on the parent id, joined in memory — pinned by `SqlTranslationTests`. The
  scope is defined **once** (`StageAssignmentExportQueries.Scoped`) and the other two reach it through
  `IN (subquery)`: two copies of a year filter is how a périodes sheet ends up describing a different
  population from the stages sheet beside it.
- **Nothing is recomputed.** `StageScoring` gives the mark and the verdict, `ServicePeriodLifecycle`
  the state, `WorkingDayCalendar` the durations, `ServiceChefDirectory` the chef. An export that averaged differently from the fiche de
  validation would be a document contradicting the system it came from.

#### ⚠ A column blank on every row is indistinguishable from a column the export forgot
`ExportNotes`, printed under the caption of every sheet. **This was reported within minutes of the
first real download**, against a file that was correct in every cell: the roll of 2026-2027 came out
with `Groupe`, `N° groupe`, `Partition`, `Source de la décision` and `Convention` empty on all 5 932
lines, and read as broken. Measured the same day, every one of those blanks was the truth —
**0 inscriptions carry a roster pointer**, `OutcomeSource` is null across a year nobody has
deliberated, and `AgreementType` is `None` for **all 10 206 students in the base**.

- **The note is computed from the rows actually exported**, not from a list somebody maintains, so a
  column added later is covered without anyone remembering to.
- **It says « aucune valeur dans cet export », never « données manquantes ».** An empty `Convention`
  means nobody is under one; the note's job is to say the export looked and found nothing, not to
  accuse the base.
- ⚠ **The roster columns get a second, specific note, because their emptiness has two causes that
  call for opposite acts** — no roster exists (cut the promotion) versus rosters holding nobody
  (assign the students). Same shape as `RepartitionSummary.DeclaredSlotCount` separating « no
  periods » from « periods nobody is in », and as `OutsideYearCount` saying what a year filter
  removed. The count of rosters in scope is queried **only when the answer will be printed**.
- **A note that fires whatever the data says is noise, and noise is dismissed** — which puts the real
  one back out of sight. A partly-filled column is not an empty one, and an export with no rows has
  no empty columns to name.

#### The roll is an export of *registrations*, and the promotion is a column
Nom, prénom, CNE and Apogée belong to a person and never move; niveau, groupe, partition and statut
are facts about the year — and 2 635 students in this base have sat in more than one. Cut from
`Students` the row would have to pick a registration and could not say which, so **the row is the
registration**.

- ⚠ **An omitted year is the current one, never all of them.** A file labelled « liste des étudiants »
  holding six promotions of history is the évaluation-import defect with a different button on it.
- **`Programme` and `Niveau` are always columns, and `levelId` still cuts the per-promotion file.**
  The columns cost nothing and make a row self-describing when two exports are merged or one is
  opened a year later; a file whose scope lived only in its name cannot do that. Asking for one *or*
  the other was a false choice — the filter gives the per-promotion document with the columns intact.
- **The CNPN column follows the read order** `r.CnpnVersionId ?? r.Student.CnpnVersionId`, and
  « Origine CNPN » says which of the two answered. Blank is « jamais résolu », not « rien dû ».

#### Several périodes is not several stays — `StagePeriodFolder`
The question the stage export had to answer: 01/01→01/02 then 02/02→02/03 is *one* rotation written
twice when the service never changed and the windows meet, and *two* when they do not. **The merge is
decided by the service, never by the dates.**

A **stay** is a maximal run of périodes in the *same service* with *no worked day between them*. One
stay prints as one span; several print joined by « · », with the services joined by « → » in the same
order, so the two cells correspond position by position.

| case | Découpage | Service(s) | Période(s) |
|---|---|---|---|
| one période | `Période unique` | `Cardiologie` | `01/01/2025 – 01/02/2025` |
| two, one service, meeting | `Service unique — 2 périodes contiguës` | `Cardiologie` | `01/01/2025 – 02/03/2025` |
| two, one service, a hole | `Service unique — 2 périodes, 1 interruption(s)` | `Cardiologie` | `01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025` |
| two services | `Rotation — 2 services, 2 périodes` | `Cardiologie → Pneumologie` | `01/01/2025 – 01/02/2025 · 02/02/2025 – 02/03/2025` |

- ⚠ **The multi-période fact is never carried by the string alone.** `Nb périodes` and `Nb services`
  are numeric columns, so « montre-moi les stages faits en deux services » is a filter rather than a
  reading exercise. Merging two windows into one span must not erase that it was recorded in two.
- ⚠ **A gap is measured in *worked* days.** A calendar-day test calls every Friday → Monday hand-over
  an interruption — which is exactly how one column follows another, since `WorkingDayCalendar` never
  lets a window swallow its trailing weekend. A declared holiday between two windows is not a hole
  either.
- ⚠ **Both break conditions matter.** Breaking only on the service change (the shape
  `SchedulePublisher.BuildStays` uses — correctly, since it works from contiguous grid columns) would
  swallow a real interruption inside one printed span. Breaking only on the gap would merge S1 → S2
  into one line and lose the second service.
- ⚠ **Durations are summed over the périodes, never measured end to end.** An interrupted stage's span
  contains days nobody served, and `Fin − Début` is the number that makes a 22-jour stage read as 60.
- **Most rows arrive already collapsed**: `SchedulePublisher` folds a `SingleService` run into one
  `ServicePeriod`, and 5ᵉ/6ᵉ année are `SingleService` in 51 923 of 51 924 imported placements. The
  folding exists for 3ᵉ and 4ᵉ année, which genuinely rotate, and for the Access history.
- `StagePeriodFolder` is **pure** — no store, no clock — like `PeriodAxis`, `RotationTiling` and
  `OccupancyTimeline`, and for the same reason: the cases are exact rather than approximately seeded.

#### A folded run is one période and several créneaux, and the file has to say both
`CoveredSlotFolder` + `StageAssignmentExportQueries.SlotCoverageQuery`. Under `SingleService`
`SchedulePublisher` collapses the `kₛ` cells of a run into **one** `ServicePeriod` spanning them —
correctly, since the student stands in one service and is marked once — so the Périodes sheet showed
one row where the grid authored three columns, and the axis those columns belong to was nowhere in
the document. Reported 2026-08-31: « on ne voit qu'une période alors qu'on en a trois ».

- **Both facts are on the row.** « Découpage » still reads « Période unique » and « Nb périodes » is
  still 1 — the fold was never the defect, the silence about what it folded was. Beside them,
  **`Nb créneaux`** (a number, so « publiés sur trois créneaux » is a filter), **`Créneaux`**
  (« P1-P3 ») and, on the Périodes sheet, **`Détail des créneaux`** — one line per column with *its
  own* window and worked-day count, which is exactly what the folded période's span cannot state.
- ⚠ **Still one row per période, never one per créneau.** A run is marked once; repeated across its
  three columns the note is counted three times by the first pivot anybody builds. The unit that
  carries a verdict stays the unit of the sheet.
- ⚠ **Read through `ServicePeriodSlotCoverage`, never `ServicePeriod.CohortSlotAssignmentId`.** That
  FK names only the **first** cell of a run, so the trailing columns — the entire subject here — have
  nothing pointing at them. Same trap as `RotationCycleContext`'s, and the fixture seeds coverage for
  every cell or the case proves nothing.
- **A période with no coverage leaves the cell empty, not `0`.** An ad-hoc période — imported
  history, a délocalisation, a revalidation — came from no grid, and « 0 » there reads as a count that
  failed. « Origine » already says « Hors grille » for exactly those rows.
- **Only consecutive numbers merge** (`P1, P3-P4`), as `GroupNumberRanges` does and for the same
  reason: « P1-P3 » is a claim that P2 is in the run, and a run that skipped it never held that
  column. The name is the créneau's authored label, falling back to `P{n}`.
- Measured on the live base 2026-08-31: **5 831 grid-linked périodes covering 7 497 cells**, the
  1 666-cell difference being folded runs. 5MED Gynécologie Obstétrique is 833 périodes each covering
  **3** créneaux (P4 08/12→07/01, P5 08/01→07/02, P6 08/02→07/03); the other six 5MED stages are
  `PerPeriod` and cover one apiece.

#### Who leads a service is one rule, and both documents ask it — `ServiceChefDirectory`
`Application/Hospitals/Chefs/`. The resolution order was a private method inside
`GetLevelRepartitionQueryHandler` until the stage export needed the same answer; two documents of one
faculty disagreeing about who leads a service is the drift `StageScoring` and
`ServicePeriodLifecycle` exist to prevent. Pure directory + `ServiceChefProvider` that builds it —
the same split as `WorkingDayCalendar` / `WorkingDayProvider`.

- **Order is authority order**: the tenure open on the date → the sitting chef → the legacy note
  (`ServiceChefSourceNote`). Unchanged; only its address is.
- ⚠ **The as-of date is per question, not per build.** The répartition asks once, at the start of the
  axis. The export asks **per période**, because a file covering a year of rotations spans months and
  a chef who took over in January did not lead the students who stood there in October. That is why
  the whole tenure trail is loaded rather than filtered in SQL — a predicate cannot be pushed down
  for a date that is not known yet — and the trail is bounded by the services in scope.
- ⚠ **`Origine du chef` travels beside the name, always.** 140 of the 148 imported services name
  their professor only in a free-text note the Access base last recorded, and that note is
  **undated**: printed unqualified it claims this student served under that chef, which nothing in
  the base supports. « Affectation » / « Note (import) » / « Mixte » — same reasoning as
  `OutcomeSource` and `CnpnSource`.
- **Linking the professor in Personnel is what upgrades the rows**, and the export needs no change
  for it: a tenure or a sitting chef simply outranks the note. Measured 2026-08-31: 2 of 148 services
  now carry one, against 140 carrying only the note.
- ⚠ **Two flat queries, not one projection carrying the tenures.** A tenure projects to a computed
  element with no key, and a collection of those inside a `Select` is the shape Npgsql refuses —
  the family that killed the macro plan. `SqlTranslationTests` pins both.

#### Three sheets, because two questions are asked at once
« Stages » is one row per **attempt** — the unit that carries a note and a verdict, and therefore the
unit a PV is drawn from. « Périodes » is one row per **période** (with the créneaux it covers stated
on that row — see above). « Synthèse » counts the verdicts per
stage. Folding the detail into the first sheet would either lose it or make every row a paragraph;
leaving the first sheet out would hand a reader several lines per student and nowhere to read a
verdict. **`Réf. stage` is on both sheets and is the join** — a detail nobody can key back to the row
it belongs to is a detail nobody reads.

- ⚠ **Scoped by the *registration's* level, not the stage's.** The file is « la promotion et ce qu'elle
  a fait cette année », so a 6ᵉ année student revalidating a 3ᵉ année stage belongs on the 6ᵉ année's
  document — his own. Both levels are printed, which is what makes the row readable as a rattrapage
  rather than as one filed in the wrong place. (`GetStudentLevelDossierQuery` scopes the other way, on
  purpose: it answers « what does this student owe *at this level* ».)
- ⚠ **The year is read, never inferred from dates** — `a.Registration.AcademicYearId`. The two rules
  disagree on 7 030 of 105 626 périodes and the registration is right every time.
- **The unmarked rows are in the document by default.** A file whose whole purpose is « où en est la
  promotion » must show the holes, or a missing évaluation reads as a student nobody planned.
  `onlyEvaluated=true` is the caller saying the file is a PV rather than a state of play — and it is
  the switch a **pre-validation** export will reuse, not a second pipeline.
- **« Moyenne du stage » is legitimate** — the mean of the students' notes *within one stage* is a
  class average. It is the mean *across* stages that this project does not have and must not invent.
  « Taux de validation » is measured over the whole population, not over the evaluated part: a stage
  with one mark entered is not 100 % validated.

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

## ⚠ Rebuilding the base from the Access file is not « migrate, then import »

Three things break a naive `drop → dotnet ef database update → import`, and two of them break
**silently**. Measured 2026-09-01.

**1 · The CNPN data migrations refuse to run against an empty base.** `Cnpn1650Med3Stages`,
`Cnpn1650ImmersionStages` and `Cnpn1650Med3CatalogueAlignment` open with
`RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine »…'` — they need the `Levels` and `Stages` the
*import* creates. So the chain is:

```
1. dotnet ef database update 20260830143914_PriorEnrolment   # the last schema-only migration
2. PGSH.LegacyImport --source Medecine.mdb --connection <cs> --apply
3. PGSH.LegacyImport --seed-curricula --connection <cs> --apply
4. dotnet ef database update                                  # the remaining CNPN migrations
5. PGSH.LegacyImport --stamp-cnpn --connection <cs> --apply
```

**2 · Step 5 is the one nothing else does — `CnpnHistoryAttributor`.** The student attribution was a
single `UPDATE` inside `CnpnVersioning` and the registration backfill another inside
`RegistrationCnpnAndLevelEffectivity`. Both were written to run *over* data already present. Replayed
in the order above they run before the import, stamp nobody, and are then marked applied — so nothing
runs them again, and the base ends with 10 200 students and 49 500 registrations carrying a null
text. ⚠ **Every reader falls back on null gracefully, so nothing complains**: the déliberation stops
knowing whose year might be his last, the final-year gate stands aside for everyone, and
`CohortProvisioner` plans against requirement sets nobody is bound by. The pass reuses
`EntryYearDeduction` and `CnpnAssignment` rather than restating them, never moves a *confirmed* stamp,
and never overwrites a registration's own.

**3 · The CNPN texts lose the intake year they are selected by — and that is silent too.**
`CnpnVersioning` reads them out of `AcademicYears`
(`(SELECT "Id" FROM "AcademicYears" ORDER BY "StartDate" LIMIT 1)` for 2174.18 and PHARM-LEGACY, the
row labelled `2024-2025` for 1650.25). Running before the import, that table is empty and all four
texts are stored with **no intake year at all**.

⚠ **A text with no intake year is not malformed — it is *citation-only***, which arrêté 2175.22
legitimately is. So nothing throws and nothing refuses; `CnpnAssignment.SelectVersionAsync` simply
finds no candidate for anybody. Measured on the first 2026-09-01 rebuild: **10 185 of 10 185 students
unresolved, 0 stamped**, reported as a count by a pass that returned success. Closed twice over —
`CnpnIntakeYearsBackfill` fills the three that are meant to have one (never 2175.22), and
`CnpnHistoryAttributor` now **refuses** when it can place nobody at all, because one unplaceable
student is a fact and the whole population is a broken catalogue.

**4 · The import cannot restore what was authored here.** Nothing in `Medecine.mdb` expresses it, and
it is not test residue. Measured on the live base before the 2026-09-01 rebuild:

| | count | why it cannot be regenerated |
|---|---|---|
| `Holidays` | 24 | Aïd, Moharram and Mawlid follow the Hijri calendar, turn on observation of the crescent, and are announced by decree — they **cannot be generated, only entered** |
| `StageAllowedServices` | 146 | authored per stage; the source has no such column |
| `CnpnLevelEffectivities` | 3 | « ce texte régit ce niveau à partir de cette année » — authored, and it decides the text of every registration created there afterwards |
| `ServiceChefAssignment` | 2 | the only dated chef evidence; 140 of 148 services carry only an undated legacy note |
| `AcademicYears` → 2026-2027 | 1 | the Access base stops at 2025/2026 |

Dump those **before** dropping anything. What is *not* preserved is planning the rebuild makes
meaningless anyway — partitions, `StageSlot`s, cells, and the verdicts of a year about to be
re-imported.

⚠ **Natural keys where they are unique, ids where they are not — and the difference is measured, not
assumed.** The obvious rule (« never key a restore on ids, the import regenerates them ») is right in
general and produced a wrong restore here: **service names are not unique.** 25 are shared across
hospitals — « Pharmacie » exists in 9 — and « Urologie » appears twice inside one hospital, so a
`JOIN Services ON Name = …` fanned **146 `StageAllowedServices` out into 178**. `Service` carries no
external identifier at all: the importer keys it on the Access `CodeS` and does not persist it.

What makes ids usable is that the import is **deterministic**, and that was checked rather than
believed: the pre-rebuild dump restored beside the rebuilt base and joined on `Id` gives 148/148
services identical, 0 stages and 0 levels differing. The restore therefore **asserts its own counts
in SQL** — `RAISE EXCEPTION` on a mismatch, so psql exits non-zero and the rebuild stops. That
assertion is what a silent fan-out needs, and what its absence cost.

⚠ **The two seeded employees have to be restored too.** `PGSH.MigrationService` creates them at
Aspire startup, which a rebuild never runs — so the chef tenures pointed at users that did not exist
and restored **0 of 2**, without an error.

⚠ **The 2026-2027 year has to be restored before the effectivity rules**, one of which takes effect
from it — and `IX_AcademicYear_IsCurrent` is unique and filtered, so demote and promote are **two
statements in that order**, never one `UPDATE`.

## Key Design Conventions

- **NuGet versions** are centralized in `Directory.Packages.props` — never add `Version=` in `.csproj` files.
- **Implicit usings** and **nullable reference types** are enabled project-wide — don't add `using System;` etc.
- Domain entities go in `PGSH.Domain/`, grouped by domain folder. Value objects and enums alongside their entity.
- `int` PKs for reference/catalog data (Level, Stage, Cohort, Hospital, Center, Service). `Guid` PKs for operational/transactional data (Registration, InternshipAssignment, ServicePeriod, User, etc.).
- Enum-backed status fields stored as `varchar` via `.HasConversion<string>()` in EF configuration.
- CORS is open (`AllowAllForDev`) in development. Lock it down before production (see Phase 12).
- `PermissionProvider.GetForUserIdAsync` is a stub returning empty — do not rely on it until Phase 8.
- No step-comments (`// 1. Fetch`, `// 2. Validate`) — code should be self-documenting. Only add comments when the WHY is non-obvious.
