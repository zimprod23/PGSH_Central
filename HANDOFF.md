# HANDOFF.md

> **▶ RESUME HERE (next session).** Temporary vs Definitive transfer model is **DONE (2026-06-25)** — see
> "Transfer types" below. Next, in order:
> 1. **Verify the transfer model end-to-end** once the stack is running (recipe in that section).
> 2. Optional refinement: scope the chef gray/green markers to the **current stage's chef** + show **net
>    group headcounts** (deferred from the transfer build — markers still use the existing group-model logic).
> 3. Then **"do delocalization"** (out-of-faculty stage, paper validation entered by admin) · **"do
>    revalidation"** (fail → revalidate in same service unless a transfer/déloc demande; may span years —
>    open: group-all-revalidations vs ad-hoc). Full 4-type model in agent memory `project_student_mobility`.
> 4. Remaining Suivi item: small-service planning skip (#3, needs your decision — see that section below).
>
> **⚠ BEFORE TESTING:** three migrations are **pending DB apply** — `EvaluationModes`,
> `ServicePeriodIsStarted`, `TransferType_OnCohortMembership` — they auto-apply via `MigrationService` on the
> next `dotnet run --project PGSH.AppHost` (no live DB was reachable this session to apply standalone: a local
> Postgres answers on :5432 but rejects postgres/postgres; Aspire's container uses a random port+password).
> Build backend via `PGSH.Infrastructure.csproj` (API DLLs lock while the app runs); add ef migrations with
> `--startup-project PGSH.Infrastructure` (design-time factory) since the running API blocks the API build.
> _Updated 2026-06-25._

## ▶ Transfer types: Temporary vs Definitive ✅ DONE (2026-06-25)

**Open question RESOLVED — transfer unit = GROUP (not service), for both types.** Full 4-type mobility model
captured in agent memory `project_student_mobility`. This session implemented types 1+2 (transfer); déloc (3)
and revalidation (4) are still queued.

- **Domain**: new `enum TransferType {Temporary,Definitive}` (`Domain/Registrations/TransferType.cs`).
  `CohortMembership` gained `TransferType` (text via `HasConversion`, **default `Definitive`** so the initial
  membership + legacy rows never look like a loan) + nullable `OriginalCohortId` (where a temporary loan
  returns). `InternshipAssignment.TransferToCohort(...)` takes a `TransferType` and records type +
  `OriginalCohortId` (= previous cohort, only for Temporary). New `CompletePeriod` path: when the stage
  finishes (all periods complete → `Completed`), `EndTemporaryTransferIfAny` closes the active temporary
  membership (sets `EndDate`) and raises `TemporaryTransferEndedDomainEvent` — remaining stages were never
  moved so nothing else is undone.
- **Application**: `TransferStudentCommand` gained `Type` (default **Temporary**) + nullable `StageId`.
  Handler branches: **Definitive** = existing behaviour (registration group changes + ALL active assignments
  cascade, same-group guard applies); **Temporary** = registration group **untouched**, only the named
  stage's active assignment(s) move (validator requires `StageId` when Temporary; returns
  `Transfers.NoActiveAssignment` 409 if none). New `TemporaryTransferEndedEventHandler` writes a
  `History.GroupTransfer` row (`temporaryReturn:true`) for the return. Added `StageName` to
  `InternshipAssignmentSummaryResponse` (used by the transfer modal's stage picker).
- **Auto-revert correctness**: the revert reads `MembershipHistory`, so **all three** CompletePeriod callers
  now `.Include(a => a.MembershipHistory)` (`StagePeriodRunner`, `CompletePeriodsCommandHandler`,
  `CompleteServicePeriodCommandHandler`) — otherwise it silently no-ops.
- **EF + migration**: `20260625091416_TransferType_OnCohortMembership` (TransferType text **defaultValue
  "Definitive"** hand-set; `OriginalCohortId` int null). **Pending DB apply** (see ⚠ above).
- **Frontend**: `GroupDetailPage` `TransferModal` got a `SegmentedControl` Temporaire (1 stage) / Définitif
  (année) + a stage `Select` (this student's Planned/Ongoing assignments via
  `useGetInternshipAssignmentsQuery`, `skipToken` when no student) shown only for Temporary, with helper
  text. `TransferStudentRequest` gained `type`+`stageId`; `transferStudent` now also invalidates
  `Assignment/LIST`. Build: backend `PGSH.Infrastructure` 0 errors; frontend `npm run build` + eslint clean
  on changed files.
- **Test recipe (after stack restart)**: pick a student doing a stage → Transférer → **Temporaire**, choose
  that stage + a target group + reason → confirm the chosen stage's assignment now sits in the target group's
  cohort while OTHER stages stay put and `Registration.AcademicGroupId` is unchanged; close that stage's
  periods (Suivi → Clôturer) → a `GroupTransfer` "retour" history row appears + the temporary membership has
  an `EndDate`. Repeat with **Définitif** → registration group changes + all active assignments cascade.

## ▶ Current work stream: Student Mobility (design LOCKED)

Full design in agent memory `project_student_mobility.md`. Build order **1 → 2 → 3 → 4**.
Recently shipped before this (verified): chef-worklist granularity + truncation fix, planning-grid
click-to-edit perf, optimistic allowed-services, central `pageSize` clamp (≤200), `GET /service-periods`
admin-only. Stress-test recipes live in `NOTES.md` → "Regression / Stress Checks".

**#1 — Normal transfer: traceability + chef gray/green visibility ✅ DONE (2026-06-05).**
- **Reason now required**: `TransferStudentCommand.Reason` is non-nullable + validator `NotEmpty`.
- **Same-group guard**: transferring to the student's current group now returns `Error.Conflict`
  ("AcademicGroups.SameGroup" → 409). Loop also skips any assignment whose target cohort == current cohort.
- **`DbUpdateConcurrencyException` on transfer — FIXED.** Diagnostic confirmed `CohortMembership[Modified]`:
  the *new* membership in `InternshipAssignment.TransferToCohort` was created with a pre-set
  `Id = Guid.NewGuid()` and added to an already-tracked assignment. Because `CohortMembership.Id` is a
  store-generated key (`ValueGeneratedOnAdd` by convention), a non-sentinel key on an entity reached via a
  tracked parent makes EF classify it **Modified** (UPDATE a non-existent row → 0 rows) instead of **Added**.
  Fix: don't set `Id` in `TransferToCohort` — let EF generate it (→ Added → INSERT). The affectation /
  schedule paths were unaffected because they `DbSet.Add(rootAssignment)`, cascading children as Added
  regardless of key. (Diagnostic try/catch was removed after the fix.)
- **Domain-event publishing under a pooled DbContext — FIXED (systemic).** After the save succeeded, MediatR
  threw `Cannot resolve INotificationHandler<…> from root provider because it requires scoped
  IApplicationDbContext`. Cause: Aspire `AddNpgsqlDbContext` **pools** `ApplicationDbContext`, so pooled
  instances are built from the **root** provider → the injected `IPublisher` was the root mediator →
  notification handlers (which need the scoped `IApplicationDbContext`) resolved from root and failed. This
  broke **every** domain event with a scoped-dependency handler (validation, rejection, status change,
  group/cohort transfer), not just transfer. Fix: `ApplicationDbContext` now injects the pool-safe singleton
  `IServiceScopeFactory` and `PublishDomainEventsAsync` opens a fresh `CreateAsyncScope()` to resolve a
  scoped `IPublisher` per save. Handlers run in their own scope/context (correct — events fire post-commit).
- **Worklist gray/green overlay** in `GetMyServicePeriodsQueryHandler` (`Application/Employees/MyServices/`),
  surfaced via `ServicePeriodResponse.Transfer` (`TransferMarker{Direction,GroupLabel,ServiceName,Reason,Date}`):
  - **Outgoing** (gray, strike-through, "→ Groupe m · Service Y") = a real published period whose
    `CohortSlotAssignment.CohortId != InternshipAssignment.CurrentCohortId` (student left after publish).
    Destination group/service from the current cohort's slot for the same window; reason/date from the
    active `CohortMembership`.
  - **Incoming** (green, "← Groupe n · Service X") = **synthesized** rows (no real `ServicePeriod`, since
    transfer does NOT re-publish): current cohort's slot lands in a chef service, assignment's active
    membership has a `TransferReason`, and no matching period exists. Origin group/service from the most
    recent closed membership.
  - Transfer rows are **non-actionable** and excluded from the active/à-évaluer/évalué counts + status
    filter (shown only under "Tous"); separate "entrant/sortant" badges at service + window + group level.
- **History DTO field fix**: `StudentHistoryResponse.EventType` → `HistoryType` (serializes as `historyType`)
  to match the frontend contract — the mismatch crashed the student dashboard/history once real history rows
  existed (they were empty before because domain events couldn't publish — see pooled-context fix above).
  Frontend `historyConfig` now exposes `getHistoryConfig()` with a fallback so an unknown type never crashes.
- **Traceability readback** works: `HistoryPage` renders `Metadata` (from/to/reason) generically
  for `GroupTransfer`/`CohortTransfer`. (Polish opportunity: friendlier metadata key labels — `fromGroup`
  shows as "Fromgroup".)
- **Note / known limitation**: transfer never moves or re-publishes `ServicePeriod`s, so the new chef sees
  the incoming student only as an informational green row — they cannot complete/evaluate until the target
  cohort's schedule is (re)published. If actionable hand-off is wanted later, that's a separate change.
- Build: `PGSH.Infrastructure` → 0 errors; frontend `npm run build` passes; no new lint errors (the one
  EmployeeServicesPage lint error at the eval-modal effect is pre-existing). API DLL-copy lock = API running.

**#2 — Evaluations, 3 modes (all evals): ✅ DONE (2026-06-05).** numeric score · validate-whole-period
(no score) · validate-each-objective (pass/fail).
- **Domain**: new `EvaluationMode {Numeric,ValidatePeriod,ValidateObjectives}` + `EvaluationOutcome
  {Validated,NotValidated}` (`PGSH.Domain/Stages/EvaluationMode.cs`). `ServiceEvaluation` gained `Mode`,
  nullable `TotalScore`, `Outcome`, and a `Normalize()` that clears fields not used by the mode and
  **derives** the period `Outcome` for ValidateObjectives (validated iff all *mandatory* objectives pass,
  or all objectives when none mandatory). `ObjectiveScore.Score` is now `int?`; added `Outcome`.
  `InternshipAssignment.SubmitEvaluation` calls `Normalize()`; `RecomputeFinalScore` only counts
  **Numeric** evaluations (validate-only periods leave `FinalScore` null). `EvaluationSubmittedDomainEvent.TotalScore` → `decimal?`.
- **Application**: Create/Update commands + validators carry `Mode`/`Outcome` and per-objective `Score?`/`Outcome?`;
  validators are mode-conditional (`When(...)`). `ServiceEvaluationResponse`/`ObjectiveScoreResponse` +
  GetByPeriod projection expose mode/outcome. Update handler calls `Normalize()` before `RecalculateFinalScore()`.
- **API**: `service-evaluations/{id}` PUT `Request` carries `Mode`/`Outcome`.
- **EF + migration**: `Mode`/`Outcome` stored as `text` via `HasConversion<string>()`; `TotalScore`/`Score`
  made nullable. Migration `20260605205344_EvaluationModes` (Mode `defaultValue:"Numeric"` so legacy rows
  stay valid). **Not yet applied to the DB** — MigrationService applies on next Aspire start, or run
  `dotnet ef database update`.
- **Frontend**: chef `EvaluationModal` (`EmployeeServicesPage`) has a mode `SegmentedControl` (ValidateObjectives
  disabled when the stage has no objectives) + per-objective/period Validé·Non validé toggles; live derived
  result preview. Student `EvaluationDetail` (`StageDetailsPage`) shows a Validé/Non validé badge in place of
  the note when the eval isn't numeric, per-objective too. Types updated in both `employee.types.ts` +
  `student.types.ts`.
- Build: `PGSH.Infrastructure` → 0 errors; frontend `npm run build` passes; lint clean except the one
  pre-existing seeding-effect error in `EmployeeServicesPage`. (API DLLs locked = API running.)

**#2 refinements (2026-06-06):**
- **Chef "Terminer" removed.** Closing a `ServicePeriod` is an **administrative** act (done when the
  scheduled window is due), not the chef's. The chef view (`EmployeeServicesPage`) now shows only
  **Évaluer** (once closed) / **Modifier**; active rows show a dimmed "En attente de clôture" hint, no
  button. Removed the dead `completeServicePeriod` mutation from `employeeApi.ts` (admin keeps its own in
  `adminApi.ts`). This also resolved a chef-side 404 (`/service-periods/[object Object]/complete`) — the
  path is simply gone now.
- **Score auto-calc (10/0 mapping).** `InternshipAssignment.RecomputeFinalScore` now produces a number for
  EVERY mode: numeric objectives use their mark; a validate-only objective/period maps to **10 (validated)
  / 0 (not)**; weighted-average → `FinalScore`. `Result` auto-derives from a **≥10 threshold**
  (`Validé`/`NonValidé`), and the existing admin `Validate()`/`Reject()` still override it terminally
  (evals lock after, so recompute can't clobber). `FinalScore`/`Result` already surface in
  `InternshipAssignmentResponses`. No schema change. (Replaces last turn's "validate-only ⇒ null score".)

**Transfer UI bug fixes (2026-06-06, shipped):** in `GroupDetailPage.tsx` `TransferModal`, a single value
was used as BOTH the academic-year id (groups query) AND the current-group id (exclude filter) — so the
target dropdown showed the student's own group and dropped any group whose id == the year id. Now takes
`academicYearId` + `currentGroupId` separately. Also the reason field was labelled "optionnel" but the
backend requires it (`NotEmpty`) → now `required` + submit disabled until filled.

**Suivi follow-ups (2026-06-06):**
- ✅ **No-op guard DONE.** `AssignmentsPage` disables Démarrer/Clôturer (+ "aucune rotation dans la période
  choisie" hint) when none of the checked cohorts has a rotation in the selected period(s)
  (`selectionHasTargetPeriod`, from the cohort→periods grid map).
- ✅ **Stale "en cours" count (#2) DONE (2026-06-08).** `GetAssignmentStatusSummaryQueryHandler` now counts
  **per ServicePeriod (rotation)** instead of grouping by ASSIGNMENT `Status`. Each in-scope period is bucketed
  into the existing `InternshipStatus` labels: complete+eval → `Evaluated`, complete+no-eval → `Completed`
  (à-évaluer), `IsStarted` → `Ongoing`, else `Planned`; terminal assignment verdicts (`Validated`/`Rejected`)
  override the period state so admin actions still show. Query gained optional `PeriodNumbers` (scoped via
  `ServicePeriod.CohortSlotAssignment.StageSlot.PeriodNumber`); endpoint accepts `periodNumbers`; frontend
  `AssignmentsPage` passes `periodArg` to `useGetAssignmentStatusSummaryQuery` so the card tracks the selected
  period chips. This also fixes the **Clôturer** issue (closing ONE period now shows complete/à-évaluer per
  period, not stuck "en cours" — the card no longer reads the cumulative assignment `Status`). NOTE: the card
  now reflects only published periods; period-less (unpublished) assignments no longer appear in the counts.
  Build: `PGSH.Infrastructure` 0 errors; API only DLL-copy-locked (running); frontend `npm run build` + lint clean.
- ✅ **Bulk start/close perf + idempotency (#4) DONE (2026-06-08).** Took option (a). New stage-level
  `StartStagePeriodsCommand` / `CompleteStagePeriodsCommand` (`Stages/Cohorts/Bulk/StageLifecycleCommands.cs`)
  act on the WHOLE selection in ONE round-trip via shared DI service `StagePeriodRunner`
  (`Stages/Planning/`, mirrors `SchedulePublisher.PublishStageAsync`): load every in-scope assignment +
  periods once, mutate, single `SaveChanges`. Scoping precedence: `CohortIds` (the Suivi UI's arbitrary
  selection) → `PartitionLabels` (whole rotation, macro parity) → all stage cohorts; `PeriodNumbers` narrows
  the window. Idempotency: re-running on an already-started/closed selection loads once and finds nothing
  pending (`!IsStarted` / `!IsComplete` filter) → near no-op, one query + zero writes, not N. Endpoints
  `POST stages/{stageId}/schedule/start` + `/complete` (`StageLifecycleRequest{CohortIds,PartitionLabels,
  PeriodNumbers}`, returns `{started}`/`{completed}`). Frontend `AssignmentsPage` Démarrer/Clôturer now fire
  ONE `useStartStagePeriodsMutation`/`useCompleteStagePeriodsMutation` with `{stageId, cohortIds: selectedIds,
  periodNumbers: periodArg}` instead of the per-cohort `runForSelected` loop (loop kept only for Valider).
  Build: `PGSH.Infrastructure` 0 errors; API DLL-copy-locked only (running); frontend `npm run build` + lint clean.
- ✅ **Chef sees the STAGE per service (#5) DONE (2026-06-09).** `ServicePeriodResponse` gained `StageName` +
  `LevelLabel` (nullable). Projected from `…Cohort.Stage.Name` / `.Stage.Level.Label` in BOTH
  `GetMyServicePeriodsQueryHandler` (published rows from `InternshipAssignment.Cohort.Stage`, incoming-transfer
  rows from the chef-service slot's `sa.Cohort.Stage`) and the admin `GetServicePeriodsQueryHandler`. Frontend
  (`EmployeeServicesPage`, `employee.types.ts`): stage shown in three places — a navy stethoscope line under the
  service name on the card header (distinct stages joined with ` · `, since a service can host several stages
  across windows), a stage badge + level on each **window** header row, and a stage badge in the evaluation
  modal subheader. Build: `PGSH.Infrastructure` 0 errors; frontend `npm run build` passes; lint clean on the
  changed files (the one line-108 seeding-effect lint error in `EmployeeServicesPage` is pre-existing/untouched).
  NOTE: API has no new endpoint — restart the stack so the running API picks up the new Application DLL/fields.
- ⏳ **Planning skips a small allowed service (#3).** By design: `RotationArranger.BuildServiceQueue` weights a
  service `floor(capacity / avgCohortSize)`; a service smaller than one (atomic) cohort gets weight 0 and is
  EXCLUDED (a whole ~group would overflow it) — so e.g. Cardiologie@Harrouchi is dropped if its capacity <
  group size, even when others saturate. Options: raise that service's capacity ≥ group size; OR partial-group
  placement (Phase 7.5 #4, big); OR soften the weight rule. Needs user decision.

**Queued (agreed, not built):**
- ✅ **Temporary vs Definitive transfer DONE 2026-06-25** — see "Transfer types" section at the top.
  Auto-revert at stage end implemented; transfer unit resolved to GROUP.
- **Gray/green marker refinement (deferred from the transfer build):** scope the chef incoming/outgoing
  markers to the **current stage's chef** + show NET group headcounts (old −1, new +1). Markers still use the
  existing group-model logic in `GetMyServicePeriodsQueryHandler`, so a temporary loan's green row shows for
  the target group's service chef (expected under the group model).
- **Delocalization (#3 in the mobility model):** out-of-faculty stage; no eval control; student returns with
  a paper validation entered by admin/scolarité via the #2 validate-only eval + fiche attachment.
- **Revalidation (#4):** fail → revalidate in the **same service** failed, unless a transfer/déloc demande is
  attached (then another group's service). May span academic years. **Open design choice:** keep all
  revalidations of one stage together in a group vs ad-hoc per-student `ServicePeriod`
  (`CohortSlotAssignmentId == null`). See `project_student_mobility` + `domain_revalidation` in agent memory.
- Dedicated admin "edit final score/result" override endpoint (beyond Validate/Reject) — small follow-up.

**Admin "suivi" period-scoped bulk start/close ✅ DONE (2026-06-06).** Root issue found: the old
`CompletePeriodsCommand` closed EVERY incomplete period of a cohort — including FUTURE ones. Now
`StartCohortAssignmentsCommand`/`CompletePeriodsCommand` take an optional `IReadOnlyList<int>? PeriodNumbers`
(scoped via `ServicePeriod.CohortSlotAssignment.StageSlot.PeriodNumber`); null/empty = all periods (old
behaviour). Endpoints `cohorts/{id}/start-assignments` + `/complete-periods` accept optional body
`{ periodNumbers }` (`PeriodScopeOptions`). Frontend `AssignmentsPage` ("Suivi → Affectations"): a
**period chip row** (P1 · dd/MM→dd/MM …, from `getStageSchedule`) in the bulk bar scopes Démarrer/Clôturer
to the chosen periods; none selected = all. Cohort selection (all / by partition A/B) already existed.
adminApi `startCohortAssignments`/`completeCohortPeriods` now take `{ cohortId, periodNumbers? }`
(also updated the call in `StageDetailPage`).
**Confirmed model (user 2026-06-06):** year (global navbar context) → stage → its cohorts. **Macro** = select a
whole partition (sidebar "Par rotation" → "Sél." on A/B/…) and act on all its periods. **Micro** = same
partition selection + pick period chip(s) to scope the action to one period window. **Cross-stage selection
is intentionally NOT supported** — stages are handled individually. (Per-period START is still
whole-assignment in the domain — the window only filters which cohorts start; Clôturer is truly per-period.)
**Period chips refinement (2026-06-06):** the period bar is now its own always-visible toolbar (not buried in
the bulk bar), **defaults to ALL periods selected** (empty `selectedPeriods` ⇒ `effectivePeriods` = all), and
**filters the cohort list**: deselecting a period hides cohorts that don't run in any still-selected period
(cohort→periods read from the `getStageSchedule` grid `cells`). This matches macro planning where a partition
only occupies a window of periods (A=P1–2, B=P3–4). All periods selected ⇒ no cohort filter.

**Per-period START — `ServicePeriod.IsStarted` ✅ DONE (2026-06-06).** Root cause of "the chef sees ALL periods
en cours, even future ones": *started* lived on the assignment (`Status=Ongoing`), not the period — so the chef
worklist showed every published period once the assignment was Ongoing. Fix: new `ServicePeriod.IsStarted` flag.
`InternshipAssignment.StartPeriod(periodId)` activates one period (Planned→Ongoing on first); `Start()`
(single-row admin action) now activates ALL periods. `StartCohortAssignmentsCommandHandler` rewritten to start
PERIODS scoped by `PeriodNumbers` (unscoped = all). `GetMyServicePeriodsQueryHandler.LoadPublishedPeriodsAsync`
now filters `p.IsStarted` — future un-started periods stay hidden from the chef. Migration
`20260606124812_ServicePeriodIsStarted` (default false + backfill `IsStarted=true WHERE IsComplete=true` so
completed/evaluated history stays visible). No frontend change needed: the period chips already send scoped
`periodNumbers` to Démarrer, and the chef worklist filters server-side. So: admin starts P1 for partition A →
only those periods go active → only the P1-service chefs see those students; future periods appear to nobody
until started. NOTE: two migrations now pending DB apply — `EvaluationModes` + `ServicePeriodIsStarted`.

**#3 — Revalidation:** new `InternshipAssignment` for the failed stage on the **current** registration,
placed as an **ad-hoc `ServicePeriod`** (`CohortSlotAssignmentId == null`) into an allowed service chosen
by scolarité — not into a year-1 cohort. Write `History.Revalidation`. Graduation gate: stage satisfied
if ≥1 assignment `Result = Validé` in any year.

**#4 — Delocalization:** new out-of-region movement type; reuse `Hospital`/`Service` catalog (add the
external one if missing); on return record the outcome via the #2 validate-only eval + attach the
**fiche de validation** file. Rare cases.

**Demande = Phase 5 (later):** transfers/delocalizations carry `Reason` + nullable `DemandeId` now;
wire to the real demande in Phase 5.

> Reminder: Clean Arch + CQRS/Result; routes under `/api`, no leading slash; enums as real types;
> build backend via `PGSH.Infrastructure.csproj` (API DLL locked while running); commit only when asked.

---

# Previous work stream — Partition Macro Planning (historical)

Living handoff for the rotation-planning work. See `PHASES.md`, `NOTES.md`, `SCHEMA.md`.

_Last updated: 2026-06-03 (published-lock + student-status fixes done; cross-stage capacity done — Phase 7.5 #1)._

---

## What this work stream is

Make hospital-rotation **planning and affectation flexible** — operable for *all groups
or a single partition / period window* — and add a one-click **macro orchestrator**, to
match the faculty rotation sheets in `example_stage_assignement/` (Med3, Med6).

**Domain model (two levels):**
- **Macro** — `AcademicGroup.RotationGroup` (A, B, C…) splits a level's groups into
  partitions. Each partition runs a stage in a window of periods. Med3: Partition A does
  Médecine in periods 1–2 then Chirurgie 3–4; Partition B mirrored.
- **Micro** — within a partition's window, groups are distributed across services by a
  capacity-proportional cyclic rotation. A cohort = one group = atomic (~15–20 students).

---

## Done (Phase 7.1 — Complete)

All in `PGSH.Application/Stages/Planning/` (DI-registered shared services; handlers and the
orchestrator call them — no nested MediatR):

- `PartitionAllocator` (labelling), `RotationArranger` (scoped cyclic rotation),
  `StudentAffectationService`, `SchedulePublisher`, `CohortProvisioner`.
- **Scoping**: `AutoArrangeStageScheduleCommand` + `AssignAllStudentsByStageCommand` take
  optional `PartitionLabels` + `PeriodNumbers`; new `PublishStageScheduleCommand`. Auto-arrange
  removal scoped to `targetCohorts ∩ targetSlots` (arranging one window never wipes another).
- **`GenerateMacroPlanCommand`** (`POST /stages/macro-plan`): per `(RotationGroup, StageId,
  PeriodNumbers)` → ensure cohorts → affect → arrange → optionally publish. Lenient on missing slots.
- **Partitions are per (year, level)**: `AssignRotationGroupsCommand.LevelId`; `CohortProvisioner`
  matches groups to each stage's level (a label reused across levels never crosses).
- **Capacity-aware allocation**: weight = `floor(capacity / avgStudents)`; services smaller than
  one group are excluded, not force-overflowed. No artificial saturation when per-period capacity ≥ demand.
- **Frontend** (`PGSH.Frontend/src/features/admin/`): `ScheduleGridModal` per-partition/window
  auto-arrange + scoped publish, stacking-guard alert, saturation banner + full-report Drawer.
  `GroupsPage` Macro Plan tab (per-level partition setup, partition×stage matrix with per-cell
  period windows, "Générer le plan" with step toggles). Per-row "Vider le groupe" + "Vider toutes"
  (`EmptyAllYearGroupsCommand`, `DELETE /groups/all/students`). Debounce on the last two searches.

---

## Done since Phase 7.1 (published-lock + student-status fixes, 2026-06-03)

Three correctness/UX gaps in the planning grid + student view — "published is locked" was
enforced inconsistently, and unplanned stages were mislabelled:

- **`DeleteStageSlot` now blocks when a slot has published cells** (`StageErrors.SlotPublished`).
  Previously it cascaded-deleted cells and `SetNull`-orphaned published `ServicePeriod`s → silent drift.
- **Bulk `ClearSlotAssignments` now returns `{ cleared, skipped }`** instead of silently skipping
  published cells under a 200 OK. UI shows "X vidés, Y ignorés (publiés)".
- **Student `StageListPage`** distinguishes **"À venir / Non planifié"** (no `InternshipAssignment`)
  from **"Planifié"** (assignment exists, status `Planned`).

## Next (Phase 7.5)

Priority order:

1. ~~**Global cross-stage capacity (CORRECTNESS).**~~ **DONE 2026-06-03.** New
   `ServiceOccupancyCalculator` (`Application/Stages/Planning/`) measures a service's load as the
   students on it over any **overlapping** slot window, across all stages. Wired into grid display
   (`GetStageScheduleQueryHandler`), auto-arrange saturation (`RotationArranger`), and a new
   **pre-publish guard** (`SchedulePublisher.EnsureCapacityAsync` → `StageErrors.CapacityExceeded`).
   The publish guard was previously **absent** (the error was defined but unused). Per-cohort publish
   fails strictly; stage publish still skips already-published/unconfigured cohorts but now **hard-fails
   on a capacity overflow** rather than silently over-booking.
2. **Long-running ops robustness.** Mutations (démarrer, publish, macro plan) run to completion
   in the background; not aborted by navigation or other requests; no client timeout. Move heavy
   ops to a background job / chunking + progress; ensure handlers are idempotent.
3. **UX**: per-stage capacity-fit gauge; one-click "ajuster les capacités" from the saturation
   drawer; window-picker chips (replace free-text "1,2") + auto free-window suggestion; validate
   referenced periods exist per stage in the macro tab; save macro plan as a reusable template.
4. **Partial-group placement (model change)**: allow splitting a group across services (as the
   faculty sheet does, 9–11 + 12) to remove wasted seats. Largest effort.

## Phase 7.6: Stage Timeline / Calendar — Phase A DONE (2026-06-04)

Gantt/Teams-style **read-only** calendar, hierarchy **Année → Niveaux → Stages → Partitions**.
Key constraint honoured: `Stage` has **no dates** — every bar is derived from `StageSlot.StartDate/EndDate`
(stage span = min/max over its slots; partition span = min/max over the slots its cohorts occupy).
- Backend: `GetYearTimelineQuery` (`Application/Stages/Timeline/`) + `GET /academic-years/{id}/timeline?levelId=`
  (`Endpoints/Stages/YearTimeline.cs`). No schema change; reuses `ServiceOccupancyCalculator` for saturation.
- Frontend: `StageTimelinePage` at `/admin/timeline` (nav "Calendrier"), custom CSS Gantt (date→% via
  dayjs, no Gantt dep), `useGetYearTimelineQuery` (tag `Stage/TIMELINE`, `refetchOnMountOrArgChange` +
  manual refresh — fine-grained invalidation not wired across the ~15 plan mutations yet).
- Range picker shipped too: `@mantine/dates` + `dayjs`, CSS in `main.tsx`, `DatesProvider locale=fr`;
  slot start/end in `ScheduleGridModal` is now `DatePickerInput type="range"` (string "YYYY-MM-DD" values).

**Phase B (planned, not started):** drag/resize bars → write `StageSlot` dates, re-run capacity check,
confirm + undo. Full breakdown in root `PHASES.md` §7.6 and `PGSH.Frontend/PHASES.md` §9.

---

## Two clarifications resolved this session

- **Does a new request abort a long one?** No. RTK Query mutations and queries have independent
  abort controllers; SPA navigation doesn't cancel in-flight mutations. They run concurrently
  (separate scoped `DbContext`; Postgres MVCC reads don't block on writes).
- **Cross-stage service capacity?** Not counted together — see Phase 7.5 item #1.

---

## Build / verification state

- `dotnet build PGSH.Application/...` → **0 errors**. `PGSH.API` → **0 CS errors**.
- **Full `dotnet build PGSH.sln` fails only on DLL-copy locks** because the API (PID seen: 16892)
  and Visual Studio hold the assemblies. Stop them for a clean solution build.
- Frontend `npm run build` (tsc + Vite) passes; `npm run lint` clean in all changed files
  (the ~17 pre-existing lint errors are in untouched files: EmployeeServicesPage, StudentLayout,
  main.tsx, routes/index.tsx).
- **Live end-to-end test still pending** an API restart. Test recipe: a year with 1Med + 2Med,
  assign 2 partitions to one level and 4 to the other (independent); define slots per stage; run
  the macro plan with A→[1,2]/B→[3,4]; verify `GET /stages/{id}/schedule` cells, partition
  isolation, and that a sufficiently-sized stage reports 0 saturées.

---

## Key files

- Backend planning: `PGSH.Application/Stages/Planning/*`, `Stages/MacroPlan/*`,
  `Stages/Schedule/AutoArrange/*`, `Stages/Cohorts/{Assign,AssignByStage,PublishSchedule,BulkCreate}/*`,
  `AcademicGroups/{AssignRotationGroups,Empty}/*`.
- Endpoints: `PGSH.API/Endpoints/Stages/{Schedule,AssignAllStudents,GenerateMacroPlan}.cs`,
  `PGSH.API/Endpoints/AcademicGroups/{AssignRotationGroups,Empty,EmptyAll,DeleteAll}.cs`.
- Frontend: `features/admin/components/ScheduleGridModal.tsx`, `features/admin/pages/GroupsPage.tsx`,
  `features/admin/api/adminApi.ts`, `features/admin/types/admin.types.ts`.
- No schema/migration changes were made in this work stream.
