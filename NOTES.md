# NOTES.md — Project Familiarization Notes

This file captures accumulated context, domain knowledge, and non-obvious observations built up while working through the PGSH codebase. It is a living document — update it as understanding deepens.

---

## Domain Understanding

### What PGSH actually is

PGSH is the internship management platform for a Moroccan medical faculty (likely FMPR — Faculté de Médecine et de Pharmacie de Rabat, inferred from an old Keycloak realm name `fmpr` found during cleanup). Medical and pharmacy students must complete clinical rotations in hospitals as part of their degree. PGSH manages the full administrative cycle: enrolling students each year, assigning them to groups, planning their rotation schedule through hospital services, tracking attendance, and recording evaluations.

### How the academic year works

Each `AcademicYear` (e.g., "2025-2026") has a set of `AcademicGroups` (e.g., "Group 1 - Rabat Zone", ~20 students each). Students are enrolled via `Registration`, which ties a student to a year, a level, and a group. The group assignment can happen automatically via the `AutoArrangeGroups` operation, which uses `GeographicZone` for geographic clustering.

### How rotations work

A `Stage` defines a rotation type tied to a `Level` (e.g., "Cardiology Rotation, Year 4 Medicine"). It has a duration, coefficient, and objectives (scored criteria). A `Cohort` is a specific instance of a Stage for a particular AcademicGroup in a given year.

The schedule is modelled as a **grid**: rows = cohorts, columns = time periods (StageSlots), cells = service assignments (CohortSlotAssignments). `StageSlot` defines a named time period (P1, P2...) belonging to a Stage **for one academic year** — the window carries concrete dates, and the stage runs again next year over different ones. `CohortSlotAssignment` is a grid cell — it maps one Cohort to one Service for one StageSlot. This mirrors the actual paper scheduling documents used by the faculty.

When an admin **publishes** a cohort's schedule (`POST /cohorts/{id}/publish-schedule`), the system creates `ServicePeriod` records for each student in the cohort × each slot assignment. **The capacity check was removed** — it blocked bulk-publishing multiple cohorts assigned to the same service (after the first cohort published, occupancy exceeded capacity for subsequent cohorts even when the total was valid). The schedule grid UI already shows a red capacity badge as a warning. Unpublishing (`DELETE`) removes all ServicePeriods for that cohort where `CohortSlotAssignmentId != null`.

### The student journey through the system

```
Enrollment:  Student → Registration (year + level + group)
Planning:    AcademicGroup → Cohort (group + stage)
             Stage → StageSlot (time period columns P1, P2, ...)
             Cohort → CohortSlotAssignment (service per slot — the grid cell)
Assignment:  Registration → InternshipAssignment (student in cohort)
Publishing:  POST /cohorts/{id}/publish-schedule → creates ServicePeriods
Execution:   InternshipAssignment → ServicePeriod (at a specific service, real dates)
             ServicePeriod → AttendanceRecord (daily presence)
             ServicePeriod → ServiceEvaluation → ObjectiveScores (graded)
Result:      InternshipAssignment.FinalScore (cached aggregate of scores)
```

### Key terminology

| Term | Meaning |
|---|---|
| Stage | A type of hospital rotation (e.g., "Chirurgie S6") |
| Cohort | A group doing a specific stage together in a given year |
| StageSlot | Time period column (P1, P2...) in the schedule grid — belongs to a Stage **and an academic year** |
| CohortSlotAssignment | Grid cell — maps one Cohort to one Service in one StageSlot |
| Period | `ServicePeriod` — actual execution record for a student in a service |
| Assignment | `InternshipAssignment` — one student enrolled in one cohort |
| Level | Academic year of study. Médecine ran to 7 under arrêté 2174.18 and runs to 6 under 1650.25 |
| CnpnVersion | One ministerial text (arrêté). Decides how many years a programme lasts and, via Curriculum, what each level owes |
| Curriculum | What one CnpnVersion requires of one Level — keyed on the text, never on the year |
| CNE | Code National de l'Étudiant — unique national student ID |
| Appogee | University software student number (second unique ID) |
| PPR | Employee government registration number |
| PV | Procès-Verbal — official document (used for employee signature dates) |
| Grade | Academic rank: `MC` (Maître de Conférences), `PES` (Professeur Enseignement Supérieur), `PH` (Praticien Hospitalier), Nurse, Administrator |
| ServiceChef | Head of a hospital department; evaluates students in that service |
| GeographicZone | Field on AcademicGroup used by the auto-arrange clustering algorithm |
| Revalidation | A student retaking a stage they previously failed (`NonValidé`). Tracked as a new `InternshipAssignment` + `History(Revalidation)`. The old failed assignment is never deleted. |

---

## Business Rules Discovered

These are rules enforced in domain or application code — not always obvious from the schema alone.

- **One registration per year**: A student cannot have two `Registration` records for the same `AcademicYearId`. Enforced in `Student.AddRegistration` and `CreateRegistrationCommandHandler`.
- **Validated registrations are locked**: `Student.RemoveRegistration` blocks deletion if `Status == RegistrationStatus.Validated`. Updates are allowed.
- **Program mismatch blocked**: A registration's `LevelId` must reference a level whose `AcademicProgram` matches the student's `AcademicProgram`. Enforced in `Create` and `Update` handlers. Error: `RegistrationErrors.ProgramMismatch`.
- **Chronological consistency enforced**: For any two registrations of the same student, the one with the higher level must belong to the later academic year (compared by `AcademicYear.StartDate`). Repeating a year (same level in two years) is allowed. Violation returns `RegistrationErrors.ChronologicalInconsistency`. Checked in `Create` and `Update` handlers; update excludes the registration being edited from the comparison set.
- **ServiceChef must be staff first**: `Service.AssignChef` throws if the employee isn't already in `Staff`, and checks `Position == Position.ServiceChef`.
- **Unique student identifiers**: CNE is hard-unique (index, never null). Appogee is unique with a nullable filter (some students may not have one yet).
- **Group uniqueness is year-scoped**: `(AcademicYearId, GroupNumber)` and `(AcademicYearId, Label)` are both unique — same group numbers can appear in different years.
- **Level uniqueness**: `(Year, AcademicProgram)` is unique — you can't have two "Year 4 Medicine" levels.
- **Attendance uniqueness**: `(ServicePeriodId, Date)` is unique — one attendance record per student per day per rotation.
- **FinalScore is a cache**: `InternshipAssignment.FinalScore` is described as "stored, not authoritative" — it must be recomputed from `ObjectiveScore.Score × StageObjective.Weight`. Currently never written. (Phase 7 item.)
- **Ad-hoc vs. planned periods**: `ServicePeriod.CohortSlotAssignmentId` is NULL for ad-hoc rotations (created outside the published schedule), non-null for schedule-driven ones (created by publish-schedule).

- **"Valider" means RATIFY, not pass** *(clarified by the user 2026-08-07 — this is the single most
  misreadable term in the domain)*: admin *Valider* on Suivi des affectations **officialises the professor's
  evaluation whatever verdict it carries**. It does not decide that the student passed. Two concepts must stay
  separate on `InternshipAssignment`:
  - **`Status`** = workflow state — who has signed off (`Planned → Ongoing → Completed → Evaluated →
    Validated/Rejected`). Moved by `Validate()` / `Reject()`.
  - **`Result`** (`StageAssignmentResult`) = academic outcome — written **only** by `RecomputeFinalScore` from
    the marks, never by an administrative action.
  Ratifying a stage the chef failed records an *official failure*. `Reject()` symmetrically means "I refuse to
  officialise this evaluation" (contested, wrong student, incomplete) — it does not retroactively fail anyone;
  the marks are corrected by amending the evaluation. Anything gating on "the student passed" checks `Result`;
  anything gating on "it is official" checks `Status`. (Before 2026-08-07 `Validate()` set `Result = Validé`
  unconditionally, so ratifying a 6/20 flipped the student to passed while `FinalScore` still read 6.)
- **Bulk ratification only touches fully evaluated assignments**: `ValidateCohortAssignmentsCommandHandler`
  filters `Status == Evaluated`, which is reached only when every non-interrupted period has an evaluation — so
  it can never validate a student the chef has not evaluated. The skip is **silent**, though: 20 students with 3
  evaluated reports "3 validés" and says nothing about the other 17.
- **No two periods of the same academic LEVEL may overlap** *(added 2026-08-06, `SlotOverlapGuard`)*: a level's
  students follow every one of its stages, so overlapping windows — whether inside one stage or across two of
  the same level — would put a group in two services on the same day. Different levels may share dates freely.
  Windows are **inclusive of both ends**, so a period ending 31/03 and one starting 31/03 collide; the next must
  start 01/04. Enforced on both create and update of a `StageSlot`. Error: `StageErrors.SlotOverlap`.
  - ⚠ **Superseded 2026-08-08 — the rule above is now per-STAGE, not per-level.** It used to refuse any two
    overlapping windows anywhere in a level, which contradicted the faculty's own published planning: in
    `Med3.png` Médecine and Chirurgie run the **same** windows, partition A in one and B in the other. That is
    the whole point of partitioning — it halves the load on every service — and the old rule made the published
    layout unauthorable. See *No group in two services at once* below for what replaced it.
- **The chef worklist is never year-scoped by default** *(learned the hard way — two live incidents)*: a chef's
  list is "what is live in my services", expressed by `IsStarted`. Any implicit scoping to the current academic
  year couples live work to a bookkeeping record that drifts out of step with the dates rotations actually run
  on — and when it drifts, the chef silently sees nothing at all. `GetMyServicePeriodsQuery.AcademicYearId` is
  **opt-in**; an unknown year id leaves the list unscoped rather than empty.
- **Reading presence is wider than recording it**: attendance may be **recorded** by an administrative user or
  the chef/staff of the period's service; it may be **read** by all of those *plus the student whose own
  rotation it is* (`ExecutionAuthorizer.EnsureCanReadAttendanceAsync`). Consulting your own attendance is not a
  privileged act — gating the read behind the write scope made the student portal 403 on every stage it showed.
- **Non-validated stages stay with the student (revalidation)**: A student who receives `Result = NonValidé` on a stage does **not** stop progressing. They continue through subsequent academic years normally. The failed stage "sticks" with them — the original `InternshipAssignment` with `Result = NonValidé` remains in the system as a permanent record. At some later year (could be final year), the student is assigned to a cohort doing that same stage again and receives a new `InternshipAssignment` for that attempt. If they pass, that new assignment gets `Result = Validé`. The old failed assignment is never deleted or modified — it is the audit trail. A `History` record of type `Revalidation` marks when this process begins. **Implication:** a student can have multiple `InternshipAssignment` records for the same `Stage` across different academic years. Queries that check "has the student passed Stage X" must look for any `InternshipAssignment` where the `Cohort.StageId == X` and `Result == Validé`, not just the most recent one. **Why the retake can be years later** *(confirmed 2026-08-07)*: a stage is **not necessarily a criterion for failing the whole registration** — a student can fail a stage, pass the year on their subjects, and carry the unvalidated stage forward. So revalidation must stay flexible across levels, not just across years at the same level. `GetStudentLevelDossierQuery` answers "what is still owed at level L" and `RevalidateStageCommand` re-opens it; the latter deliberately does **not** constrain the stage to the registration's own level, because the prior failed attempt is the real constraint.

---

## Current State of Each Layer

### Domain — Clean
All orphan models removed. Entities correctly model the domain with proper navigation properties. The main gap is that most entities outside the User/Registration/Student cluster are plain POCOs (no domain methods, no business logic encapsulated). Domain events are only raised on Registration operations.

### Infrastructure — Clean
EF configurations are complete and well-structured. All relationships are explicitly configured. The 10 new indexes are in place. `PermissionProvider` is a stub. The Aspire connection name `"TodoDatabase"` is a legacy artifact from scaffolding — it has no functional meaning and refers to the main PostgreSQL database.

### Application — Complete through Phase 7
- **Hospital/Center/Service**: Full CRUD + GetById (returns chef, staff list, hospital city/GPS). `GetServiceByIdResponse` includes `HospitalCity`, `HospitalDescription`, `Latitude` (y), `Longitude` (x).
- **Academic**: AcademicYear (GetMany, Create), AcademicGroup (GetMany, GetById, Update — now includes `RotationGroup`, Delete, **EmptyGroup**, TransferStudent, AutoArrange), Level (full CRUD). `DeleteGroup` guards against both active cohorts AND assigned registrations (returns `Error.Conflict` for each). `EmptyGroup` (`DELETE /groups/{id}/students`) sets `AcademicGroupId = null` on all registrations in the group, returning the count of students unassigned. `AcademicGroup.RotationGroup` is a persistent partition label (A, B, C…) used by auto-arrange to ensure consistent rotation across all stages of the year; also exposed via `GetMany`, `GetById`, `Update`, and `GetStageSchedule` responses.
- **Student/Registration**: Full CRUD complete. Bulk registration works. History read works.
- **Stage/Cohort**: Full CRUD + AssignStudents + AssignAllByStage + Bulk (Start/Complete/Validate). Schedule grid (GetStageSchedule, slot CRUD, assignment CRUD). Publish/Unpublish. Auto-arrange (`AutoArrangeStageScheduleCommandHandler`) — capacity-proportional cyclic rotation (see dedicated section below).
- **InternshipAssignment**: GetMany, GetById, Start, Validate, Reject.
- **ServicePeriod**: GetMany, Complete, GenerateAttendance.
- **Attendance**: GetByPeriod, Record.
- **ServiceEvaluation**: GetByPeriod, Create. (Update handler exists in Application but no endpoint yet.)
- **Employee**: Full CRUD + GetCurrent + service staff ops (AssignStaff, RemoveStaff, AssignChef, RemoveChef).
- **Users**: GetById and GetByEmail work. `UserRegisteredDomainEventHandler` is an empty placeholder.

### API — Clean
All endpoints follow the `IEndpoint` pattern. All dead files removed. All routes consistent (no leading slashes, no `/api/` prefix in Created URLs). Enum fields in request types use proper enum types — no `int` casts anywhere. `GlobalExceptionHandler` catches all `DomainException` subclasses generically. `CustomResults.Problem` correctly maps all error types to HTTP status codes.

### SharedKernel — Clean
All types are correct and concise. `BulkResponse<TId, TResponse>` is well-designed for partial-success scenarios (used by bulk registration).

---

## Frontend Architecture Notes

### RTK Query cache tags — known gotchas
- `assignStudentsToCohort` and `assignAllStudentsByStage` both invalidate `Stage.cohorts-{stageId}` in addition to `Assignment.LIST`. Without this, `CohortResponse.studentAssignmentCount` stays stale (0) after assigning students, which hid the "Publier toutes" button.
- `publishSchedule` and `unpublishSchedule` invalidate `Stage.schedule-{stageId}`, `Stage.cohorts-{stageId}`, `Stage.cohort-detail-{cohortId}`, and `Assignment.LIST`.
- `autoArrangeStageSchedule` now accepts `{ stageId, partitionCount? }` and invalidates `Stage.schedule-{stageId}` + `Level.GROUPS` (since it may write `RotationGroup` labels onto `AcademicGroup` records for the first time).

### Global AcademicYear context
`AcademicYearContext` (`src/features/admin/contexts/AcademicYearContext.tsx`) wraps `AdminLayout`. All admin pages access `useAcademicYear()` to get `currentYearId` / `currentYear` / `setCurrentYearId`. The selector in the header updates this globally. `StageDetailPage` and `AssignmentsPage` sync their local year filter from this context via `useEffect`.

### ServiceDetailPage (student)
Route: `/student/services/:serviceId`. Loaded lazily in `routes/index.tsx`. Entry point: clicking a service name in `PeriodCard` inside `StageDetailsPage`. Uses `GET /services/{id}` which returns `hospitalCity`, `hospitalDescription`, `latitude` (Hospital.LocalisationMaps.y), `longitude` (Hospital.LocalisationMaps.x). Layout: back-nav + title → stats chips row (serviceType, capacity, chef badge, staff count) → full-width 360 px OpenStreetMap iframe embed with overlaid "Ouvrir dans OpenStreetMap" link button → hospital address strip (name + city + coordinates) → two-column grid: left = description card + chef card (teal-tinted panel with initials avatar), right = staff list (per-member rows with grade badge and PPR). Falls back to a placeholder when coordinates are missing.

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

### Execution authorization — a chef controls only his own services
The internship-execution writes are scoped per service chef so "each one controls only what he has":
- `ExecutionAuthorizer` (`Application/Employees/MyServices/`, DI-scoped) is the single rule: an action on a `ServicePeriod` (or its evaluation) is allowed only for an **administrative role** (`Roles.Administrative` = Scolarite/Secretaire/SuperUser) **or the chef of that period's service** (`Service.ServiceChefId == IUserContext.UserId`). Otherwise `StageErrors.NotServiceChef` (the new `ErrorType.Forbidden` → HTTP 403; added to `Error`, `ErrorType`, and `CustomResults`).
- Enforced in `CompleteServicePeriodCommandHandler`, `CreateServiceEvaluationCommandHandler`, `UpdateServiceEvaluationCommandHandler`.
- `IUserContext` gained `bool IsInRole(string)` (impl. delegates to `ClaimsPrincipal.IsInRole`; realm roles arrive as `ClaimTypes.Role` via `KeycloakRoleTransformer`). Role name constants live in `Application/Abstractions/Authentication/Roles.cs` (mirror of the frontend `common/constants/roles.ts`).
- The chef's worklist read is also scoped: `GetMyServicePeriodsQuery` (`GET /employees/me/service-periods`) derives the chef's services server-side from the identity — a `serviceId` the caller doesn't lead is ignored, so a chef can never read another chef's periods. The generic `GET /service-periods` (admin) and the student-facing `GET /service-periods/{id}/evaluation` (read-only) are unchanged.
- **Frontend**: `EmployeeServicesPage` calls `/employees/me/service-periods`; active rotations have a **"Terminer"** action (`PUT /service-periods/{id}/complete`, now chef-enforced) so a chef can complete a rotation → then **Évaluer** (submit the note). Secrétaire absences already work through the admin `AttendancePage` (its `AuthGuard` includes `Secretaire`).

### CohortMembership tracks transfer history, not current cohort
`InternshipAssignment.CurrentCohortId` is the FK for the *current* cohort. `CohortMembership` is a history table — it records every cohort a student has ever been in, with start/end dates and transfer reason. A null `EndDate` means they're currently in that cohort. This enables tracking cohort transfers mid-rotation.

### GenerateScheduleCommandHandler creates Cohorts and CohortSlotAssignments
Located at `Application/AcademicGroups/Manage/Schedule/GenerateScheduleCommandHandler.cs`. This operation:
1. Creates `StageSlot` records per-Stage (find-or-create, saves to get IDs)
2. Creates `Cohort` records per AcademicGroup per Stage
3. Creates `CohortSlotAssignment` grid cells (one per Cohort × StageSlot, assigning services by rotation)
It does **not** create `ServicePeriod` records — that is done later by `POST /cohorts/{id}/publish-schedule`.

### Seeder scale and safety patterns
The `MigrationService/Seeder.cs` generates ~5700 students (Med: 7 promos × 600, Pharma: 6 promos × 250). Three critical patterns:

1. **UniqueIndex-based identifiers** — `f.UniqueIndex` in Bogus is a sequential counter (per `Faker` instance) that guarantees uniqueness. CIN uses `$"MA{f.UniqueIndex:D6}"`, CNE uses `$"G{f.UniqueIndex:D9}"`, Appogee uses `(20_000_000 + f.UniqueIndex).ToString()`. Using `f.Random.Number(range)` is dangerous at this scale: with 5700 students and 6 M possible Appogee values the birthday-problem collision probability is ~93% per run.

2. **Batch inserts with ChangeTracker.Clear()** — students and registrations are inserted in batches of 500, with `ChangeTracker.Clear()` after each `SaveChangesAsync`. Without clearing, all previously tracked entities accumulate in the change tracker across seeder steps, making each subsequent `SaveChangesAsync` progressively slower (and eventually OOM).

3. **`SeedShowcaseServiceAsync`** — runs after `SeedCentersAsync`, before `SeedStagesAsync`. Sets GPS coordinates on Hôpital Ibn Sina (Rabat: lon=-6.8498, lat=34.0167), updates Youssef Alaoui to Position.ServiceChef with PPR "PHC-10042", creates 3 staff members (Karim/Bensouda PH, Sara/El Ouafi MC, Omar/Tahiri PES), assigns them to the Cardiologie service. Guard: `if (await context.Hospitals.AnyAsync(h => h.LocalisationMaps != null, ct)) return;`

### GetStudentsQueryHandler filters CNE, Appogee, CIN as exact match
These are not partial-text filters — they use `==` equality. The `SearchTerm` filter does a `Contains` on FirstName, LastName, Email. This is intentional: CNE/Appogee/CIN are precise identifiers.

### The `int? AcademicProgram` bug in GetLevelsQuery is now fixed
Previously the query accepted `int?` and the handler cast it: `(int)l.AcademicProgram == request.AcademicProgram`. This was type-unsafe and broke JSON deserialization from string enums. Now correctly typed as `AcademicProgram?`.

### Planning services (shared, partition + window scoped)
The complex planning operations were extracted into DI-registered services under
`Application/Stages/Planning/` so command handlers and the macro orchestrator share one source of truth:
- `PartitionAllocator` (static) — the A/B/C labelling rule. Used by `AssignRotationGroupsCommandHandler` and `RotationArranger`.
- `RotationArranger` — the cyclic rotation below, **scoped to optional partition labels + period numbers**. Removal of prior cells is restricted to `targetCohortIds ∩ targetSlotIds`, so arranging one partition's window never wipes another's. **The cyclic shift is anchored to the targeted cohort set's *participation footprint* — the slots they already occupy in this stage ∪ the slots being arranged now (`phaseBySlotId`), step = `n / footprintLength`.** This single rule is correct for both planning paths: (a) the **macro matrix** (a partition runs one window, e.g. A→P1-2) keeps footprint = that window → the clean half-cycle swap is preserved (no regression); (b) **adding new periods to an already-arranged set** grows the footprint to include them → the new periods get fresh phases and *continue* the rotation instead of repeating services the cohorts already did. All cohorts of the stage participate in the queue (not just unpublished ones) so the cycle stays consistent; only individual **published cells** (a `ServicePeriod` points at them) are protected — never deleted nor rewritten — letting a started stage keep its history while its new periods are arranged. Backs `AutoArrangeStageScheduleCommand`.
- `StudentAffectationService` — affectation per-cohort or per-stage (optionally partition-filtered). Backs `AssignStudentsToCohort` + `AssignAllStudentsByStage`. `BulkResponse.TotalProcessed` now reports eligible registrations considered (not just newly created).
- `SchedulePublisher` — `ServicePeriod` generation per-cohort (strict) or per-stage+partition+window (lenient/idempotent). Backs `PublishCohortScheduleCommand` + new `PublishStageScheduleCommand`.
- `CohortProvisioner` — idempotent cohort creation per (partition, stage). Backs `BulkCreateCohortsFromPartitions` + macro plan.
- `ServiceOccupancyCalculator` — global cross-stage service load (`LoadOn(serviceId, start, end)` = students on a service over any overlapping window, all stages). Used by `GetStageScheduleQueryHandler`, `RotationArranger`, and `SchedulePublisher` (see cross-stage capacity note below).

### Stage timeline / calendar (read-only Gantt)
`GetYearTimelineQuery` (`Application/Stages/Timeline/`, endpoint `GET /academic-years/{id}/timeline?levelId=`) returns a `Level → Stage → Partition → Group` tree for the calendar view. Each `TimelinePartition` now carries its `Groups` (`TimelineGroup`: id, label, number, student count) — the academic groups whose cohorts make up that partition in the stage. Frontend `PartitionDrawer` renders each partition as a clickable row (`PartitionRow`) that expands to a grid of its group cards. **A `Stage` has no dates** — every span is *derived* from `StageSlot.StartDate/EndDate`: a stage spans the union of its slots; a partition spans the slots its cohorts occupy. The year is reached via `AcademicGroup.AcademicYearId` (slots/cohorts are not year-stamped). Reuses `ServiceOccupancyCalculator` for the per-partition saturation flag. Frontend: `StageTimelinePage` (`/admin/timeline`, nav "Calendrier") — a **custom CSS Gantt** (date→% offset via dayjs, no Gantt library); year picker, month axis, collapsible level rows, stage bars → partition-window Drawer. Cache tag `Stage/TIMELINE` + `refetchOnMountOrArgChange` (fine-grained invalidation from the ~15 plan-mutations is not wired yet — Phase 7.6 Phase B / robustness).

### The catalogue/CNPN duplication broke for real on day one (2026-09-01)

`Stage.DurationInDays` and `CurriculumStage.DurationInDays` have always been two statements of one
fact. `CLAUDE.md` predicted they would disagree the first time a text reweighted a stage. Recording
1650.25's 3ᵉ année did exactly that, and it was spotted from the UI within minutes: the four new
stages read 30 j.o., while Chirurgie and Médecine — reused rather than duplicated, correctly — still
read **66 j.o.** and **PerPeriod** from the legacy catalogue.

- **The two halves fail differently, and only one of them is cosmetic.** The duration is duplicated,
  so 1650.25's 30 was already recorded and the stale 66 only misleads the Stages page and
  `PreviewRotationCycleQuery.DurationChecks` — which reports, never guards.
- ⚠ **`RotationMode` is not duplicated at all.** It lives only on `Stage`; no text carries one. So
  `PerPeriod` was live and authoritative, and would have made `RotationArranger` advance the service
  between columns and `SchedulePublisher` write one `ServicePeriod` per column instead of collapsing
  the run. **Latent only because each stage holds a single column** — with kₛ = 1 the two modes are
  indistinguishable. It would have surfaced the first time an axis gave a stage two columns.
- `Cnpn1650Med3CatalogueAlignment` fixes it, and the order is the point: **preserve, then overwrite.**
  Once the catalogue says 30, the only place 66 can still be read is 2174.18's own requirement set —
  and 4ᵉ/5ᵉ/6ᵉ année students are still governed by that text and can still carry a 3ᵉ année credit
  under it.
- ⚠ **The migration raises on the same condition the UI refuses on**
  (`Stages.RotationModeLockedByPublication`). A migration that steps around a guard somebody wrote on
  purpose is worse than one that fails.
- This makes the `Stage.DurationInDays` → advisory rework (Phase 15.1) **due**, not deferred: it will
  recur for every level the new text lands on.

### A failed year is annulled, and so is everything served in it (settled 2026-09-01)

The second half of the redoublant rule, and the half that had teeth. `RegistrationStatus.AnnulsItsStages()`
— one predicate beside `IsYearOutcome()` and `EndsTheCursus()`, read by `OutstandingStageFinder.Fold`
and by the dossier's `DeriveState`, so the two screens cannot disagree about what a student owes.

- **The case that forced it.** Passes Chirurgie, fails the year, repeats the year, fails Chirurgie
  again. Before: he had « une tentative validée », the stage was cleared for good, and
  `FinalYearGuard` let him into his last year — on the strength of a year the faculty had struck
  out, while the last thing he actually did was fail it.
- ⚠ **The filter drops attempts; it never creates a debt.** A stage failed *only* inside an annulled
  year is **not** owed — he is repeating that year and will serve it again. So a stage whose every
  attempt was annulled reads `NotAttempted`, not `ToRevalidate`.
- ⚠ **Only `Failed` annuls.** `Withdrawn` and `Excluded` end the cursus rather than repeat the year,
  and nobody has ruled that what was served before an abandon never happened.
- ⚠ **`Active` annuls nothing, and that is what makes this safe to switch on.** `LegacyImportPlanner`
  wrote `Active` on every historical registration (`Withdrawn` for the 12 « Retrait » rows) and never
  `Failed` — verified before the change — so no imported cursus becomes outstanding retroactively.
  Reading silence as a failure would have done exactly that.
- **The badge the faculty asked for is derived, never stored.** `DossierAttempt` now carries
  `YearOutcome` and `AnnulledByFailedYear`, so « validé — année redoublée » is a different row from
  « validé ». A stored flag would go stale the moment a year is reopened, and
  `ReopenRegistrationYearCommand` exists because that happens.

### A redoublant redoes the whole year, not the remainder (settled 2026-09-01)

Asked of the faculty while recording 1650.25's 3ᵉ année: a student who failed the year re-serves
**every** stage of the promotion, including the ones he had already validated. Not the outstanding
ones only.

- **No code change: that is already what happens.** `CohortProvisioner` gives the roster a cohorte
  per stage of the promotion, and `StudentAffectationService` dedupes on
  *(registration, cohorte)* — a repeat is a **new** registration, so he gets a fresh
  `InternshipAssignment` for each. Nothing anywhere filters on « déjà validé ».
- ⚠ **Do not confuse it with what `OutstandingStageFinder` answers.** That reads « owed = every
  attempt came back NonValidé » and drives the *final-year gate*, not provisioning. A validated
  Chirurgie is not *owed*, and he still re-serves it. The two questions are different and must stay
  that way.
- It matters more under 1650.25 than it did before: a 3ᵉ année repeater now re-serves the new,
  larger requirement set rather than the old one he failed.
- The other case is untouched: a 4ᵉ/5ᵉ/6ᵉ année student carrying a *credit* from an earlier year
  stays on 2174.18 and goes through `RevalidateStageCommand`, which reopens the one failed attempt
  against the original `Stage` row.

### `Student.Registrations` / `Student.HistoryEntries` (renamed 2026-09-01)

They were `registrations` and `history` — lowercase, public `ICollection`, open setters — on an
aggregate whose CNPN fields are correctly `private set`. Nothing had ever assigned them and
`UserConfiguration` already declared `PropertyAccessMode.Field`, so the encapsulation was intended
and only the naming never followed. Now PascalCase with `private set`.

- `HistoryEntries`, not `History`: a property carrying its own element type's name compiles, but it
  shadows the type inside the class, so the first person who needs `History` as a type there gets an
  error with no obvious cause.
- ⚠ **No migration — but the model snapshot did need updating.** Navigations are CLR metadata, so the
  rename produces no SQL; `ApplicationDbContextModelSnapshot` nonetheless records navigation *names*,
  and left stale the next `migrations add` folds the rename into an unrelated migration. Verified with
  `dotnet ef migrations has-pending-model-changes`. The per-migration `*.Designer.cs` snapshots record
  the model as it was and are never edited.
- No API change: both read handlers project into `StudentResponse` / `StudentRegistrationSummary`.

### The CNPN aggregate, and why one guard was moved *out* of it (2026-09-01)

A clean-code/DDD pass over `Application/Stages/Cnpn/` + `Domain/Stages/Cnpn*`. Behaviour-preserving
except where it was already wrong; 1 278 tests green.

**What moved into the domain.** `CnpnVersion` became an `Entity` with `init` accessors over backing
fields (the `AcademicYear` shape) and three acts — `Correct`, `DeclareEffectivity`,
`WithdrawEffectivity` — which now own the four rules a text can decide alone: another programme's
level, the withdrawal marker, a level beyond its span, a level it already speaks for. The two
handlers had been stating the span rule twice, from opposite sides. `CnpnVersionErrors` sits beside
the entity; error **codes are unchanged**, because they are asserted by tests and read by the
frontend.

**What deliberately did not.** `Correct` is handed a `CnpnSpanFloor` rather than counting its own
`Curricula` / `LevelEffectivities`. An un-Included collection reads as an empty one, this rule has
no unique index behind it, and — measured — removing the `Include` leaves all 23 handler tests
green, because the in-memory provider fixes navigations up from the change tracker. That mistake
would only appear on PostgreSQL, as stranded requirement sets.

**Two `Result`s that could not fail, one parameter that lied.** `RegistrationCnpnStamper.StampAsync`
returns a `StampReport` directly now (its five callers were branching on an unreachable
`IsFailure`), and `CnpnAssignment.ResolveAsync` is gone — no production callers, and an
`asOfAcademicYearId` the body never read while the doc described what it anchored. The walk-back it
carried is now `EntryYearDeduction`, pure and shared with the stamper, which had a second copy.

⚠ **The regression this pass produced and caught.** Reusing the targeting selector as a subquery
made the *apply* silently write nothing: `AsNoTracking()` on the shared query propagated to the
query loading the students the apply mutates. Two tests failed; the marker moved to the callers.

### Répartition annuelle des stages (the published planning table)
`GetLevelRepartitionQuery` (`Application/Stages/Repartition/`, endpoint
`GET /levels/{levelId}/repartition?academicYearId=`) is the schedule grid **turned a quarter**: the
grid asks "where does this cohort go each period", this asks "which groups does this service hold
each period", across every stage of the level at once. Pure pivot of `CohortSlotAssignment` — no new
modelling, no marks, no execution state.

- **The column axis is the finest partition present.** Stages of one level do not share a period
  length (`Med6`: ANES REA monthly, Chirurgie two-monthly), so `PeriodAxis.Build` drops any window
  that strictly contains another and keeps the atoms. A stage then occupies the columns whose
  **midpoint** falls inside its own window — bare overlap would let a window spilling a few days past
  a boundary seize the next column from the stage that really runs there. A multi-column period
  repeats its cell in each column carrying one `SlotId`, exactly as the published table does.
- **Rows and stages share one sort key: the lowest group number the line opens on.** That single rule
  reproduces both reference images — `Med3` (Médecine 1-40 above Chirurgie 41-80) and `Med6`
  (Chirurgie 1-20, ANES REA 21-30, URGS-TRAUMA 31-40…) — and keeps the rotation cycle readable down
  the page. Neither is alphabetical or by id; don't "fix" it to either.
- **The colour band is the partition of the row's *first* period.** A row visits groups from every
  partition over the year, so only the opening cell says which block of the rotation it is.
- **The chef is resolved as of the first column's start**, from `ServiceChefAssignment`, falling back
  to the sitting chef where no tenure covers it. A répartition reprinted years later keeps naming the
  chef it was published with.
- **It ships as an export, not a public page** — the faculty uploads the file to its own site.
  `RepartitionPage` (admin → Formation → Répartition annuelle) previews it, then serializes **the
  very DOM node on screen** into a standalone `.html` (`buildRepartitionFile`, stylesheet inlined via
  Vite `?raw`) and prints that same file to PDF. Preview, print and upload are one document by
  construction, not three implementations kept in step.
- ⚠ **Nothing renders yet: the database holds 0 `StageSlots` and 0 `CohortSlotAssignments`** (13,604
  cohorts, but no grid). The legacy Access base had no planning grid — see *No periods in legacy*.
  Every level's répartition is empty until periods and cells are authored in `ScheduleGridModal`.
- **Verified end-to-end 2026-08-08** on level 3 (Médecine + Chirurgie, 80 groups each): allowed
  services and 2 periods per stage seeded, then **the real `RotationArranger`** run from the UI —
  320 cells, 26 rows, 4 columns, 12.8 KB self-contained export. The union axis did its job: two
  stages declaring *different* windows merged into one 4-column axis. Partition banding tracked the
  arranger's own A/B allocation, and the 52 unplanned cells were hatched and counted. Non-contiguous
  cells printed as `1, 3, 5, 7, 9, 11` — correctly *not* collapsed to `1-11`.
  Revert script: `scratchpad/revert-repartition-testdata.sql` (slots cascade to cells).
  - ⚠ That table is **half empty by construction**, and the empty half *is* the `SlotOverlapGuard`
    defect made visible: because the two stages may not share date windows, they were forced into
    sequential ones, so Médecine holds P1-P2 and Chirurgie P3-P4. In the published `Med3.png` all
    four columns are full for **both** stages.
- ⚠ **Two things block authoring a real planning**, both found in that pass — see
  `SlotOverlapGuard` above, and the empty-`AllowedServices` note below.

### No group in two services at once (`GroupScheduleConflictGuard`, 2026-08-08)
The rule that actually prevents double-booking, checked **where a group is really placed** rather than
where a date column is merely declared. A `StageSlot` on its own places nobody; only a
`CohortSlotAssignment` puts a group somewhere.

- **The planning model it exists to permit.** For a level with *S* stages, each stage declares
  `periods-per-stage × number-of-partitions` periods over **one shared date axis**, and the partitions
  cross over. Med3: 2 stages, 2 partitions → 4 periods in each stage, A→Médecine P1-P2 + Chirurgie
  P3-P4, B mirrored. Every column is full for every stage, and each service carries only half the
  promotion at a time. `SlotOverlapGuard`'s old level-wide rule made this impossible to author.
- **What it refuses**: assigning a cohort to a slot when *its academic group* already occupies another
  slot whose window overlaps. A group has one cohort per stage, so the conflict is naturally
  cross-stage. Error `Schedule.GroupAlreadyPlaced`, which names the group — that is what lets it tell
  the legitimate case (A in Médecine P1, B in Chirurgie P1, same dates) from the mistake (group 1 in
  both). A windows-only comparison never could.
- **Applied in three places**, because there are three ways to double-book:
  1. `SetCohortSlotAssignmentCommandHandler` — assigning a cohort to a slot.
  2. `RotationArranger` — bulk, one snapshot query via `BuildAsync`, so no N+1. Skips conflicting
     cells and returns `GroupConflicts`; `AutoArrangeResult` and `MacroPlanResult` both carry it and
     both UIs show it, because a run that writes nothing otherwise looks like it had nothing to do.
     `GenerateMacroPlanCommand` inherits the check — both arrange paths go through the arranger.
  3. `UpdateStageSlotCommandHandler` via `EnsureSlotCanMoveAsync` — **dragging a period's dates
     double-books every group already in it without touching a single cell**, and the per-stage
     `SlotOverlapGuard` cannot see that. Missing this was a regression opened by narrowing the
     overlap rule.
  - ⚠ The slots being rewritten are **excluded** from the snapshot (`ignoredSlotIds`), or a second
    arrange of the same stage would see the cells it is about to replace and refuse every one.
  - ⚠ **Conflicts are computed before the stale-cell removal, and conflicting pairs are excluded
    from it.** Deciding afterwards deleted a cohort's existing cell and then declined to write the
    replacement — re-running an arrange across all partitions silently destroyed a correct plan.
    Covered by `A_refused_cell_keeps_the_plan_it_already_had`.
- Changing only the *service* of a cell the group already holds is not a conflict.

### An empty `Stage.AllowedServices` blocks planning — it does not mean "all services"
`RotationArranger` refuses with `Schedule.NoAllowedServices` when the list is empty, so
"Répartition auto." can never run. **25 of 27 stages have zero allowed services** (2 rows in
`StageAllowedServices` for 148 services), which is a large part of why the grid has never been filled.
`StageDetailPage` used to read *"Aucune restriction — tous les services peuvent être utilisés dans la
grille"* and badge it **Tous** — the exact opposite of what the backend does — so an admin had no way
to learn why the button was disabled. Corrected 2026-08-08 to an orange **Aucun** badge and a warning.
The backend is the side that is right: auto-arranging Médecine across all 148 services, Pharmacie and
Réanimation included, is not a sensible default.

### Date pickers (`@mantine/dates`)
`@mantine/dates` + `dayjs` are installed; `@mantine/dates/styles.css` is imported in `main.tsx` and the app is wrapped in `<DatesProvider settings={{ locale: 'fr', firstDayOfWeek: 1 }}>`. Mantine 8 date components use **string `"YYYY-MM-DD"` values** (no `Date` conversion), which matches the backend `DateOnly`. `StageSlot` start/end in `ScheduleGridModal` uses `DatePickerInput type="range"`.

`GenerateMacroPlanCommand` (`Application/Stages/MacroPlan/`, endpoint `POST stages/macro-plan`) fans out per `(RotationGroup, StageId, PeriodNumbers)` entry to those services — one call creates cohorts → affects → arranges → optionally publishes. This is what realises the macro split (e.g. Med3: A→Médecine[1,2]+Chirurgie[3,4], B mirrored). The frontend Macro Plan tab (`GroupsPage`) drives it via a partition×stage matrix with a per-cell period window.

### Auto-arrange algorithm (capacity-proportional cyclic rotation + RotationGroup partitions)
`RotationArranger.ArrangeAsync` (invoked by `AutoArrangeStageScheduleCommand`) uses the same pattern visible in the faculty rotation documents (`example_stage_assignement/`):

**How it works** (over the scoped cohort/slot subset — by default all cohorts/slots of the stage):
1. **Capacity-aware proportional allocation** (largest-remainder method): the weight of a service is `floor(capacity / avgStudents)` = the number of whole average cohorts it can hold. **A service smaller than one cohort gets weight 0 and is excluded** (forcing an atomic group into it would always overflow). N cohort-slots are distributed proportionally to weight, leftover by largest fractional remainder → `allocated[i]` cohorts per service per period, summing to N. When total cohort-capacity ≥ N this never over-fills a service; only a genuine per-period shortfall saturates. (Planning preview, before students are assigned, falls back to raw-capacity proportions.)
2. **Service queue**: build an ordered list `[S1 × allocated[0], S2 × allocated[1], ...]` of length N.
3. **Cyclic shift per period**: period P reads the queue at offset `phase(P) × (N / cycleLength)`, where **`cycleLength` = the targeted cohorts' participation footprint** (the slots they already occupy in this stage ∪ the slots being arranged now) and `phase(P)` = P's position within that ordered footprint — *not* the filtered-window index (which would restart at offset 0 and repeat services) and *not* the full stage-slot count (which would break the macro matrix's single-window half-cycle swap). With no prior assignments the footprint equals the window, so a first-time full arrange behaves exactly as before. Every cohort visits a different service block each period.

**Why this matches the faculty documents:**
- Service "Méd A" takes 2 groups per period: P1=groups 1-2, P2=21-22, P3=41-42, P4=61-62. The offset is 80/4=20. This exact pattern falls out of the cyclic rotation naturally.
- **Capacity is a per-(service × period) constraint, not a global one.** Total allowed-service capacity ≥ total students does NOT guarantee no saturation: groups are placed as atomic ~20-student units, so any allowed service smaller than a group always saturates, and a single period can stack two partitions onto the same service if both are arranged into it (the reason to use period windows — see saturation diagnosis below). `RotationArranger` counts saturated services from the **actual** per-cell load, not the global average.

**RotationGroup / Partition system:**
- `AcademicGroup.RotationGroup` is a persistent nullable string label (A, B, C…). **Partitions are scoped per (AcademicYear, Level)** — different levels can have different partition counts (e.g. 2 in 1Med, 4 in 2Med). `AssignRotationGroupsCommand` takes an optional `LevelId`; a group belongs to a level by `AcademicGroup.LevelId` **and nothing else** (since `SplitAcademicGroupsPerLevel`, 2026-08-13). ⚠ It used to fall back to "has a registration at that level", for legacy rows that carried no `LevelId` — but that also reached « Non réparti », which holds every promotion's unassigned students at once, so cutting one level handed a partition label to a bucket of 4,725 people. `GetAcademicGroupsQuery` keeps the wider reach on purpose: it is the screen those students are assigned *from*. `CohortProvisioner` matches groups to each stage's level so a label reused across levels never produces cross-level cohorts. The label is set once and reused across all stages **of that level**.
- ⚠ **Before that migration one roster served several promotions at once.** The importer keyed rosters on `(ANNEE_UNIV, GROUPE_STG)`, and legacy group numbers restart at 1 per promotion — so the 3rd year's "Groupe 1" and the 5th year's became one row. 80 of the 100 numbered rosters of 2025-2026 carried four or five promotions. Since a roster is the unit `GroupScheduleConflictGuard` forbids from being in two services at once, the 3rd year's placements refused the 5th year's, and one global `RotationGroup` cut per year meant re-cutting any promotion silently re-cut every other. Keyed `(AcademicYearId, LevelId, GroupNumber)` `NULLS NOT DISTINCT` now, and numbering restarts at 1 per promotion.
- `numPartitions` = `existingLabels.Count` if any groups already carry a label; otherwise = `request.PartitionCount ?? services.Count`. This respects the structure set up on a previous stage's auto-arrange.
- Unassigned groups are distributed round-robin into the smallest partition (by current count) in `GroupNumber` order.
- Cohorts are sorted by `(RotationGroup, GroupNumber)` before building the queue. All cohorts in partition A occupy a contiguous block → the cyclic shift moves the entire A-block to a different service section each period, consistent across stages.
- **Frontend**: `ScheduleGridModal` shows a violet dot badge per cohort row and partition filter chips above the grid. The "Répartition auto." dialog targets the active partition chip + a period-window multi-select (sent as `partitionLabels` + `periodNumbers` in the POST body), and shows an orange Alert when the chosen window already holds another partition's cells (stacking guard). The saturation banner lists the real offending cells (`{service} · P{n} : {occupied}/{capacity}`) using the backend's per-cell `occupiedSeats`, not a misleading global total. `GroupsPage` table shows `RotationGroup` badge; `EditGroupModal` has a text input to set/override the label manually.

**Why the greedy approach (previous version) was wrong:**
- The greedy treated each period as an independent assignment race — large cohorts competed for capacity every period, consistently over-filling high-capacity services and under-filling others.
- The `visited` tracking plus capacity competition caused cascading saturation in later periods.

**Capacity is measured GLOBALLY across stages (fixed 2026-06-03, was Phase 7.5 #1):** occupancy is no longer grouped by `(StageSlotId, ServiceId)` within one stage. `ServiceOccupancyCalculator` (`Application/Stages/Planning/`) loads every planned `CohortSlotAssignment` targeting a service and exposes `ServiceOccupancyLookup.LoadOn(serviceId, start, end)` = total students on that service whose slot window **overlaps** `[start,end]` (overlap = `a.Start <= end && start <= a.End`), across **all** stages. This is the single source for the three places load matters:
- `GetStageScheduleQueryHandler` — each cell's `occupiedSeats` shows the real cross-stage load (the macro case: a service shared by partition A in stage X and partition B in stage Y over overlapping dates shows the combined load, not a per-stage half).
- `RotationArranger` — saturation is counted after the save against the global load per `(slot window, service)`.
- `SchedulePublisher.EnsureIntakeAsync` (named `EnsureCapacityAsync` until 2026-08-17) — **pre-publish guard** (previously absent): both `PublishCohortAsync` and `PublishStageAsync` refuse with `StageErrors.CapacityExceeded` if any service would exceed capacity over an overlapping window — **unless `allowOverCapacity` is set**, an explicit opt-in override threaded from the publish commands/endpoints (`PublishCohortScheduleCommand.AllowOverCapacity`, `PublishStageScheduleCommand.AllowOverCapacity`, `GenerateMacroPlanCommand.AllowOverCapacity`). ⚠ Since 2026-08-17 the override waives only the *occupancy* half: `LevelNotAdmitted` is checked whatever the caller asks for, because a service that does not take a promotion is not a target being missed. The frontend exposes it as an « Autoriser le dépassement d’effectif » checkbox in the publish confirm dialogs (per-cohort and "Publier tout"). Distinct cohorts across stages are different physical students, and a group's cohort-in-X vs cohort-in-Y run in non-overlapping windows, so summing overlapping windows does not double-count. Counting `CohortSlotAssignment`s covers both planned and published load (publish keeps the cells); purely ad-hoc `ServicePeriod`s (null `CohortSlotAssignmentId`) are not yet included.

### Long-running requests are not aborted by navigation or other requests
RTK Query mutations ("démarrer", publish, macro plan) each get their own `AbortController` and are independent of queries (student search etc.), so one never cancels another. SPA navigation does not reload, so an in-flight mutation runs to completion server-side (RTK auto-aborts only *queries* when their last subscriber leaves, never mutations). There is no client request timeout. Implication: a slow mutation finishes invisibly in the background — keep handlers idempotent so an accidental re-trigger is safe (Phase 7.5 robustness item).

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

## Legacy Access database (`Medecine.mdb`)

The system PGSH replaces was VB.NET over a Microsoft Access file. **The file is gitignored** (`*.mdb`)
— it carries 10,203 real students' CIN, date of birth, address and a plaintext `password` column, and
must never enter the repository. Read it with `Microsoft.ACE.OLEDB.16.0` (64-bit, already installed);
ADO uses **ANSI-92 wildcards** (`%`, `_`), not Access's `*`/`#`.

**It is a properly normalized schema, not a spreadsheet** — 29 declared foreign keys, composite primary
keys, and zero orphans across `AffectStage→stages`, `AffectStage→SERVICES`, `AffectStage→Inscription`,
`Inscription→ETUDIANT`. The joins are structurally guaranteed; an importer needs no orphan handling.

| Legacy | Rows | PGSH |
|---|---|---|
| `AffectStage` (PK `NUMINS,CODEST,PER1`) | 104,924 | `ServicePeriod` |
| `Inscription` (PK `Numins`) | 43,608 | `Registration` |
| `ETUDIANT` (PK `NO_ORDRE`) | 10,203 | `Student` |
| `SERVICES` / `stages` / `Niveaux` / `anneeuniv` | 148 / 27 / 20 / 21 | `Service` / `Stage` / `Level` / `AcademicYear` |

**Grain mismatch — the key structural fact.** `AffectStage` is at *period* grain. There is no
stage-level row: the internship is implicit in `(NUMINS, CODEST)` — 92,187 single-period, 6,368
two-period, 1 three-period. So one `(NUMINS, CODEST)` → one `InternshipAssignment`, each row → one
`ServicePeriod` + `ServiceEvaluation`, and `FinalScore` is **derived** by `StageScoring`, never imported.

**Repeating students are the norm, and the model already handles them.** 2,920 `(student, level)` pairs
repeat (2,269 ×2, 515 ×3, 136 ×4), concentrated in MED07. `CreateRegistrationCommandHandler` already
allows this deliberately — its duplicate guard is on `(StudentId, AcademicYearId)`, not level, and its
chronological check skips when `levelDiff == 0`. The old app re-created the full stage set each year and
the operator graded only what was genuinely redone, so marks accumulate across registrations. That is
what `GetStudentLevelDossierQuery` folds together.

**Mark semantics — settled with the user 2026-08-07.** `Note >= 10` is validated, `Note < 10` is not
(so `0` is a real failing mark, not a "not applicable" sentinel); `-1` and NULL mean ungraded. That is
exactly `StageScoring.ValidationThreshold`, so an imported row becomes an `EvaluationMode.Numeric`
evaluation with `TotalScore = Note` and the verdict falls out of the existing domain rules untouched.

Consequence: **`Resultat` never needs decoding.** PGSH derives the verdict from the mark, so the
undecodable `{0,3,6,10}` column (which does not correlate with pass/fail — `Resultat=0` holds 71,320
passing *and* 2,744 failing rows) is simply not imported. Rows with `Note` of `-1`/NULL import as
periods with no evaluation, i.e. `NonÉvalué`.

**Other migration hazards.** No email column at all (`Users.Email` is NOT NULL UNIQUE) — `prenom_nom@um5.ac.ma`
yields only 31 collisions over 10,203 names, but the numeric suffix must be allocated in a **deterministic
order** (by `NO_ORDRE`) or re-running the import swaps identities between real people. Names are one
`NOM PRENOM` field, surname first, and 2,730 (27%) have 3+ tokens where the split is genuinely
undecidable. Only 5,510 of 10,203 have a CNE (`Student.CNE` is NOT NULL UNIQUE). `SERVICES` has no
hospital FK — the hospital is embedded in the name string (`"Hôp.IbnSina: Médecine A - Pr.H.Harmouch"`),
~16 parseable prefixes, 10 with none, and `CHEF_SERV` is empty throughout. Dates are `dd/MM/yyyy` text;
824 `PER2` values hold free text like `"31/05/2019 & de: 25/06/2019 à:12/07/2019"` — a split period the
old app could not model and PGSH can, as two `ServicePeriod`s.

### The importer — `PGSH.LegacyImport`

A console tool, run manually, **dry run by default**:

```bash
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb                    # plan only, no DB needed
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb --review           # hospital tree, for checking
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb --connection "…" --apply
```

Split so the rules are testable without the gitignored .mdb: `AccessLegacyReader` (OleDb, Windows-only,
reads verbatim) → `LegacyImportPlanner` (pure, builds the whole graph) → `LegacyImportWriter` (saves in
dependency order, batched). 53 tests cover the mapping; the reader has none by design.

**✅ Applied to the development database 2026-08-07** — 1 min 24 s, counts match the plan exactly.
The fixture data it replaced was backed up first (`pgsh-prefixture-backup-*.dump`, gitignored).

**Maps all 104,924 rows with no failures:**

| | |
|---|---|
| 1 Center / 16 Hospital / 148 Service | hospital tree parsed out of the service name strings |
| 16 Level / 27 Stage / 21 AcademicYear / 1,003 AcademicGroup | |
| 10,203 Student | 4,695 with a synthesised `LEGACY-{NO_ORDRE}` CNE |
| 43,605 Registration / 13,604 Cohort | 3 legacy duplicates dropped, see below |
| 98,555 InternshipAssignment / 105,626 ServicePeriod / 87,092 ServiceEvaluation | 83,439 marks ≥ 10, 3,653 below |
| 727 notes | 702 split periods, 18 students with no registration, 4 services naming no hospital, 3 duplicate registrations |

Counts reconcile exactly: 98,555 = the distinct `(NUMINS, CODEST)` pairs; 105,626 = 104,924 rows + 702
interrupted rotations expanded into a second period.

**Decisions worth knowing before re-running it:**
- **Cohorts and groups are derived, not imported.** Legacy has no cohort concept; `AcademicGroup` comes
  from `(ANNEE_UNIV, GROUPE_STG)` and `Cohort` from `(Stage, AcademicGroup)`. 17,529 registrations carry
  no group — only 67 of those have rotations — so they land in a per-year "Non réparti" group (number 0).
- **`CohortSlotAssignmentId` is always null.** There was no planning grid, and null already means
  "recorded outside the published schedule", which is exactly what history is. No `StageSlot` rows.
- **The lifecycle is replayed, never back-filled.** `Start() → CompletePeriod() → SubmitEvaluation() →
  Validate()`, so `FinalScore`/`Result` come from `StageScoring` like everything else. Fully-marked
  stages are ratified so 100k finished rotations don't sit in the "to ratify" worklist.
- **Registrations import as `Active`** (withdrawals excepted): whether a student passed the *year*
  depends on subject marks, which are out of scope. Inventing a pass or fail would be a lie.
- **No Employee rows are created.** The professor is a name inside the service string; a unique email
  cannot be conjured from "Pr.H.Harmouch". `ServiceChefId` stays null, the name goes in `Description`.
- **One synthetic Center** ("Centre Hospitalier Universitaire de Rabat") because `Hospital.CenterId` is
  required.
- **City and `ServiceType` do not exist in the source** — both are inferred, and `--review` prints the
  whole reconstructed tree so they can be checked in one pass. Cities the legacy string actually names
  (Kénitra, Salé, Témara) are kept; the other 13 of 16 default to Rabat and are flagged `?` in the
  review. Type comes out 82 Medical / 55 Chirurgie / 11 Biologie.

  ⚠ **Classify on word boundaries, never bare `Contains`.** The first version put every neurology ward
  in surgery — `"Neurologie"` contains `"urologie"` — and missed `"Urg.Porte.Chirurgicale"`, because
  `"Chirurgicale"` does not contain `"chirurgie"`. `ServiceNameParser` now matches accent-stripped text
  with `\b` anchors and a `chirurg` stem; `LegacyMapperTests.Look_alike_specialities_are_not_confused`
  pins all ten of the real catalogue entries that exposed it.

**One student, two registrations in one year** — `IX_Registration_Student_Year` is UNIQUE, Access
enforced nothing, and 3 pairs out of 43,608 break it. Each is a stale row somebody re-entered instead of
correcting (student 21025416 holds both a `MED00` retrait with no rotations and the real `MED04` with
seven, same year). The importer keeps the row with the most rotations, tie-breaking on the later
`Numins`; on the real data that picks correctly all three times and loses no rotations, because the
discarded side has none in every case.

> ⚠ This one only surfaced against real PostgreSQL. `UseInMemoryDatabase` does not enforce unique
> indexes, so the 406-test suite was green while the import died 25 s into writing. The regression is
> pinned in `LegacyImportPlannerTests` at the planner level, which is the only layer that *can* catch it.

### ⚠ The sample seeder collides with the import

`PGSH.MigrationService` runs `Seeder.SeedAsync` on **every Aspire start**, and it creates 13 `Level`
rows and 3 `AcademicYear` rows. Both carry unique indexes — `UNIQUE(Year, AcademicProgram)` and
`UNIQUE(Label)` — and the importer creates 16 levels and 21 years overlapping them. Seeded and imported
data cannot coexist.

Set **`Seeding:Enabled = false`** (`PGSH.MigrationService/appsettings.json`, or the env var
`Seeding__Enabled=false`) before importing, and leave it false. Migrations still run; only the Bogus
fixtures are skipped. The importer's own `--apply` guard now counts every table it writes — not just
students — so it refuses rather than failing part-way through on a constraint.

**The three static users are not fixtures, and have their own flag.** `amine.bennani@um5.ac.ma`,
`admin.pgsh@um5.ac.ma` (Scolarité) and `employee.test@um5.ac.ma` carry fixed GUIDs and known e-mails so
Keycloak's email-matching resolves them — they are the only way into the application. They are
therefore seeded by **`Seeding:StaticUsers`** (default `true`), which is deliberately independent of
`Seeding:Enabled`: a database holding real imported records still needs the accounts even though it
must never receive the Bogus data. Each is created only when its e-mail is absent, so it is safe on
every start, and none of their identifiers collide with imported students (checked: `Appogee`
`22003344` and CNE `G135000111` are unused by the legacy set).

That split exists because re-enabling the whole seeder is *not* a safe way to get them back: every
section skips when its table has rows — **except** `SeedShowcaseServiceAsync`, whose guard is
`Hospitals.AnyAsync(h => h.LocalisationMaps != null)`. No imported hospital carries GPS, so that one
would fire and inject a demo service into real data.

**Out of scope by decision:** the pedagogical half of the file (`MATIERES`, `notes*`, `ResExamClin`,
`creditmat`, `jury`, `amphi`, `groupetp`, anonymized double-marking, credits) belongs to a separate
project/microservice. PGSH owns stages only, so the Access app cannot simply be switched off at cutover.
Useful signal: `CRDST` (stage credits) is `0` on every row while `CRDMAT`/`CRDTP` are used on ~42,500 —
the stage half of their credit system was already dead, which is a clean seam.

## Scale: what real data broke, and the rule it taught

Seeded fixtures hid every unbounded query. The legacy import turned them into crashes overnight, so
treat these figures as the baseline any list screen must survive:

| | |
|---|---|
| 1,003 AcademicGroups (101 in 2025-2026) | 13,604 Cohorts (1,684 in the current year) |
| **"Non réparti" 2025-2026 holds 4,725 students in one group** | 681 cohorts on a single stage across years |
| 10,203 Students · 43,605 Registrations · 98,555 Assignments | 105,626 ServicePeriods |

Fixed (2026-08-07):

| Handler | Was | Now |
|---|---|---|
| `GetGroupByIdQuery` | whole roster + 2 correlated loan lookups **per student** — 4,725 rows | paged (25) + debounced search; header count via `StudentCount` |
| `GetCohortsByStageQuery` | **no year filter at all** — every year the stage ever ran | year-scoped + paged |
| `GetAcademicGroupsQuery` | all 1,003 groups | paged, with `StudentCount` per row |
| `GetStageScheduleQuery` | unfiltered year returned every year's cohorts | falls back to the current year |
| `GetYearTimelineQuery` | all 1,684 cohorts of a year to draw one stage | optional `StageId` narrows the tree |

> ⚠ **A nested collection is the one pagination audit misses.** Grepping for handlers returning
> `List<T>` finds the flat offenders; it does *not* find `GetGroupByIdQuery`, whose 4,725 rows sit
> inside a single `GroupDetailResponse`. When reviewing a query for scale, ask what the *response*
> contains, not what the handler's generic parameter says.

> ⚠ **Counting is not listing.** `AdminDashboardPage` fetched all 1,003 groups to render one number.
> Ask for `pageSize: 1` and read `TotalCount`.

On the frontend, screens that need a list as a *lookup* (dropdowns, filters, assignment grids) use
`getAcademicGroupOptions` / `getCohortOptionsByStage` — thin wrappers that still hit the paged
endpoint but ask for one large page and unwrap `.items`. Screens that display a list paginate for real.

## CNPN versioning — why the curriculum is its own aggregate

The CNPN (national pedagogical standards) is reissued whenever the ministry decides, and what it
contains is a political outcome — a new minister can change a level's stages at will. **This already
happened five times in the imported history**, e.g. *Pharmacie Clinique 3* ran 2019-20 → 2022-23 and
then vanished while Clinique 1 and 2 carried on.

Before `Curriculum`, the model could not express any of it: `Stage` was `(Id, Name, Coefficient,
DurationInDays, LevelId)` with **no temporal dimension at all**. The only link to a year was indirect,
through `Cohort → AcademicGroup → AcademicYear` — which records *what was executed*, not *what was
required*. Three consequences: "which stages did 3e année Médecine have in 2024?" was answerable only
by inference (a stage nobody ran that year looks removed); editing a `Stage` retroactively rewrote
what every past assignment displays; and adding a successor left the old one indistinguishable from a
live one.

**The rejected design was a validity window** (`Stage.EffectiveFrom/To`). It is the wrong shape, not
merely impractical: a window says *"this stage is valid from X to Y"*, which forces someone to know
when it ends, and cannot express a stage that is dropped and later reinstated. A CNPN says
*"in year Y, level L requires exactly this set"* — a per-year requirement set predicts nothing.

```
Curriculum  (aggregate root, UNIQUE(LevelId, AcademicYearId))
  └── CurriculumStage  (StageId, Coefficient, DurationInDays)   ← that year's weight
```

- `Stage` stays the **timeless catalogue entry**, so historical `InternshipAssignment`s keep pointing
  at a stable identity. Everything that varies between years lives on `CurriculumStage` — including
  coefficient and duration, because a text can keep a stage and reweight it.
- `Curriculum.CopyFrom(previous)` is the realistic flow: most years repeat the last one with one edit,
  and cloning keeps each year an independent record rather than a diff nobody can read back.
- `RemoveStage` raises `CurriculumStageRemovedDomainEvent`. Removal settles nothing on its own —
  students who failed that stage still owe it, and the administration decides each case by hand.

### An abolished stage is still served — settled 2026-08-07

**When a stage is removed from the CNPN, a student who failed it still has to serve it.** Removal
releases *new* students from the requirement; it does not erase an obligation already incurred. There
is **no waiver and no substitution** — no "dispense", no equivalent stage standing in for another.

This is exactly why `Stage` is a timeless catalogue entry rather than something with an expiry date:
the stage record survives its removal from the curriculum, so it can still be served. The service is
still physically there too — only the curriculum entry went away.

Nothing extra was needed to support it: `RevalidateStageCommand` already re-opens any stage the
student has a settled `NonValidé` on, regardless of whether the current CNPN still lists it, and
places the retake back in the service of the failed rotation. `AbolishedStageRevalidationTests` walks
the whole *Pharmacie Clinique 3* scenario — comparison flags it Removed, the retake is served in the
original service, the original failure stays untouched as history, and passing the retake moves the
dossier to `Validated` with both attempts on record.

> The one wart: `InternshipAssignment.CurrentCohortId` is non-null, so a retake of an abolished stage
> needs a cohort created for it even though no *group* runs that stage. It is harmless but it models
> a group activity for what is one student's individual rotation. Fixing it properly means moving
> `StageId` onto `InternshipAssignment` and making the cohort optional — a wide refactor, deliberately
> not done for a case that currently affects nobody.

**Writes:** the aggregate's behaviour is reached through two commands, both Scolarité-only.

| | |
|---|---|
| `PUT levels/{levelId}/curriculum/{yearId}` | `SaveCurriculumCommand` — the **whole text at once**, because that is how a CNPN is issued. Idempotent, so re-sending the same set changes nothing. |
| `POST levels/{levelId}/curriculum/{yearId}/copy` | `CopyCurriculumCommand` — opens a year from another year's text. Refuses if the target already has one; amending goes through Save. |

Save **reconciles** against what is stored rather than replacing it wholesale: a stage that disappears
from the submitted set goes through `Curriculum.RemoveStage` and raises its domain event, while one
that is kept with a different coefficient is amended in place and raises nothing. Without the
reconciliation a dropped stage would vanish silently, which is precisely the event students who failed
it depend on.

**Screen:** `CurriculumPage` (admin → Académique → *CNPN (programme)*, `/admin/curriculum`). Pick a
level and two years; the table shows each stage as `Ajouté | Retiré | Recoté | Inchangé` with both
coefficients, changes first. When a stage is `Retiré` a red banner states the rule explicitly — the
student repasses it anyway, because removal releases only new inscrits.

> **The page is still read-only.** It compares and displays; recording a newly published CNPN is
> API-only for now. The editing UI — stage picker, per-stage coefficient/duration, and a "cloner
> l'année précédente" button over `CopyCurriculumCommand` — is the remaining piece.

**Reads:** `GetCurriculumQuery(levelId, yearId)` answers "what was the CNPN then";
`CompareCurriculaQuery(levelId, fromYear, toYear)` returns per stage
`Unchanged | Added | Removed | Reweighted` with both sides' coefficients — the view behind manual
revalidation, where a student is judged against the text of the year they failed in but can only be
re-planned against today's.

**Backfilling history — ✅ applied 2026-08-07: 51 curricula / 175 stage entries.** The derivation lives
in `CurriculumHistoryReconstructor` (Application) because it has two callers:

```bash
# authenticated endpoint, dry run by default
POST curricula/seed-from-history          { "dryRun": true }

# same rule without an HTTP identity — the pass that follows a legacy import
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "…"          # dry run
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "…" --apply
```

It is an approximation and says so: a stage the text required but which no group ran leaves no trace.
**Idempotent** — verified by re-running: the second pass reported `0 created, 51 already recorded,
left alone`, so a year confirmed by hand survives.

The history is now explicit data rather than inference:

| Pharmacie Y5 | required |
|---|---|
| 2019-20 → 2022-23 | Clinique 1, Clinique 2, **Clinique 3** |
| 2023-24 → 2025-26 | Clinique 1, Clinique 2 |

> Migrations can now be authored with `--startup-project PGSH.MigrationService` (it carries
> `Microsoft.EntityFrameworkCore.Design`), which avoids having to stop a running `PGSH.API` to free
> its build output.

## Revalidation is demande-driven and served where the student failed

Confirmed with the user 2026-08-07:

1. **It starts from a demande.** Revalidation is never automatic — the student requests it. The
   Demande service is Phase 5, so `RevalidateStageCommand.DemandeId` carries the reference and, since
   only `Delocalization` has a column for it today, the link survives in the audit entry. **A
   revalidation needs its own `DemandeId` column when Phase 5 lands.**
2. **The retake is served in the service where the stage was failed**, not wherever this year's grid
   would send the student's group. Leave `ServiceId` null and the failed rotation's service is reused.
   A change of service is itself subject to an approved demande.
3. It is therefore an **ad-hoc placement like a délocalisation** — `CohortSlotAssignmentId` stays null,
   the schema's own meaning for "outside the published schedule".

**Batch or individual?** Neither, exactly — the target service is *per student*, and batching is only a
convenience over that rule. Failures scatter far too widely to send everyone to one place:

| Stage | Failed attempts | Distinct services |
|---|---|---|
| CHIRURGIE | 377 | **26** |
| MEDECINE | 441 | **21** |
| GYNECOLOGIE OBSTETRIQUE | 386 | 5 (≈77 per service — a real cluster) |

So: resolve each student's own service, then group the resulting list by service to process a whole
cluster in one window.

> ⚠ **`Result<T>`'s implicit operator maps a null value to a FAILURE** (`Error.NullValue`), so an
> optional result can never be expressed as `Result<T?>` returning null — it silently becomes an
> error. Resolve optional values only when they are wanted, and keep `Result<T>` non-nullable.

## There is no average above the stage — settled 2026-08-08

The **only** mean in this domain is *inside* a stage: a stage holds several periods, so its note is
the mean of its periods' marks. That is `StageScoring` / `InternshipAssignment.RecomputeFinalScore`,
and it already exists.

Above that, **nothing**. No year average, no cursus average, no coefficient-weighted roll-up. A
relevé prints each stage's own note and stops. `Stage.Coefficient` exists in the schema but **no rule
uses it**, and it must not be pressed into service to invent a moyenne.

This closes an open question carried since the student portal was built (session 10), where the
dashboard deliberately shipped without a "moyenne générale" rather than guess a formula. Do not add
one — if a future requirement needs it, it is a new rule to be agreed, not a derivation to be
assumed.

## `Registration.Status` is unmanaged — and why past years all read "En cours"

*(Established with the user 2026-08-08, after the student portal made it visible.)*

**The gap.** PGSH covers stages only. It is **not linked to the pedagogical side of the faculty** —
the system that knows a student's subject marks and therefore who passed the year and who repeated
it. Nothing in PGSH ever moves a `Registration` off `Active`, so every past registration a student
holds still reads `Status = Active` → the badge "En cours" on a year that ended two years ago. The
badge is faithful; the data was simply never closed out. Do **not** patch this in the UI.

⚠ Not to be confused with `InternshipAssignment.Status`/`Result`, which *are* managed and are the
subject of the ratify-vs-pass rule above. This is the **registration** (the academic year enrolment),
one level up.

**The rule to apply later** *(user, 2026-08-08 — an inference, deliberately deferred)*. Order a
student's registrations by academic year. The most recent one is the year in progress; every earlier
one can be settled from the shape of the sequence alone, because a student only re-registers at a
level they did not clear:

| Level sequence | Reading |
|---|---|
| 1, 2, 3, 4 | 4 = **en cours**; 1, 2, 3 = **validated** (they progressed past each) |
| 1, 2, 3, 3, 4 | the **first** 3 = **failed** (repeated), the second 3 = validated, 4 = en cours |

So: *a registration is failed when a later registration exists at the same level; otherwise it is
validated; the latest is in progress.*

**Why it is not implemented yet.** It is an inference from enrolment history, not a fact — the
authoritative answer lives in the pedagogical system PGSH cannot see. It also has cases the table
does not cover and which need a ruling before any code is written:

- a student whose **latest** registration is not in the current academic year (dropped out, on leave,
  graduated) — is the last one still "en cours"?
- a **skipped** level, or a non-consecutive repeat (1, 2, 3, 4, 3) — the 2nd-year-later retake is a
  revalidation pattern, not a repeat of the year;
- a student who **passed the year while still owing a stage** — already a settled rule here (see the
  revalidation notes above), so "registration validated" must **not** be read as "all stages
  validated". The two verdicts are independent, and the graduation gate is the place that joins them.
- writing an inferred verdict into `Registration.Status` **destroys the distinction** between
  "inferred" and "known". If it is implemented, it should be a derived/read-model field, or carry a
  provenance flag, so a later link to the pedagogical system can overwrite guesses without
  overwriting facts.

**⚠ Superseded in part, 2026-08-09 — a year is now closed by declaration, not by inference.** The
last bullet above is answered: `Registration.OutcomeSource` is the provenance flag, and
`RecordYearOutcome` refuses to let an `Inferred` verdict overwrite a `Declared` one. See *Closing a
year* below. What is **still open** is the inference itself, for the six imported years nobody will
ever upload a canvas for — the first three bullets need rulings before that is written (Phase 14.3c).

## Stages of unequal length on one axis — the general solution (2026-08-09, session 15)

The first version of the rotation cycle took **one** `periodsPerStage` for a whole block, which is fine
for the new 3rd year (two semesters × three stages × one period) and useless for the 6th, where four
stages take two periods and two take one. Generalised properly rather than worked around, because the
user's framing was right: it is a pure mathematical problem.

**The counting identity.** A partition needs `T = Σkₛ` columns to visit every stage of the block. If `Lₛ`
partitions sit in stage *s* at once, counting partition-columns two ways gives `Lₛ·T = P·kₛ`, hence
`Lₛ = P·kₛ/T`. Integrality pins **`P` to a multiple of `T / gcd(kₛ)`** — the only arithmetic condition.

Run the real 6th year through it: `k = [2,2,2,2,1,1]` → `T = 10`, `gcd = 1`, so `P = 10`, and
`L = [2,2,2,2,1,1]` summing to 10. **That is the ten monthly columns of `Med6.png`**, with the two-period
stages holding two partitions at a time. The reference document *is* the formula.

⚠ **A period is one service, not one stage — and I modelled it backwards first.** "Chirurgie has 2
periods" means the group passes through **two different services**. I initially gave a 2-period stage a
single two-column slot, which keeps a group in one service for two months. The ported tests failed
immediately and correctly. The right model: every stage carries a slot **per axis column** (so `SlotCount`
is `T` for all stages, as before), and a partition takes a **run of `kₛ` consecutive** ones. This also
removed a `kₛ | T` condition I had briefly imposed — a run need not start on a multiple of its length.

⚠ **The closed form had to go.** `(lane + t) mod S` is a cyclic Latin square and only exists when the
durations are equal; with unequal `kₛ` the stage boundaries stop lining up, so shifting a partition by one
stage no longer maps a valid schedule to another. `RotationTiling` solves an exact cover instead,
backtracking across **partitions and columns together** — filling each partition greedily would report
"impossible" whenever an early partition took a column a later one needed, which is a wrong answer rather
than a slow one. The search space is tiny (a level has ≤ ~12 columns, ≤ ~8 stages).

⚠ **Some mixes are genuinely impossible.** Stages of 2 and 1 give `T = 3`; a two-column run must cover
column 2 wherever it starts, so every partition is in that stage there and the other stands empty. No `P`
rescues it. Because the search is exhaustive, `NoFeasibleArrangement` is a proof rather than a giving-up —
worth keeping that property if it is ever optimised.

**Authoring the dates once** was the other half of the request, and it falls out for free: the caller
supplies the axis at its **finest** granularity (10 monthly windows), and every stage's slots are cut from
that one list. A 2-period stage and a 1-period stage on the same block therefore cannot drift, because
there is only one set of dates. `PeriodAxis` on the read side already handled multi-column stages by
repeating their cell across the columns they span, so the répartition needed no change.

**Chosen for the partition-count question:** the command **takes** `P` (from the promotion's real
partitioning) and validates it, rather than deriving one. Deriving would silently re-cut partitions to
suit one block, which fights the `Reassign` guard, and a level's partitioning is shared across its blocks.
The refusal names the multiples that would work, so it is as helpful as deriving would have been.

## The crossover is generated now, and the shared axis is authored once (2026-08-09)

Asked for as a "mirror effect": configure one stage, get the opposite for the other. Generalised,
because two stages is the easy case and the new CNPN's 3rd year is six.

**`Stages/RotationCycle/`** produces the `PartitionStagePlan[]` matrix that `GenerateMacroPlanCommand`
already consumes — so this generates what was previously ticked by hand and changes nothing downstream.

- ⚠ **`S × k` columns, not `partitions × k`.** The user's formula (partitions × periods) held only
  because their example had 2 stages *and* 2 partitions. The timeline must fit one partition visiting
  every stage of the block; partitions subdivide *who is where*, they do not lengthen it. Three stages,
  k = 1, six partitions ⇒ 3 columns with two partitions per stage per turn.
- **Lane `p mod S`, stage `(lane + t) mod S` in turn `t`.** A Latin square. S = 2 gives exactly the
  requested mirror; S = 3 gives the three-way rotation the new CNPN needs; P a multiple of S puts
  several partitions in a lane and they travel together.
- **Uneven or short is reported, not refused.** P not a multiple of S still gives every partition every
  stage — the turns just carry unequal effectifs, which is a capacity surprise worth naming. Fewer
  partitions than stages leaves a stage empty for a whole turn, which gets its own warning. Neither
  blocks: only the faculty knows whether it was intended.
- **`RotationCyclePlanner` is pure.** No DB, no clock — which is why the crossover is tested by
  properties over many (S, k, P) combinations (every partition visits every stage exactly once; no
  partition is in two stages at a time; every stage is occupied in every column) rather than through
  a fixture that only proves one shape.
- **Authoring the axis ≠ running the plan.** Apply writes the slots and returns the matrix; the caller
  passes it to the macro plan. Same split as déliberation / réinscription, and it keeps
  `CohortProvisioner` / `RotationArranger` / `SchedulePublisher` on their existing path.
- **Replacement is wholesale and scoped to the named stages.** Half-old, half-new columns are the exact
  misalignment this removes; and two blocks of one level (semester 1 / semester 2) do not disturb each
  other. Refused outright while any cell is published.
- ⚠ Stage order in the command **is** the rotation — `RotationCycleContext` preserves it deliberately
  rather than sorting, because reordering would silently change which stage a partition starts in.

### The gap it works around: nothing declares that two stages share a period

`StageSlot` is keyed (stage, year, period number), so Médecine P1 and Chirurgie P1 are independent rows
with independent dates. Nothing asserts they are the same window — the axis is *derived* from dates by
`PeriodAxis`, never declared. And neither guard notices: `SlotOverlapGuard` is per-stage (that is what
makes the crossover authorable at all) and `GroupScheduleConflictGuard` only fires on a group genuinely
double-booked, which a crossover never is.

- ⚠ **The small drift is the dangerous one.** Chir P1 = 01/10–02/11 against Med P1 = 01/10–31/10:
  Chirurgie's window *strictly contains* Médecine's, so `PeriodAxis` treats it as a composite, drops it,
  and Chirurgie claims Médecine's column by midpoint. The two-day error vanishes without trace. A
  *partial* overlap is louder — both survive as columns, the table grows one, and the holes are hatched
  and counted.
- `PeriodAxisDiagnostics` (pure) reports period numbers whose stages declare different windows, on
  `LevelRepartitionResponse.AxisDisagreements`. **Never an error:** Med6 legitimately runs Chirurgie's
  P1 over two months and ANES REA's over one. Code cannot separate that from a typo; a human can.
- It reads the **slots**, not the cells — a period nobody is placed in yet still has dates, and that is
  exactly when you want to hear about it.
- ⚠ `AxisDisagreements` sits on the *response*, not on `RepartitionSummary`. The summary is a bag of
  counts compared by value in tests; a collection member silently breaks record equality (caught by two
  existing tests when it was first put there).
- The structural fix — one declared axis entity per block, with slots referencing it — is deferred to
  15.1, same root cause as the semester gap. `RotationCycle` avoids the class in practice by writing all
  of a block's windows in one act.

## Why the répartition prints `1, 3, 5, 7` where the faculty prints `1-10` (2026-08-09)

Raised by the user from reading the annual planning, and their diagnosis was exactly right. Three links
in one chain, none of them a defect:

1. **`PartitionAllocator` fills the smallest partition, walking groups in `GroupNumber` order.** With two
   empty partitions that alternates on every single group → `A = {1,3,5,7…}`, `B = {2,4,6,8…}`. The
   stripe was never a deliberate interleave for efficiency; **balance was the only property sought**, and
   the stripe is what falls out of chunking one group at a time. In general the step is the *partition
   count* — four partitions give `1,5,9,13`.
2. **`RotationArranger` orders by `(label, groupNumber)` and indexes the service queue by position**
   (`serviceQueue[(ci + offset) % n]`, line 265). The queue repeats each service in a run sized by its
   capacity share, so consecutive `ci` share a service. Contiguity is real — it just lives in *index*
   space, and index space is one partition.
3. **`GroupNumberRanges` then has nothing to collapse.** It is correct to refuse: `47-50` is a promise
   that 48 and 49 are in that service too, so merging across a hole would misdirect two whole groups.

`Med3.png` reads `47-50` because the faculty cuts in **blocks** (1-40 Médecine / 41-80 Chirurgie).
PGSH cut in stripes. `PartitionStrategy` now offers both, `Interleaved` remaining the default so no
existing plan moves.

- **Correctness-wise the two are interchangeable** — equal-sized, disjoint, and the crossover
  (A→P1-2, B→P3-4) works identically. The difference is entirely in the *published artefact*, whose
  function is to be read at a glance: ten comma-separated numbers per cell over 26 rows × 10 columns is
  a materially worse table than the one the faculty publishes. Cosmetic in the code, not cosmetic on paper.
- ⚠ **Re-cutting is destructive to a plan.** `Reassign: true` is refused while any cell of the promotion
  is **published** (a `ServicePeriod` points at it — students were sent there, possibly already served),
  and reports `PlannedCellsAffected` for the merely-planned ones so the caller knows to re-arrange.
- ⚠ **Contiguous partitions inherit whatever ordered the group numbers**, and
  `AutoArrangeGroupsCommandHandler` numbers sequentially *per CNPN bucket*. From 2026-2027 a contiguous
  partition can therefore be entirely one text where an interleaved one mixes both — which changes which
  rows exist in that partition's half of the matrix, since `CohortProvisioner` skips unrequired stages.
  Verify on real data before switching the default.
- The interleaved tie-break used to be `counts.MinBy(...)` over a `Dictionary`, i.e. it depended on
  dictionary enumeration order — stable in practice, guaranteed nowhere. It is now an explicit
  `(count, labelIndex)` ordering, so A-before-B is stated rather than inherited.
- `AssignRotationGroupsCommand` returns each partition's membership through `GroupNumberRanges.Format`,
  which is what makes the two strategies comparable at a glance without arranging anything first.

## Closing a year — the déliberation canvas and the réinscription (2026-08-09)

The user's framing, and it is the right one: *"we do not possess the pedagogical side — exams, TP,
déliberation — so we cannot know who graduated or failed, so we adjust our side"*. PGSH stops trying to
derive the verdict and accepts it as input, in the shape the évaluation import already proved works.

**Two acts, months apart, and they must stay apart.**

| | when | what it does | on re-run |
|---|---|---|---|
| Déliberation (`…/Deliberation/`) | July | writes each registration's verdict | replaces (a jury corrects itself) |
| Réinscription (`…/Reinscription/`) | September | creates next year's registrations | **skips** — idempotent |

Fusing them was considered and rejected: not every *admis* comes back, so the combined act would
invent registrations for students who abandoned, and it would need next year's `AcademicYear` row to
exist in July.

- **The canvas is per (year, level)** — one jury, one file. It is also what makes the CNE index mean
  something: unique within a promotion, ambiguous across years.
- **`RegistrationStatus` gained `Graduated` and `Excluded`**, and both distinctions earn their keep in
  exactly one consumer — the réinscription, which sends *Admis* up a level, *Redoublant* round again,
  and *Diplômé / Exclu / Abandon* nowhere. Collapsing either pair breaks it.
- ⚠ **The two error policies differ on purpose.** All-or-nothing for the déliberation, because the
  uploaded file is *not stored* and a half-closed promotion could not be reconstructed. Skip-and-report
  for the réinscription, because re-running it is safe, and refusing 690 legitimate rows over three
  anomalies buys nothing. Do not "make them consistent".
- ⚠ **`NextLevelMissing`** — *Admis* on a level with nothing above it. Almost always a PV that meant
  *Diplômé*, and it is reported rather than converted: guessing a graduation is guessing the one thing
  the faculty is there to say.
- ⚠ **`Diplômé` is checked against `CnpnVersion.TotalYears`, and stands aside when the stamp is
  missing.** ~2,200 stamps are inferred and 19 students carry none; refusing on absence would make the
  feature unusable on the real base. Same standing-aside rule as `CohortProvisioner`'s.
- **An *Admis* with an unvalidated stage is flagged, not blocked.** PGSH sees stages; the jury sees the
  year. With **0 authored `StageSlots`** an unmarked stage is currently the *normal* state, so enforcing
  this would refuse every import — and the graduation gate (still unbuilt) is the place that joins the
  two verdicts, not the déliberation.
- **A motif is kept only on an adverse verdict** (`FailureReasons`), and a row that had its motif
  dropped says so. Silently discarding what someone typed is how a user learns not to trust a preview.
- New registrations are `Active` with **no group**. Nothing in the app filters planning by
  `Registration.Status` — checked — so `Pending` would be planned exactly like `Active` while claiming
  not to be enrolled. The empty group is deliberate: auto-arrange reads the "Non réparti" bucket next.
- `IX_Registration_Year_Level` added while here — `Registration.LevelId` had no index at all, which
  Phase 13 had logged.

## The third act — the people the other two structurally cannot see (2026-08-30, session 30)

The user's observation, and it is exact: *« ça concerne seulement les étudiants qu'on a déjà dans la
base — pour ceux qui n'y sont pas encore, comme les nouveaux 1MED ou ceux transférés d'une autre
faculté, ça ne marchera pas »*.

**It is structural, not a missing edge case.** `DeliberationPlanner` opens with
`Registrations.Where(r => r.AcademicYearId == yearId)`; `ReinscriptionPlanner` starts from the closing
year's registrations and their verdicts. Both are correct — you cannot deliberate on somebody who was
not there — and both are therefore blind to anybody who holds no registration to be read.

Nor was there anywhere else to go: **`PGSH.Application/Students/CreateMany/` was an empty directory**,
`CreateStudentCommand` is one student with ~20 fields and four OR'd uniqueness comparisons per call
(700 round-trips for an intake), and `CreateManyRegistrationsCommand` takes `List<Guid> StudentIds` —
it presupposes the people exist.

Five populations, three of them already in the live base:

- new 1MED / 1PHARM — no `Student` row at all;
- transfers in mid-cursus — no row, plus study PGSH never saw;
- **returners** — in the base, no registration in the closing year. Not hypothetical: 2 of the 12
  « Retrait » students came back (Retrait 2023-24 → 5ème année 2025-26) and nothing creates that
  registration;
- réorientations — same person, other programme;
- étudiants sous convention — `Student.AgreementType` existed and **nothing read it**.

### What the équivalence is actually for

The decision that took the most argument. Today a transfer owes **nothing**:
`OutstandingStageFinder` reads « owed » as *every attempt came back NonValidé*, and a student with no
attempt has no failed one, so `FinalYearGuard` stands aside and nothing objects.

That is a correct reading of our own record — and it holds only while « owed » is defined negatively.
CLAUDE.md already states the intended widening: read « owed » from the CNPN's requirement set once
1650.25's sets are entered. **On that day a student transferred into 5ᵉ année owes every stage of the
four years he did elsewhere**, and PGSH holds nothing that says otherwise. `PriorEnrolment`
(`LastLevelYearCompleted`) is the boundary the widening must not look below, and it cannot be
reconstructed afterwards from anything in the base — which is why it had to be written now rather
than when the widening lands.

Rejected: materialising validated `InternshipAssignment`s for the years done elsewhere. It makes the
dossier look complete at the price of rows nobody served, which every count, every mean, every chef
worklist and every occupancy figure would then have to learn to exclude.

### Two latent defects the work uncovered

- **`RegistrationCnpnStamper.Fallback` was programme-blind.** It returned the student's stamp, or the
  one carried on his most recent earlier registration, without checking that the text governs the
  programme he is registering in. A `CnpnVersion` belongs to exactly one `AcademicProgram`, so any
  réorientation — including one done through the ordinary registration form, long before this
  feature — stamped the new registration with a text governing the cursus the student had just left,
  and `TotalYears` read from it answers « est-ce sa dernière année ? » from the wrong arrêté. Now
  refused, falling through to `ResolveFromEntryAsync`, which resolves from the level's own programme.
  Where nothing resolves, `Student.ClearCnpnVersion()` removes the stamp rather than keeping a false
  one: null means « never resolved », which every reader already handles.
- **`Students.Appogee` is NOT NULL UNIQUE**, not optional — I read the filtered index
  `IX_Student_Appogee` (« WHERE Appogee IS NOT NULL ») as permitting absence and wrote a test
  asserting it. The column is required, so the filter can never be false; `""` is a *value* and the
  second student without an Apogée collides with the first. The in-memory provider caught this one,
  unusually — it enforces required properties even though it enforces no unique index.

### Three more, found by re-reading the code rather than by a test

None of these was caught by the 1 144 tests that were green at the time, which is the point worth
keeping:

- **The e-mail was treated as identifying.** `Classify` matched a row against *any* of the four
  unique identifiers and took the first hit. A newcomer whose address cell was mistyped to an existing
  student's therefore resolved to **that student**, and the row quietly gave him a registration under
  somebody else's name. CNE and Apogée identify; CIN and e-mail corroborate, and a corroborating
  identifier pointing elsewhere is `IdentifierConflict`.
- **In-file duplication was keyed on `cne ?? appogee`.** One person written twice — once with his CNE,
  once with his Apogée — passed, and `IX_Registration_Student_Year` is unique: a 500 at `SaveChanges`
  with nothing actionable in it. Every identifier the row carries is claimed now, plus the student it
  resolved to.
- **A manufactured CNE could be unsaveable.** `SANS-CNE-` is 9 of the 20 characters
  `StudentIdentifierRules.CnePattern` allows, so a long Apogée produced a student whose file could
  never be saved again — the refusal naming a field nobody was editing. Third instance of the same
  failure (the old CNE regex, `Objectives.NotEmpty()`, this), so it is now checked at creation through
  the same rule the validator uses.

### What the live run added, that the tests could not (2026-08-30)

`SMOKE-TEST.md` §28 through the real screen, after a dump. Two things are worth keeping:

- **The created rows are the only proof of the identifier rules.** 1 157 tests pass against an
  in-memory provider that enforces no unique index; what says the provisional Apogée works is two rows
  in Postgres reading `SANS-APOGEE-SMOKETEST01` and `SANS-APOGEE-SMOKETEST02`, side by side, on a
  column that is `NOT NULL UNIQUE`. Same for the two homonyms holding `nour_zaimi@` and
  `nour_zaimi2@`, and for `CnpnSource = Effectivity` — the stamper resolving a real rule.
- ⚠ **Read a capped table from the DOM, not from a text capture.** I reported the 58
  `FinalYearBlocked` rows as missing from the réinscription table; they were there. The page-text tool
  had truncated at 335 of 1000 rows, and a report whose row list is capped at 1 000 is exactly where
  that truncation is invisible. `document.querySelectorAll('table tbody tr').length` settled it in one
  call.

### Why the confirmation is a number here too

`ConfirmedStudentCount`, echoed back from the preview, with the same argument as the déliberation's
`ConfirmedDefaultCount` and a sharper stake: a student row is an **identity** — a CNE, a numéro
Apogée, and an address `SyncUserMiddleware` matches a Keycloak login against. Nothing puts a
wrongly-created promotion back. A file edited between preview and apply is exactly what the
comparison catches, and a boolean would wave it through.

Same reason generated addresses are reported per row and counted: `Users.Email` is NOT NULL UNIQUE,
an intake list has no address column, and the legacy import manufactured all 10 204 the same way
(`prenom_nom@um5.ac.ma`). Because the middleware falls back to matching on e-mail, a manufactured
address that somebody already holds hands a student **another person's account** — so the taken set
is read from the store, not merely from the batch.

⚠ **And the two generators had already drifted.** I wrote « same rule as `LegacyIdentityMapper.Slug`,
so the two generations agree » in a comment and it was false: the importer keeps ASCII **letters**,
my copy kept letters *and digits*. « Mohamed2 Alaoui » would have been `mohamed_alaoui` on the 10 204
imported rows and `mohamed2_alaoui` on every new one — two address namespaces for one faculty, and
the re-import Phase 16 plans would have renumbered people who already log in. The rule now lives once,
in `StudentIdentifierRules`, and states the behaviour already on disk. Same shape as
`ServiceChefSourceNote`: where an importer and a reader must agree on a format, the format is a
shared thing, not a comment saying they agree.

### The single-row way in

`InscribeStudentCommand` (`POST inscription/student`). The « exceptions, not exhaustive lists » rule
demands one for every bulk import, and it binds harder here than for the déliberation: an inscription
file names people who do not exist yet, so re-sending it to add one November transfer means
re-stating a whole promotion to say one thing.

Two decisions worth keeping:

- **Every value arrives as text**, exactly as a sheet cell does, and goes through the same parser. The
  alternative — typed fields on the form, strings in the sheet — is two grammars for one column, and
  the first thing to disagree would be a date. It also means the validator asserts nothing beyond the
  level: a rule stated twice is a rule that can disagree with itself.
- **The refusal carries the row's own sentence**, not « 1 ligne en erreur ». The count is what a file
  needs; on a form it names nothing the operator can act on.

The writes are extracted into `InscriptionApplier` and shared. Sharing only the planner would have
left two copies of the half that creates identities — the same reasoning as
`FinalYearGuard.EnsureMayEnterManyAsync` being the implementation the single-student call delegates to.

## Code review 2026-08-08 — what it caught

Two real defects, both now fixed with regression tests:

- **`RevalidateStageCommandHandler`: `Any` where it needed `All`.** The guard read
  `!priorAttempts.Any(r == NonValidé)`, so a student with a settled 2022 failure *and* a 2023 attempt
  still awaiting its verdict got a retake opened alongside the live one — the exact "two live
  attempts" the comment claimed to prevent, and the dossier would call the same student `InProgress`.
  `DossierStageState` uses `All`; the command now matches it.
- **The level dossier missed cross-level retakes entirely.** Attempts were found through the
  registrations *of that level*, but a retake of an earlier level's stage hangs off the registration
  the student holds *now*. It was therefore absent from the earlier level's dossier (wrong
  registration) and dropped from the later one (wrong stage) — so the earlier level read
  `ToRevalidate` forever, even after the retake passed. Attempts are now scoped by the **stage's**
  level, and each carries its own year rather than looking it up in the level's registration list.

Smaller fixes: `BacYear` was the one string not `Truncate`d (varchar(10) — one long `ANNEE_BAC` would
abort the whole students batch); the reader's `when (provider == Provider)` filter made its own
friendly ACE-provider error unreachable and leaked the connection; `LegacyPeriodParser.DanglingDate`
was computed and never reported, contradicting its own "reported, never guessed at" comment (now a
`DanglingPeriodDate` problem kind); the retake's default service came from an unordered `First()`.

**Checked and accepted, not fixed:** the importer folds a legacy `revalide='O'` row into the same
assignment when it shares `(NUMINS, CODEST)` with a normal rotation. That happens on **3 keys out of
98,555**, and in all three every mark involved is ≥ 10 — so the verdict is `Validé` either way and only
the displayed `FinalScore` differs. Not worth a re-import.

**Left for a decision:** `Seeding:Enabled=false` sits in the committed base `appsettings.json`, not a
Development/user override — correct for the imported database, but anyone checking out this branch
against a fresh one gets migrations plus three login accounts and nothing else.
`GetStageScheduleQuery` also now substitutes the current year silently when none is given, with no way
to ask for all years and no field in the response saying which year was applied.

## A query that never ran on Postgres, and a debugger that hid it (2026-08-26, session 27)

The 6ᵉ année macro plan hung three times before anyone knew why, and neither half of the answer was
where we looked first.

### The query

`CohortProvisioner` asked, *inside* a projection:

```csharp
.Select(g => new {
    …,
    CnpnVersionIds = g.Registrations
        .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
        .Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId!.Value)
        .Distinct().ToList(),
})
```

Npgsql refuses it: *"Unable to translate a collection subquery in a projection since either parent or
the subquery doesn't project necessary information required to uniquely identify it…"*. The element of
the subquery is a **computed value carrying no key** — `??` across a navigation — and `Distinct()`
then leaves nothing to correlate the rows back to their roster with.

- It was written in **session 24**, when the CNPN moved onto the registration. Before that the line
  read `r.Student.CnpnVersionId`: a plain property access, which translates.
- **1 004 tests were green the whole time.** `UseInMemoryDatabase` executes LINQ against objects and
  never translates anything — the blind spot CLAUDE.md has warned about since session 22, biting for
  the first time in a way that cost a working feature.
- The fix is a **flat, top-level query** keyed on the roster id, folded in memory
  (`CohortProvisioner.GroupTextsQuery`) — and cheaper than the subquery it replaces, since it is one
  round trip instead of a correlated per-roster one.
- A sweep of the Application layer found seven candidates of the same shape; six were in-memory LINQ
  over already-materialised lists. This was the only EF-side one.

### Half the blind spot closes with no database at all

Translation happens when a query is **compiled**, before any connection is opened. So a context on the
Npgsql provider pointing at nothing answers "does this become SQL?":

```csharp
public static ApplicationDbContext NewNpgsqlContext() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=translation-only;Username=none;Password=none")
        .Options);
```

`SqlTranslationTests` then calls `ToQueryString()`: SQL comes back, or the provider says why not. Two
cases live there — the fixed query compiles, and **the shape that broke it still does not**, kept
executable because a comment does not fail a build. It is not Testcontainers (nothing here proves the
SQL returns the right *rows*, or that a FK or unique index behaves) but it is the half that costs a
500.

### Why nothing surfaced — the diagnosis worth keeping

Visual Studio was set to break on **thrown** CLR exceptions, so it paused the process *at the throw*,
before `ExceptionHandlerMiddlewareImpl` — right there in the stack — could turn it into a 500. The
consequences are not obvious from the browser:

- the HTTP request never completes, so the button spins forever and **no toast ever appears**;
- the whole API stops answering — an anonymous `GET /api/levels` hangs too, because the *process* is
  frozen, not the endpoint;
- restarting the stack "fixes" it, which makes it read like a data or cache problem.

**The signature, and it is unambiguous:** CPU flat across a several-second sample, Postgres
connections all idle, zero HTTP responses. Measured on the hang: 21.6 s of CPU before and after a
5-second sample, five idle connections, 6½ minutes with no response. Compare a genuine deadlock (also
flat, but the DB usually shows an open transaction or a lock wait) and a slow query (DB active).

⚠ **This is almost certainly what "the frontend showed nothing until I reloaded the stack" was**, days
earlier — same freeze, whatever query was running then. Without a debugger attached the same bug is a
fast, visible 500 with an error toast. The debugger did not cause it; it converted a loud failure into
a silent one.

⚠ **And the UI cannot tell the two apart**: `fetchBaseQuery` sets no `timeout`, so a request that never
answers never rejects, `errorMiddleware` has nothing to toast, and every screen sits on a skeleton.
That is a queue item, and deliberately not a blanket value — `stages/macro-plan` legitimately runs for
minutes, and aborting a mutation client-side does not stop the server writing.

---

## Sweeping the macro-plan path for SQL, and what compiling does not prove (2026-08-26, session 28)

Follow-up to the note above. One query on that path had been proven to compile; the other eight had
not, and the class of defect had already cost a production outage with the suite green. The sweep is
`CohortProvisioner` → `StudentAffectationService` → `RotationArranger` (with
`GroupScheduleConflictGuard` and `ServiceOccupancyCalculator`) → `SchedulePublisher`.

**Result: every query compiles. No second defect.** That is worth stating plainly rather than
quietly — the sweep was worth running because a negative result here is only knowable by running it,
and the alternative was finding out on the first publication.

### ⚠ `SchedulePublisher` had never executed against PostgreSQL at all

Not "was not covered by a translation test" — had never run. The Med6 rehearsal of the day before was
`publish: false`, and the base holds 0 grid-linked périodes, so **the first real publication would
have been the first execution of every one of its queries** — the per-cohort publish included,
which shares nothing with the stage-wide one but the class. Its `SlotAssignmentsQuery` is the
heaviest projection on the path: four navigation hops, a null-coalesce over `"niveau " + LevelId`
(a string concatenated with an `int`), and an enum stored through `HasConversion<string>`. It
compiles — `COALESCE(l."Label", 'niveau ' || s."LevelId"::text)` — and so does the same shape in
`GroupScheduleConflictGuard`, which is where it was copied from.

### A query has to be *named* to be testable

Translation is checked by compiling an `IQueryable`, and a query built inside a private `async`
method cannot be reached without executing it. So each swept query is now an
`internal static IQueryable<T> …Query(IApplicationDbContext, …)` sitting beside its caller, which
executes it — the shape `CohortProvisioner.GroupTextsQuery` established when it was fixed. The cost
is one indirection per query; the benefit is that the shape is compiled on every `dotnet test`.

Two of them are compiled **twice**, in both their forms: `StageCohortsQuery` with and without
partition labels (a `Contains` over `string` is not the `int` translation used everywhere else), and
`SlotAssignmentsQuery` with and without a period window — the windowed form being the one a real
publication of a concurrency block actually takes.

### ⚠ A projection is not a predicate — measured while proving the tests bite

Breaking a query on purpose to watch its test fail is where this turned up. A **client-side method
call in the final `Select` does not fail the test**: EF Core evaluates the top-level projection on the
client by design, so `ToQueryString()` returns SQL for it quite happily. The same call in a `Where`
throws *"could not be translated"*, and both of that query's cases went red.

So the boundary this file guards is: **the provider refusing a query**, not every query that reaches
the database in a shape somebody would want. A projection that silently client-evaluates costs
round-trips, not a 500, and finding those still needs a real database.

### What Testcontainers is still owed for

Narrower than it was, and the narrowing is the point. Not translation on this path any more —
**rows** (does the SQL return what the handler assumes?), FK behaviour, unique indexes and
`OnDelete`. The `NULLS NOT DISTINCT` roster indexes and the `CASCADE`/`RESTRICT` asymmetries the
delete guards are written against are all invisible to both InMemory and `ToQueryString()`.

---

## A modal that "does not open" in a background tab (2026-08-26)

Verifying « Supprimer le bloc » from browser automation, the confirmation dialog never appeared. The
state was right and the component was mounted — the trap is worth knowing before anyone spends an
hour on it, as this did.

What the DOM said, after the button was clicked programmatically:

- the React state flipped correctly — walking the fiber's hook chain, `confirmingDelete` went
  `false → true` (it sits at hook **175 of 220**: RTK Query hooks are hook-heavy, so a walk capped at
  40 or 120 finds nothing and looks like proof the hook does not exist);
- `.mantine-Modal-root` **was** in the DOM — with **zero children** and height 0;
- no console error, no failed request.

**Cause: Mantine's `Modal` mounts its content through `<Transition>`, which schedules on
`requestAnimationFrame` — and rAF is paused in a hidden/background tab.** Clicking through
`javascript_tool` does not bring the tab to the front, so the transition never runs and the root stays
empty forever. Nothing is wrong with the component.

**The control is what settles it, and it should be the first move, not the last:** drive a modal that
is already known to work (the dossier's « Ajouter une inscription »). It failed identically in the
same tab, which proves the fault is the *context*, not the code under test. A control costs one call;
reasoning about a component from an empty DOM node costs an hour.

**Rule for browser verification:** anything that renders through a transition, an animation or an
`IntersectionObserver` must be driven with real input events in the **foreground** tab (the `computer`
tool activates it), not with `element.click()` from an evaluated script. Reading state is safe from
the background; asserting that something *appeared* is not.

---

## The faculty's own canvas, measured before a line was written (2026-09-01, session 34)

The three acts of the year were built against canvases PGSH generates. For 2026-2027 the faculty sent
its own: **`Réinscriptions 26-27 VF.xlsx`**, five columns —
`Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027`.

Everything below was measured against `Medecine.mdb` and the live base *before* deciding anything,
because the whole design turns on whether the file can be trusted to agree with what PGSH holds.

| | |
|---|---|
| rows | 6 862 |
| distinct `Code` values | 6 862 — **no duplicate** |
| `Code` shape | integer in every row; `Code` = `NO_ORDRE` = `Students.Appogee` |
| codes matching a student | **6 813** |
| …of the 49 that do not, masters (`MMBTM`) | 23 |
| …genuinely unknown students | 26 |
| rows whose from-étape **disagrees** with the registration on record | **0** of 6 810 checkable |
| students in `ETUDIANT` with no 2025-2026 `Inscription` | 3 |
| 2025-2026 registrations the file never mentions | 1 216 |

**The zero is the load-bearing number.** It is what makes « the file says one étape, the registration
says another » safe to treat as a **refusal of the whole file** rather than as a skip: on the real
document it costs nothing, and a verdict written onto the wrong registration cannot be walked back.

### The 1 216 the file does not name

Not an omission — they are the students who are not coming back. 999 are 7ᵉ année Médecine and 211 are
6ᵉ année Pharmacie, i.e. the thesis years, i.e. where the graduates are. PGSH cannot tell a graduate
from an exclusion from an abandon, so nothing is written for them.

⚠ **This inverts the déliberation canvas and the two must never be confused.** That one is a list of
*exceptions*: a student it does not name is admis. This one is the roll of who *is* re-registering: a
student it does not name is not. Same file format family, opposite reading of silence.

### 804 rows where the level does not move and it is not a failure

`MED07 → MED07` (659) and `MDPH06 → MDPH06` (145). The thesis year runs until the thesis is defended —
already measured in session 24: **855 of the 1 657 students in 7ᵉ année Médecine had been there
before, 132 of them four times.** Reading those lines as « redoublant » would be wrong twice over:

1. it is not a failure; and
2. `RegistrationStatus.AnnulsItsStages` treats `Failed` as annulling the year's stages, so it would
   have **wiped a year of stage record for 804 students who did nothing wrong**.

The rule that stands aside is `FinalYearTest`, lifted out of `DeliberationPlanner` so the two acts ask
it once. Two copies would disagree about exactly these 804 people.

### Two level codes that exist in no legacy row

`MDME3` and `MPHAR3`. The faculty renames its codes one promotion at a time as each cohort moves up,
so in 2026-2027 the third year is `MED03` for the students repeating it and `MDME3` for the ones
arriving — the same `Level` under two names, which is why the mapping is a table and not a column.
Confirmed against `Niveaux`: `MDME1` reads « 1ère année Médecine » and `MPHAR1` « 1ère année
Pharmacie », so the `MDME`/`MPHAR` families are the rename, not a new programme.

---

## `LEGACY-` was never an identifier, and the column was never required (2026-09-01, session 34)

Phase 16.1 said to leave the CNE null. Two things measured while doing it were not in the plan.

**The column was already nullable.** `Users` is a TPH table and an `Employee` has no CNE, so
PostgreSQL had always accepted a null there; the requirement lived only in EF's model and in the
validators. What the database really enforced was `IX_Student_CNE`, unique and **unfiltered** — and
Postgres treats NULLs as distinct, so that index already tolerated any number of students without a
code. The filter added with the change states the intent and keeps 4 695 unmatchable rows out of the
index; it does not change the rule.

**Phase 16.2 is answered, and the answer is no.** The open question was whether some source rows carry
an appogée *in* the CNE column. Measured over the 5 508 usable CNEs in `Medecine.mdb`:

| | count |
|---|---|
| CNE identical to the row's own `NO_ORDRE` | **0** |
| CNE equal to *another* row's `NO_ORDRE` | **0** |
| CNE of exactly eight digits (the `NO_ORDRE` shape) | **1** |

Shapes: 4 561 letter + digits, 835 digits-only of other lengths, 104 alphanumeric, 4 with
punctuation, 3 with an internal space. So the 835 digits-only codes are not appogées — not one matches
any `NO_ORDRE`. Nothing to move, nothing to blank.

### A third instance of the read-only-validator defect

`UpdateStudentCommandValidator` required `int.TryParse` on the Apogée. `InscriptionPlanner` derives
`SANS-APOGEE-{cne}` when the faculty has not allocated one yet, which is not a number and never will
be — so **every student the inscription import created that way was read-only the day somebody opened
his file**, the refusal naming a field nobody was editing. After the CNE regex (5 646 students) and
`Objectives.NotEmpty()` (the whole stage catalogue), this is the third. The pattern to watch for is a
validator asserting a *shape* rather than what the column actually requires.

### And a nullability trap the in-memory suite gets backwards

`null == null` is **true** in LINQ-to-Objects and **NULL — i.e. false** — in SQL. So an unguarded
uniqueness check on an optional identifier (`s.CNE == request.CNE`) reports a phantom « CNE déjà
utilisé » against the next student without one *in the test suite*, and passes silently on PostgreSQL.
The guard is on the request value being present, which is what the filtered index says too. Same
family as the `Include`/in-memory-fixup blind spot: the provider disagreeing with the store in the
direction that hides the real behaviour.

---

## Rebuilding the base: one loud failure and one silent one (2026-09-01, session 34)

`drop → dotnet ef database update → import` does not work, and only the first problem announces
itself.

**Loud.** `Cnpn1650Med3Stages`, `Cnpn1650ImmersionStages` and `Cnpn1650Med3CatalogueAlignment` open
with `RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine » : le catalogue des niveaux doit exister
avant les stages.'` — they need the `Levels` and `Stages` the *import* creates, so they have to run
**after** it.

**Silent, and worse.** The CNPN attribution — students stamped from their deduced entry, registrations
backfilled from that stamp — was written as two one-off `UPDATE`s inside `CnpnVersioning` and
`RegistrationCnpnAndLevelEffectivity`. Both assume data is already there. Run before the import they
stamp nobody and are then recorded as applied, so nothing will ever run them again. The base ends with
**10 200 students and 49 500 registrations carrying a null text**, and every reader falls back on null
*gracefully* — which is precisely why nothing complains:

- the déliberation's « est-ce sa dernière année ? » has no `TotalYears` to read, so nobody is left
  undecided and silence promotes everyone, including the thesis years;
- `FinalYearGuard` stands aside for the entire faculty;
- `CohortProvisioner` stands aside where no requirement set is recorded, so a promotion plans as if it
  owed no stage.

Closed by `CnpnHistoryAttributor` + `PGSH.LegacyImport --stamp-cnpn`, which reuses
`EntryYearDeduction` and `CnpnAssignment` rather than restating the rule in SQL.

### What the Access file cannot give back

Measured on the live base before the rebuild. None of it is test residue:

| | count | why |
|---|---|---|
| `Holidays` | 24 | Aïd, Moharram and Mawlid follow the Hijri calendar and are announced by decree — **cannot be generated, only entered** |
| `StageAllowedServices` | 146 | authored per stage; the source has no such column |
| `CnpnLevelEffectivities` | 3 | authored, and they decide the text of every registration created at that level afterwards |
| `ServiceChefAssignment` | 2 | the only *dated* chef evidence; 140 of 148 services carry an undated legacy note |
| `AcademicYears` → 2026-2027 | 1 | the Access base stops at 2025/2026 |

⚠ **Dump it on natural keys, never on ids.** The import regenerates every surrogate key, so an
id-keyed restore lands rows on the wrong rows — which is not a failure that announces itself either.
And restore 2026-2027 *first*: one effectivity rule takes effect from it, and
`IX_AcademicYear_IsCurrent` is unique and filtered, so demoting and promoting are two statements in
that order rather than one `UPDATE`.

---

## The rebuild, run for real: four silent traps, and the one that was loud (2026-09-01, session 37)

Executed against the live base. The dump is `pgsh-avant-reimport-20260901-223756.dump`.

**Loud, and the only one that announced itself.** The three CNPN data migrations `RAISE EXCEPTION`
against an empty base. Expected, documented, handled by the ordering.

**Silent #1 — the drop that did nothing.** `psql -c 'DROP DATABASE IF EXISTS "TodoDatabase";'` from
PowerShell. **PowerShell strips double quotes from native-command arguments**, so Postgres received an
unquoted identifier, folded it to lowercase `tododatabase`, found no such database, and `IF EXISTS`
downgraded it to a NOTICE — **exit code 0**. `CREATE DATABASE TodoDatabase` then created a *lowercase*
database, and the rebuild carried on believing it had an empty one.

⚠ **What that turned into is the part worth remembering.** The next step was
`dotnet ef database update <target>`, which reacts to a *populated* database by rolling migrations
**back**. It began undoing the CNPN migrations and failed on an FK
(`FK_CurriculumStages_Stages_StageId`). Nothing was lost — EF wraps each migration in a transaction —
but a destructive step with a silent no-op turned the next step into an unintended downgrade of the
live base. Closed by putting the SQL in a **file** (no quoting layer) and by asserting emptiness in
SQL before migrating, so psql exits non-zero and the script stops on its own.

**Silent #2 — every CNPN text came out citation-only.** `CnpnVersioning` reads
`AppliesToEntrantsFromAcademicYearId` out of `AcademicYears`, which is empty when the migration chain
runs before the import, so all four texts stored NULL. ⚠ **A text with no intake year is not
malformed** — arrêté 2175.22 legitimately is one — so nothing threw and
`CnpnAssignment.SelectVersionAsync` simply found no candidate for anybody:
**10 185 of 10 185 students unresolved, 0 stamped, and the pass returned success.** Closed twice:
`CnpnIntakeYearsBackfill` fills the three that should have one (never 2175.22), and
`CnpnHistoryAttributor` refuses when it can place nobody — one unplaceable student is a fact about
him, the whole population is a broken catalogue, and the two must not read the same.

**Silent #3 — service names are not unique.** The first restore keyed `StageAllowedServices` on
`JOIN Services ON Name = …`, following the rule « never key a restore on ids, the import regenerates
them ». **25 service names are shared across hospitals** — « Pharmacie » exists in 9 — and
« Urologie » appears **twice inside one hospital**, so 146 rows fanned out into **178**. `Service`
carries no external identifier at all: the importer keys it on the Access `CodeS` and does not
persist it.

⚠ The fix inverts the rule, on evidence rather than taste. **The import is deterministic**, verified
by restoring the pre-rebuild dump beside the rebuilt base and joining on `Id`: **148/148 services
identical, 0 stages and 0 levels differing.** So the restore uses ids — and **asserts its own counts
in SQL**, which is what a silent fan-out needs and what its absence cost.

**Silent #4 — the chef tenures restored 0 of 2.** They point at the two seeded employees, which
`PGSH.MigrationService` creates at Aspire startup — and a rebuild never runs it. No error; the
`JOIN Users ON Email = …` simply matched nothing.

### What the corrected run produced

| | |
|---|---|
| students / registrations | 10 203 / 43 605 |
| périodes / évaluations | 105 626 / 87 092 |
| CNE `LEGACY-%` | **0** · with no CNE: 4 695 |
| students stamped | **10 185** (2 769 from a deduced entry), 0 unresolved |
| registrations backfilled | **43 605** |
| students unstamped | 18 — exactly the 18 the import reports as having **no registration at all** |
| holidays / allowed services / effectivity rules / chef tenures | 24 / 146 / 3 / 2 |
| 2026-2027 registrations | **0** — the test rollover is gone |

---

## The roll's silence, and the one thing it does decide (2026-09-01, session 37)

Absence from the réinscription roll is not a verdict — except in a student's last year, where it is a
defence. Measured on the 2026-2027 file, absent from it by level:

| | absent | of |
|---|---|---|
| 7ᵉ année Médecine | **1 006** | 1 657 |
| 6ᵉ année Pharmacie | **212** | 356 |
| Médecine 1-4 + Pharmacie 1-2 | **47** | — |
| Retrait / Interne CHU | 2 | — |

The split is clean, which is what makes the rule safe: **1 218 absentees are in a final year** and are
recorded « Diplômé »; the 47 below one are not, and nothing in the file distinguishes an abandon from
an exclusion from a réinscription that has not arrived. They are named, not decided.

- **`Inferred`, never `Declared`** — nobody named them on a document. It also makes the correction
  free: a defence roll is `Declared`, `Declared` overwrites `Inferred`, the reverse is refused.
- ⚠ **Stricter than the déliberation's own « Diplômé » check, deliberately.** `IsExactlyFinal`
  compares with `==` (keeping out the 6 registrations sitting *above* their text's span) and refuses
  to answer without a text, where the déliberation stands aside. The difference is who spoke: the
  faculty naming a student may override PGSH's ignorance; an absence may not.
- ⚠ **It brought back the confirmation number I had argued was unnecessary.** The act needed none
  while every write landed on a student the file names. A graduation lands on one it does not.

---

## The final year is not a year you pass — and the gate was refusing the people it is for (2026-09-02)

**Told by the user, then measured.** The final year (7ᵉ Médecine under 2174.18, 6ᵉ under 1650.25, 6ᵉ
Pharmacie) works unlike every other year:

- **There is no déliberation for it.** The student cannot fail the year.
- He validates and revalidates his stages **one at a time**, and **never redoes one already
  validated**.
- He is **re-registered every September** until they are all acquired.
- Once the stages are done he sits the **examens cliniques**, which open immediately — and if he fails
  them he is re-registered again, stages complete or not.

So a final-year student who still owes a stage is not an anomaly: he is the ordinary case, and the
re-registration is *how he gets to clear it*.

### What that broke

`FinalYearGuard` reads « on ne commence pas la dernière année tant que tout ce qui précède n'est pas
validé » — and was applying it to anyone *registered into* a final year, not to anyone *beginning*
one. Measured against the faculty's 2026-2027 roll:

| | |
|---|---|
| 7ᵉ année Médecine the roll re-registers into the 7ᵉ | 651 |
| …**refused by the gate** | **182** |
| 6ᵉ année Pharmacie re-registered into the 6ᵉ | 144, none refused |
| « Réinscrits sans décision » shown on screen | **616** instead of 798 |

469 + 144 + 3 = 616 — the screen's number, to the row. A quarter of the 7ᵉ année promotion was being
refused re-registration, every one of them named by the faculty as coming back, and the refusal was
reported as an ordinary skip.

**The fix:** the gate stands aside for a student who already holds a registration at that level. It
now refuses **60**, which is precisely the MED06 → MED07 population documented for it in session 24.
A gap does not make it a beginning — « has he ever been registered here », not « was he here last
year ».

### ⚠ And the first test written for it passed with the rule removed

`Debt.LevelYear` comes from **`a.Registration.Level.Year`** — the registration's level, not the
stage's. Seeding the failed attempt against the *final-year* registration therefore produced a
year-7 debt, which `d.LevelYear < levelYear` correctly discards, so the guard returned no refusal
either way. The debt has to hang off an **earlier registration** to be an earlier debt. Worth
remembering whenever a fixture needs a student to owe something from further back.

---

## Open Questions / Things to Verify

- **`GenerateScheduleCommandHandler`**: Does it correctly handle the case where students are transferred between cohorts mid-year? The `CohortMembership` model supports it but the handler logic needs review.
- **`IsCurrent` flag on `AcademicYear`**: Is this maintained automatically or manually? Currently no logic enforces that only one year has `IsCurrent = true`. Should there be a check in `CreateAcademicYear` or `UpdateAcademicYear`?
- **`TodoDatabase` connection name**: This is the Aspire-assigned connection name for the main PostgreSQL database. It's a legacy artifact from project scaffolding. Consider renaming it in `AppHost/Program.cs` and `appsettings` to something like `"pgsh-db"` for clarity.
- **`Employee.WorkPlace`**: The enum has values `Hospital` and `Fmpr`. Is `Fmpr` still the correct name for the faculty workplace, or should it be renamed to match the actual institution name?
- **Domain events on Hospital/Center/Service**: Currently no domain events are raised on creation or modification of these entities. If downstream notifications (e.g., service capacity changes affecting rotation scheduling) are needed, events should be added.
- **`Student.Ranking`**: What is this field for? National ranking for program entry? It's nullable and has no business logic around it.
- **Revalidation cohort assignment** *(answered 2026-08-07 — see `RevalidateStageCommandHandler`)*: `RevalidateStageCommand` takes an optional `CohortId`. Left null it falls back to the cohort of the student's own registration group for that stage (the repeating student, same level, next year). Given explicitly it slots the student into any cohort currently running that stage — which is how a **cross-level** retake works, e.g. a 6th-year student redoing a 1st-year stage joins a current 1st-year cohort. No cohort of that stage hangs off any group they still belong to, so the fallback fails with `NoCohortForRevalidation`, whose message names the missing field. No schema change was needed: `Cohort.AcademicGroupId` still means "one specific group" — the student joins that group's cohort for the duration of the retake.
- **Settling `Registration.Status` for past years** *(raised 2026-08-08, deferred by the user)*: PGSH has no link to the pedagogical side, so no registration is ever closed out and every past year reads "En cours". The inference rule (a registration is failed when a later one exists at the same level) and the four cases that still need a ruling are written up in "`Registration.Status` is unmanaged" above. Feature work is queued as **Phase 14.3**.
- **Graduation gate on revalidation**: Is there a check before a student can graduate (registration `Status → Validated`) that all stages in their program have at least one `InternshipAssignment` with `Result = Validé`? This would be the enforcement point for the revalidation rule. Not yet implemented.

## Regression / Stress Checks (verification recipes)

These are the scenarios used to verify the pagination/auth hardening. Re-run after touching
pagination, the execution-authorization path, or `SyncUserMiddleware`.

**Environment gotchas (Aspire dev) — learned the hard way:**
- The API's HTTPS listens on the `launchSettings` `sslPort` (e.g. `https://localhost:7014`); the
  Aspire-assigned HTTP ports **307-redirect** there, and `curl` can't TLS-handshake the HTTP ports.
  Hit the HTTPS port directly with `-k`. First authenticated call is slow (~12s EF/JIT cold start).
- **All endpoints are under the `/api` route group** (`app.MapGroup("api")` in `Program.cs`) — e.g.
  `GET /api/service-periods`, not `/service-periods`.
- **Every request needs a local `User`**: `SyncUserMiddleware` returns **403 "Profile Not Found"** if the
  Keycloak `sub` has no matching `Users` row (by `IdentityProviderId`, then email). For headless tests,
  temporarily set an existing user's `IdentityProviderId` to the test token's `sub` and restore after.
- **Minting a test token**: `pgsh-frontend` has `directAccessGrantsEnabled = false`. Create a throwaway
  public client with direct grants + temp users with realm roles (`Scolarite`/`Secretaire`/`SuperUser` =
  admin; `Student`/`Professor` = not), password-grant against `localhost:8082`, then delete the artifacts.
  Tokens must carry `aud=account` (the API's configured audience) and `iss=…/realms/pgsh`.

**Scenarios:**
1. **`GET /api/service-periods` is administrative-only** — non-admin token → **403 `ServicePeriods.AdministrativeOnly`**; admin token → **200**. (Verified ✅)
2. **Central `pageSize` clamp** — request `?pageSize=100000` on an endpoint **without** its own validator
   (`/services`, `/service-periods`, `/employees`, `/hospitals`, `/centers`, `/academic-groups`,
   `/internship-assignments`, `/academic-years`) → response echoes **`pageSize: 200`**, `totalCount`
   accurate. Endpoints **with** a validator (`/students`, `/stages`, `/levels`) return **400** instead
   (their own `InclusiveBetween` rule) — both are safe, just different contracts. (Verified ✅)
3. **Chef worklist returns all periods (no 100 cap)** — as the chef of a service with **>100** periods,
   `GET /api/employees/me/service-periods` returns the full set; every academic group shows its true
   student count (regression guard for the "group of 8 rendered as 2" truncation). *Needs planning data
   seeded (ServicePeriods > 0) to exercise.*
4. **Planning grid perf (manual/GUI)** — open/close `ScheduleGridModal` on a stage with many cohorts×slots;
   should be snappy (only the edited cell mounts the heavy Combobox). Click-to-edit still assigns/clears.
5. **Optimistic allowed-services (manual/GUI)** — add/remove a service on a stage: chip toggles instantly
   and persists after reload.

## Jours ouvrables — measured, not assumed (2026-08-13)

`Stage.DurationInDays` is **already in worked days** for 25 of 27 stages: 14×7, 22×7, 30×2, 42×3, 44×6,
66×2. 22 is a month of worked days, 44 two, 66 three, 14 about three weeks. Only the two 30s (pharmacie
officine, stage hospitalier d'initiation) are ambiguous — 30 worked days is six weeks, so they are most
likely calendar days the import left behind.

**Consequence for planning: author the axis in `jours ouvrables`.** Med6 at 22 j.o. per column meets every
stated duration exactly (CHIRURGIE k=2 → 44) while its calendar span swings 60–67 days. A monthly axis
cannot make that guarantee — the same six months give columns of 18 to 22 worked days.

The academic year 2025-2026 (01/09/2025 → 31/08/2026) holds **365 calendar days, 261 weekdays, 252 worked
days** with the ten fixed national fériés, **247** once the four lunar ones are entered. Two of the fixed
days fall on a weekend and cost nothing.

⚠ The lunar dates are not computable — Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram, Aïd al-Mawlid turn on
observation of the crescent and are fixed by decree. Enter the estimate unconfirmed (a Hijri date drifts
~11 days earlier per Gregorian year) and confirm when the decree lands. Under `mois`/`semaines` correcting
it moves no dates; under `jours ouvrables` it shifts that column and every one after it.

---

## "The system knows and does not say" — three of them, closed (2026-08-13, session 17)

Session 16 ended with three open findings. They looked unrelated; they are one shape. In each case the
backend held the distinguishing fact and the surface collapsed it into a state that read as something
else — and in each case the wrong reading pointed the user at the *opposite* action.

### 1 · An empty répartition has two causes

`PeriodAxis` was fed the **cells**, so a level whose axis had just been applied — slots written, nothing
arranged — produced **zero columns**, and the document printed « Aucune période n'est planifiée ». The
apply had worked perfectly and looked like it had failed.

The axis is now built from `declaredSlots ∪ cells`, which is what `PeriodAxis`'s own doc comment always
claimed ("out of the windows its stages actually declare"). `RepartitionSummary.DeclaredSlotCount` names
the fact that separates the two states:

| `declaredSlotCount` | `rowCount` | what it means | what to do |
|---|---|---|---|
| 0 | 0 | no period exists for this (level, year) | author an axis |
| > 0 | 0 | the periods exist, nobody is in them | arrange |
| > 0 | > 0 | a table | read it |

⚠ **The union, not the declared slots alone.** A cell is tied to the level through its *cohort*
(`a.Cohort.Stage.LevelId`), while a declared slot is tied through its *stage*. They are the same set in
practice, but assuming it means a slot reached by some other route silently drops its column — and the
cells in it — out of the printed table. Unioning costs a `Distinct()` and cannot lose a cell.

⚠ **Deliberate knock-on: `EmptyCells` rises on partially arranged levels.** Unarranged periods now print
as columns of hatched cells and the orange alert counts them. Those holes were always there; hiding the
column hid the hole with it, which is the opposite of what a pre-publication review is for.

### 2 · A safe act with no control is not a safe act

`AssignUnlabelled` has always existed and has always been the harmless half of partitioning — it fills
only `null` labels, so it cannot move a group that an existing plan placed. But the UI showed the assign
form **only while no label existed** (`showSetup = partitions.length === 0`). A promotion that later grew
groups could therefore be repaired only through « Redécouper », a full re-cut, sitting one button away
from « Supprimer les partitions » with no confirmation on it. That adjacency is what cleared level 3.

The rule worth keeping: **an unreachable safe path makes the destructive one the default**, and that is a
defect of the same size as an unguarded destructive action. Backend needed no change at all — the command
had taken `Reassign: false` since it was written.

### 3 · Moving a date is deleting it, for every window laid over it

Delete reported `SlotsSpanning`. Edit reported nothing — and edit is the path that actually runs, because
the whole lunar-date workflow *is* "enter the estimate in September, correct it when the décret lands".

`UpdateHolidayCommand` now reports the same number over the **union of the span it left and the span it
arrived at**. Both halves are affected, for opposite reasons: the old span's windows were laid around a
holiday that is no longer there; the new span's have just gained one they never counted. Counted **once**
where the spans overlap — the usual correction is a day, so both fall inside one window and counting twice
would name a number the confirmation cannot justify — and counted **before** the write, or the old span is
already gone.

`DatesMoved` gates the whole thing. Ticking « Date confirmée » on a span that was already right moves no
day count; reporting slots there is how a real report becomes one users dismiss. Same reasoning as
`IsConfirmed` itself: the flag is load-bearing precisely because it is *not* what changes the arithmetic.

### 4 · A partition is a fact about a cell (2026-08-13, reported from the running base)

3Med with two partitions printed **Chirurgie orange and Médecine green** under a legend reading
« Partition A / Partition B ». Both partitions pass through both stages — they mirror — so a colour
that tracks the stage cannot be a colour that tracks the partition.

`RepartitionRow.RotationGroup` was "the partition its first period belongs to". The row is a *service*,
and over the year a service holds groups from every partition; the crossover **is** that. So the row
band was never a fact about the row, and with `P = 2` it degenerates exactly onto the stage:

```
P1: A → Médecine,  B → Chirurgie        every Médecine row opens on A
P2: A → Chirurgie, B → Médecine         every Chirurgie row opens on B
```

⚠ **The dangerous shape of this bug is that it is self-consistent.** The colours were stable, the
legend matched the data it was given, and every row really did open on the partition named. Nothing
was inconsistent — the model was just answering a question nobody had asked. The tell was that the
answer never varied along a row, which is the one thing a crossover guarantees it must.

The handler already computed the band per cell and then discarded all but the first
(`bandByColumn[firstOccupied]`); the fix moves `RotationGroup` onto `RepartitionCell` and tints the
`<td>`. Med3 now reads A,B along the Médecine row and B,A along the Chirurgie row — the mirror, which
is the single most important thing the published document communicates.

⚠ **The old test encoded the bug**: `Rows.Select(r => r.RotationGroup).Should().Equal("A","A","B","B")`
passed, described the wrong model, and would have kept passing after any "fix" that preserved it. It
survived because `SeedAsync`'s fixture puts partition A only in Médecine and B only in Chirurgie —
a static split, not a crossover. A fixture that cannot exhibit the phenomenon cannot catch the bug in
it, and this one had been green through every session. Its replacement mirrors.

---

## The promotion split, audited against the base (2026-08-14, session 19)

`SplitAcademicGroupsPerLevel` is applied. Re-measured on the live dev database, every cross-promotion
invariant now holds:

| check | rows |
|---|---|
| rosters whose registrations span more than one promotion | **11** — and all 11 are the year's « Non réparti » bucket, which is what it is for |
| registrations whose year disagrees with their roster's | **0** |
| registrations whose promotion disagrees with their roster's | **0** |
| cohortes whose stage's promotion disagrees with their roster's | **0** |
| cells whose slot year disagrees with their roster's year | **0** |
| rosters with no promotion | **11** — exactly one per académic year, all `GroupNumber = 0` |

3,696 of 3,707 rosters carry a promotion. The data is clean; what was still open was the *code*.

### An index makes rosters distinguishable; it cannot stop them being mixed

`IX_AcademicGroup_Year_Level_Number` gives every roster an identity. It says nothing about who may
point *at* one, and two writes did so by plain FK with no promotion check — after which every
downstream guard is keyed on the roster the row claims, so nothing objects:

- **`TransferStudentCommand`** took any `TargetGroupId` that existed. A 3rd-year could be moved into a
  5th-year roster, or into last year's; `StudentAffectationService` then affects him to that roster's
  cohorts (stages he does not owe) and `SchedulePublisher` books him against the other promotion's
  service quota, because a cell's promotion is read off `Cohort.Stage.LevelId`. This is the single
  write that could have recreated by hand what the migration repaired across 1,003 rows.
- **`CreateCohortCommand`** paired any roster with any stage. `CohortProvisioner` has always checked
  this on the bulk path; the hand-built path had no equivalent.

Both refuse now, naming both promotions — see `AcademicGroupErrors` / `StageErrors`.

### The bucket needed closing from two more sides

`CohortProvisioner` still carried the legacy "or has a registration at that level" fallback. Post-split
the only rows without a `LevelId` are the buckets, and a bucket holds every promotion at once — so the
fallback matched it for *every* level named in a plan. It could only fire on a bucket carrying a
partition label, which `AssignRotationGroups` can no longer write; but `CreateGroup` and `UpdateGroup`
write `RotationGroup` directly and had no guard at all. Both are refused, and the fallback is gone.

⚠ **12 cohortes hang off buckets already** (2018-2019 ×3, 2019-2020 ×3, 2024-2025 ×6, 161 assignments,
0 cells). Legacy-import artefacts for students who had rotations but no roster. Left alone: they are
history, they carry no cell so they never reach a répartition, and the level filter in
`StudentAffectationService` keeps their assignments promotion-correct. The guard is on *creation*.

### « Groupe 1 » has to exist twice

The label index was `(year, label)`, so the obvious name for the 4th year's first roster was already
taken by the 3rd year's — a promotion could be *numbered* 1-60 per the faculty's own document but not
*named* that way. `GroupLabelPerPromotion` widens it to `(year, level, label)`, `NULLS NOT DISTINCT`.
A pure relaxation: the old key is a superset of the new one, so no existing row can collide.

---

## Why the 5th year's document became unreadable, and what the cell actually costs (2026-08-14)

Reported as "on stages with few services and many students we can't see all the groups in a période —
like médecine sociale in 5Med". Two independent causes, both in the published document.

**1 · The cell was held to one line.** `.repartition-doc__groups` was `white-space: nowrap` inside a
`table-layout: fixed` table. A `<td>` does not clip, so a cell wider than its column paints across its
neighbours and *both* périodes become unreadable. Measured at a 1280px document width on 5Med
2025-2026: **15 group tokens outside their own cell**. Now 0, at most two lines, and the whole document
20px taller.

⚠ **The length of that cell is a consequence of the partition strategy, not a defect.** Santé Publique
is *one* service for the whole 5th year, so a période's cell holds 6-7 groups whatever happens. Cut
`Contiguous` they collapse to « 21-27 » — five characters, and exactly what `MED05.png` prints. Cut
`Interleaved` (the default) they cannot collapse at all: « 3, 12, 21, 30, 39, 48, 57 », twenty-five.
Both are correct plans. The renderer must survive the second; the faculty may still prefer the first.

**2 · The palette wrapped at six.** `bandIndexes` mapped label → `i % 6`. The 5th year has **nine**
partitions (A–I) and the 6th has **ten** (A–J), so A and G printed in the same tint — under a legend
giving each its own swatch. A key asserting that two different things are one is worse than no key.

That was first fixed by suppressing the tint past six partitions and saying so in the legend. The
right answer, settled the same day, is that **the document should never have coloured by partition at
all**: a partition is scolarité's internal division for building the rotation, and the reader of this
page is a student looking for his own group, to whom « Partition G » explains nothing he can act on.
It now colours by **stage** and does not mention partitions anywhere — verified: the word does not
appear in the document's text, and the only surviving `title` is the chef-source note.

⚠ **Tinting by stage is not the old per-row partition band coming back.** That one asserted a row
belonged to one partition, which is false — a row visits every partition over the year, and that is
exactly what the crossover is. A row belongs to exactly one *stage*, so this states something true,
and it states what the first column already says in words. Which is also why the stage palette may
cycle where the partition palette could not: blocks are contiguous, separated by a heavy rule, and
each names itself, so five tints cover any promotion with **no two adjacent blocks alike** (verified
on 5Med: 7 stage blocks → tints 0,1,2,3,4,0,1, zero adjacent clashes). `RepartitionCell.rotationGroup`
is still sent and still shown where it is actionable — `ScheduleGridModal`, `AssignmentsPage`.

The legend is now one item, the hatch, which is the only mark the document makes that is not also
written out in words.

Two smaller things fell out of the same pass: the identity columns were `nowrap` + ellipsis, which cut
the *end* of every long service line — and the end is where the chef's name is; and the table's
`min-width` (a screen affordance bought against a horizontal scrollbar) was still in force in print,
where A4 landscape gives about 1060px and there is no scrolling, so the last périodes ran off the sheet.


---

## A service's load is not readable one period at a time (2026-08-14)

Asked for "saturation across all the stages active in the service at the same time". The reason that
could not already be read anywhere: **nothing ties two stages' periods together**. `StageSlot` is
keyed (stage, year, number), so Chirurgie P1 and ANES REA P1 have independent dates and legitimately
different lengths. List a service's load one créneau at a time and each number is that créneau's own
cohorts — while the students standing in the service on a given morning are the union of every window
covering that day. **The peak therefore lives in the overlap, and a per-slot list never shows it.**

`OccupancyTimeline` cuts the year at every boundary instead (each window's first day, and the day
after its last) so each row carries one exact simultaneous load. Pure, like `PeriodAxis` and
`RotationTiling`, so the arithmetic is tested rather than seeded. Two traps worth keeping:

- boundaries are `start` and **`end + 1`**. Using `end` makes the last day of one window and the
  first of the next share a boundary, and two back-to-back windows merge into one row showing a load
  neither ever had;
- stretches with nobody in them are dropped. A row reading 0 suggests something is planned there.

⚠ **It measures the load exactly the way the guard does** — `Cohort.Assignments.Count`, planned cells
rather than `ServicePeriod`s, date overlap, and the same `HasLevelRestrictions` branch as
`SchedulePublisher.EnsureCapacityAsync`. A page that explained a refusal with a number that never
produced it would be worse than no page.

### What the measurements say about why this was needed

| | |
|---|---|
| planned cells 2025-2026 | 353 |
| over capacity | **233 (66%)** |
| worst cell | **85 students against 20** |
| services carrying the imported default `Capacity = 20` | **148 of 148** |
| `ServiceLevelCapacity` rows authored | **0** |

So today every capacity verdict in the base is measured against a number nobody wrote, and the whole
per-level quota machinery — built, documented, tested — is unused. The page states which of the two
rules is in force on every service, because an empty quota table reads as "not configured yet" when
it means "open to everyone", and a total on a restricted service reads as live when it is dead data.

✅ **`AllowOverCapacity` is split** (2026-08-17). `EnsureCapacityAsync` → `EnsureIntakeAsync`, called
unconditionally: **admissibility is checked whatever the caller asks for**, occupancy only when the
override is off.

The 66% is not background colour here — it *is* the argument. A flag that has to be ticked on two
thirds of the plan is ticked as a matter of routine, so whatever else it happened to govern was
switched off every time it was reached. **A rule enforced only when nobody needs the override is not
enforced**, and this one was the rule against sending 1ère année to a service that does not take
them. The same table also says why the *other* half must stay waivable: with 148 of 148 services on
the imported default and 0 quotas authored, a capacity verdict is measured against a number nobody
wrote, and refusing on it outright would stop planning altogether.

Two consequences worth keeping. The refusal now says it cannot be forced — the checkbox is on screen
promising otherwise, and its description literally listed « service n'accueillant pas cette
promotion » among what it would push through. And the occupancy lookup, the expensive half, is built
only when a number will actually be read, so splitting the flag did not make the common publish
slower than when it skipped everything.

⚠ **Still open** (same review, in the order that pays): real capacities first; then turn
`RotationArranger`'s bare `saturatedServices` count into the same service × période × overflow report
this page now gives; then change `BuildServiceQueue`'s objective to distribute unavoidable excess in
proportion to capacity instead of dropping services smaller than one cohort; and close the two silent
degeneracies (`shiftPerSlot == 0` when cohorts < périodes, weight 0 excluding a service outright).

---

## Several périodes is not several services (2026-08-14)

Asked for the right to run a multi-période stage in **one** service with **one** evaluation — the
example given was 5MED Gynécologie: k=3 périodes of ~20 days each, and splitting that across three
services is not how the faculty runs it.

Before designing anything I asked the imported Access history what the faculty actually did. Periods
recorded per (student, stage):

| promotion | 1 period | 2 | 3 |
|---|---|---|---|
| **5ᵉ Médecine** | **30,614** | 0 | 0 |
| **6ᵉ Médecine** | 21,309 | 1 | 0 |
| 4ᵉ Médecine | 26,156 | 294 | 1 |
| 3ᵉ Médecine | 6,557 | **5,385** | **409** |

So this is not a new mode: **5ᵉ and 6ᵉ année have always been one service, one mark**, and the
per-période rotation PGSH assumed as universal is the 3ᵉ/4ᵉ année case. 5MED Gynécologie in
2025-2026 is one imported period averaging **70 calendar days** against a catalogue of 44 j.o. —
three columns of the axis, one service. The old CLAUDE.md note "a period is one *service*, not one
stage" was right about why the axis needs `kₛ` columns and wrong to conclude that the group must move
between them. `Stage.RotationMode` separates the two questions.

The mechanical change is small and that is the point: the axis, the cells, the conflict guard and the
printed répartition are all untouched, because the group genuinely does occupy all `kₛ` columns. The
arranger freezes its rotation offset across the run instead of advancing it per column; the publisher
collapses the run's cells into one `ServicePeriod`. Nothing in `StageScoring` changed at all — the
mean of one mark is that mark.

⚠ **What it cost was the cell↔period 1:1.** Four guards ask "is this cell published?" by reading
`ServicePeriod.CohortSlotAssignmentId`, which under `SingleService` names only the *first* cell of a
run — so the trailing cells read as free, and the arranger would rewrite them or `DeleteStageSlot`
would drop a column out from under a running stage. `ServicePeriodSlotCoverage` is the honest
one-to-many, written under both modes so the guards read one table. **The backfill was zero rows**
(see below), which is the cheapest this refactor will ever be.

### Did the automatic répartition overwrite the imported data? No — nothing was ever published

| 2025-2026 | slots | cells arranged | cells published |
|---|---|---|---|
| 5MED | 63 | 540 | **0** |
| 6MED | 60 | 0 | 0 |
| 3MED | 8 | 320 | **0** |

Across all 51 (year × promotion) pairs, `ServicePeriods` with a grid link = **0**. Every one of the
~104k periods is an ad-hoc row (`CohortSlotAssignmentId IS NULL`) exactly as `LegacyImportPlanner`
wrote it. Auto-arrange only ever writes `CohortSlotAssignment` cells; `SchedulePublisher.BuildPeriods`
only ever `Add`s. There is no delete path from planning to execution.

⚠ **But publishing would have duplicated every 5MED student.** All 706 assignments of 5MED 2025-2026
already carry an imported period per stage, and `IsPublishedAsync` counts only *grid-linked* periods —
so it would not have seen them, would have published on top, and each student would have ended with two
sets for one stage: averaged into the note by `RecomputeFinalScore`, and waited on by the lifecycle
before the stage could reach `Evaluated`. Publishing now skips any assignment that already holds a
period and reports the count (`SkippedAlreadyServed`). Per assignment, not per cohort — a cohort mixes
repeaters and délocalisés with students who still need their schedule.

### The undo chain was right, and its first link was not guarded

`unpublish → clear cells → delete slot` is the correct shape, and links 2 and 3 were properly guarded
(`SlotPublished`; `ClearSlotAssignments` skips published cells and reports the count). Link 1 was not:
`UnpublishCohortScheduleCommandHandler` deleted the periods with **no check at all**, and
`ServiceEvaluation`, `AttendanceRecord`, `PeriodPause` and `Delocalization` all cascade from
`ServicePeriod`. So unpublishing a cohort mid-rotation silently destroyed every mark a chef had
entered and every day of attendance recorded. It also left `Status` and `FinalScore` untouched, so an
assignment could read *Validated, 14.5* with zero periods behind it.

Now: refused with `ScheduleUnderway` naming what would be lost, `Force: true` for the caller who has
read that sentence, removal through the aggregate so status and note are recomputed, and ad-hoc
periods explicitly left alone (`AdHocPeriodsKept`). The UI asks twice — the second time showing the
server's own count.

---

## A count taken from a subset — the same defect in three places (2026-08-16, session 18)

Session 17 ended with five open findings. Three of them are one shape, and it is not the shape the
handoff guessed: **a number computed over part of a set and then used as if it described the whole.**
Each time the wrong number was plausible, self-consistent, and silently written to disk.

### The partition count of a promotion, read from one stage's cohorts

`PartitionAllocator.BuildLabels` resolves "how many partitions are there" from the labels it is given,
and `RotationArranger` gave it the cohorts of the stage being arranged. That is a subset by
construction — `CohortProvisioner` skips a stage a text does not require, and cohorts are provisioned
stage by stage — so a promotion cut into ten, seen through a stage whose cohorts carried only A and B,
*is* a promotion cut into two. Every unlabelled roster was then filled into those two and written.

Med6's **A = 42, B = 42, C–J = 2 each** is exactly that: 80 rosters filled 40/40 on top of a clean
A–J × 2. It is not what `ReassignAll` produces (it fills the smallest partition each time, so it cannot
leave that distribution), which is what made it look inexplicable last session. The suspect named in
the handoff, `AutoArrangeGroupsCommandHandler`, never writes `RotationGroup` at all.

Two further consequences of the same subset:

- **The balance was measured over it too.** "Fill the smallest partition" against a stage's cohorts is
  not the promotion's smallest partition, so two stages gap-filling in different orders disagree and
  neither matches the promotion.
- **The mirror case is silent.** A stage whose own cohorts are all unlabelled made `alreadyCut` false,
  so a promotion that *is* cut got `PromotionNotPartitioned` — or, with no partition targeted, its
  rosters were simply left unlabelled and invisible to every partition filter downstream.

`PromotionPartitioning` reads the cut from (année, niveau). ⚠ The write is deliberately narrower than
the read: an arrange labels only the rosters **it is placing**. Partitioning a roster it never touches
is `AssignRotationGroupsCommand`'s act, and that command has the strategy, the published-cells refusal
and the audit entry that make it one.

### The promotion itself, read as "the year"

`AssignRotationGroupsCommand` and `ClearRotationGroupsCommand` applied the level filter *only when a
level was given*. Year-wide they cut every promotion at once — three numberings with different
partition counts, folded by `BuildLabels` into one — and reached « Non réparti », whose labelling moves
4,725 students of every promotion as a single body. CLAUDE.md already asserted that
`AssignRotationGroups` could no longer reach the bucket. It could. `int LevelId` on both commands is
the guard, expressed where it cannot be forgotten.

### The promotion's size, read off a page

The Plan macro tab derived the partitions, each one's size and « N groupes sans partition » from
`GET /groups` at `pageSize: 200`. A promotion adds ~100 rosters a year, so past 200 every one of those
numbers reads low — including the one whose only job is to say a gap-fill is owed.
`GetPromotionPartitioningQuery` counts them where the rows are. **Raising a page size does not fix a
count; it moves the cliff.**

### And one number that was never wrong, because nobody could write it twice

Not from that family, but closed in the same pass: `IX_AcademicYear_IsCurrent`, a partial unique index
on `IsCurrent`. `AcademicYearResolver` takes the *first* row flagged current, so two flagged at once is
two screens disagreeing about which promotion they are showing, with nothing on either to say so.
`CreateAcademicYear` demotes the others — one write path guarding an invariant of the table.

⚠ **Two Phase-13 items turned out never to have been real.** `Registration.LevelId` and
`Registration.AcademicGroupId` are not missing indexes: EF Core creates one per foreign key by
convention, and both exist. Scaffolding the "fix" produced a `RenameIndex` and nothing else.

### Measured live, 2026-08-16 — and the promotion was not the one we thought

The lopsided cut was **not** on Med6. Med6 reads A–J × 10, clean. The defect is on **4ème année
Médecine** and **5ème année Pharmacie**, and the two are identical to the roster:

```
A  13  1,10,19,28,37,46,55, 61,63,65,67,69,71
B  13  2,11,20,29,38,47,56, 62,64,66,68,70,72
C   7  3,12,21,30,39,48,57      G  6  7,16,25,34,43,52
D   7  4,13,22,31,40,49,58      H  6  8,17,26,35,44,53
E   7  5,14,23,32,41,50,59      I  6  9,18,27,36,45,54
F   7  6,15,24,33,42,51,60
```

Groups **1–60** are a textbook interleave over nine partitions. Groups **61–72** — the twelve rosters
the promotion gained afterwards — alternate over **two**. That is the subset gap-fill and nothing else:
no other write path can produce a label set of exactly {A, B} on a promotion cut nine ways.
`ReassignAll` fills the smallest partition each time and cannot leave it; `AssignRotationGroupsCommand`
would have been shown all nine labels and used them.

⚠ **The severity of this defect is invisible in the partition sizes.** A = 13 against C = 7 looks like
rounding. It is not: it is 12 rosters — ~240 students — in the wrong half of every crossover the
promotion will ever run. Session 17 spotted it on Med6 only because the numbers there were absurd
(42 / 42 / 2), and Med6 was subsequently re-cut by hand, which is why the absurd case had disappeared
by the time the cause was found. **The mild case is the dangerous one**, and it survived a whole
session of looking straight at it.

Both promotions were repaired the same day — cleared and re-cut into 9 *Alterné*, giving 8 per
partition. Neither carried a planned cell or a published period, and the totals were byte-identical
across the repair: 13,604 cohortes, 860 cellules, 98,555 affectations, 105,626 périodes. That is the
`ClearRotationGroupsCommand` guarantee holding in practice, on real data, for the second time.

## « Retrait » — not damaged data, and that is the point (2026-08-16)

Raised as "damaged data we fetched from the Access db". It is not. `CODE_N = 'MED00'` is a **withdrawal
marker** the old base wrote in place of a year of study, and `LevelMapper` mapped it to a `Level` with
`Year = 0` on purpose — with a comment saying so — because dropping those rows would have destroyed
real history.

**The evidence that it is coherent:**

| | |
|---|---|
| registrations at level « Retrait » | 12, **all** `Status = Withdrawn` |
| carrying real rotations | **8 of 12** — up to 5 périodes each |
| stages / cohortes at that level | **0 / 0** |
| parcours shape | 1ère → 2ème → 3ème → **Retrait** |
| students who came back afterwards | 2 (Retrait 2023-24 → 5ème année 2025-26; Retrait 2018-19 → 4ème 2019-20) |

Eight students served stages that year and then left; two later returned. Dropping the marker rows
would have deleted both facts. Keeping them was right.

⚠ **And it cannot be repaired anyway**: MED00 *replaced* the real year in the source, so the year each
student withdrew from is gone, not corrupted. There is nothing to reconstruct.

### What it does cost — a marker offered wherever a promotion is

Because it is a `Level`, it behaves like a promotion in every screen and every command that takes a
`levelId`. Two things followed:

- « Retrait » was **selectable in the planning pickers**, next to « Troisième Année ».
- One of its rosters carried partition **E**. Not a deliberate cut: `SplitAcademicGroupsPerLevel`
  shredded the folded roster « Groupe 59 » into one row per (year, level) and copied the parent's
  `RotationGroup` onto every shard, the Retrait one included. Inert — the marker has no stages, so
  nothing could ever be provisioned — but wrong, and 1 of its 10 rosters carried it.

`CnpnTargetPlanner` had **already** been forced to special-case year 0 by hand (« année ≤ 2 » must not
sweep the withdrawn into a new text). That is the tell: a rule that has to be rediscovered at each
call site is a rule that belongs on the entity. Hence `Level.IsPromotion`, the refusal on assign and
auto-arrange, and `PromotionsOnly` on the levels read.

⚠ **The read filter is off by default, deliberately.** A withdrawn registration still has to be able to
name its level in the dossier, the parcours and the catalogue. Only the screens that ask "which
promotion am I planning?" pass it. Same reasoning as `GetAcademicGroupsQuery` keeping the wider reach
so scolarité can still see « Non réparti »: **hiding a row from the screen that exists to show it is
its own defect.**

## The pointer is not the plan — four teardown acts, three of them wrong (2026-08-30, session 31)

The question was ordinary: *what happens if « Vider les étudiants » is clicked when affectations
already exist, and should a started stage block the cohorte/bloc reset?* Reading the four paths, the
answer was worse than the question assumed.

### What « Vider le groupe » actually did

`EmptyGroupCommandHandler` set `Registration.AcademicGroupId = null` and saved. That is the whole
handler. And an **`InternshipAssignment` is (inscription × cohorte)**, with `ServicePeriod` hanging off
the affectation — nothing in that chain passes through the roster pointer. So after the click:

| what the screen said | what was on disk |
|---|---|
| roster: **0 étudiants** | its cohortes still held every affectation |
| — | the chefs' worklists still listed every période |
| — | `ServiceOccupancyCalculator` still counted them against the service |
| — | the printed répartition still named the roster's cells |

Not a corrupted state — a **coherent** one that two screens describe differently, with nothing on
either to say so. Same family as the roster that carried four promotions at once and as the chef whose
3 220 périodes were invisible: the system knew and did not say.

⚠ **And it does not undo.** Putting the students back is not the inverse:

- A re-découpage sends them to *different* rosters.
- `StudentAffectationService` dedupes on **(registrationId, cohortId)** — verified in the handler, both
  the bulk path and `AssignRegistrationAsync`.
- The new cohortes are not the old ones, so the dedupe misses.

Result: one student, **two** `InternshipAssignment` rows for the same stage — double in his dossier,
double against the service quota, two rows for one rotation, and only one of them attached to a roster
he is actually in. That is precisely the shape `SplitAcademicGroupsPerLevel` had to repair across 1 003
rows, recreated one click at a time.

### The guard that existed on the wrong path

`DeleteAllCohortsCommand` — « Réinitialiser les cohortes », the button that touches a hundred cohortes
at once — refused as soon as one affectation had left `Planned`.

`DeleteCohortCommand` — the trash icon beside **each line** — had **no guard at all**. Its own comment
read « Delete plan-generated **and ad-hoc** service periods », and `ServiceEvaluation`,
`AttendanceRecord`, `PeriodPause` and `Delocalization` all cascade from `ServicePeriod`. So a chef's
marks and a term of attendance could be destroyed by one click on a running cohorte, and the endpoint
answered `204 No Content` — no number, nothing to read, nothing to regret in time.

**The lesson generalises:** when a bulk act and a single-row act do the same thing, the single-row one
is the one that gets shipped without the guard, because it looks smaller. It is not smaller; it is the
same destruction with a narrower `WHERE`.

### The year, again

`DeleteAllCohortsCommand(int StageId, int? AcademicYearId = null)` — and the handler read
`request.AcademicYearId == null ? no predicate`. On the one command in this area that **deletes rows**.
A stage keeps a cohorte per (groupe, année) and CHIRURGIE has 563 across six years, so a caller that
omitted the year was asking to reset the stage *for every promotion that ever took it*. The frontend
passed `currentYearId ?? undefined`, so an unresolved year context was one dropdown away from it.
Resolved through `AcademicYearResolver` now, like everything else.

### What was already right

`RotationCycleContext` counts published cells through `PublishedCells` — the **coverage** table, not
`ServicePeriod.CohortSlotAssignmentId` — so both the rotation-block apply and its delete are refused
while anything on the axis is published, and a started rotation is published by construction. Nothing
needed adding.

⚠ Its blind spot is narrow and benign: a cohorte served only by **ad-hoc** périodes (imported history,
délocalisations, revalidations) hangs off no cell, so it neither blocks the removal nor is destroyed by
it — removing slots cascades cells, never périodes.

### The rule that came out of it

One reader, `AffectationTollReader`, answers "what is this about to destroy" over three scopes
(cohortes · roster · a year's rosters), counting the same four things the unpublish refusal already
names — so no two refusals can describe the same rows differently.

| state | « Vider le groupe » | « Supprimer la cohorte » |
|---|---|---|
| nothing planned | empties | deletes |
| affectations, all `Planned` | refused, count named → `DropAffectations: true` | deletes, **returns the count** |
| anything underway | refused, **not forceable** | refused, **not forceable** |

**Why not forceable.** Unpublishing has a `Force` because it is the declared inverse of publishing and
its whole subject *is* the schedule — the caller reaching for it is reaching for the périodes. Nobody
clicking « vider le groupe » or « supprimer la cohorte » means "and destroy the marks". A second flag
there would be ticked as routine, exactly as `AllowOverCapacity` was ticked on 66% of cells until the
hard half was split out of it. **A rule enforced only when nobody needs the override is not enforced.**

**Why `EmptyAllYearGroupsCommand` gets no `DropAffectations` at all.** A roster's affectations are a
handful of rows an admin can be shown a number for and consent to. A year's are the whole faculty's
planning — 8 077 registrations, ~105 000 périodes on this base. That act exists, per stage, where its
cost is announced stage by stage.

**Order for taking a promotion apart:** dépublier → réinitialiser les cohortes du stage → vider les
groupes → supprimer les groupes / le bloc de rotation. Each step refuses until the one before it is
done, and each refusal is a sentence naming numbers rather than a « Impossible de… ».

⚠ **Nothing back-fills what the old behaviour already stranded.** An affectation whose registration
points at no roster is invisible to every roster screen and visible to every chef; `SMOKE-TEST.md` §29
step 0 counts them.

---

## Printing the year: two exports, and what a merged date span may claim (2026-08-31, session 32)

Two .xlsx downloads — `GET students/export` (the roll) and `GET stages/assignments/export` (the
post-validation stage record). Both are reads over the schema as it stands; nothing was migrated.

### The roll is an export of registrations, and that settles the "file or column" question

The ask was « NOM, PRENOM, APPOGEE, CNE, GROUP — un fichier par (programme, niveau), ou une colonne
programme et une colonne niveau ? ».

**A column — and `?levelId=` still cuts the per-promotion file, with the columns intact.** It was a
false choice: the columns cost nothing, and they are what lets a row still say where it came from
when two exports are merged, or when one is opened a year later. A file whose only statement of scope
was its own name cannot do that.

The correction underneath the question matters more than the answer. Nom, prénom, CNE and Apogée
belong to a *person*; niveau, groupe, partition and statut are facts about a *year*, and **2 635
students in this base have sat in more than one**. An export cut from `Students` would have to pick
one registration per row and would have no way to say which it picked. So the row **is** the
registration, and the year is part of its identity — omitted, it resolves to the current one, like
everything else in this system.

The CNPN column follows the read order `r.CnpnVersionId ?? r.Student.CnpnVersionId`, and a second
column says which of the two answered. Blank means « jamais résolu » — the ~2 200 unstamped students
— not « rien dû ».

### Several périodes is not several stays

The second question was the substantive one: a stage recorded as `01/01→01/02` and `02/02→02/03` —
can that print as `01/01/2025 – 02/03/2025`, or does it need to say it was multi-période?

**The merge is decided by the service, never by the dates.** A *stay* is a maximal run of périodes in
the **same service** with **no worked day between them**. One stay prints as one span; several print
joined by « · », with the services joined by « → » in the same order so the two cells correspond
position by position.

| what happened | Découpage | Service(s) | Période(s) |
|---|---|---|---|
| one période | `Période unique` | `Cardiologie` | `01/01/2025 – 01/02/2025` |
| two, one service, meeting | `Service unique — 2 périodes contiguës` | `Cardiologie` | `01/01/2025 – 02/03/2025` |
| two, one service, a hole | `Service unique — 2 périodes, 1 interruption(s)` | `Cardiologie` | `01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025` |
| two services | `Rotation — 2 services, 2 périodes` | `Cardiologie → Pneumologie` | `01/01/2025 – 01/02/2025 · 02/02/2025 – 02/03/2025` |

Three things about this that are easy to get wrong:

1. **The multi-période fact must not live in the string.** `Nb périodes` and `Nb services` are numeric
   columns of their own. Collapsing two windows into one span is allowed to make the document
   readable; it is not allowed to erase that the stage was recorded in two. With the numbers in their
   own columns, « montre-moi les stages faits en deux services » is a filter, not a reading exercise.
2. **A gap is measured in worked days.** Calendar days would call every Friday → Monday hand-over an
   interruption — and that is exactly how one column of the axis follows another, because
   `WorkingDayCalendar` never lets a window swallow its trailing weekend. A declared holiday between
   two windows is not a hole either. This is the third feature to lean on that calendar and the first
   where the *absence* of a day, rather than a count of days, is what is being asked.
3. **Both break conditions are load-bearing.** Breaking only on the service change — which is what
   `SchedulePublisher.BuildStays` does, and correctly, since it works from contiguous grid columns —
   would swallow a real interruption inside one printed span. Breaking only on the gap would merge a
   genuine S1 → S2 rotation into one line and lose the second service entirely.

**Most rows never reach the interesting cases.** `SchedulePublisher` already folds a `SingleService`
run into one `ServicePeriod`, and 5ᵉ/6ᵉ année are `SingleService` in 51 923 of 51 924 imported
placements. The folding exists for 3ᵉ and 4ᵉ année, which genuinely rotate, and for the Access
history, which was carried in one row per stay.

### Why three sheets

« Stages » is one row per **attempt** — the unit that carries a note and a verdict, and therefore the
unit a PV is drawn from. « Périodes » is one row per **période**. « Synthèse » counts the verdicts per
stage. Folding the detail into the first sheet would either lose it or turn every row into a
paragraph; dropping the first sheet would hand a reader several lines per student and nowhere to read
a verdict. `Réf. stage` is on both sheets and is the join — a detail nobody can key back to its row is
a detail nobody reads.

### Two scoping decisions worth remembering

- **The stage export is scoped by the registration's level, not the stage's.** The document is « la
  promotion et ce qu'elle a fait cette année », so a 6ᵉ année student revalidating a 3ᵉ année stage
  belongs on the 6ᵉ année's file — his own. Both levels are printed, which is what makes the row
  readable as a rattrapage rather than as one filed in the wrong place. ⚠ `GetStudentLevelDossierQuery`
  scopes the other way (`a.Cohort.Stage.LevelId`) and is right to: it answers a different question —
  « what does this student owe *at this level* ».
- **The unmarked attempts are in the document by default.** A file whose whole purpose is « où en est
  la promotion » must show the holes, or a missing évaluation is indistinguishable from a student
  nobody planned. `onlyEvaluated=true` is the caller saying the file is a PV rather than a state of
  play — and it is the switch the pre-validation export will reuse, not a second pipeline.

### The shape of the code, and the two traps it avoids

`PGSH.Application/Exports/` holds a format-agnostic workbook model; `ClosedXmlExportWorkbookWriter` in
Infrastructure is the only code that knows what .xlsx is. One writer, or every export ends up styled
by its own handler and the faculty gets three documents that agree on nothing.

- ⚠ **Cells are typed.** A date written as text cannot be sorted and a mark written as text cannot be
  averaged, which is the first thing anybody does to a post-validation file — and it fails silently.
  Identifiers stay text on purpose: a CNE that looks like a number must not lose its leading zeros.
- ⚠ **Three flat queries, never one nested read.** The périodes of an attempt and the objective scores
  of an évaluation are both collections; folded into the assignments projection — which is the obvious
  way to write « one row per stage, with its périodes » — that is a collection subquery in a
  projection, the exact family that killed the macro plan on 2026-08-26. The scope is defined once and
  the other two reach it through `IN (subquery)`; two copies of a year filter is how a périodes sheet
  ends up describing a different population from the stages sheet beside it. Pinned by
  `SqlTranslationTests`.

## The 2026-2027 roll came out full of blanks, and every blank was true (2026-08-31, session 32c)

Reported minutes after the first real download: « le 4MED a des groupes et je ne les vois pas dans
l'export 2026-2027 ». Audited column by column against the base, the file was **faithful everywhere**:

| colonne | rempli | ce que dit la base |
|---|---|---|
| Groupe · N° groupe · Partition | 0 / 5 932 | **0 inscription ne porte de `AcademicGroupId`** |
| Source de la décision | 0 / 5 932 | `OutcomeSource` null partout — personne n'a délibéré une année qui vient de s'ouvrir |
| Convention | 0 / 5 932 | `AgreementType = None` pour **les 10 206 étudiants** de la base |
| CIN | 5 904 | 28 null ou vides |
| Sexe | 5 005 | 927 portent `Gender = None` |
| Date de naissance | 5 930 | 2 null |

### The roster columns, and what the state actually is

2026-2027 holds **90 `AcademicGroup` rows for the 4ᵉ année Médecine** — numbered 1-90, carrying
partition labels A-F interleaved — and **every one of them is empty**: 0 inscriptions rattachées, 0
cohortes. The year also holds 0 affectations and 0 `StageSlot`s. So the Groupes page shows 90 rosters
(it counts `g.Registrations.Count`, so it correctly shows *0 étudiants* on each) while the export
shows no group on any student, and both are right.

⚠ **`AutoArrangeGroupsCommandHandler` cannot have produced this.** It refuses outright when no
registration is unassigned (`Groups.NoUnassignedStudents`), and where it does create rosters it sets
`reg.AcademicGroupId` in the same unit of work. The state is reachable by cutting the promotion and
then emptying it — « Vider les groupes » clears the pointer and leaves the roster standing, which is
the documented behaviour — but **the audit trail cannot say**, because roster creation, rotation-group
assignment and emptying are all **unaudited**, while `PARTITIONS_CLEARED` is recorded. The
destructive act leaves a trace and the constructive one does not, which is the wrong way round for
the question « qui a découpé cette promotion, et quand ? ».

### What was actually wrong, and it was not the reads

**A column empty on every row looks exactly like a column the export forgot to fill.** The reads were
right and the document still misled, because it had no way to say « j'ai regardé et il n'y a rien ».
That is the same « one state standing in for two » that `RepartitionSummary.DeclaredSlotCount` and
`OutsideYearCount` exist to prevent — and it is worth noting that the rule was already written down
twice and the export was built without it anyway.

`ExportNotes` now prints, above the header of every sheet:
- the columns carrying no value in **any** row — computed from the exported rows, so a column added
  later is covered automatically;
- and, when the roster columns are among them, **which of the two causes it is**: « aucun groupe
  n'existe encore » (découper) versus « 90 groupe(s) existent mais aucune inscription n'y est
  rattachée » (répartir). A single blank column collapses those into a third reading the user reaches
  first: that the export is broken.

⚠ **The note must not fire on a partly-filled column**, or it becomes noise and gets dismissed —
which puts the real one back out of sight. That control is a test, not a comment.

## Three slow screens, measured before anything was changed (2026-08-31, session 33)

Reported as performance. Measured against the live base (`TodoDatabase`, 22 années, 14 339 cohortes,
104 387 affectations, 111 457 périodes) before touching code, because « c'est lent » names a symptom
and not a place.

### The planning grid — the cost was never on the wire

| what | measured |
|---|---|
| cohortes on the current year's biggest stage (Gynécologie, 2026-2027) | **105** |
| the grid's cohort query, `EXPLAIN ANALYZE` | **18.8 ms** |
| `ServiceOccupancyCalculator.EntriesQuery` for that stage's services (695 rows) | **18.4 ms** |
| cells shipped in one response | ~1 000 |

⚠ **The decisive fact was in the report and not in the numbers: closing was slow too.** Closing issues
no request. So ~40 ms of SQL was never what the user was waiting for — it was the browser mounting and
unmounting a thousand cell components, each a `Box` + `Group` + `Stack` + two `Text` + `ActionIcon` +
`Badge`. Somebody had already been here once: the cells use a native `title` instead of a Mantine
`Tooltip`, with a comment saying « 320 floating-ui instances were most of the seconds this modal took
to open ». The remedy was right and the cause was the row count, which had since grown.

Paging the rows is therefore the fix at both ends. What it costs is that **every number on the screen
had to move to the server** — see `CLAUDE.md`, « The planning grid is a matrix ».

### The affectation loop — 92 ms against 4 ms, before EF is counted

```
-- one read per cohorte, 105 of them, for one stage      →  92.1 ms
-- one read for the whole call                           →   4.2 ms
```

Server-side only: no round trip, no query compilation, no connection acquisition. In EF each of those
105 is a separate round trip, and the macro plan walks the loop **once per concurrency block** — seven
stages on that promotion, so ~700 of them for one press of « Générer le plan ».

⚠ **The batched form is not automatically the same query.** The pair `(roster, niveau)` has to survive
the batching: keyed on either half alone, a student registered in this roster at another level is
affected to a stage he does not owe. That is the same shape as the two-independent-`Any` trap that
turned 833 students into 2 127, and it now has a test that fails when the pairing is removed.

### The publish storm — a loop on the client, and a first-failure refusal on the server

Two independent causes producing one symptom. `StageDetailPage.handlePublishAll` looped
cohorte-by-cohorte and `errorMiddleware` toasts every rejected mutation, so *N* cohortes gave *N* red
toasts; and `EnsureIntakeAsync` returned on the first breach, so even the single stage-wide call could
only ever name one service. Both had to change for the symptom to go.

⚠ **Worth keeping in mind for the next one of these: the base is structurally over-subscribed.**
Measured 2026-08-14 and still true — 233 of 353 planned cells over capacity, worst 85 against 20. A
refusal that names one cell at a time on data like that is a refusal nobody can act on.

### What is still not proven

- **Atomicity.** `ExecuteAtomicallyAsync` cannot be exercised by the suite: `UseInMemoryDatabase` has
  no transactions, and the harness now says so explicitly rather than letting the call throw. Add it
  to the Testcontainers list, which already carries FKs, unique indexes and `OnDelete`.
- **The browser.** The grid has not been opened, paged or published from since the change.
  `SMOKE-TEST.md` §31.

## The fold is right and the document was silent about it (2026-08-31)

Measured on the live base the day the first stage export was read properly:

| fact | count |
|---|---|
| grid-linked `ServicePeriod`s | 5 831 |
| `ServicePeriodSlotCoverage` rows | 7 497 |
| périodes covering **3** créneaux | 833 — all 5MED Gynécologie Obstétrique, `SingleService` |
| périodes covering 1 créneau | 4 998 — the six other 5MED stages, all `PerPeriod` |
| total `ServicePeriod`s | 111 457 (the rest is Access history, hanging off no cell) |
| services with a configured chef | 2 of 148 |
| services carrying only the legacy note | 140 of 148 |

The 1 666-row difference between the first two lines **is** the folded runs, and it is the whole
subject. One example, verbatim from the base: a période 08/12/2026 → 07/03/2027 covering P4
(08/12→07/01), P5 (08/01→07/02) and P6 (08/02→07/03) — three authored columns, one published
rotation, one mark.

⚠ **Nothing was wrong with the fold.** A `SingleService` run *is* one stay and *is* marked once; the
défaut was that the document stated the fold and not what it folded, so a reader who knew the grid
had three columns saw a file that had lost them. `Nb créneaux` beside `Nb périodes` is the fix, and
the two genuinely differ only here — which is why nobody noticed until a `SingleService` promotion
was published for the first time.

⚠ **The chef half is a data fact, not a code one.** 140 of 148 services name their professor only in
`Service.Description`, undated, because the Access base carried no identity to build an `Employee`
from. The export prints it — on 95 % of the rows it is the only name there is — and says so in
`Origine du chef`. Linking professors in Personnel upgrades those rows with no code change.

## The catalogue and the texts now disagree, and both are right — measured 2026-09-01

The three 1650.25 migrations are **applied** on the live base (`MigrationService` ran them on an
Aspire startup, not by hand). `Cnpn1650Med3CatalogueAlignment` did not raise, so no grid-published
période locked the rotation-mode change on Chirurgie or Médecine.

| | catalogue (`Stage`) | 1650.25 | 2174.18 |
|---|---|---|---|
| MED3 Chirurgie / Médecine — coefficient | **3** | 1 | 3 |
| MED3 Chirurgie / Médecine — durée | **30 j.o.** | 30 | **66** |
| the four stages brought down from MED4 | 30 j.o. / coef 1 | 30 / 1 | — |

All six MED3 stages are now `SingleService`.

⚠ **This is not drift to be repaired.** A 4ᵉ/5ᵉ/6ᵉ année student revalidating a 3ᵉ année credit is
still governed by 2174.18, so 66 has to stay readable after the catalogue moved to 30 — which is why
the migration wrote it into 2174.18's own requirement set *before* overwriting the catalogue. The
coefficient 1 is the documented placeholder from `Cnpn1650Med3Stages`, awaiting the faculty.

What was wrong was the **Stages page**, which rendered the catalogue figure alone. Closed for
display by `StageCatalogueFigure`; which figure is *authoritative* is still `PHASES.md` §15.1 and
waits on `Stage.LevelId` becoming advisory.

⚠ **A guess about a population is not a fact about a subset.** `HANDOFF` item 1c reasoned that
because « 25 of 27 stages carry no `AllowedServices` », the four MED4 counterparts of the new MED3
stages were probably empty too. They are not: Cardiologie 3, Dermatologie Endocrinologie 4,
Pneumologie 3, Rhumatologie Radiologie 7 — **17 rows**, so item 1c is a copy and not fresh entry. No
service in the base carries a `ServiceLevelCapacity` row, so nothing would refuse the copy — and
after it, MED3 (895 inscriptions) and MED4 (898) list the same services, which is a scheduling
question wherever their windows overlap.

## The old text's duration is recorded, visible, and applied nowhere — measured 2026-09-01

`SMOKE-TEST.md` §34 was written to walk one fact end to end: **MED3 Chirurgie (`Stage.Id = 2`) is a
single catalogue row owed by two populations under two texts.**

- **895** inscribed in 3ᵉ année 2026-2027, stamped **1650.25** with `CnpnSource = Effectivity` —
  895 of 895, from the rule « 1650.25 governs level 3 from 2026-2027 ». The mechanism works exactly
  as designed: read once, at the creation of the registration, and frozen there.
- **92** in 6ᵉ année 2026-2027 on **2174.18** still owe it, plus 3 in 5ᵉ année.

⚠ **The expectation that a revalidation carries the old 66 days is not met, and the gap is real.**
No code on the revalidation / dossier / progression / export path reads a duration at all —
`RevalidateStageCommand` takes the `StartDate` / `EndDate` the operator supplies. The figure is
recorded (2174.18's requirement set) and, since `StageCatalogueFigure`, visible; it is not applied.

**The evidence is exact.** Abdallah Jad (CNE 2136598214) failed it in 2023-2024, served in Chirurgie
Vasculaire from 18/03/2024 to 14/06/2024 — **65 jours ouvrables** against the weekend-and-holiday
calendar. 2174.18 states 66. The catalogue now states 30. So the catalogue is not merely a second
source of truth, it is the *wrong* one for anyone still on the old text — which is every student who
could possibly be revalidating a 3ᵉ année credit, since 1650.25's own cohort has not reached the
point of owing anything yet.

⚠ **And there is no door.** `POST stages/revalidate` has no caller anywhere in `PGSH.Frontend/src`.
The act exists, is guarded, is tested, and can only be reached through Scalar by someone holding a
registration id, a stage id and a cohort id.

⚠ **A 6ᵉ année student needs an explicit `cohortId`.** He has no roster in the current year, so
`ResolveCohortAsync`'s fallback — « the cohorte of his own group » — has nothing to find, and the
call fails `NoGroupForRevalidation`. The cohorte has to come from the target promotion's own plan,
which is why §34 plans the 3ᵉ année *before* revalidating into it.

---

## Refusing a row loses the faculty's statement — measured 2026-09-02, session 38

The réinscription roll was applying the right rules and doing the wrong thing with the answer. Asked
« may this student begin his final year owing an earlier stage? », `FinalYearGuard` said no for
**182 of the 651** 7ᵉ année Médecine the faculty's own file re-registers, and the roll skipped them.

⚠ **That 182 predates session 37's « entrer » fix.** Corrected, the gate reaches only genuine
entrants to a final year, and the live preview on 2026-09-02 holds **60**. The argument below is
unchanged by which number it is; the count to check against the screen is 60.

**Why the no was right and the skip was wrong.** In most of those 182 the stage *was* served — the
évaluation is simply not keyed in yet. That is a fact about our data entry, not about the student.
PGSH's stage record is behind the faculty's, and the roll is the faculty stating, in a document it
produced for its own purposes, that these people are coming back. Refusing the row throws that
statement away; applying it silently throws ours away. The hold keeps both.

### The number that decided the absentee question

| | rows | what the roll does |
|---|---|---|
| named, final-year debt | 60 | registered **and held** (`OutstandingPriorStages`) |
| absent, final year | 1 217 | « Diplômé » (Inferred) **and held** |
| absent, undecidable | 50 | nothing recorded **and held** |

⚠ **The 1 217 are held on purpose, and the first proposal was to leave them alone.** The argument for
leaving them was that a `Graduated` registration ends the cursus, so there is no next-year row to keep
out of a partition — the flag would be inert — and 1 217 rows on a worklist where ~1 200 need no
action is the kind of marker that gets dismissed wholesale, which then hides the 50 that matter. Two
things break that:

1. **It is not inert.** Their 2025-2026 registration is still live, and an absence is exactly the
   shape a *late réinscription* takes — the hold is still standing on the day somebody registers one
   of them by hand.
2. **The graduation is our inference, read off a blank cell**, never the faculty's statement. If the
   roll was partial, « il a soutenu » is wrong for people still enrolled and nothing on the row says a
   human ever looked.

So the verdict is still recorded (`Inferred`, self-correcting when a real defence roll arrives) and
the hold sits on top of it as the review marker. **Holding costs a genuine graduate nothing** — his
year is closed and there is nothing left to plan.

### What a hold is, and what it deliberately is not

It withdraws a registration from **planning** and from nothing else: no roster cut, no cohort
affectation, no published période. It keeps its status, its verdict, and every période already
published under it — taking those away is `UnpublishCohortScheduleCommand`'s act, which names what it
destroys and asks twice. A hold only stops *new* work being built on a registration nobody confirmed.

⚠ **Released by hand, never by the condition lapsing.** A registration that quietly re-entered the
répartition the day an évaluation was keyed in would be the same silent behaviour the flag replaces.

### The line that did not move

Errors still refuse the whole file — a duplicated code, an unknown level code, a level contradicting
the registration on record, a level going backwards, a « Retrait ». Those say the *file* is mistaken
rather than that our data is behind, and the write they would produce is a verdict on somebody's
year. The manual registration paths still refuse too, with `FinalYearEntryWaiver` as the override:
the roll is the faculty's own document and outranks a hand-typed form, and per-student ceremony is
precisely what does not scale to 182 at once.

### Still not modelled: the *examens cliniques*

Described by the user and real: they open as soon as a student's stages are all done, he can fail
them, and he is re-registered to sit them again. PGSH therefore cannot tell « still finishing stages »
from « stages done, waiting on the exams » — both read as re-registered. Left out deliberately; the
logic was described as complex and not spelled out, and inventing it would put a state on screen
nobody can act on.

---

## A flag that freezes and a flag that only asks are not the same flag (2026-09-02, session 38b)

The user wanted the 26 students the réinscription roll names and PGSH has never seen to be
**created, flagged, and partitioned with everyone else**. The first two were easy; the third
contradicted the mechanism, because a signalement freezes by construction — flagging them the
existing way would have excluded them from the exact planning he wanted them in.

**The resolution is that blocking is a property of the *reason*, not of the flag.** A signalement
means « quelqu'un doit regarder ceci »; whether it also withdraws the registration from planning is a
second question. `OutstandingPriorStages` and `AbsentFromReinscriptionRoll` both say *nobody has
established that this student may go on* — a debt not cleared, an absence not explained — and acting
before a human rules would send somebody where he may not belong. `IncompleteStudentFile` says
something much weaker: *we are missing his paperwork*. Nothing about a missing date de naissance says
he may not stand in a service.

⚠ **Collapsing them fails in both directions**: freeze people over a birth date, or let an unexplained
absence plan itself.

### What that cost in shape

- `RegistrationHoldPolicy` needs **three** forms, not two — `Plannable` (no *blocking* hold, what
  planning obeys), `OnHold`, and `Flagged` (any hold, what the worklist counts). A screen conflating
  them reports 1 353 blocked students where 1 327 are.
- The blocking set is a **static array**, not a method, because EF cannot call a method inside a
  predicate but translates `Contains` over an array into an `IN`.
- `BlocksPlanning` is **sent** to the client, never re-derived there — the same split as
  `ServicePeriodResponse.State`, for the same reason. `ReleaseHoldReport` grew `StillBlocked` beside
  `StillHeld`: a student left carrying only « dossier à compléter » is on the worklist *and is
  planned*, and reporting him as frozen would be false.

### Two defects the compiler could not see, and the tests could

- ⚠ **`dbContext.Students.Add(student)` left the registration untracked and nothing was written.**
  `Add` marks the reachable graph, and the graph is whole only from the *registration*: it references
  the student and owns the hold, while `Student.Registrations` was never populated. A silent no-op —
  the apply returned success and created nothing.
- ⚠ **`ReleaseHold` on an unsaved hold lifted an arbitrary one.** Keys are store-generated, so every
  hold added in one unit of work carries `Guid.Empty`, and `FirstOrDefault(h => h.Id == holdId)`
  matched whichever sat first. Refused outright now — nothing on the worklist can carry an empty id,
  so reaching it is a defect in the caller rather than a user action.

### What is invented, and what is deliberately not

Only the **e-mail**, because `Users.Email` is NOT NULL UNIQUE and doubles as a Keycloak login: it is
allocated in the *planner*, against the addresses in the **store** rather than merely the batch, and
printed on the row — « N adresses générées » says nothing about *which* address a student was handed,
and that address is how he logs in.

**No CNE is manufactured**: the row carries an Apogée and `Student.CNE` is optional since the
`LEGACY-` placeholders were cleared, so a `SANS-CNE-…` would read in every list exactly like a code
somebody holds. `BacYear` is required by the schema and absent from the file, so it is left **empty**
rather than guessed — and that emptiness is precisely what « dossier à compléter » names.

---

## A skip is still a mention (2026-09-02, found by the smoke test)

The réinscription roll is **designed to be re-runnable**, and 15.16b made the second run the ordinary
path: it is how the students the file names and PGSH does not hold get created. The second run was
destructive.

**What it offered on the live base:** 8 077 signalements and **791 « Diplômé » déduits**, where the
first pass had found 1 267 and 1 217. Those 791 were students the same file re-registers, on their own
named lines, minutes earlier.

**Cause.** `Skip()` returned `SourceRegistrationId: null`. A row skipped as « déjà inscrit » therefore
dropped the closing-year registration it had resolved, so it never entered the `mentioned` set — and
`ReadAbsence` reads *not mentioned* as *ne revient pas*, which in a final year it records as a
soutenance.

⚠ **The rule this violates:** « couvert par le fichier » means the file **named** him, not that the
line produced a write. A skip is still a mention.

**Why it stayed invisible.** The roll had only ever been applied once. Every count was correct on a
first pass, and the defect needed a *second* upload to appear — which is precisely the operation the
act advertises as safe. A feature whose safety story is « you can just run it again » has to be tested
by running it again.


## Two facts about a plan exist only when every service is read at once (2026-09-02, session 39)

`Services/OccupancyReport/` — `GET services/occupancy-report`, and the « Charge des services » page
and printable document behind it.

The service detail page has answered « what does *this* service hold » since session 24. What nothing
answered is **« which services are the problem »** — the question asked once, before publishing a
promotion, and which opening 148 pages does not answer. Two findings are not merely inconvenient to
reach one page at a time; they are **unreachable**:

- **A service that holds nobody all year.** On its own page that is a service with nothing planned,
  which is exactly what it is — there is no fact on the page that makes it a defect. It becomes one
  only beside the service that took its groups. Measured previously on 5MED Psychiatrie: all nine
  columns went to a single service and **two of the five were never used**, 69-85 students against a
  capacity of 20, and the printed répartition was the only place it ever showed.
- **A stage that uses fewer services than it may.** The denominator is `Stage.AllowedServices` and
  the numerator is the cells, so the question spans a stage's whole service list — `ServicesUnused`.

**The trap this design had to avoid, and it is the same one as the omitted year.** A filter is
offered for a promotion and for a stage, and the obvious implementation narrows the placements before
measuring. That prints « ok » for a service that is over *because of another promotion* — and then
the publish is refused anyway, by a guard counting everyone. So the filter narrows which services are
**listed** and what `Share` attributes to them; `PeakStudents`, `Saturation` and every overflow stay
measured on the service's whole load. One number quietly standing in for another is the defect, not
the filter.

**Three arithmetic rules, each of which produces a plausible wrong answer if it slips.**

- A peak is **simultaneous presence**, from the same pure `OccupancyTimeline` the service page uses.
  One cohort of 40 passing through three windows is 40. Summed it is 120 — a saturation that never
  happened, indistinguishable from a real one.
- A month's bar is the **maximum reached inside the month**, never its mean. A month with one
  saturated week reads comfortable on an average, and the week is what somebody has to act on.
- `Saturation` is **null, never 0**, when there is no ceiling to divide by. Zero sorts as « the least
  saturated », which is exactly backwards for a service admitting nobody — and those sort *first*,
  above even a service at 400 %, because theirs is the one refusal publication cannot force.

⚠ **The report's first audience is an empty year, and that had to be designed rather than fallen
into.** Measured 2026-09-02: the live base holds **0 `StageSlots` and 0 `CohortSlotAssignments` on
every one of its 22 academic years**. So the first thing anybody sees is a report of nothing — and
« 0 étudiant » collapses two states that call for opposite acts: no créneau authored (author an axis)
versus créneaux nobody is in (arrange). `Notes` separates them in a sentence naming the button.
Same shape as `RepartitionSummary.DeclaredSlotCount`, and the same rule as `ExportNotes`: the
uniform-capacity note fires only when every open service really does carry the imported default,
because a warning that fires whatever the data says is noise, and noise is dismissed — which puts the
real one out of sight.

**Every figure is inline SVG or a CSS box, and that is a constraint, not a preference.** The page
serializes the document node into a standalone .html the faculty can keep or upload (the répartition's
mechanism, lifted into `common/utils/printableDocument` now that two documents use it). A canvas
serializes empty; a charting library that measures the DOM on mount draws nothing in a file opened
elsewhere. The reader would get a document with holes where the charts were and nothing anywhere
saying so — which is why no charting dependency was added.

## The verdict is a fact about one registration, and that is what makes it findable (2026-09-02)

`GetStudentsQuery.Status`, and its twin on `GetStudentsExportQuery`.

The réinscription roll recorded **1 217 « Diplômé » déduits**, and there was no way to see them in the
app — only in a downloaded file. The filter is three lines; what matters is *where* they go.

⚠ **The status joins the level and the year inside the **same** `Any`.** Asked as a second
independent condition, « Diplômé » ∧ « 2026-2027 » returns every student who ever graduated and
happens to hold a 2026-2027 registration. In a thesis year re-registered every September until the
defence, that is **most of them** — the false positive is the ordinary case, not an edge one. It is
the same trap the level/year pair already documents (833 students as one `Any`, 2 127 as two), and it
is stricter here because a verdict is by construction a fact about exactly one year.

**The test for it passed with the handler broken**, first time round: `TestHarness.SeedRegistration`
mints a new `Student` on every call, so « one student, two registrations » was silently two students,
and two students satisfy the assertion whatever the predicate does. Broken deliberately, confirmed
red, restored — which is the only reason the fixture defect was found at all.

**The export takes the same parameter** so the file matches the list it is downloaded from, and the
caption names the verdict: 1 217 rows captioned « toutes promotions » is unreadable three months
later as a list of graduates.

## Three reads existed on the API with no caller in the admin app (2026-09-02)

`students/{id}/parcours`, `students/{id}/levels/{levelId}/dossier` and
`students/{id}/outstanding-stages` all shipped with their features and none was ever wired into the
student file. So scolarité could see a student's *registrations* and not what he had done under them
— and the réinscription's refusal, « il doit encore N stages », could not be understood from the file
it is about.

**The three answer different questions and none replaces another**, which is why the tab shows all
three rather than folding them:

| read | scope | answers |
|---|---|---|
| `outstanding-stages` | cursus-wide | what `FinalYearGuard` refuses on |
| `levels/{id}/dossier` | one level, every registration folded | whether a stage is *acquired* — only readable folded, because a repeater has several |
| `parcours` | one year at a time | what actually happened, in order |

**Verified on real data the same day** (Houda Aamoud, 7ᵉ MED, 21 stages): 6ᵉ 6/6, 5ᵉ 7/7, 4ᵉ 5/5 —
and **3ᵉ année 2/6, with the other four « jamais tenté » and the banner correctly reading « aucun
stage en attente de revalidation »**. That is the distinction the tab exists to draw: « owed » means
*every attempt came back NonValidé*; a stage nobody has sat is not a debt and an unmarked one is not a
failure. Folding the three reads into one list would have lost exactly that.

⚠ **`refetchOnMountOrArgChange` on all three**, because the revalidation mutation lives in `adminApi`
and the parcours query in `studentApi` — RTK Query cannot invalidate across slices, so without it the
tab keeps showing the state from before the retake was opened.

## A test asserted a filter that was never applied (2026-09-02, found closing session 39)

`SqlTranslationTests.The_registration_hold_exclusion_compiles_to_sql` asserts that
`CohortProvisioner.GroupTextsQuery` mentions `RegistrationHolds`, and `CLAUDE.md` lists it as one of
the three reads a held registration is excluded from. **It was not in the query.** The exclusion had
been applied to `AutoArrangeGroupsCommandHandler` and to
`StudentAffectationService.EligibleRegistrationsQuery`, documented for all three, and tested for two.

**What it would have cost.** `GroupTextsQuery` decides which CNPN texts a roster follows, and
`CohortProvisioner` provisions one cohorte per (roster, required stage). A frozen registration's text
therefore counted: the roster could be given a cohorte for a text nobody in it will be planned under,
because the same student is excluded from the roster cut *and* from cohort affectation. Latent only
because the base has no cells at all — but the 1 327 signalements the roll raised are real, and the
first « Générer le plan » would have read them.

**Why it survived a green suite.** The assertion is on `ToQueryString()` output, and the query
compiles perfectly well without the filter — the test failed on `Should().Contain(…)`, not on a
translation error. It is the one case in that file that checks *what the SQL says* rather than *that
there is SQL*, and that is the only reason it was caught at all. Applying the filter fixed it with no
other change.

## L'axe de la 3ᵉ MED 2026-2027, posé pour de vrai (2026-09-02)

Premier axe jamais écrit dans cette base : elle tenait **0 `StageSlot` sur ses 22 années**.

**L'arithmétique.** Six stages à `k = 1` → `T = Σkₛ = 6` colonnes ; six partitions → `Lₛ = P·kₛ/T = 1`,
une partition par stage et par colonne. Le découpage que la scolarité avait fait (A-F, 94 rosters,
933 inscrits, **0 non placé**) tombe donc juste sans reste — ce qui n'est pas une coïncidence : six
partitions est le seul choix qui donne un `Lₛ` entier pour six stages de même durée.

**Les six colonnes font exactement 30 jours ouvrables**, la durée que le catalogue *et* le CNPN
1650.25 énoncent pour les six stages depuis `Cnpn1650Med3CatalogueAlignment`. C'est la propriété que
`WorkingDays` existe pour donner : les spans calendaires vont de 42 à 46 jours (P6 en fait 46) et
pourtant chaque étudiant fait le même stage. Coupure de 2 jours entre deux périodes, à la demande.

⚠ **Les quatre fêtes lunaires ont dû être saisies d'abord, et ce sont des estimations.** La base n'en
avait aucune pour 2026-2027 — 10 fériés nationaux, 0 religieux — et la page « Jours fériés »
l'affichait elle-même. Sans elles « jours ouvrables » veut dire « moins les week-ends », donc P5 et
P6 auraient compté l'Aïd comme des jours de stage : 28 jours réels au lieu de 30, sur les deux
dernières colonnes. Saisies **non confirmées** : les jours sont bloqués sur la meilleure estimation
et toute fenêtre posée dessus est signalée, ce qui est exactement le marché que `IsConfirmed`
propose. P5 et P6 portent chacune ce drapeau.

⚠ **Écrit en SQL, donc sans entrée d'audit.** `HolidayCommands` et `ApplyRotationCycleCommand` sont
l'un et l'autre `IAuditableCommand` ; l'automatisation du navigateur n'a pas tenu (le viewport se
redimensionnait entre deux captures, les coordonnées ne visaient plus rien) et la boîte de dialogue
refusait de s'ouvrir. La transaction **affirme ses propres comptes** avant de valider — 4 fêtes, 36
créneaux, six stages partageant la même fenêtre par colonne, aucun chevauchement par stage — et la
première version a d'ailleurs été annulée par sa propre assertion, trop large. C'est la pratique que
le rebuild du 01/09 avait établie. Mais 40 lignes écrites sans trace, c'est l'angle mort de
`HANDOFF.md` 0e sous un autre nom.

**Vérifié sur la page Répartition annuelle** : « Périodes définies, aucune répartition — les 36
créneaux de ce niveau sont en place (6 colonnes), mais aucun groupe n'y a encore été affecté. » C'est
`RepartitionSummary.DeclaredSlotCount` séparant « pas de période » de « des périodes où personne
n'est », sur des données réelles, et les deux appellent bien des actes opposés.

## Quatre corrections sur le rapport de charge, dont deux invisibles à l'écran (2026-09-02)

**1 · « 11 148 étudiants placés » n'était pas un effectif.** C'était la somme des effectifs de cohorte
sur chacune des 1 500 cellules — un compte de *placements*, où un étudiant compte une fois par créneau
qu'il occupe. La 3ᵉ année compte **933** étudiants réels. Signalé par l'utilisateur en une phrase :
« either its wrong or it means assignement, the administration won't understand it ». Les deux à la
fois, en réalité. Retiré de l'en-tête, remplacé par le **pic simultané**, qui est une mesure de
personnes ; les colonnes « Étudiants » des deux tableaux sont renommées **Placements** avec une phrase
disant ce qu'elles comptent. ⚠ Un chiffre qui ressemble à un effectif sans en être un est pire qu'un
chiffre absent — c'est la même famille que les colonnes vides de l'export, où la donnée était juste et
la lecture fausse.

**2 · Le bloc de rotation gardait les stages de la promotion précédente.** Le formulaire n'était vidé
qu'à l'intérieur du `if (block)` : une paire (promotion, année) *sans* bloc laissait donc le
précédent en place. Changer la promotion masquait le défaut — ce sélecteur vide à la main — mais
changer l'**année** dans la barre du haut ne passe pas par lui, et atterrir sur une promotion dont le
bloc vient d'être supprimé non plus. « Simuler » aurait alors porté sur les stages d'une promotion que
personne ne regardait. **Restaurer *rien* est un état, et il doit remettre le formulaire à zéro.**

**3 · « Occupation sur l'année » ne s'imprimait pas — et rien ne le disait.** Les navigateurs
suppriment les fonds à l'impression. Les graphiques SVG survivent (`fill` est du contenu), mais la
bande annuelle dessine chaque intervalle comme un `background` coloré sur un `<span>` vide : le PDF
sortait avec des pistes grises vides. La seule figure qui montre *quand* un service est plein, absente
du document, intacte à l'écran. `print-color-adjust: exact` sur `.charge-doc *` — déclaré sur chaque
élément, pas sur la racine, parce que la propriété n'hérite pas partout et que ce fichier est imprimé
par le navigateur de quelqu'un d'autre.

**4 · …et en le corrigeant, un second défaut d'impression, plus grave.** `break-inside: avoid` était
posé sur **toutes** les sections, y compris celle du tableau de 148 services. Un bloc qu'un navigateur
ne peut pas faire tenir sur une page n'est pas déplacé, il est **rogné** : le plus long tableau du
document était celui qui risquait le plus de perdre sa fin. La règle ne vaut plus que pour les
figures ; les tableaux se coupent entre les pages, gardent leurs lignes entières et **répètent leur
en-tête** (`display: table-header-group`), sans quoi la page 2 est un mur de nombres sans étiquette.

**Et la mise en page.** Le document s'étirait sur toute la largeur de la fenêtre : sur un écran de
1920 px, un SVG en `width: 100%` sur un viewBox de 720 donnait des barres de six cents pixels de haut
et le graphique horizontal débordait à droite. Il est désormais borné à 860 px — la largeur utile
d'un A4 portrait (190 mm ≈ 718 px) — et présenté comme une feuille blanche sur un fond gris, si bien
que l'aperçu, le PDF et le .html téléchargé ont enfin la même forme. C'est le marché que cette
feuille de style annonçait depuis le début et qu'elle ne tenait qu'en apparence.

## Le pic annonçait un mois pour un plateau de six (2026-09-02)

`OccupancyReportTotals.PeakStart/PeakEnd` venait d'un `MaxBy` : **le premier** intervalle atteignant
le maximum, pas l'étendue sur laquelle il est atteint. Sur le plan réel 2026-2027, la 3ᵉ et la 4ᵉ
année tournent ensemble, donc 1 858 étudiants se tiennent dans la faculté sur des dizaines
d'intervalles consécutifs — et le document annonçait « du 07/09 au 06/10 », un mois, **directement
sous un graphique montrant le plateau**. Le nombre était juste et la fenêtre fausse : la sorte
d'erreur qui se lit.

- Corrigé en **enveloppe** : premier jour atteint → dernier jour atteint.
- ⚠ **Et `PeakDays` à côté, parce que l'enveloppe seule ment aussi.** Les intervalles au pic ne sont
  pas forcément contigus : l'axe 3MED porte une coupure de 2 jours entre chaque période, pendant
  laquelle la charge retombe à 925. L'enveloppe fait ~181 jours, le temps réellement passé à 1 858
  en fait ~171. Dire l'un sans l'autre transforme un plateau troué en plateau plein.

**Et « 5 549 jours » n'était pas des jours.** C'est la somme des `DaysOverCapacity` **sur tous les
services** : un service au-dessus pendant dix jours et dix services au-dessus pendant un jour donnent
tous deux 10. Renommé **jours-service**, avec « un service, un jour = 1 » écrit à côté. Même famille
que les 11 148 « étudiants placés » — une unité fausse sur un nombre juste, et c'est l'unité qui se
retient.

## La bande annuelle n'avait pas d'axe (2026-09-02)

« Occupation sur l'année » portait un seul libellé — « sept. 2026 → mai 2027 » — et rien entre les
deux. La **position** d'une bande ne disait donc rien : on voyait qu'un service était plein sans
pouvoir dire quand, ce qui est la seule question que cette figure existe pour répondre. Ajoutés :
une échelle de mois (initiales) et une trame verticale derrière chaque piste, si bien que l'œil
descend une date d'un service à l'autre — c'est ainsi qu'on lit « ces quatre services sont pleins la
même quinzaine ». Les bandes reçoivent aussi un filet blanc intérieur, sans quoi deux périodes
consécutives dans le même service se lisent comme un seul séjour continu.

## Deux figures ajoutées, et pourquoi celles-là

- **La barre mensuelle est empilée par promotion.** « 1 858 » ne dit pas qui pousse ; 933 + 925 le
  dit. ⚠ Le découpage est lu sur **l'intervalle de pic du mois**, jamais sur le pic propre de chaque
  promotion — deux promotions ne culminent pas le même jour, donc la seconde méthode donne une somme
  supérieure au total, et une barre empilée dont les parts ne font pas le tout est pire qu'aucune
  barre. Un test l'affirme.
- **Une distribution des services par taux d'occupation.** Un classement montre les pires ; il ne
  montre pas la *forme*. Mesuré sur le plan réel : **102 services sans occupant, 23 sous 85 %, 0
  entre 85 et 100 %, 1 entre 100 et 200 %, 22 au-dessus de 200 %.** C'est un parc vide à 69 % dont
  une poignée porte tout — le fait qui appelle une décision, et il n'est visible que comme
  distribution.

⚠ **Le document se défend maintenant d'une réponse d'API plus ancienne** (`m.levels ?? …`,
`peakDays ?? 0`). L'AppHost vit longtemps ici, donc une API servant encore la forme précédente est un
état ordinaire en développement — et lire `.map` sur une collection absente a fait tomber tout le
document par l'error boundary. Même défense que `service.chefHistory ?? []`.

## Deux colonnes consécutives comptées ensemble (2026-09-03, signalé depuis l'écran)

`ServiceOccupancyLookup.LoadOn(service, start, end)` **sommait toute cellule chevauchant la
fenêtre** — donc deux cellules qui touchent chacune la fenêtre *sans se toucher entre elles* étaient
additionnées.

**Reproduit exactement sur le plan réel.** Sur Pédiatrie2, pour le créneau P2 de Pharmacie Clinique 1
(06/10 → 03/11) :

| cellule | fenêtre | étudiants |
|---|---|---|
| 4ᵉ année Pédiatrie **P1** | 07/09 → **06/10** | 56 |
| Pharmacie Clinique 1 P2 | 06/10 → 03/11 | 6 |
| 4ᵉ année Pédiatrie **P2** | **07/10** → 06/11 | 56 |
| **somme affichée** | | **118** |
| **présence réelle, chaque jour** | | **62** |

Les deux colonnes de Pédiatrie sont **consécutives** — l'une finit le 6, l'autre commence le 7 — et
ne coexistent jamais. Elles chevauchent seulement, l'une et l'autre, la fenêtre des pharmaciens.

⚠ **Ce n'est pas un défaut d'affichage : c'est le nombre sur lequel une publication est refusée.**
Les trois lecteurs de cette classe sont la saturation de la grille de planning, l'équilibrage de
`RotationArranger`, et la garde de `SchedulePublisher`. La publication était donc refusée sur des
charges qui n'existent pas.

**Et la fiche du service et le rapport de charge avaient raison depuis le début** — ils passent par
`OccupancyTimeline`, qui découpe à chaque frontière. Le CLAUDE.md affirmait que la fiche « mesure la
charge exactement comme la garde le fait » ; c'était vrai de l'intention et faux de l'arithmétique.
`LoadOn` fait désormais le même balayage, si bien que les quatre ne peuvent plus diverger.

- **Le maximum se lit sur les seuls jours où la charge peut monter** : le début de la fenêtre, ou le
  premier jour d'une cellule qui s'ouvre dedans. C'est exact, pas échantillonné.
- ⚠ **La garde devient moins stricte, et c'est correct.** Elle ne peut pas rater un vrai
  dépassement : un dépassement réel est un instant où la somme franchit le plafond, et cet instant
  fait partie des candidats évalués. Les 1 409 tests passent sans qu'aucune attente existante ait dû
  changer — personne ne s'appuyait sur la somme.
- **C'est l'utilisateur qui l'a vu**, en comparant deux écrans qui affichaient 118 et 62 pour le même
  service. Aucun test ne pouvait le trouver : les deux chemins étaient couverts séparément et chacun
  était cohérent avec lui-même.
