# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**PGSH** — *Plateforme de Gestion des Stages Hospitaliers* — manages hospital internships for medical and pharmacy students (Medecine, Pharmacie, Master, Doctorat programs). It covers the full lifecycle: hospital structure, academic groups, student registrations, rotation planning, execution, attendance, and evaluation.

The documents that carry the rest are mapped just below.

## ▶ The map — read the document before you touch the area

`CLAUDE.md` holds what is true of **every** change: the architecture, the testing contract, and the
traps that bite any handler. Everything area-specific lives in `docs/`, in the same words it was
written in — the file is not a summary of the rule, it *is* the rule.

⚠ **These are not background reading.** Each one records defects that shipped, were measured on the
live base, and are invisible to the test suite. Open the document named for the area **before**
planning a change to it, not after a review finds the same defect again.

| Touching this | Read | Because |
|---|---|---|
| a requirement set, a CNPN stamp, `Curriculum`, the Stages catalogue figures | [`docs/cnpn.md`](docs/cnpn.md) | what a student owes is a fact about a **registration**, not about the student — and the read order is always `r.CnpnVersionId ?? r.Student.CnpnVersionId` |
| the rotation cycle, the macro plan, `RotationArranger`, `SchedulePublisher`, the planning grid, a stage's allowed services | [`docs/planning-rotation.md`](docs/planning-rotation.md) | the axis is `T = Σkₛ`, the balance is per **column**, an unscoped auto-arrange is a fill that silently decides the year — and the axis is laid on the **promotion's** calendar, exam weeks included |
| `AcademicGroup`, partition labels, pauses, unpublishing, clearing or deleting part of a plan | [`docs/planning-rosters.md`](docs/planning-rosters.md) | a roster is keyed **(year, level, number)**, and an affectation does not hang off the roster pointer — so « vider le groupe » leaves every one of them where it was |
| `Registration.Status`, a bulk canvas or roll, `RegistrationHold` | [`docs/year-closing.md`](docs/year-closing.md) | PGSH cannot know who passed — the faculty declares it, silence means opposite things on the two documents, and a refused row loses the faculty's statement |
| what a student owes, who may enter a final year, re-opening a failed stage | [`docs/progression.md`](docs/progression.md) | « entrer » means *begin*, not *be registered in* — reading it the other way refused a quarter of a promotion the faculty had named |
| creating, correcting or deleting an `AcademicYear`, or moving the current-year flag | [`docs/academic-year.md`](docs/academic-year.md) | `IX_AcademicYear_IsCurrent` is unique and filtered, so demote and promote are **two statements in that order** |
| capacity, admissibility, occupancy maths, who leads a service, the chef's worklist | [`docs/services.md`](docs/services.md) | quotas **replace** `Service.Capacity` rather than sitting under it, no rows means *open*, and a load over a window is the **peak inside it**, never the sum |
| a stage served outside the faculty, `Service.IsExternal`, the mass délocalisation, the dates it is recorded under, how many students *stand* in a service | [`docs/delocalization.md`](docs/delocalization.md) | a délocalisé stays in his cohorte and must stop occupying the service he left — counted per membership, sending sixty students away relieved the grid by **nothing** — and the window is the **cohorte's** passage, never the stage's whole axis |
| an export sheet, column, or a second export | [`docs/exports.md`](docs/exports.md) | an export is the one read deliberately exempt from pagination, and a column blank on every row reads as a column the export forgot |
| a bulk act on the live base, a transaction, rebuilding from `Medecine.mdb` | [`docs/operations.md`](docs/operations.md) | the base **is** the faculty's data; the rebuild is not « migrate then import », and it fails silently |
| an audited act or act code, anything measured in worked days, a « suspension d'examens » | [`docs/audit-calendar.md`](docs/audit-calendar.md) | a refused act must write nothing, an empty holiday calendar quietly means "minus weekends" — and there are **two** calendars, the faculty's and each promotion's, so a reader that knows its (année, niveau) must ask for that one |

### The other documents at the repo root

| File | What it is | When to open it |
|---|---|---|
| [`SCHEMA.md`](SCHEMA.md) | the database schema, delete behaviours, enum reference | before a migration, or whenever an `OnDelete` decides whether a guard is needed |
| [`PHASES.md`](PHASES.md) | the development roadmap, phase by phase | to find out whether a gap is deferred work or an oversight |
| [`PLANNING.md`](PLANNING.md) | the crossover arithmetic, and the end-to-end procedure for a répartition annuelle | when you need the *procedure* rather than the rule |
| [`NOTES.md`](NOTES.md) | accumulated domain knowledge and how each finding was measured | when you need the evidence behind a rule, or the story of a defect |
| [`HANDOFF.md`](HANDOFF.md) | the live queue, and the last few sessions | at the start of a session, to find what is actually waiting |
| [`SMOKE-TEST.md`](SMOKE-TEST.md) | the manual verification pass over the current sessions | before handing work back for the user to check |

Superseded history lives in [`HANDOFF-ARCHIVE.md`](HANDOFF-ARCHIVE.md) and
[`SMOKE-TEST-ARCHIVE.md`](SMOKE-TEST-ARCHIVE.md). Nothing there is current; it is kept because it
records *why* a decision was taken.

## ⚠ The rules that bite on any handler

The detail is in the documents above. These are the ones that have shipped a defect in more than one
area, so they are worth carrying in your head on **every** change:

- **An omitted academic year means the current one, never all of them.** Widening on absence is the
  defect, not the fallback. Resolve through `AcademicYearResolver`; a read that genuinely spans years
  says so some other way. → « Shared helpers » below
- **Filtering by level *and* year is one `Any`, never two.** 2 635 students here have repeated, so
  each predicate lands on a different registration: « 5ᵉ année, 2026-2027 » is 833 students asked as
  one `Any` and **2 127** asked as two. `RegistrationStatus` joins the same `Any`.
- **Paginate by what the response *contains*, not by the handler's return type.** A single-object
  response hides unbounded collections from any `List<T>` grep — that is what shipped 4 725 students
  in one object. To show a count, ask for `pageSize: 1` and read `TotalCount`. → « Shared helpers »
- **A collection subquery in a projection is the shape Npgsql refuses.** Reach for a flat, top-level
  query keyed on the parent id and fold in memory. Half of this is checkable without a database —
  add a case to `SqlTranslationTests`. → the Testing section below
- **`AsNoTracking()` on a reusable subquery reaches its host**, so a composed query mutates detached
  objects and `SaveChanges` writes nothing. A shared query states no tracking behaviour; each caller
  states its own.
- **An un-Included collection is indistinguishable from an empty one** — and the in-memory provider
  fixes navigations up from the change tracker, so this suite *cannot see* the mistake. Ask the store
  for the fact and let the aggregate decide what to do about it.
- **Say what a blank means.** One number standing for two states is the recurring defect in this
  codebase: « aucune période » and « rien n'est encore réparti » call for opposite acts. A warning
  that fires whatever the data says is noise, and noise is dismissed — which puts the real one out of
  sight.
- **A cell a human chose is not the arranger's to rewrite.** `CohortSlotAssignment.Source`
  (`Arranged` / `Pinned`) is read as a lock exactly like publication, and the count of what was left
  alone travels in the result (`PinnedCellsKept`). Before it, an auto-arrange destroyed every
  nominative placement in its reach reporting a perfectly normal `Assigned = N`. Any new act writing
  or deleting cells has to make the same distinction. → [`docs/planning-rotation.md`](docs/planning-rotation.md)
- **A service can be held out of the rotation** — `StageAllowedService.PlacementMode = Reserved`.
  ⚠ Its capacity leaves `TotalCapacity` with it, so whatever withholds places must **say how many**.
  And note what does *not* enforce this: `RotationArranger` computes `saturatedServices` **after**
  `SaveChangesAsync`, as a report — the placement weights by capacity and never reads live occupancy,
  so a service cannot be reserved by filling it first. → [`docs/services.md`](docs/services.md)
- **A date derived from a stage is not a date about a cohorte.** An axis holds one column per
  partition, so `min`/`max` over *all* a stage's créneaux is as many times too long as there are
  partitions — the délocalisation window wrote **14/09/2026 → 25/03/2027** into twelve dossiers for a
  stage those students serve in one month, and would have overlapped every other stage of their year
  the moment it was published. Anything derived per (stage, année) and then applied to a group has to
  be re-asked at the level it is actually true at, and a bulk act resolves it **after** it knows who
  it is acting on, never once before the loop. → [`docs/delocalization.md`](docs/delocalization.md)
- **Confirm what cannot be undone.** A bulk act that writes onto rows nobody named carries the count
  the operator was shown and refuses on a mismatch — a boolean cannot do it, because a row created
  between the preview and the apply is the whole risk.
- **An audited act has to reach a `SaveChanges`, and its entry has to say *how much*.**
  `AuditLogPipelineBehavior` stages the row **before** the handler and the handler's `SaveChanges`
  commits it — which is what makes a refused act write nothing, and what makes an act that never
  saves write nothing *either*. ⚠ So a handler writing only through `ExecuteDelete`/`ExecuteUpdate`
  ends with an explicit `SaveChangesAsync`. And the code alone is not the record: « Réinitialiser les
  cohortes » on a virgin promotion and on a published one are the same code and unrelated events, so
  the handler deposits what it actually destroyed through **`IAuditTrail.RecordOutcome`**, before it
  saves. → `docs/audit-calendar.md`
- **A validator describes what a *save* must satisfy, not what a good record looks like.** It is
  applied to rows that already exist; a rule the imported data fails makes those rows read-only.
  → its own section below
- **A delete asks the schema first, or the constraint answers for it.** A `RESTRICT` FK reached
  without a guard surfaces as `DbUpdateException` → a **500** whose only content is the name of a
  PostgreSQL constraint; a `CASCADE` one takes its children **silently**. `SCHEMA.md` says which is
  which, and the count has to be asked *before* the delete — afterwards there is nothing left to
  count. Count every reason **together** rather than short-circuiting on the first: a user who clears
  one and is then told about the next has been sent round the loop twice, and the second trip looks
  like the first fix having failed. `DeleteAcademicYearCommand` is the shape; `DeleteStageCommand` and
  `DeleteServiceCommand` were the two that had no guard at all. ⚠ And a `CASCADE` is not automatically
  the right bargain: it is, when the children are meaningless without the parent (a créneau of a
  deleted stage), and it is **not** when the join row hangs off a *second* aggregate that survives
  amputated — `StageAllowedService` carries a `Rank` and a `PlacementMode`, so deleting a service
  refuses on the stages authorising it rather than punching a hole in an order
  `ServiceRotationOrder` holds contiguous from 1. → [`docs/services.md`](docs/services.md)
- **`ErrorType.Problem` means a *fault*, and nothing else may use it.** `CustomResults` maps it to
  **500**, and the client discards `detail` above 500 (`errorMiddleware` shows the fixed « Une erreur
  serveur est survenue »), so a business refusal typed `Problem` loses the one sentence that explains
  it. Thirteen did. The worst was `AcademicYearResolver`'s `NoCurrentAcademicYear` — the fallback of
  *every* handler that omits a year, i.e. a base with no current year made **every screen** 500 with
  nothing on any of them saying to pick a year. A refusal is `Conflict` (the request meets the state),
  `Validation` (the request is malformed), `NotFound` or `Forbidden`; `Problem` is for an unreachable
  archive or a `pg_dump` out of disk. → `PGSH.Tests/Integration/ErrorStatusMappingEndpointTests.cs`
- **A multi-step write is one `ExecuteAtomicallyAsync`, or it is a half-written state somebody will
  read as deliberate.** ASP.NET cancels the token whenever the tab closes or the connection drops, so
  "the request stopped between two saves" is the ordinary case, not the exotic one. The roster cut
  committed its rosters and their members separately — a promotion left carrying **empty rosters**,
  and re-running builds a *second* set beside them because the numbering continues. ⚠ It does **not**
  compose with `IAuditTrail.RecordOutcome` (a retry re-stages the entry as opened and the trail holds
  its replacement, giving two rows): a handler picks one. → « Shared helpers », `IAuditTrail`
- **The base is live.** Take a `pg_dump -Fc` before every bulk act, and never write to the base to
  verify something. → [`docs/operations.md`](docs/operations.md)

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

⚠ **The error's `ErrorType` is what picks the status code**, in `CustomResults.GetStatusCode`:
`Validation` → 400, `NotFound` → 404, `Conflict` → 409, `Forbidden` → 403, and `Failure`/`Problem`
→ 500. `Failure` additionally *masks* its own message (« An unexpected error occurred »). So typing a
business refusal `Problem` is how a carefully written sentence becomes « Une erreur serveur est
survenue » on screen — see the rule above.

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
  - ⚠ **And it *refuses* `ExecuteDelete` / `ExecuteUpdate` outright** — « not supported by the current
    database provider ». A handler that writes only through them therefore has a **success path no
    test in this repository can reach by any route**: the handler tests can assert its refusals and
    nothing else, and an endpoint test 500s before it touches the act. Measured 2026-09-10, and it is
    how `DeleteAllGroupsCommand` and `EmptyAllYearGroupsCommand` carried `IAuditableCommand` from
    Phase 20 onward while **writing no journal entry at all** — every write went through
    `ExecuteDelete`, so nothing ever called `SaveChanges` to commit the pending row.
  - **`TestHarness.NewSqliteContext(connection)` is the way in** (`OpenSqlite()` holds the
    connection open — an SQLite in-memory database dies with it). Relational, so it runs both, and it
    enforces foreign keys and unique indexes as a bonus — that is what exposed every fixture building
    a `Hospital` with `CenterId = 0`. ⚠ **It is not PostgreSQL**: no filtered indexes, no
    `NULLS NOT DISTINCT`, different type affinities. It answers « does this act run and write », never
    anything about the real schema. `ExecuteDeleteAuditTests` is the pattern.
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
  - ⚠ **One ceiling, and it *clamps* — `QueryableExtensions.MaxPageSize` (200).** A larger page is
    served short and the response still carries the true `TotalCount`, so nothing is hidden. Every
    query validator states it through `PaginationRules.IsAPageSize()` / `.IsAPageNumber()`, never by
    hand. Four validators had spelled their own **stricter** 100 and *refused*, so a request the
    pipeline would have served never reached it: `GET /stages?levelId=3&pageSize=200` — the CNPN
    editor's « which stages may this text require » read — 400'd on every open, the stage list came
    back **empty**, and the picker rendered disabled saying « Tous les stages du niveau sont listés ».
    Two stages the faculty had just created could therefore not be required by any text, and the
    refusal read on screen as a broken control rather than as a rule. The client names the same
    number once (`common/constants/pagination.ts`). Pinned by
    `PGSH.Tests/Integration/PaginationBoundsEndpointTests.cs`.
  - ⚠ **Paginate by what the response *contains*, not by the handler's return type.** A single-object
    response hides unbounded collections from any `List<T>` grep: `GetGroupByIdQuery` returned one
    `GroupDetailResponse` carrying 4,725 students — one per registration in the "Non réparti" group —
    each with two correlated sub-queries, and that is what crashed the browser.
  - ⚠ **Anything scoped per academic year must filter on it server-side.** Cohorts exist per
    (stage, group) and groups per year, so an unscoped stage query returns every year it ever ran
    (681 rows for "Chirurgie"). Never fetch all years and filter in the client.
  - ⚠ **To show a count, ask for `pageSize: 1` and read `TotalCount`** — never fetch the rows.
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
- **Naming a group of students is `StudentSelectionResolver`** (`Application/Students/Selection/`),
  never a per-act parse. Roster ids ∪ registration ids ∪ a pasted list of CNE/Apogée lines, with a
  row for every line that names nobody and `NotFound` kept distinct from `WrongYear`. Shared by the
  mass délocalisation and the nominative roster assignment; the FIFO choice will be the third.

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
  - ⚠ **`errors[]` sits at the *top level* of the problem document, never under `extensions`.**
    `Results.Problem(extensions: …)` fills `ProblemDetails.Extensions`, which is `[JsonExtensionData]`
    — the members are written flat, exactly as RFC 7807 says extension members are. The `ApiError`
    type declared `extensions.errors`, so the global `errorMiddleware` never found the array and
    *every* refused save toasted « Données invalides · One or more validation errors occurred » — the
    fixed sentence of `ValidationError`, which names no field and no rule. `StagesPage` read the real
    shape and was right all along; the middleware behind it was not. Pinned by
    `StageEndpointTests.A_refusal_carries_a_message`.
  - ⚠ **And a non-validation refusal carries its code in `title`, not in `errors[]`.**
    `Error.Conflict("Schedule.AlreadyPublished", …)` produces no `errors` array at all, so
    `StageDetailPage.extractErrorCode` — which read only `errors[0].code` — always returned null and
    both of its branches were dead code. Read `title` first.

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
