# HANDOFF.md

> **▶ RESUME HERE (next session) — admin evaluation entry + Excel import.** Agreed order:
> 1. **Admin one-by-one evaluation UI** (small, FE-only). The backend ALREADY allows it —
>    `ExecutionAuthorizer` bypasses chef-scoping for `Roles.Administrative`, and
>    `CreateServiceEvaluationCommandHandler` / `UpdateServiceEvaluationCommandHandler` honour it (covered by
>    `EvaluationHandlerTests.An_administrative_user_may_evaluate_any_service`). The gap is purely frontend:
>    `features/admin/api/adminApi.ts` exposes **no** evaluation endpoints. Reuse the chef's `EvaluationModal`
>    from `features/employee/pages/EmployeeServicesPage.tsx`.
> 2. **Excel/CSV bulk evaluation import** — agreed design (see "Evaluation import" section below): explicit
>    mode (whole-stage vs per-period), mandatory **preview/dry-run** before apply, all-or-nothing transaction,
>    every row routed through `assignment.SubmitEvaluation` (no bulk shortcut writes), `IEvaluationSheetParser`
>    port in Application + ClosedXML adapter in Infrastructure, downloadable pre-filled template.
> 3. **Fiche gate** — `GetFicheDeValidationQueryHandler` gates only on `Result == Validé`, so a student can
>    print the fiche BEFORE the administration ratifies. Suggest also requiring `Status == Validated`. One line,
>    needs your yes.
> 4. Still open from the audit: **délocalisation authorization hole** and the **IDOR on record/fiche** (below),
>    plus the **pause-scope** decision (level-wide vs per-cohort — my recommendation is level-wide).
>
> **▶ NEW (2026-08-07) — "Valider" re-defined as RATIFICATION, not academic override.** ⚠ Semantics change.
> Admin *Valider* (Suivi des affectations) means **officialise the professor's evaluation whatever it says** —
> it does NOT mean "the student passed". `InternshipAssignment.Validate()` previously set
> `Result = StageAssignmentResult.Validé` **unconditionally**, so ratifying a chef's 6/20 flipped the student to
> *passed* while `FinalScore` still read 6. `Reject()` was the mirror image. Both now move **`Status` only**:
> * **`Status`** = workflow — who has signed off (`… → Evaluated → Validated/Rejected`);
> * **`Result`** = academic outcome, written **solely** by `RecomputeFinalScore` from the marks.
> Bulk *Valider* was already correct in one respect: `ValidateCohortAssignmentsCommandHandler` only touches
> assignments at `Status == Evaluated`, so it can never validate a student the chef has not evaluated — but the
> skip is **silent** (20 students / 3 evaluated → toast says "3 validés", nothing says the other 17 are
> ineligible). Two existing tests had encoded the OLD meaning and were rewritten, not patched. No migration.
>
> **▶ NEW (2026-08-07) — student portal 403 on Stages fixed.** Opening a stage rendered `AttendanceSummary`,
> which called `GET /service-periods/{id}/attendance`; that handler used `EnsureCanRecordAttendanceAsync` — the
> **write** scope (admin, or chef/staff of the service) — so a student got `AttendanceNotAllowed` (403).
> `errorMiddleware.ts` toasts every 403 while the other queries succeed → "error, but the page works fine".
> New `ExecutionAuthorizer.EnsureCanReadAttendanceAsync` = everyone who may record it **plus the student who
> owns the period**. Write scope untouched (a student still cannot record presence, nor read a classmate's).
> An unknown period still returns **NotFound**, not Forbidden, so the widened check can't leak row existence.
>
> **▶ NEW (2026-08-06) — chef worklist year-scoping REMOVED (two live incidents).** The chef saw his services
> but **no groups**, twice. Root cause both times: the worklist was implicitly scoped to the current academic
> year — first by `Registration.AcademicYearId`, then (my first fix) by the year's calendar span. Both couple
> live work to bookkeeping that drifts out of step with the dates rotations actually run on; when it drifts the
> chef silently sees **nothing**. Incident 2 data: year flagged current = 2024-2025, rotations ran Jun–Sep 2026
> → all 263 started periods filtered out. **Year scoping is now opt-in**: no `AcademicYearId` ⇒ no year filter
> at all (the chef UI has no year selector). An unknown year id leaves the list unscoped rather than empty.
> `ChefWorklistYearScopeTests` was rewritten (the old 4 asserted the implicit default). **Lesson: never gate a
> worklist on a bookkeeping record that can disagree with the data.**
>
> **▶ NEW (2026-08-06) — stage-period OVERLAP validation (new business rule).** Creating a period never checked
> dates (only duplicate `PeriodNumber`), and updating one checked **nothing**. New `SlotOverlapGuard`
> (`Application/Stages/Slots/`) enforces: **no two periods of the same academic LEVEL may run at the same time**
> — inside one stage or across two. A level's students follow every one of its stages, so an overlap would put a
> group in two services on the same day. Different levels may share dates. Windows are **inclusive of both ends**
> → a period ending 31/03 and the next starting 31/03 now COLLIDE (next must start 01/04). New error
> `StageErrors.SlotOverlap`. Wired into both create + update; DI in `Application/DependencyInjection.cs`.
>
> **▶ NEW (2026-08-06) — Postgres now PERSISTS + search fixes.** `PGSH.AppHost/Program.cs`: Postgres had no
> volume (Keycloak did — that's why only its data survived). Added `.WithDataVolume("pgsh-postgres-data")` +
> `ContainerLifetime.Persistent`. Seeder verified idempotent (every block guarded by `AnyAsync`). ⚠ A named
> volume starts EMPTY — a pre-switch backup is at `pgsh-backup-20260806.dump` (repo root, `*.dump` gitignored);
> restore with `pg_restore -U postgres -d TodoDatabase --clean --if-exists`. **Seeder academic years were
> hardcoded** (fixed 2022-2025, 2024-2025 flagged current) while the rest of the seed lays rotations out around
> *today* → the "current year" was two years stale. Now derived from the current date (year runs 1 Sept → 31 Aug).
> **Search:** `GroupsPage.tsx` fetched matches then silently kept `items[0]` — "Alaoui" with two Alaouis showed
> an arbitrary student's group, and the query destructured only `data` (no `isFetching`) so nothing rendered
> while loading. Now lists every match as pickable chips + spinner + skeletons. Backend: `Appogee` was matched
> **case-sensitively** while every other field was lowered (`"ap2200a"` never found `AP2200A`); fixed, and
> `.Trim()` added to all 7 search handlers. Other pages already had debounce + `isFetching` + `setPage(1)`.
>
> **▶ NEW (2026-08-06) — test suite 36 → 260.** `PGSH.Tests` now covers the assignment state machine, all three
> evaluation modes + chef scoping, chef worklist scoping, pause/resume, student record + fiche, délocalisation
> (domain + handler), cohort transfers, bulk cohort ops, schedule publishing incl. cross-stage capacity, the
> mid-stage reroute, slot overlap, student search, attendance read/write scope, and 22 command-validator tests.
> Shared seeding lives in `PGSH.Tests/TestHarness.cs`. ⚠ **Still 100% unit-level** — `UseInMemoryDatabase`
> ignores FK constraints, unique indexes, `OnDelete` and SQL translatability, so constraint/translation defects
> are invisible. **Agreed next step: Testcontainers-over-real-Postgres integration tests + `WebApplicationFactory`
> functional tests** (the only way to cover the authorization gaps below). Neither is built yet.
>
> **▶ NEW (2026-08-06) — code audit: CONFIRMED defects, none fixed yet.** Reproduced with runnable probes:
> 1. 🔴 **`stages/delocalize` has NO authorization at any layer** — endpoint is bare `.RequireAuthorization()`
>    and `DelocalizeStudentCommandHandler` has no `ExecutionAuthorizer` check. A **student** can POST their own
>    `registrationId` with `outcome: Validated`, which runs `Delocalize()` → `SubmitEvaluation()` → `Result =
>    Validé` → the fiche gate then passes. Self-service stage validation. Needs your policy call (Scolarité +
>    SuperUser only?).
> 2. 🔴 **Editing an evaluation with objectives throws `DbUpdateConcurrencyException`.**
>    `UpdateServiceEvaluationCommandHandler.cs:63` pre-sets `Id = Guid.NewGuid()` on `ObjectiveScore` children
>    of an **already-tracked** evaluation → EF classifies them `Modified`, not `Added` (proven via
>    `ChangeTracker.DetectChanges`) → `UPDATE … WHERE Id = <new guid>` → 0 rows. Only fires when
>    `ObjectiveScores` is non-empty, so "Valider le stage" mode works and the other two break. Fix: drop the
>    pre-set `Id`, mutate the collection in place. Same gotcha the domain warns about at
>    `InternshipAssignment.cs:246` and `:277`.
> 3. 🔴 **`MidStageTransferRescheduler.RerouteAsync` NREs on a slot-less period.** The "missing slots" guard
>    (`:51-62`) admits `CohortSlotAssignmentId == null`, then null-propagation drops it from `missing`, so the
>    guard passes and `:66` dereferences `CohortSlotAssignment!`. Any ad-hoc period hits this — including the
>    one `Delocalize()` creates. Confirmed `NullReferenceException`.
> 4. 🟠 **`RerouteAsync:79` start-date is not clamped** — `date < target.EndDate ? date : target.StartDate`
>    produced a period starting 2025-12-20 for a slot opening 2026-01-01. Should be
>    `date > target.StartDate ? date : target.StartDate`. Also `:72` / `MaterializeAtTargetAsync:168` set
>    `EndDate` with no floor at `StartDate` → a backdated transfer yields `EndDate < StartDate`.
> 5. 🟠 **IDOR on `GET internship-assignments/{id}/record` and `/fiche`** — neither handler checks ownership or
>    role, and `GET internship-assignments` is unscoped, so a student can enumerate ids and read every
>    classmate's marks, comments and attendance.
> 6. 🟡 `UpdateServiceEvaluationCommandHandler` bypasses the aggregate (mutates fields inline, calls
>    `RecalculateFinalScore`) → **no domain event**, so a mark change leaves no audit trail.
> 7. 🟡 `EvaluationSubmittedDomainEvent` publishes `evaluation.TotalScore`, which `Normalize()` has just set to
>    `null` for both validate-only modes. Should publish `StageScoring.PeriodMark(evaluation)`. Latent (no
>    subscriber yet).
> 8. 🟡 Objective ids are never validated against the period's stage — `GetValueOrDefault(...)!` in both
>    evaluation handlers; a foreign id is silently weighted 1, a nonexistent one dies on the FK as a 500.
> 9. 🟡 `ResumePeriod` (`InternshipAssignment.cs:137`) shifts every later period with no filter on
>    `IsComplete` / `IsInterrupted` → resuming back-dates closed rotations and terminal history rows.
> 10. 🟡 **`CompletePeriod` has no `IsStarted` guard** (unlike `PausePeriod`), so a rotation that never ran can
>     be closed and then evaluated. Deliberately left **uncovered by tests** — a test either way would cement
>     an asymmetry that has not been ruled on. Your call.
>
> **▶ NEW (2026-07-02b) — Stage validation roll-up (all-periods-must-pass) + student notes record + fiche de validation.**
> Validation rule CHANGED (`InternshipAssignment.RecomputeFinalScore` + new `Domain/Stages/StageScoring.cs`):
> a stage is `Validé` only when EVERY (non-interrupted) period is individually validated — one failed period
> => `NonValidé`; the final note is the **mean of the periods' marks** (validate-only period = 10/0); the
> verdict is withheld (`NonÉvalué`) until all periods are evaluated. Replaces the old "mean ≥ 10" rule.
> `StageScoring.PeriodMark/IsPeriodValidated` is the shared source of truth (domain + read handlers).
> New reads: **`GET internship-assignments/{id}/record`** (`GetStudentStageRecordQuery`) = full per-period
> detail (mark, verdict, full evaluation, attendance counts) for the click-through detail view;
> **`GET internship-assignments/{id}/fiche`** (`GetFicheDeValidationQuery`) = print-ready fiche payload,
> gated on `Result == Validé` (`StageErrors.FicheNotAvailable`), objective table with marks (validate-only
> => 10), empty header/footer left for the FE attestation template. Notes list (`GET internship-assignments`)
> gained `partitionLabels` + `periodNumber` + `search` (name/appogée/CNE) filters and an `AllPeriodsEvaluated`
> flag on the summary. No migration. Tests green (19 total): `StageValidationScoringTests`,
> `StageScoringTests`, `FicheDeValidationHandlerTests` (+ the mid-stage set below).
>
> **▶ NEW (2026-07-02) — Mid-stage transfer now auto-hands-off + xUnit test project added.**
> Bug fixed: a Temporary transfer done while the stage is *en cours* with the "Transfert en cours de stage"
> toggle OFF left the in-flight `ServicePeriod` pinned to the ORIGIN service, so the NEW chef got
> `NotServiceChef` and couldn't evaluate after clôture. `MidStageTransferRescheduler.MaterializeAtTargetAsync`
> now (a) cuts the origin in-flight period to `IsInterrupted` and (b) lands the student on the target group's
> running period as started — **only when the target group is already running that period** (else keeps the
> origin period, no void). `CompletePeriod`/`PausePeriod` reject interrupted periods
> (`StageErrors.PeriodInterrupted`) so a bulk close can't revive them; the old chef's worklist now shows
> interrupted periods as grayed "parti vers…" rows (`ServicePeriodResponse.IsInterrupted` added). **No new
> migration** (`IsInterrupted` column already exists). New **`PGSH.Tests`** xUnit project (7 tests green):
> `MidStageTransferReschedulerTests`, `InterruptedPeriodTests`, `InternshipAssignmentLifecycleTests`. Caveat:
> students transferred BEFORE this fix are not auto-repaired — re-do the transfer.
>
> **▶ RESUME HERE (next session).** Délocalisation (#3) is **DONE (2026-06-25, session 7)** — see
> "Délocalisation" below. ⚠ **One migration pending DB apply: `20260625194358_Delocalization`** (the running
> API holds the old schema → the délocalisation endpoint 500s until applied; restart the Aspire stack so
> MigrationService runs it, or `dotnet ef database update`). Next, in order:
> 1. **Apply the migration + verify délocalisation end-to-end** (recipe in that section).
> 2. **Revalidation (#4)** — fail → revalidate in same service unless a transfer/déloc demande; may span
>    years. **Open: group-all-revalidations vs ad-hoc per-student.** Full 4-type model in agent memory
>    `project_student_mobility`.
> 3. Optional refinement: scope the chef gray/green markers to the **current stage's chef** + show **net
>    group headcounts** (deferred from the transfer build — markers still use the existing group-model logic).
> 4. Remaining Suivi item: small-service planning skip (#3, needs your decision — see that section below).
> 5. **Follow-up (admin eval UI):** there is still NO standalone admin screen to enter/edit a ServiceEvaluation;
>    délocalisation captures the validate-only verdict + fiche inline at recording time. If a délocalisation's
>    verdict must be entered LATER (the two-phase "student returns" flow), build an admin period-eval entry.
>
> **✅ MIGRATIONS APPLIED + VERIFIED (2026-06-25, session 6).** All five migrations — `EvaluationModes`,
> `ServicePeriodIsStarted`, `TransferType_OnCohortMembership`, `ServicePeriodIsInterrupted`,
> `ServicePeriodPause` — are now in `TodoDatabase` (confirmed against the running Aspire Postgres). Schema
> checked: `ServicePeriods.{IsStarted,IsInterrupted,IsPaused}`, `CohortMembership.{TransferType,OriginalCohortId}`,
> the `PeriodPause` table, and `ServiceEvaluation.{Mode,Outcome,TotalScore}` all present. The running API
> already serves the session-5 routes (`schedule/pause` + `/resume`).
> Build backend via `PGSH.Infrastructure.csproj` (API DLLs lock while the app runs); add ef migrations with
> `--startup-project PGSH.Infrastructure` (design-time factory) since the running API blocks the API build.
> _Updated 2026-06-25._

## ▶ Evaluation import (Excel/CSV) — DESIGN AGREED, NOT BUILT (2026-08-07)

Bulk entry of evaluations from a sheet keyed on **CNE / Apogée**, carrying `valid | unvalid | note`, either
**per period** or **for the whole stage** (applied across all its periods).

**Non-negotiables (agreed):**
- **Mandatory preview / dry-run.** Parse → validate every row → show a report → user confirms → apply.
  Grades are the highest-consequence data in the system; an import that silently applies 200 rows where 8 were
  mis-keyed is worse than no import. Matches the pre-flight-guard rule already in force.
- **All-or-nothing** per import, one transaction. Partial grade imports are unreconcilable.
- **Every row goes through `assignment.SubmitEvaluation`** — the same aggregate path as a chef's single
  evaluation, so scoring, roll-up and domain events stay identical. No bulk shortcut writes.
- **Mode is chosen explicitly at upload**, never inferred from which columns are present (inference is where
  import tools become unpredictable).

**Sheet shape** — `CNE | Apogée | Période | Résultat | Note | Remarque`. `Période` blank/absent in
whole-stage mode. `Résultat` ∈ {Validé, Non validé} **or** `Note` ∈ 0–20, matching the three existing
`EvaluationMode`s.

**Per-row preview outcomes:** unknown student · student not in this stage · period not yet closed ·
already evaluated (⇒ *will overwrite*, not an error — amending is a requirement) · assignment already ratified
(⇒ refused, consistent with `StageErrors.EvaluationReadOnly`).

**Layout (clean-architecture split — Application never learns what .xlsx is):**
```
Application/Stages/Evaluations/Import/
  ImportEvaluationsCommand.cs      // stageId, mode, rows — ALREADY parsed
  PreviewEvaluationImportQuery.cs
  EvaluationImportRow.cs
  EvaluationImportReport.cs
  IEvaluationSheetParser.cs        // port
Infrastructure/Evaluations/ClosedXmlEvaluationSheetParser.cs   // adapter (ClosedXML, MIT)
API/Endpoints/Evaluations/ImportEvaluations.cs                 // IFormFile boundary
```
Plus a **downloadable template** generated from the stage's real periods, pre-filled with its students'
CNE/Apogée, so nobody hand-builds columns or mistypes an identifier.

## ▶ Verification + stale-status refetch fix ✅ DONE (2026-06-25, session 6)

- **Verified the transfer + pause stack against the live DB** (stack was already running): 5 migrations applied,
  schema columns/tables present, session-5 API routes live. Transferred-student data is correct — the one
  Temporary loan's 2 periods are both `IsStarted=true, IsPaused=false` (no corruption).
- **Bug fixed — student/chef saw stale period status ("Planifié" after Reprendre).** Root cause was NOT the
  domain (`ResumePeriod` correctly keeps `IsStarted=true`, only flips `IsPaused→false`) but **cross-API-slice
  cache staleness**: `pauseStagePeriods`/`resumeStagePeriods` live in the **admin** RTK Query slice and
  invalidate `Assignment/LIST`, but the student detail reads `getAssignmentById` (**student** slice, tag
  `Registration/assignment-{id}`) and the chef reads `getServicePeriodsByService` (**employee** slice) — a
  mutation in one slice can't invalidate a query in another. Fix: `refetchOnMountOrArgChange: true` on the
  student stage-detail (`getAssignmentById`), student stage-list (`getMyAssignments`), and chef worklist
  (`getServicePeriodsByService`) call sites, so revisiting a page always shows live status. FE build + lint
  clean (only the pre-existing `EmployeeServicesPage.tsx:109` eval-effect error remains). _Frontend repo commit._
- **⚠ Backlog (security): schedule endpoints are unauthenticated.** `stages/{id}/schedule/{auto-arrange,
  publish,start,complete,pause,resume}` have **no `.RequireAuthorization()`** — an unauthenticated POST mutates
  data (a probe paused 604 periods). Pre-existing project-wide posture (auth/CORS lockdown = Phase 12), but a
  mutating endpoint with no guard is worth fixing ahead of the rest. `GET /service-periods` 401s (it has a
  guard); the schedule group never did.

## ▶ Délocalisation (mobility #3) ✅ DONE (2026-06-25, session 7)

Student does the **whole stage outside the faculty** (hometown / abroad); the app has no control over the
rotation. Recorded as a single **ad-hoc, pre-completed** `ServicePeriod` at an external service; the
paper-validation verdict + fiche reference are captured in the same admin action (délocalisation is logged
after the student returns). Build: Application → 0 errors; API reached copy-step with 0 CS errors (DLL lock
only = API running); migration scaffolded; frontend `npm run build` + eslint clean on changed files.

- **Domain**: `ServicePeriod.{IsDelocalized (bool), Delocalization?}`; new child entity `Delocalization
  {Reason, DemandeId?}` (1:1, mirrors `PeriodPause`). `ServiceEvaluation.FicheReference (string?)` — paper-proof
  ref, URL/text for now (real upload = Phase 5 with Demande). `HistoryType.Delocalization`. New
  `StudentDelocalizedDomainEvent`. `InternshipAssignment.Delocalize(stageId, serviceId, start, end, reason,
  demandeId?)`: refuses if any period is started/complete/interrupted (`StageErrors.StageAlreadyUnderway`),
  drops planned periods, adds ONE ad-hoc period `IsStarted=IsComplete=IsDelocalized=true`, Status→Completed,
  raises the event. **Tracked-child key gotcha respected** — period/Delocalization/eval Ids never pre-set.
- **Application**: `DelocalizeStudentCommand` (+validator, IAuditableCommand `STUDENT_DELOCALIZED`). Handler
  finds the student's `(group, stage)` cohort (errors: `NoGroupForDelocalization`, `CohortMissingForStage`,
  `Services.NotFound`), reuses an existing not-yet-started assignment or creates one, calls `Delocalize`, and
  — when `Outcome` is supplied — records a **validate-only** `ServiceEvaluation` (`Mode=ValidatePeriod` +
  `FicheReference`) via `SubmitEvaluation` (→ Status Evaluated, score auto-maps 10/0). `StudentDelocalizedEventHandler`
  writes `History.Delocalization {stage, service, reason}`. `FicheReference` also threaded through the generic
  Create/Update eval commands + `ServiceEvaluationResponse` + GetByPeriod projection; `ServicePeriodSummary`
  (admin GetById) now carries `IsDelocalized`/`DelocalizationReason`.
- **Infra**: `DelocalizationConfiguration` (unique FK on ServicePeriodId, cascade) + `FicheReference` maxlen
  1000. Migration `20260625194358_Delocalization` (IsDelocalized bool default false, FicheReference nullable,
  Delocalization table). **⚠ pending DB apply.**
- **API**: `POST stages/delocalize` (binds command directly, `.RequireAuthorization()`). Eval Update Request
  gained `FicheReference`.
- **Frontend**: `GroupDetailPage` roster gets a teal plane action → `DelocalizeModal` (stage Select = Planned
  assignments; searchable/debounced external-service Select via `getServices`; date-range; required motif;
  optional verdict SegmentedControl `Validation plus tard / Validé / Non validé` → required fiche-reference
  TextInput when a verdict is chosen, pre-flight guarded). `delocalizeStudent` mutation + `DelocalizeStudentRequest`
  type. Admin still has no standalone period-eval screen (see RESUME #5).
- **Test recipe (after migration applied)**: ensure the stage has cohorts for the student's group (run
  affectation/macro-plan first), and that the external hospital/service exists in *Infrastructures* (add it if
  not). Group detail → student row → ✈ **Délocaliser** → pick the Planned stage + external service + date range +
  motif → optionally **Validé** + fiche ref → Enregistrer. Verify: the stage's assignment shows **Terminé/Évalué**
  with the validate-only result; the student **History** shows a *Délocalisation* row (stage · service · motif);
  the in-faculty chef worklist never shows the student (external service has no chef).

## ▶ Transfer bug-fixes + Stage Pause/Resume ✅ DONE (2026-06-25, session 5)

Batch from user testing the transfer model. Build: backend `PGSH.Infrastructure` + `PGSH.API`
(temp-output to dodge the running-API DLL lock) 0 errors; frontend `npm run build` + eslint clean on
changed files (the one pre-existing `EmployeeServicesPage.tsx:109` eval-effect lint error is untouched).

1. **History UX (direction + motif).** A temporary loan was only a `CohortTransfer`, which
   `GetStudentHistoryQueryHandler` filters out → the outgoing loan + motif were invisible and only the
   *return* row showed (reading loaned→home, hence "reversed"). Fix: `StudentCohortTransferredDomainEvent`
   now carries `TransferType`; for a **Temporary** move `StudentCohortTransferredEventHandler` writes a
   **visible** `GroupTransfer` history with GROUP labels + `stage` + `reason` + `temporary:true`
   (definitive still writes the hidden `CohortTransfer`). `HistoryPage.tsx` got a transfer-aware renderer:
   `Groupe X → Groupe Y` arrow (correct order) + "Motif :" + stage chip; `temporary`→"Prêt temporaire",
   `temporaryReturn`→"Retour de prêt"; French key labels replace the raw capitalized dump.
2. **"Loaned out" badge.** `GetGroupByIdQuery` roster rows now carry `LoanedToGroup`/`LoanedStage`
   (active Temporary `CohortMembership`) + an `IncomingLoans` list. `GroupDetailPage` shows a grape
   "Prêt → Groupe X · stage" badge on loaned-out students and an "N prêt(s) entrant(s)" table.
3. **Incoming loan now evaluable.** New `MidStageTransferRescheduler.MaterializeAtTargetAsync`: on a
   normal (non-reschedule) transfer it rehomes the future, not-started rotation onto the **target
   cohort's slot cells** → real, actionable `ServicePeriod`s (joins the target group's progress via
   `IsStarted`), so the synth green row is suppressed and the new chef can clôturer/évaluer. No-op when
   the target group has no schedule for the stage (informational row stays as fallback). Wired in
   `TransferStudentCommandHandler` (the assignment query now always `.Include(ServicePeriods…StageSlot)`).
4. **Stage Pause/Resume (new).** `ServicePeriod.IsPaused` + child `PeriodPause{StartDate,ResumeDate?,Kind,Reason}`.
   `InternshipAssignment.PausePeriod`/`ResumePeriod` — resume closes the open pause, adds the lost days to
   that period's end and **cascade-shifts every later period** of the assignment. `CompletePeriod` now
   refuses a paused period. New `StagePauseRunner` (mirrors `StagePeriodRunner`) + `PauseStagePeriodsCommand`/
   `ResumeStagePeriodsCommand` (scope: cohorts/partition/periods), endpoints `POST stages/{id}/schedule/pause`
   + `/resume`. Suivi count bucket `InternshipStatus.Paused` (display-only, never persisted on assignments).
   Migration `ServicePeriodPause`. **FE:** Suivi bar Pause (modal: kind Examens/Vacances/Autre + reason) /
   Reprendre buttons (pre-flight guarded by `selectionHasTargetPeriod`); chef worklist "En pause" badge;
   admin/chef period responses carry `IsPaused`/`PauseReason`; calendar (`StageTimelinePage` +
   `GetYearTimelineQuery`) draws hatched orange pause bands per partition and extends the partition bar to
   the shifted `ServicePeriod` end (dates shift on ServicePeriod, NOT StageSlot — slots are shared across
   partitions). **Deferred:** capacity re-check on resume (shifted windows can overflow a service — currently
   no guard; matches the forced-override stance, flag if a warning is wanted).

## ▶ Forced mid-stage transfer (hand-off) ✅ DONE (2026-06-25)

Exceptional escape hatch (e.g. a student must leave her group mid-rotation). **Opt-in** so the normal
transfer is untouched. Decisions locked with user: old in-progress period **closed early + kept as history,
NOT re-evaluated** by the old chef; new chef evaluates the remainder **as a full stage**; new window =
**remaining time only** (transfer date → slot end) + future periods in full.

- **Domain**: new `ServicePeriod.IsInterrupted` — terminal history (kept w/ attendance, excluded from chef
  worklist, suivi counts, score, and the "all periods done" lifecycle checks: `CompletePeriod`/`SubmitEvaluation`
  now treat `IsComplete || IsInterrupted` / `Evaluation != null || IsInterrupted` as done).
- **`MidStageTransferRescheduler`** (`Stages/Planning/`, DI-registered): runs only on an assignment with a
  started, not-complete period. Completed periods untouched; the in-progress one → `IsInterrupted` + EndDate
  clamped to the transfer date, and a NEW started period is created against the **target cohort's slot cell**
  for the remaining window; future periods → old removed, re-created full (inactive). Fails with
  `StageErrors.TargetScheduleMissingPeriods` if the target group has no slot for a moved period.
- **Command**: `TransferStudentCommand.Reschedule` (default false). Handler loads ServicePeriods +
  `CohortSlotAssignment.StageSlot` only when rescheduling, calls the rescheduler after `TransferToCohort`.
- **Worklist/counts**: `GetMyServicePeriodsQueryHandler` filters `!IsInterrupted` (actionable rows) and the
  incoming-synth existence check now matches on `CohortSlotAssignmentId == sa.Id` (so a re-materialised period
  dated from the transfer day suppresses the synthesized green row → the new chef sees a **real, actionable**
  row instead). `GetAssignmentStatusSummaryQueryHandler` excludes interrupted periods.
- **EF + migration**: `ServicePeriodIsInterrupted` (bool default false). **Pending DB apply** (see ⚠ — now
  FOUR migrations pending).
- **Frontend**: `TransferModal` got a "Transfert en cours de stage" `Switch` (default off) wiring
  `reschedule`. Build: backend 0 errors; FE `npm run build` + eslint clean.
- **Capacity** on the target service is **not** hard-blocked for this forced path (admin override by design).
- **Follow-up (small):** surface `IsInterrupted` as an "interrompu (transfert)" badge in the student/admin
  period detail views (logic already excludes it everywhere that matters; this is cosmetic).
- **Test recipe**: start a stage (admin Démarrer P1) so the student has a started period → Transférer →
  Définitif + target group + reason + **toggle "Transfert en cours de stage"** → the old chef's in-progress
  row disappears (kept as interrupted history), the **target group's service chef** now sees the student as a
  real actionable rotation (remaining dates) and can clôturer/évaluer normally; suivi count no longer shows a
  stuck "en cours".

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

**Optimization sweep (cross-cutting, agreed 2026-06-25 — ongoing).** Two standing quality rules now
documented in `PGSH.Frontend/CLAUDE.md` → "Performance & Pre-flight Validation"; apply them whenever touching
a page, and burn down the known offenders below:
- **Pre-flight guards** — disable any action the server is guaranteed to reject / that is a no-op, with an
  inline reason, instead of firing the request and showing an error toast. Known: `ScheduleGridModal`
  "Répartition automatique" is clickable even when the stage has **no periods/slots** (should be disabled,
  like the Suivi bar's `selectionHasTargetPeriod` guard). Audit every page's primary mutation for the same.
- **Debounce search inputs** — every server-querying free-text field must use `useDebouncedValue` (300–350ms)
  + `skip` until ≥2 chars (pattern in `EmployeesPage`/`GroupsPage`/`InfrastructurePage`). Reported laggy:
  the **academic-group student search**. Sweep all search/filter inputs for missing debounce; memoize
  client-side filters of already-loaded lists.

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
